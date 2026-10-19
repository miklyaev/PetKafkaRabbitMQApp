# Спецификация: MiniShop — пет-проект Kafka + RabbitMQ

**Репозиторий:** `PetKafkaRabbitMQApp` · **Версия:** 1.0 · **Дата:** 2026-10-13

## 1. Назначение

Пет-проект для отработки интеграции ASP.NET Core с брокерами сообщений **RabbitMQ** и
**Apache Kafka** через библиотеку **MassTransit**. Домен — витрина «MiniShop»: приём заказов
из веб-интерфейса и уведомления об их жизненном цикле.

Обучающие цели:

- решение Visual Studio 2026: **2 микросервиса** (`Shop.Orders`, `Shop.Notifications`) +
  веб-приложение **ASP.NET Core MVC + React** (`Shop.Web`) + общие контракты (`Shop.Contracts`);
- MassTransit: команды и request/response через RabbitMQ, событийный поток через Kafka (rider);
- весь код работы с I/O — асинхронный (`async/await`): контроллеры, консьюмеры, продюсеры, EF Core;
- современный UI: React 19 + TypeScript + Vite + Tailwind CSS 4, обновления в реальном времени (SignalR).

Решение сознательно компактное: 5 проектов, минимум зависимостей, SQLite без внешней БД.

## 2. Стек

