// ─────────────────────────────────────────────────────────────────────────────
//  Shop.Notifications — микросервис уведомлений «MiniShop».
//
//  Worker-сервис без HTTP: живёт в MassTransit и слушает события.
//  Позиция в цепочке (см. spec.md, разделы 3–4):
//
//    Shop.Orders ──(события OrderEvent)──► Kafka-топик «orders-events» ──► ЗДЕСЬ
//                                                                     │
//                              формируем текст уведомления            │
//                              (журнал: SQLite notifications.db)      │
//                                                                     ▼
//                    RabbitMQ (fanout) ──► Shop.Web ──► SignalR ──► браузер
//
//  Ключевое отличие от Shop.Orders: здесь Kafka — на стороне ПОТРЕБЛЕНИЯ
//  (consumer), а не публикации (producer). Как это устроено:
//    • консьюмер-группа «notifications» — см. ниже у TopicEndpoint;
//    • смещения (offsets) коммитит MassTransit после успешной обработки;
//    • события хранит Kafka (retention), а не RabbitMQ: перезапуск сервиса
//      не теряет историю — мы продолжаем с последнего offset.
//
//  Почему оба брокера в одном сервисе? Уведомления приходят из Kafka
//  (событийного потока), а уходить должны в браузер через Shop.Web,
//  который слушает RabbitMQ. Значит, этот сервис обязан «перекладывать»
//  между транспортами: consume Kafka → publish RabbitMQ. Это осознанная
//  связка именно этого микросервиса (см. spec.md, §3 «роли брокеров»).
// ─────────────────────────────────────────────────────────────────────────────

using Confluent.Kafka;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Shop.Contracts;
using Shop.Notifications.Consumers;
using Shop.Notifications.Data;
using Shop.Observability;

// «Лёгкий» хост без веб-сервера (как и в Shop.Orders): DI + конфигурация + логи.
var builder = Host.CreateApplicationBuilder(args);
var diagnosticsEnabled = builder.Services.AddBrokerDiagnostics(builder.Configuration, builder.Environment, "Shop.Notifications");

// ── Конфигурация брокеров ────────────────────────────────────────────────────
// Секции MessageBus:RabbitMq и MessageBus:Kafka из appsettings.json →
// типизированные опции (Shop.Contracts). Дефолты зашиты в самих опциях.
// GroupId используется ТОЛЬКО здесь (у консьюмера): это «имя подписчика»
// в Kafka, по которому хранятся смещения (offsets) — см. ниже.
var rabbit = builder.Configuration.GetSection(RabbitMqOptions.SectionName).Get<RabbitMqOptions>() ?? new RabbitMqOptions();
var kafka = builder.Configuration.GetSection(KafkaOptions.SectionName).Get<KafkaOptions>() ?? new KafkaOptions();

// Однострочные логи с таймстампом — читаемость, когда рядом крутятся три сервиса.
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
});

// ── База данных (EF Core + SQLite) ───────────────────────────────────────────
// Журнал уведомлений (таблица NotificationRecords). Отдельная БД — по паттерну
// database-per-service: notifications.db никого, кроме этого сервиса, не интересует.
builder.Services.AddDbContext<NotificationsDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Notifications") ?? "Data Source=notifications.db"));

// ── MassTransit: райдер Kafka (consume) + шина RabbitMQ (publish) ────────────
// Порядок внутри не важен, MassTransit разберётся: и то и другое «поднимется»
// одним Bus.StartAsync. Но читается логичнее так: сначала откуда читаем,
// потом куда пишем.
builder.Services.AddMassTransit(x =>
{
    if (diagnosticsEnabled)
        x.AddBrokerDiagnosticObservers();

    x.SetKebabCaseEndpointNameFormatter(); // имена очередей/эндпоинтов — kebab-case

    // ── Kafka: райдер-консьюмер ────────────────────────────────────────────────
    x.AddRider(rider =>
    {
        // Регистрируем обработчик событий OrderEvent. Регистрация ДО
        // TopicEndpoint — иначе ConfigureConsumer ниже не найдёт тип.
        rider.AddConsumer<OrderEventConsumer>();

        rider.UsingKafka((ctx, k) =>
        {
            k.Host(kafka.BootstrapServers);

            // TopicEndpoint — «читатель топика»: к какой теме подключаться
            // и в какой консьюмер-группе.
            //
            // ГРУППА — важнейшее понятие Kafka:
            //   • offset (позиция чтения) хранится ПО ГРУППЕ, не по сервису;
            //   • все копии сервиса с одной GroupId ДЕЛЯТ партиции (нагрузка
            //     балансируется, каждое событие обрабатывается один раз);
            //   • другая группа (например, будущий сервис аудита) читает
            //     тот же топик НЕЗАВИСИМО с своими offset'ами — это отличие
            //     Kafka от очередей RabbitMQ.
            k.TopicEndpoint<OrderEvent>(kafka.Topic, kafka.GroupId ?? "notifications", e =>
            {
                // Earliest: при ПЕРВОМ старте группы начать с самого начала
                // топика, а не с «сейчас». Когда группа уже имеет сохранённый
                // offset — Kafka продолжает с него, эта настройка игнорируется.
                e.AutoOffsetReset = AutoOffsetReset.Earliest; // читать события, накопившиеся до старта сервиса

                // Если топика ещё нет — создать: 3 партиции, replication 1
                // (пет-режим, один брокер; в проде RF ≥ 3). NumPartitions = 3
                // позволит масштабировать консьюмеров до трёх параллельных.
                e.CreateIfMissing(t => { t.NumPartitions = 3; t.ReplicationFactor = 1; });

                // Привязать OrderEventConsumer (зарегистрирован выше) к этому
                // эндпоинту. В отличие от RabbitMQ, где ConfigureEndpoints угадал
                // всё сам, у Kafka НЕТ автопривязки сообщений к темам — только явно.
                e.ConfigureConsumer<OrderEventConsumer>(ctx);
            });
        });
    });

    // ── RabbitMQ: основной транспорт ──────────────────────────────────────────
    // Сам сервис НИЧЕГО не слушает в RabbitMQ — только публикует NotificationSent
    // (это делает OrderEventConsumer через context.Publish). Fanout-обменник
    // создастся по первому publish, очередь notification-sent — на стороне Shop.Web.
    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(rabbit.Host, (ushort)rabbit.Port, rabbit.VirtualHost, h =>
        {
            h.Username(rabbit.Username);
            h.Password(rabbit.Password);
        });

        // Ретраи для publish'ей (если RabbitMQ моргнул). На consume здесь
        // ретраев нет — чтением управляет Kafka: при ошибке обработки offset
        // не коммитится, и событие придёт снова (см. OrderEventConsumer).
        cfg.UseMessageRetry(r => r.Exponential(5, TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2)));
        cfg.ConfigureEndpoints(ctx);
    });
});

var app = builder.Build();

// ── Инициализация БД ──────────────────────────────────────────────────────────
// Пет-упрощение: EnsureCreated вместо миграций (как в Shop.Orders; spec.md, §12).
using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
    await db.Database.EnsureCreatedAsync();
}

await app.RunAsync();