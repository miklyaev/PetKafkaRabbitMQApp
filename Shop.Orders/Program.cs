// ─────────────────────────────────────────────────────────────────────────────
//  Shop.Orders — микросервис заказов «MiniShop».
//
//  Это Worker-сервис (НЕ веб-приложение): у него нет HTTP-порта, он целиком
//  живёт в MassTransit. Как он вписан в архитектуру (см. spec.md, разделы 3–4):
//
//    Shop.Web ──(команды CreateOrder / ChangeOrderStatus / GetOrders)──► ЗДЕСЬ
//                                          │
//                                          ▼
//                             Kafka-топик «orders-events»
//                                          │
//                                          ▼
//                                 Shop.Notifications
//
//  Два брокера — с разными ролями (это ключевая идея проекта):
//    • RabbitMQ — «командная шина»: точечные команды и request/response.
//      Каждое сообщение адресовано конкретному обработчику и после обработки
//      удаляется из очереди.
//    • Kafka — «журнал событий»: append-only поток фактов OrderEvent.
//      Событие никто конкретно не «заказывал» — его читает любой, кто
//      подключится (сейчас это Shop.Notifications, группа «notifications»).
//
//  Данные сервис хранит у себя (паттерн database-per-service): SQLite-файл
//  orders.db. Никакой другой сервис в это БД не лезет — все вопросы к заказам
//  только через сообщения (GetOrders) или REST Shop.Web.
//
//  ВАЖНО: DbContext и потребителей (consumers) по умолчанию регистрируются как
//  Scoped, а MassTransit создаёт scope НА КАЖДОЕ сообщение — поэтому DbContext
//  безопасно инжектить прямо в консьюмер (см. Consumers/*.cs).
// ─────────────────────────────────────────────────────────────────────────────

using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Shop.Contracts;
using Shop.Orders.Consumers;
using Shop.Orders.Data;

// Host.CreateApplicationBuilder — «лёгкий» хост без веб-сервера (в отличие от
// WebApplication.CreateBuilder в Shop.Web). Даёт DI, конфигурацию и логирование,
// которых Worker-сервису достаточно.
var builder = Host.CreateApplicationBuilder(args);

// ── Конфигурация брокеров ────────────────────────────────────────────────────
// Оба подключения читаются из appsettings.json (секция MessageBus) и биндятся
// на строго типизированные классы-опции из Shop.Contracts. Если секции нет —
// сработают дефолты, зашитые в опции (localhost): сервис не упадёт на старте.
// Значения можно переопределить переменными окружения, например:
//   MessageBus__RabbitMq__Host=my-rabbit   MessageBus__Kafka__BootstrapServers=kafka:9092
var rabbit = builder.Configuration.GetSection(RabbitMqOptions.SectionName).Get<RabbitMqOptions>() ?? new RabbitMqOptions();
var kafka = builder.Configuration.GetSection(KafkaOptions.SectionName).Get<KafkaOptions>() ?? new KafkaOptions();

// Однострочный формат логов с таймстампом: в консоли, где крутятся сразу три
// сервиса, так гораздо читабельнее, чем дефолтные многострочные блоки.
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
});

// ── База данных (EF Core + SQLite) ───────────────────────────────────────────
// Строка подключения — из appsettings (ConnectionStrings:Orders). Добавляется
// как Scoped: MassTransit создаст scope на каждое сообщение, поэтому в
// консьюмерах DbContext — «свежий» на каждую доставку.
builder.Services.AddDbContext<OrdersDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Orders") ?? "Data Source=orders.db"));

// ── MassTransit: шина RabbitMQ + райдер Kafka ─────────────────────────────────
// Один вызов AddMassTransit настраивает СРАЗУ оба брокера. Ключевое слово здесь
// — «rider» (райдер): Kafka — не замена RabbitMQ в этом проекте, а «пассажир
// поверх той же шины»: тот же DI-контейнер, тот же жизненный цикл запуска/останова.
builder.Services.AddMassTransit(x =>
{
    // Все очереди/обменники будут назваться в kebab-case: create-order,
    // change-order-status, get-orders. Имена фиксированы и предсказуемы —
    // удобно сверяться с RabbitMQ Management UI (http://localhost:15672).
    x.SetKebabCaseEndpointNameFormatter();

    // Регистрируем потребителей (handlers) команд из RabbitMQ.
    // AddConsumer ДОЛЖЕН идти до ConfigureEndpoints: MassTransit сканирует
    // этот список и создаёт под каждого отдельную очередь + привязку.
    x.AddConsumer<CreateOrderConsumer>();        // создать заказ (ответ+событие в Kafka)
    x.AddConsumer<ChangeOrderStatusConsumer>();  // завершить/отменить (ответ+событие)
    x.AddConsumer<GetOrdersConsumer>();          // список заказов (только ответ)

    // ── RabbitMQ: основной транспорт ──────────────────────────────────────────
    // Здесь живут команды и request/response. Shop.Web шлёт команды с
    // requestId — MassTransit маршрутизирует ответ обратно в запросившую
    // временную очередь (temporary response queue) сам, по заголовкам.
    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(rabbit.Host, (ushort)rabbit.Port, rabbit.VirtualHost, h =>
        {
            h.Username(rabbit.Username);
            h.Password(rabbit.Password);
        });

        // Ретраи на случай, когда наш же сервис спотыкается (например, SQLite
        // залочен): nak → повторная доставка. 5 попыток, интервал растёт
        // экспоненциально от 0.5 с до 30 с (шаг 2 с). После 5-й неудачи
        // сообщение уходит в очередь create-order_error и т.п. (dead-letter).
        // Допущение идемпотентности: в консьюмерах это учтено — повторная
        // обработка не создаёт дубль (проверка по OrderId).
        cfg.UseMessageRetry(r => r.Exponential(5, TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2)));

        // Создать очереди/обменники/привязки для всех AddConsumer выше.
        // Имена kebab-case уже настроены, поэтому тут больше ничего не нужно.
        cfg.ConfigureEndpoints(ctx);
    });

    // ── Kafka: райдер (продюсер событий) ──────────────────────────────────────
    // AddRider добавляет Kafka «поверх» той же шины. Продюсер объявляется типом
    // сообщения: <Guid, OrderEvent> означает «ключ партиции Guid, значение
    // OrderEvent». Ключ = OrderId: все события одного заказа попадают в одну
    // партицию и читаются в порядке записи (гарантия Kafka в пределах ключа).
    x.AddRider(rider =>
    {
        rider.AddProducer<Guid, OrderEvent>(kafka.Topic);

        // Адрес кластера — bootstrap-серверы из appsettings
        // (MessageBus:Kafka:BootstrapServers). Kafka-клиент сам найдёт всех
        // брокеров кластера, зная один адрес.
        rider.UsingKafka((ctx, k) => { k.Host(kafka.BootstrapServers); });
    });
});

var app = builder.Build();

// ── Инициализация БД ──────────────────────────────────────────────────────────
// Пет-упрощение: вместо миграций EF Core — EnsureCreated (создаст файл
// orders.db со схемой при первом старте). Для реальных проектов — миграции
// (dotnet ef migrations add ...), здесь это осознанное упрощение (spec.md, §12).
using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
    await db.Database.EnsureCreatedAsync();
}

// RunAsync (а не Run) — чтобы Main был async и последний «горячий» await
// не съел поток впустую.
await app.RunAsync();