| Слой | Технология |
|---|---|
| IDE / среда | Visual Studio 2026, .NET 10 (C# 14), Windows 10/11, Docker Desktop |
| Веб | ASP.NET Core MVC + SignalR; React 19 + TypeScript + Vite; Tailwind CSS 4 |
| Микросервисы | ASP.NET Core Worker Host (консольные хосты) |
| Шина | MassTransit **v8** (Apache-2.0): `MassTransit.RabbitMQ` + `MassTransit.Kafka` |
| Брокеры | RabbitMQ 3 (`:5672`, UI `:15672`), Apache Kafka 3 KRaft (`:9092`), Kafka UI (`:8080`) — все в Docker |
| БД | SQLite + EF Core 10, по одному файлу БД на сервис |

## 3. Архитектура

```
React (браузер)
   │  REST /api/*                WebSocket /hubs/notifications (SignalR)
   ▼                                    ▲
┌──────────────────────────────────────────────────┐
│                     Shop.Web                      │
│  ASP.NET Core MVC + SignalR + MassTransit         │
│  • REST: каталог, заказы (тонкие контроллеры)     │
│  • IRequestClient<CreateOrder / ChangeOrderStatus │
│                     / GetOrders>                 │
│  • консьюмер NotificationSent → push в SignalR    │
└──────┬───────────────────────────────────▲───────┘
       │ (1) команды + request/response    │ (6) NotificationSent (publish)
       ▼                                  │
╔═════════════════ RabbitMQ ══════════════╝
║  очереди: create-order, change-order-status,
║  get-orders, notification-sent  (+ *_error при сбоях)
╚═══════════▲─────────────────────────
            │ (2) консьюмеры команд
┌───────────┴─────────────┐
│       Shop.Orders       │   SQLite: orders.db
│  домен заказов, валидация, статусная модель
└───────────┬─────────────┘
            │ (3) OrderEvent, ключ партиции = OrderId
            ▼
╔════════════ Kafka: topic «orders-events» ═════════════╗
╚═══════════════════════════▲═══════════════════════════╝
                            │ (4) consume, группа «notifications»
              ┌─────────────┴──────────────┐
              │     Shop.Notifications     │  SQLite: notifications.db
              │  формирует уведомления,    │
              │  publish NotificationSent  │──(5)──► RabbitMQ
              └────────────────────────────┘
```

### Проекты решения

| Проект | Тип | Ответственность |
|---|---|---|
| `Shop.Web` | ASP.NET Core (MVC) | UI: MVC-вью, хостинг SPA, REST API, SignalR-хаб; MassTransit: request-клиенты и консьюмер `NotificationSent` |
| `Shop.Orders` | Worker-хост | Домен заказов: консьюмеры `CreateOrder`/`ChangeOrderStatus`/`GetOrders`, SQLite, продюсер событий в Kafka |
| `Shop.Notifications` | Worker-хост | Консьюмер Kafka-топика `orders-events`, генерация уведомлений, публикация `NotificationSent` в RabbitMQ |
| `Shop.Contracts` | ClassLib | Контракты сообщений (records), статический каталог товаров, классы опций конфигурации брокеров |

### Ключевое решение: роли брокеров

- **RabbitMQ — «командная шина».** Точечные команды (`CreateOrder`, `ChangeOrderStatus`),
  request/response (`GetOrders`), fanout-уведомления (`NotificationSent`). Очереди, retry,
  error-очереди (DLQ). Быстрая доставка небольшого числа сообщений конкретному получателю.
- **Kafka — «событийный поток».** Append-only топик `orders-events`, сообщение `OrderEvent` —
  факт изменения заказа. Ключ партиции = `OrderId` (упорядоченность в рамках заказа),
  консьюмер-группа `notifications`, лог хранения/ретридов — на стороне брокера.

Один сервис может работать с обоими брокерами: RabbitMQ — основной транспорт
(`x.UsingRabbitMq(...)`), Kafka — райдер поверх того же контейнера (`x.AddRider(r => r.UsingKafka(...))`).

## 4. Потоки сообщений

### 4.1. Сценарий «Создание заказа»

1. React: `POST /api/orders` с телом `{ customerName, items: [{ productName, quantity }] }`.
2. `Shop.Web`: генерирует `OrderId`, отправляет команду `CreateOrder` через `IRequestClient` → очередь `create-order` (RabbitMQ).
3. `Shop.Orders` (консьюмер): валидирует позиции по каталогу из `Shop.Contracts`, считает
   сумму, сохраняет заказ (статус `Accepted`) в SQLite, отвечает `CreateOrderAccepted` → Web → HTTP 200.
4. `Shop.Orders`: публикует `OrderEvent { OrderId, Status: Accepted, … }` в Kafka-топик
   `orders-events` (`ITopicProducer<Guid, OrderEvent>`, ключ = `OrderId`).
5. `Shop.Notifications` (Kafka-консьюмер, группа `notifications`): формирует текст уведомления,
   сохраняет в `notifications.db`.
6. `Shop.Notifications`: публикует `NotificationSent` в RabbitMQ.
7. `Shop.Web` (консьюмер очереди `notification-sent`): пушит payload через `IHubContext<NotificationHub>`.
8. React: тост-уведомление + обновление списка заказов.

### 4.2. Сценарий «Смена статуса» (завершить/отменить)

`POST /api/orders/{id}/complete|cancel` → `ChangeOrderStatus` (RabbitMQ) → `Shop.Orders`
проверяет переход (`Accepted → Completed|Cancelled`), обновляет БД → `OrderEvent` в Kafka →
далее шаги 5–8 из сценария 4.1. Ответ клиенту: `OrderStatusAccepted` либо `OrderStatusRejected`.

### 4.3. Сценарий «Список заказов»

React опрашивает `GET /api/orders` (и сразу после каждого SignalR-уведомления): Web →
`GetOrders` request/response → `Shop.Orders` → `GetOrdersResponse` со списком заказов.

## 5. Контракты сообщений (`Shop.Contracts`)

| Тип | Транспорт | Издатель → Потребитель | Назначение |
|---|---|---|---|
| `OrderItem(ProductName, Quantity)` | — | часть контрактов | позиция заказа |
| `CreateOrder(OrderId, CustomerName, Items)` | RabbitMQ | Web → Orders | создать заказ |
| `CreateOrderAccepted(OrderId, Status, TotalAmount)` | RabbitMQ (response) | Orders → Web | успех |
| `CreateOrderRejected(Code, Reason)` | RabbitMQ (response) | Orders → Web | отказ |
| `ChangeOrderStatus(OrderId, NewStatus)` | RabbitMQ | Web → Orders | сменить статус |
| `OrderStatusAccepted(OrderId, Status)` | RabbitMQ (response) | Orders → Web | успех |
| `OrderStatusRejected(Code, Reason)` | RabbitMQ (response) | Orders → Web | отказ |
| `GetOrders()` / `GetOrdersResponse(Orders)` | RabbitMQ (request/response) | Web → Orders | список заказов |
| `OrderSummary(Id, CustomerName, Status, TotalAmount, CreatedAt, Items)` | — | — | DTO заказа |
| `OrderEvent(OrderId, CustomerName, Status, TotalAmount, Items, OccurredAt)` | Kafka | Orders → Notifications | факт изменения заказа |
| `NotificationSent(OrderId, CustomerName, Status, Text, OccurredAt)` | RabbitMQ | Notifications → Web | готовое уведомление |
| `OrderStatus` (enum: `Accepted`, `Completed`, `Cancelled`) | — | — | статус заказа |
| `RejectionReason` (enum: `EmptyOrder`, `InvalidData`, `UnknownProduct`, `NotFound`, `InvalidTransition`) | — | — | код отказа → HTTP-код |
| `Product(Name, Price, Icon)` + `Catalog.Items` | — | — | статический каталог (единый источник цен) |
| `RabbitMqOptions` / `KafkaOptions` | — | — | классы-опции конфигурации брокеров |

## 6. Конфигурация

### 6.1. Переменные в `appsettings.json`

**`Shop.Web`** (RabbitMQ только):

```json
{
  "Logging": {
    "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" }
  },
  "MessageBus": {
    "RabbitMq": {
      "Host": "localhost",
      "Port": 5672,
      "Username": "guest",
      "Password": "guest",
      "VirtualHost": "/"
    }
  }
}
```

**`Shop.Orders`** (RabbitMQ + Kafka-продюсер + строка подключения):

```json
{
  "MessageBus": {
    "RabbitMq": { "Host": "localhost", "Port": 5672, "Username": "guest", "Password": "guest", "VirtualHost": "/" },
    "Kafka": {
      "BootstrapServers": "localhost:9092",
      "Topic": "orders-events"
    }
  },
  "ConnectionStrings": { "Orders": "Data Source=orders.db" }
}
```

**`Shop.Notifications`** (RabbitMQ + Kafka-консьюмер):

```json
{
  "MessageBus": {
    "RabbitMq": { "Host": "localhost", "Port": 5672, "Username": "guest", "Password": "guest", "VirtualHost": "/" },
    "Kafka": {
      "BootstrapServers": "localhost:9092",
      "Topic": "orders-events",
      "GroupId": "notifications"
    }
  },
  "ConnectionStrings": { "Notifications": "Data Source=notifications.db" }
}
```

| Параметр | Тип | Назначение |
|---|---|---|
| `MessageBus:RabbitMq:Host` | string | хост брокера RabbitMQ (докер-порт публикуется на localhost) |
| `MessageBus:RabbitMq:Port` | int | AMQP-порт (5672) |
| `MessageBus:RabbitMq:Username` / `Password` | string | учётка AMQP (guest/guest работает только с localhost) |
| `MessageBus:RabbitMq:VirtualHost` | string | vhost, по умолчанию `/` |
| `MessageBus:Kafka:BootstrapServers` | string | bootstrap-адрес Kafka (`host:port`) |
| `MessageBus:Kafka:Topic` | string | имя топика для событий заказов (`orders-events`) |
| `MessageBus:Kafka:GroupId` | string | консьюмер-группа (используется только `Shop.Notifications`) |
| `ConnectionStrings:Orders` / `:Notifications` | string | SQLite-файл БД сервиса |

Свойства опций имеют значения по умолчанию (хард-резерв в `RabbitMqOptions`/`KafkaOptions`),
поэтому сервис стартует даже без секции. Значения переопределяются переменными окружения
(`MessageBus__RabbitMq__Host` и т.д.) — стандартный `IConfiguration`.

### 6.2. Инфраструктура Docker (`docker-compose.yml`, корень решения)

| Сервис | Образ | Порты | Назначение |
|---|---|---|---|
| `rabbitmq` | `rabbitmq:3-management` | 5672, 15672 | брокер + веб-консоль (guest/guest) |
| `kafka` | `apache/kafka:3.9.0` (KRaft, single-node) | 9092 | брокер; dual-listeners: `PLAINTEXT` (внутри сети, `kafka:29092`) и `EXTERNAL` (`localhost:9092`) |
| `kafka-ui` | `provectuslabs/kafka-ui` | 8080 | веб-UI для топиков/групп/сообщений |

Автосоздание топиков включено (`KAFKA_AUTO_CREATE_TOPICS_ENABLE=true`), плюс MassTransit
конфигурирует `CreateIfMissing` (1 партиция по умолчанию для `orders-events`, replication factor 1).

### 6.3. Порты приложений

| Приложение | URL |
|---|---|
| `Shop.Web` (Kestrel) | `http://localhost:5080`, `https://localhost:7080` |
| Vite dev-сервер (hot reload) | `http://localhost:5173` |
| RabbitMQ Management | `http://localhost:15672` |
| Kafka UI | `http://localhost:8080` |

## 7. База данных

- Каждый сервис владеет своей SQLite-БД (`orders.db`, `notifications.db`) — паттерн
  database-per-service в минимальном виде.
- Схема создаётся при старте `EnsureCreatedAsync()` (пет-упрощение вместо миграций).
- `Shop.Orders`: таблицы `Orders` (Id, CustomerName, Status, TotalAmount, CreatedAt),
  `OrderItems` (OrderId, ProductName, UnitPrice, Quantity); статус хранится строкой.
- `Shop.Notifications`: `NotificationRecords` (Id, OrderId, CustomerName, Status, Text, OccurredAt).
- Идемпотентность: `OrderId` генерирует `Shop.Web`; при повторной доставке `CreateOrder`
  заказ с таким Id не создаётся заново — безопасные ретраи.

## 8. Веб-интерфейс и дизайн

### 8.1. Стек и режимы

- React 19 + TypeScript + **Vite** (`Shop.Web/clientapp`), Tailwind CSS 4 через плагин
  `@tailwindcss/vite`; клиент SignalR — `@microsoft/signalr`.
- **Dev-режим:** `npm run dev` (Vite на `:5173`, proxy `/api` и `/hubs` на `:5080`, ws-прокси
  для SignalR). Открыть `http://localhost:5173`.
- **Билд:** `npm run build` → `wwwroot/app.js` + `wwwroot/app.css` (имена без хэшей, чтобы
  MVC-вью ссылался на фиксированные пути); открытие `http://localhost:5080` — MVC-вью
  `Home/Index` монтирует React в `#root`.

### 8.2. Макет

- **Шапка (sticky):** логотип «MiniShop», подпись «Kafka × RabbitMQ», справа — пилюля
  статуса SignalR (зелёная «на связи» / красная «нет связи»).
- **Основная сетка** (`lg:grid-cols-[400px,1fr]`):
  - карточка «Новый заказ»: имя клиента, список товаров (иконка, название, цена, степпер
    количества −/+), итоговая сумма, кнопка «Оформить заказ» (градиент);
  - карточка «Заказы»: таблица (№, клиент, позиции, сумма, статус, действия) с кнопками
    «Завершить»/«Отменить» для активных заказов; пустое состояние «Заказов пока нет».
- **Тосты:** правый нижний угол, слайд-анимация, авто-скрытие 6 с, цветная полоса по типу
  (создан/завершён/отменён/ошибка), закрытие вручную.
- **Обратная связь:** spinner на кнопках при запросе, disabled-состояния, живой пересчёт
  суммы, поллинг списка каждые 15 с + мгновенное обновление по SignalR.

### 8.3. Дизайн-система

| Токен | Значение | Использование |
|---|---|---|
| Фон страницы | `slate-100` (#F1F5F9) | `body` |
| Карточки | `white`, `rounded-xl`, `border border-slate-200`, `shadow-sm` | панели |
| Текст | `slate-900` основной / `slate-500` вторичный | везде |
| Акцент | `indigo-600 → violet-600` (градиент) | primary-кнопки, логотип, активные элементы |
| Успех | `emerald-600` | статус `Completed`, уведомление об успехе |
| Опасность | `rose-600` | статус `Cancelled`, ошибки |
| Ожидание | `indigo` | статус `Accepted` |
| Шрифт | системный стек: `ui-sans-serif, Segoe UI, …` | современный нативный рендер на Windows |
| Радиусы/тени | `rounded-lg` для контролов, `rounded-xl` для карточек, мягкие тени | — |

Статус — пилюля с точкой: `Accepted` (indigo-100/indigo-700), `Completed` (emerald),
`Cancelled` (rose). Все интерактивы: `transition-colors`, `focus-visible:ring-2 ring-indigo-500`.

### 8.4. Доступность

Семантические теги (`header`, `main`, `table`/`thead`/`tbody`), `aria-live="polite"` для стека
тостов, `aria-label` на кнопках-иконках, видимый focus-ring, управление формой с клавиатуры.

## 9. REST API `Shop.Web`

| Метод | Путь | Запрос → Успех | Ошибки (ProblemDetails c `extensions.code`, `extensions.reason`) |
|---|---|---|---|
| GET | `/api/catalog` | `Product[]` | — |
| POST | `/api/orders` | 200 `{ orderId, status, totalAmount }` | 400 (`EmptyOrder`/`UnknownProduct`) |
| GET | `/api/orders` | `OrderSummary[]` | — |
| POST | `/api/orders/{id}/complete` | 200 `{ orderId, status }` | 404 (`NotFound`), 409 (`InvalidTransition`) |
| POST | `/api/orders/{id}/cancel` | 200 `{ orderId, status }` | 404, 409 |

## 10. SignalR

- Хаб: `NotificationHub`, путь `/hubs/notifications`.
- Метод сервер→клиент: `NotificationReceived`, payload (camelCase):
  `{ orderId, customerName, status, text, occurredAt }` — `status` строкой ("Accepted" и т.д.).
- Клиент: `HubConnection` с `withAutomaticReconnect`, статус выводится в шапке; при событии —
  тост + refetch списка заказов.

## 11. Надёжность и семантика доставки

- **RabbitMQ:** консьюмеры с экспоненциальным retry (5 попыток, 0.5 с → 30 с); сообщения,
  ушедшие все ретраи, попадают в error-очередь (`{очередь}_error`, штатный механизм MassTransit).
- **Request/response:** таймауты на стороне request-клиентов (`AddRequestClient<T>(30 сек)`).
- **Kafka:** `GroupId` из appsettings; ключ партиции = `OrderId` → порядок событий в рамках
  заказа; авто-создание топика; ретраи чтения — через перебалансировку группы.
- **Идемпотентность:** `OrderId` присваивает Web; повторная доставка `CreateOrder` безопасна.

## 12. Известные упрощения (принято для пет-проекта)

1. Нет транзакционного outbox: событие в Kafka публикуется после ответа клиенту; сбой в этом
   месте теряет событие (в проде — Outbox/EF-транзакция).
2. `EnsureCreated` вместо миграций EF Core.
3. Нет аутентификации (по договорённости; добавить JWT — отдельный шаг).
4. `guest/guest` RabbitMQ (работает только с localhost — приемлемо для локальной разработки).
5. Single-instance сервисов, нет health-checks/OpenTelemetry.
6. Каталог товаров — статический класс в контрактах (нет отдельного каталога-сервиса).

## 13. Запуск (Windows 10/11, PowerShell)

```powershell
# 1. Инфраструктура (Docker Desktop должен быть запущен)
docker compose up -d

# 2. Клиент React (один раз)
cd Shop.Web\clientapp
npm install
npm run build          # сборка в wwwroot; либо npm run dev для hot reload на :5173
# Из корня репозитория: npm --prefix Shop.Web/clientapp run build

# 3. Приложения (Visual Studio 2026)
# Открыть PetKafkaRabbitMQApp.slnx (формат решений VS 2026) → в свойствах решения
# «Multiple startup projects» → Shop.Web, Shop.Orders, Shop.Notifications → F5.
# Альтернатива в трёх терминалах:
#   dotnet run --project Shop.Orders
#   dotnet run --project Shop.Notifications
#   dotnet run --project Shop.Web

# 4. Открыть http://localhost:5080 (или :5173 в dev-режиме Vite)
```

Проверка: создать заказ → в шапке зелёный индикатор SignalR → через секунды появляется тост
«Заказ … создан» (путь Web → RabbitMQ → Orders → Kafka → Notifications → RabbitMQ → Web).

## 14. Структура решения

```
PetKafkaRabbitMQApp.slnx
├── docker-compose.yml / spec.md / dev_notes.md
├── Shop.Contracts/                Catalog.cs, Orders.cs, MessagingOptions.cs
├── Shop.Orders/                   Program.cs, appsettings.json,
│                                  Data/ (OrdersDbContext, Order), Consumers/ (Create, ChangeStatus, GetOrders)
├── Shop.Notifications/            Program.cs, appsettings.json,
│                                  Data/ (NotificationsDbContext, NotificationRecord), Consumers/OrderEventConsumer
└── Shop.Web/
    ├── Program.cs, appsettings.json, Controllers/, Hubs/, Consumers/, Views/
    ├── wwwroot/                   артефакты сборки React (app.js, app.css)
    └── clientapp/                 package.json, vite.config.ts, tsconfig.json,
                                   index.html, src/ (main, App, api, signalr, components/)
```

## 15. Идеи развития

Транзакционный outbox · сага «заказ → оплата → доставка» (MassTransit Saga) · вторая
консьюмер-группа Kafka для аудита · OpenTelemetry + Jaeger · xUnit-тесты консьюмеров ·
health-checks UI · ключи вместо guest/guest · кластеризация Kafka.
