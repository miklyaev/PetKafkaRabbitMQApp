// ─────────────────────────────────────────────────────────────────────────────
//  Shop.Web — витрина «MiniShop».
//
//  Роль в архитектуре (см. spec.md, раздел 3):
//    • ASP.NET Core MVC — хостит React-SPA (clientapp собирается в wwwroot/app.js);
//    • REST API /api/*  — тонкие контроллеры; вся логика домена в микросервисе
//      Shop.Orders, обмен с ним — через MassTransit request/response (RabbitMQ);
//    • SignalR-хаб /hubs/notifications — пушит уведомления в браузер;
//    • сам выступает консьюмером NotificationSent (fanout из Shop.Notifications).
//
//  Брокеры в этом проекте не «на равных»: Web работает ТОЛЬКО с RabbitMQ.
//  Kafka (топик orders-events) — между Shop.Orders и Shop.Notifications,
//      сюда события не приходят, а приходят уже «готовые» уведомления.
//
//  Конфигурация подключений не захардкожена: адреса/учётки читаются из
//  appsettings.json → секция MessageBus:RabbitMq (класс RabbitMqOptions
//  из Shop.Contracts). Для тестов значения можно переопределить переменными
//      окружения, например: MessageBus__RabbitMq__Host=rabbit.local
// ─────────────────────────────────────────────────────────────────────────────

using System.Text.Json.Serialization;
using MassTransit;
using Shop.Contracts;
using Shop.Web.Consumers;
using Shop.Web.Hubs;
using Shop.Web.Diagnostics;
using Shop.Observability;

var builder = WebApplication.CreateBuilder(args);
var diagnosticsEnabled = builder.Services.AddWebBrokerMonitoring(builder.Configuration, builder.Environment);

// ── Конфигурация брокера ─────────────────────────────────────────────────────
// Bind секции MessageBus:RabbitMq на строго типизированные опции.
// Если секции нет (запуск «как есть», без appsettings) — используем значения
// по умолчанию, зашитые в RabbitMqOptions (localhost:5672, guest/guest):
// сервис не упадёт на старте, а попробует подключиться к локальному брокеру.
var rabbit = builder.Configuration.GetSection(RabbitMqOptions.SectionName).Get<RabbitMqOptions>() ?? new RabbitMqOptions();

// ── MVC + REST API ───────────────────────────────────────────────────────────
// AddControllersWithViews: и [ApiController]-контроллеры (api/orders, api/catalog),
// и Razor-вьюхи (Views/Home/Index.cshtml — монтирует React в #root).
//
// JsonStringEnumConverter — сериализация enum'ов строками ("Accepted"), а не
// числами (0). Причина: контракт REST/SignalR читает фронтенд, человеку в логах
// и в браузере строка тоже понятнее числа.
builder.Services
    .AddControllersWithViews()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// ── SignalR ──────────────────────────────────────────────────────────────────
// Хаб NotificationHub пустой: клиенту НЕ нужен метод «клиент → сервер» —
// пуш всегда идёт только от сервера (см. NotificationSentConsumer).
// Конвертер enum → строку тот же, что у REST: фронтенд получает единый формат.
builder.Services
    .AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// ── MassTransit (транспорт — RabbitMQ) ───────────────────────────────────────
// Что здесь регистрируется:
//   1. Клиенты request/response — это «прокси» для контроллеров OrdersController:
//      IRequestClient<T>.GetResponse<...>(...) отправляет сообщение и ждёт ответ
//      из очереди временных ответов (temporary response queue, автоудаление).
//   2. Consumer NotificationSent — конечная точка цепочки уведомлений
//      (Orders → Kafka → Notifications → RabbitMQ → здесь) → SignalR → браузер.
builder.Services.AddMassTransit(x =>
{
    if (diagnosticsEnabled) x.AddBrokerDiagnosticObservers();
    // Очереди/обменники получат имена в kebab-case: create-order, get-orders,
    // notification-sent. Предсказуемая схема топологии — как в спецификации.
    x.SetKebabCaseEndpointNameFormatter();

    // Request-клиенты: таймаут 30 с — щедрый, но конечный. Если Shop.Orders
    // лежит, HTTP-запрос не зависнет навсегда, а получит TimeoutException →
    // контроллер вернёт 5xx (см. global exception handling / ProblemDetails).
    x.AddRequestClient<CreateOrder>(TimeSpan.FromSeconds(30));
    x.AddRequestClient<ChangeOrderStatus>(TimeSpan.FromSeconds(30));
    x.AddRequestClient<GetOrders>(TimeSpan.FromSeconds(30));

    // Консьюмер уведомлений из Shop.Notifications (fanout → очередь notification-sent).
    x.AddConsumer<NotificationSentConsumer>();

    // Настройки транспорта RabbitMQ — параметры из appsettings.
    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(rabbit.Host, (ushort)rabbit.Port, rabbit.VirtualHost, h =>
        {
            h.Username(rabbit.Username);
            h.Password(rabbit.Password);
        });

        // Retry на уровне консьюмера: если Shop.Web деградировал (например,
        // SignalR/SQLite недоступны), сообщение не потеряется — MassTransit
        // вернёт nack и повторит доставку: 0.5 с → 30 с, максимум 5 попыток.
        // После — в очередь notification-sent_error (dead-letter, штатный
        // механизм MassTransit для RabbitMQ).
        cfg.UseMessageRetry(r => r.Exponential(5, TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2)));

        // По зарегистрированным выше консьюмерам MassTransit сам создаст
        // привязки: очередь notification-sent ← fanout-обменник (по типу
        // сообщения NotificationSent).
        cfg.ConfigureEndpoints(ctx);
    });
});

var app = builder.Build();

// ── HTTP-конвейер ────────────────────────────────────────────────────────────
// В Development оставляем подробные страницы ошибок разработчика.
// В Production — аккуратную страницу /Home/Error + HSTS (форсировать HTTPS).
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// app.js / app.css и другие статические артефакты сборки React (clientapp).
app.UseBrokerMonitoringGuard(diagnosticsEnabled);
app.UseStaticFiles();
app.UseRouting();

// Атрибутные маршруты ApiController'ов (/api/orders, /api/catalog).
app.MapControllers();

// Классический MVC-маршрут: / → Home/Index → Views/Home/Index.cshtml → React #root.
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");

// SignalR-хаб: клиент (clientapp/src/signalr.ts) подключается сюда,
// получая пуш «NotificationReceived» из NotificationSentConsumer.
app.MapHub<NotificationHub>("/hubs/notifications");
if (diagnosticsEnabled) app.MapHub<DiagnosticsHub>("/hubs/diagnostics");

app.Run();