# Спецификация: MiniShop — пет-проект Kafka + RabbitMQ

**Репозиторий:** `PetKafkaRabbitMQApp` · **Версия:** 1.1 · **Дата:** 2026-10-20

## 1. Назначение

Пет-проект для отработки интеграции ASP.NET Core с брокерами сообщений **RabbitMQ** и
**Apache Kafka** через библиотеку **MassTransit**. Домен — витрина «MiniShop»: приём заказов
из веб-интерфейса и уведомления об их жизненном цикле.

Обучающие цели:

- решение Visual Studio 2026: **2 микросервиса** (`Shop.Orders`, `Shop.Notifications`) +
  веб-приложение **ASP.NET Core MVC + React** (`Shop.Web`) + общие контракты (`Shop.Contracts`)
  и отдельная библиотека observability (`Shop.Observability`);
- MassTransit: команды и request/response через RabbitMQ, событийный поток через Kafka (rider);
- весь код работы с I/O — асинхронный (`async/await`): контроллеры, консьюмеры, продюсеры, EF Core;
- современный UI: React 19 + TypeScript + Vite + Tailwind CSS 4, обновления в реальном времени (SignalR).

Решение сознательно компактное: 6 проектов, SQLite без внешней БД. Страницы наблюдения брокеров — локальная
Development-функция: read-only снимки, чтение Kafka по запросу и best-effort журнал событий; это не средство
управления брокерами и не гарантия доставки или обработки.

## 2. Стек

| Слой | Технология |
|---|---|
| IDE / среда | Visual Studio 2026, .NET 10 (C# 14), Windows 10/11, Docker Desktop |
| Веб | ASP.NET Core MVC + SignalR; React 19 + TypeScript + Vite; Tailwind CSS 4 |
| Микросервисы | ASP.NET Core Worker Host (консольные хосты) |
| Шина | MassTransit **8.5.11** (Apache-2.0): `MassTransit.RabbitMQ` + `MassTransit.Kafka` |
| Наблюдаемость | `Shop.Observability`: MassTransit send/publish/consume/receive observers, санитайзер payload и bounded sink; `Confluent.Kafka` 2.15.1 только для read-only снимков и чтения Kafka |
| Тесты | xUnit 2.9 + Moq (`Shop.Tests`); Vitest + React Testing Library (`clientapp`) |
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
| `Shop.Web` | ASP.NET Core (MVC) | UI: MVC-вью, хостинг SPA, REST API, SignalR-хабы; MassTransit: request-клиенты и консьюмер `NotificationSent`; локальный журнал диагностики и read-only мониторинг брокеров |
| `Shop.Orders` | Worker-хост | Домен заказов: консьюмеры `CreateOrder`/`ChangeOrderStatus`/`GetOrders`, SQLite, продюсер событий в Kafka; диагностические observers |
| `Shop.Notifications` | Worker-хост | Консьюмер Kafka-топика `orders-events`, генерация уведомлений, публикация `NotificationSent` в RabbitMQ; диагностические observers |
| `Shop.Contracts` | ClassLib | Контракты сообщений (records), статический каталог товаров, классы опций конфигурации брокеров |
| `Shop.Observability` | ClassLib | Общие MassTransit-обозреватели, маскирование и ограничение диагностических событий, bounded sink и HTTP-экспортёр для worker-процессов |
| `Shop.Tests` | xUnit | Unit-тесты контроллеров, журнала/снимков мониторинга и санитайзера |

### Диагностический контур (не входит в бизнес-поток)

```
Shop.Orders / Shop.Notifications ──(HTTP POST /internal/diagnostics/events)──► Shop.Web
     observers + sanitizer + bounded buffer                         DiagnosticJournal (memory)
                                                                           │
                                                                  SignalR /hubs/diagnostics
                                                                           ▼
                                                         /brokers/rabbitmq, /kafka, /flow
```

Наблюдение опционально, включается только в Development и не должно влиять на обработку
сообщений: ошибки диагностики изолированы, а потери диагностических событий допустимы.

### Ключевое решение: роли брокеров

- **RabbitMQ — «командная шина».** Точечные команды (`CreateOrder`, `ChangeOrderStatus`),
  request/response (`GetOrders`), fanout-уведомления (`NotificationSent`). Очереди, retry,
  error-очереди (DLQ). Быстрая доставка небольшого числа сообщений конкретному получателю.
- **Kafka — «событийный поток».** Append-only топик `orders-events`, сообщение `OrderEvent` —
  факт изменения заказа. Ключ партиции = `OrderId` (упорядоченность в рамках заказа),
  консьюмер-группа `notifications`; offsets и позиция чтения принадлежат Kafka-группе.

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

Автосоздание топиков включено (`KAFKA_AUTO_CREATE_TOPICS_ENABLE=true`; `KAFKA_NUM_PARTITIONS=3`),
а `Shop.Notifications` конфигурирует `CreateIfMissing` для `orders-events` с 3 партициями и
replication factor 1. Это создаёт топик, если его нет; уже существующий топик с другим
числом партиций не перестраивается.

### 6.3. Порты приложений

| Приложение | URL |
|---|---|
| `Shop.Web` (Kestrel) | `http://localhost:5080`, `https://localhost:7080` |
| Vite dev-сервер (hot reload) | `http://localhost:5173`; в Development разрешён Origin также `http://127.0.0.1:5173` |
| RabbitMQ Management | `http://localhost:15672` |
| Kafka UI | `http://localhost:8080` |

### 6.4. Мониторинг брокеров (`BrokerMonitoring`)

Секция читается всеми тремя процессами. В поставляемых `appsettings.json` флаг `Enabled` выключен,
а `appsettings.Development.json` каждого сервиса включает его; при отсутствии Development-файла
мониторинг выключен. Работает только при `Development` **и** `BrokerMonitoring:Enabled=true`.

**`Shop.Web`** (сборщик, страницы и локальный журнал):

```json
"BrokerMonitoring": {
  "Enabled": true,
  "RabbitMqManagementUrl": "http://localhost:15672",
  "KafkaUiUrl": "http://localhost:8080",
  "AllowedOrigins": ["http://localhost:5173", "http://127.0.0.1:5173"]
}
```

**`Shop.Orders` / `Shop.Notifications`** (экспортёры событий):

```json
"BrokerMonitoring": {
  "Enabled": true,
  "CollectorUrl": "http://localhost:5080/internal/diagnostics/events"
}
```

`CollectorUrl` можно не задавать — используется указанное значение по умолчанию. Ключ
`IngestionKey` не задан ни в одном `appsettings.json`: он передаётся через серверную конфигурацию
или переменные окружения и должен совпадать во всех трёх процессах.

| Параметр | По умолчанию | Назначение |
|---|---|---|
| `BrokerMonitoring:Enabled` | `false` | Единственный opt-in-переключатель диагностики (только Development) |
| `BrokerMonitoring:CollectorUrl` | `http://localhost:5080/internal/diagnostics/events` | HTTP(S)-приёмник диагностики для воркеров |
| `BrokerMonitoring:IngestionKey` | — | Общий ключ приёма (`X-Diagnostics-Key`); без него ingestion возвращает 401, воркеры не отправляют события |
| `BrokerMonitoring:RabbitMqManagementUrl` | `http://localhost:15672` | Loopback URL Management API для снимка RabbitMQ |
| `BrokerMonitoring:KafkaUiUrl` | `http://localhost:8080` | Loopback ссылка на Kafka UI в интерфейсе |
| `BrokerMonitoring:RabbitMqManagementUsername` / `Password` | — (fallback на `MessageBus:RabbitMq:Username` / `Password`) | Серверная учётка Management API, если она отличается от AMQP |
| `BrokerMonitoring:AllowedOrigins` | `[]` | Дополнительные loopback-origins для браузерных запросов; same-origin принимается автоматически |

Все значения переопределяются стандартной конфигурацией .NET, например
`BrokerMonitoring__IngestionKey`, `BrokerMonitoring__CollectorUrl`, `BrokerMonitoring__Enabled=false`.
Ключ не хранится в репозитории и не попадает в UI; в документации используется только заглушка,
а не реальный секрет.

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

### 8.2. Экраны и навигация

Общий header содержит навигацию: «Заказы» (`/`), «RabbitMQ», «Kafka», «Путь сообщения».
Главная — форма создания заказа и таблица заказов; список обновляется опросом раз в 15 секунд
и после SignalR-уведомления.

- `/brokers/rabbitmq` — очереди, exchanges, bindings, consumers; счётчики сообщений и rates
  (при отсутствии метрик показывается «—»).
- `/brokers/kafka` — partitions, low/high/committed offsets и lag; отдельная форма читает
  до 50 записей выбранной partition с указанного offset.
- `/brokers/flow` — статическая схема этапов (не индикатор успешности этапов) и живой журнал.
  Все страницы мониторинга содержат журнал с фильтрами по сервису, операции, фазе,
  типу сообщения и orderId.

Прямая загрузка `/brokers/*` обслуживается MVC-вью, которая монтирует ту же React-SPA;
маршрутизацию выполняет React Router.

### 8.3. Дизайн-система

| Токен | Значение | Использование |
|---|---|---|
| Фон страницы | `zinc-950` (#09090B) | `body` |
| Панели | `zinc-900/40`, `border-zinc-800` | карточки и таблицы |
| Текст | `zinc-50` основной / `zinc-400` вторичный | везде |
| Акцент | `orange-500` | активная навигация, primary-кнопки, фокус |
| Успех | `emerald-400` | `Completed`, успешные уведомления |
| Опасность | `rose-400/500` | `Cancelled`, ошибки |
| Шрифт | системный стек: `ui-sans-serif, Segoe UI, …` | нативный рендер на Windows |
| Радиусы/тени | `rounded-none` у карточек/кнопок, без декоративных теней | строгий Swiss Style / dark minimal |

Статус — бейдж с точкой; активная навигация и фокус — `outline-orange-400`. Мелкие служебные
элементы (таблицы мониторинга, ссылки на RabbitMQ Management и Kafka UI) выдержаны в том же
тёмном стиле.

### 8.4. Доступность

Семантические теги (`header`, `main`, `table`/`thead`/`tbody`), `aria-live="polite"` для стека
тостов, `aria-label` на кнопках, видимый focus-ring, управление формами и таблицами с клавиатуры.

## 9. REST API `Shop.Web`

| Метод | Путь | Запрос → Успех | Ошибки (ProblemDetails c `extensions.code`, `extensions.reason`) |
|---|---|---|---|
| GET | `/api/catalog` | `Product[]` | — |
| POST | `/api/orders` | 200 `{ orderId, status, totalAmount }` | 400 (`EmptyOrder`/`UnknownProduct`) |
| GET | `/api/orders` | `OrderSummary[]` | — |
| POST | `/api/orders/{id}/complete` | 200 `{ orderId, status }` | 404 (`NotFound`), 409 (`InvalidTransition`) |
| POST | `/api/orders/{id}/cancel` | 200 `{ orderId, status }` | 404, 409 |

## 10. SignalR

- Бизнес-хаб `NotificationHub`, путь `/hubs/notifications`; метод сервер→клиент
  `NotificationReceived`, payload (camelCase): `{ orderId, customerName, status, text, occurredAt }`;
  `status` сериализуется строкой.
- Отдельный хаб `DiagnosticsHub`, путь `/hubs/diagnostics` (только при включённом мониторинге),
  шлёт `DiagnosticsChanged`. Клиент с автоматическим reconnect использует его как сигнал
  обновить журнал, а polling каждые 5 секунд — как резервный механизм.
- Header показывает состояние только подключения бизнес-хаба уведомлений, а не здоровье брокеров.
  При бизнес-событии UI показывает toast и обновляет список заказов.

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
7. Диагностический журнал — in-memory (2000 событий / 30 минут) без персистенции: после рестарта
   Web события теряются, при переполнении появляются отметки о пропуске и потере.
8. Доступ к мониторингу — Development + loopback (+ общий `IngestionKey` для приёма от воркеров),
   без отдельной аутентификации; в Production и при выключенном флаге endpoints отвечают 404.
9. Просмотр сырых Kafka-сообщений может содержать `CustomerName` и другие персональные данные —
   только для локального обучения, не для production.
10. Мониторинг наблюдательный, а не гарантия доставки: снимки могут быть устаревшими, lag не
    доказывает обработку, потеря диагностических событий не влияет на бизнес-поток.

## 13. Мониторинг брокеров и живой журнал (read-only)

Опциональный диагностический контур: состояние RabbitMQ/Kafka и путь сообщений. Он не меняет
бизнес-поток, топологию и offsets консьюмеров и ничего не гарантирует: снимки могут быть
устаревшими, часть диагностических событий может теряться.

### 13.1. Условия работы и защита

- Работает только при `Development` **и** `BrokerMonitoring:Enabled=true`; иначе observers не
  регистрируются, а `/brokers/*`, `/api/diagnostics/*`, `/hubs/diagnostics` и
  `/internal/diagnostics/events` отвечают **404**. Прямой запрос отключённого `/brokers/*` —
  обычный 404 без SPA; notice «Мониторинг выключен» показывается только при клиентском переходе
  внутри SPA (страница получает 404 от `/api/diagnostics/config`).
- Запросы мониторинга допускаются только с loopback: фактический remote IP — `127.0.0.1`/`::1`,
  Host — `localhost` или loopback-IP; `Origin`, если есть, должен быть same-origin либо входить
  в `BrokerMonitoring:AllowedOrigins`; запросы к ingestion с заголовком `Origin` отклоняются.
  Ответы получают `Cache-Control: no-store` и `X-Content-Type-Options: nosniff`.
- Приём от воркеров: `POST /internal/diagnostics/events` с `X-Diagnostics-Key`; ключ сравнивается
  с `BrokerMonitoring:IngestionKey` fixed-time сравнением SHA-256 до чтения тела. Без ключа
  приём отвечает 401; воркеры без ключа не отправляют события и пишут warning в лог. Ключ
  задаётся только через конфигурацию/окружение и не хранится в репозитории или UI.

### 13.2. Сбор событий (`Shop.Observability`)

- Обозреватели MassTransit 8.5.11: `ISendObserver`, `IPublishObserver`, `IConsumeObserver`,
  `IReceiveObserver`; операции `send|publish|receive|consume|produce`, фазы
  `started|completed|faulted`, длительность, `MessageId`/`RequestId`/`CorrelationId`/
  `ConversationId`, `OrderId`, partition/offset Kafka, санитизированный адрес.
- Kafka-продюсер наблюдается через `KafkaSendContext` (операция `produce`), отдельного wrapper'а
  нет. `receive` — транспортный уровень, `consume` — уровень консьюмера; `receive completed`
  не означает закоммиченный offset.
- Payload — best-effort JSON ≤ 8192 байт. Поля `CustomerName`, `Text`, `Reason`, `Message`,
  `StackTrace`, `ExceptionMessage` маскируются как `[REDACTED]`; слишком большой payload
  не передаётся (`payloadTruncated=true`). Заголовки — только allowlist технических полей
  (`MT-MessageId`, `MT-RequestId`, `MT-CorrelationId`, `MT-ConversationId`, `traceparent`);
  из адреса удаляются credentials/query; ошибка — только тип исключения, без текста и стека.
- Sink воркеров — bounded in-memory очередь (2048), запись неблокирующая: при переполнении
  отбрасывается новое событие и растёт счётчик потерь. Экспортёр отправляет батчи ≤100 событий /
  ≤256 КиБ на `CollectorUrl`, таймаут 3 с, без редиректов и без повторов; неудачный батч
  считается потерянным. В `Shop.Web` sink заменён на локальный журнал, Web не экспортирует
  события по HTTP.

### 13.3. Живой журнал `Shop.Web`

- `DiagnosticJournal` — потокобезопасный in-memory буфер: **2000 событий или 30 минут** (что
  наступит раньше), монотонный `sequence`, дедупликация по `EventId`, валидация полей и лимитов.
- Чтение: `GET /api/diagnostics/events?after=<cursor>&broker=rabbitmq|kafka&orderId=<GUID>`;
  ответ `{ events:[{sequence,event}], nextCursor, oldestCursor, gap, droppedEvents }`.
  `gap=true` означает, что часть событий вытеснена (или курсор новее сервера);
  `droppedEvents` — накопленные потери от воркеров/выключенного экспорта.
- Ingestion: тело ≤1 МиБ, ≤100 событий, `DroppedEvents` — кумулятивный счётчик источника;
  `Service:InstanceId` должен быть согласован внутри батча.
- UI получает `DiagnosticsChanged` из `/hubs/diagnostics` (коалесценция ~500 мс) и догружает
  журнал по курсору; без хаба — опрос каждые 5 с. В браузере хранится не более 2000 событий;
  доступны пауза ленты, фильтры (сервис/операция/фаза/тип) и фильтр по `orderId`.
  Пауза останавливает только запросы ленты в клиенте, а не брокеры.
- Журнал — best-effort наблюдение MassTransit callbacks, а не полный и не гарантированно точный
  wire capture; потеря или дубли событий диагностики не влияют на бизнес-поток.

### 13.4. Снимок RabbitMQ (read-only)

- Фоновый опрос каждые 3 с (таймаут запроса 2 с, опроса — 8 с); UI обновляет снимок каждые 10 с.
- Используются только `GET /api/queues|exchanges|bindings|consumers/{vhost}` (vhost URL-encoded,
  ответ ≤2 МиБ и ≤10 000 элементов). Peek/consume не выполняется: вызов
  `queues/{vhost}/{queue}/get` не используется, поэтому снимок не удаляет и не блокирует сообщения.
- Учётка Management API: `BrokerMonitoring:RabbitMqManagementUsername/Password`, иначе fallback
  на `MessageBus:RabbitMq:Username/Password`.
- Показатели очередей: messages, ready, unacknowledged, consumers, publish/deliver/ack rates.
  Rate берётся из `message_stats.*_details.rate`; если метрик нет — `null`, в UI «—».
- RabbitMQ не хранит доставленные сообщения: после ack сообщение удаляется, «подтверждённых»
  сообщений для просмотра нет. Страница показывает метрики и журнал приложения, а не тела очередей.
- При ошибке/таймауте снимок помечается `available=false`, сохраняются последние данные
  (`lastSuccessAt`) и текст ошибки; UI предупреждает об устаревании.

### 13.5. Снимок Kafka

- Метаданные — `AdminClient.GetMetadata` (auto-create topics выключен, топик должен существовать;
  >128 партиций отклоняется). Для `MessageBus:Kafka:GroupId` (по умолчанию `notifications`)
  читаются committed offsets, low/high — через `QueryWatermarkOffsets`. Lag = high − committed,
  при отсутствии committed — `null`.
- Снимок не подписывается на топик и не входит в consumer group, поэтому не вызывает ребалансировку
  и не меняет offsets. Таймауты: 1 с на отдельный Kafka-запрос, ограниченный цикл чтения
  watermarks ~3 с после metadata+committed; общий бюджет фонового опроса — 8 с.
- Lag — запаздывание, а не потеря: commit может быть отложенным, поэтому lag временно растёт и не
  означает, что сообщения не будут обработаны. Порядок существует только внутри partition.

### 13.6. Чтение сообщений Kafka (read-only)

- `GET /api/diagnostics/kafka/messages?partition=0&offset=0&limit=50`, `limit` 1–50, partition —
  только из последнего успешного снимка.
- Каждое чтение создаёт отдельного консьюмера с уникальным `GroupId`, делает ручной
  `Assign(partition, offset)` и **никогда** не вызывает `Subscribe`, `Commit`, `StoreOffset`
  или `Close` (только `Dispose`): membership группы, её committed offsets и другие консьюмеры
  не затрагиваются. Ограничения: 2 с, 200 просканированных записей, 256 КиБ, key ≤1024 Б,
  value ≤8192 Б, ≤32 заголовков из allowlist; при превышении — `truncated=true`.
- Ответ: `available`, `error`, `topic`, `partition`, `lowOffset`, `highOffset`, `nextOffset`,
  `messages[]`, `truncated`. `nextOffset` — позиция после последней прочитанной записи.
- Значения выводятся как получены: **сырые Kafka-сообщения могут содержать `CustomerName` и другие
  персональные данные**. Просмотр предназначен только для локального обучения, не для production.

### 13.7. HTTP API диагностики `Shop.Web`

| Метод | Путь | Назначение | Примечания |
|---|---|---|---|
| GET | `/api/diagnostics/config` | конфигурация для страниц | `enabled`, `rabbitMqManagementUrl`, `kafkaUiUrl`; только loopback URL, иначе `null` |
| GET | `/api/diagnostics/rabbitmq/snapshot` | снимок RabbitMQ | метаданные, не тела сообщений |
| GET | `/api/diagnostics/kafka/snapshot` | снимок Kafka | partitions/offsets/lag |
| GET | `/api/diagnostics/kafka/messages` | чтение записей | ручной Assign, без commit |
| GET | `/api/diagnostics/events` | журнал | `after`, `broker`, `orderId` |
| POST | `/internal/diagnostics/events` | приём от воркеров | `X-Diagnostics-Key`, ≤1 МиБ, ≤100 событий |

### 13.8. Страницы

- `/brokers/rabbitmq` — очереди/exchanges/bindings/consumers и журнал RabbitMQ;
- `/brokers/kafka` — partitions/offsets/lag, чтение сообщений по offset и журнал Kafka;
- `/brokers/flow` — статическая схема пути сообщения (не индикация успеха) и общий журнал.
  Шаги схемы: Shop.Web: запрос → RabbitMQ: команда/ответ → Shop.Orders: заказ → Kafka: событие
  статуса → Shop.Notifications → RabbitMQ: NotificationSent → Shop.Web: обработка уведомления →
  SignalR: браузер.
- Индикатор SignalR в header относится только к каналу уведомлений, а не к доступности брокеров;
  доступность каждого брокера показывается отдельно на его странице.

## 14. Запуск (Windows 10/11, PowerShell)

```powershell
# 1. Инфраструктура (Docker Desktop должен быть запущен)
docker compose up -d

# 2. Клиент React (из корня репозитория)
npm --prefix Shop.Web/clientapp install
npm --prefix Shop.Web/clientapp run build  # app.js/app.css в Shop.Web/wwwroot
# hot reload вместо сборки: npm --prefix Shop.Web/clientapp run dev (Vite :5173)

# 3. В каждом из трёх терминалов задайте ОДИН И ТОТ ЖЕ локальный ключ
#    (пример-заглушка; подставьте свою случайную строку, не коммитьте её).
$env:BrokerMonitoring__IngestionKey='<одинаковый случайный локальный ключ>'
# Терминал 1:
dotnet run --project Shop.Orders
# Терминал 2:
dotnet run --project Shop.Notifications
# Терминал 3:
dotnet run --project Shop.Web
```

`dotnet run` наследует Development из launch profiles: Web — `ASPNETCORE_ENVIRONMENT`,
воркеры — `DOTNET_ENVIRONMENT`; вместе с `appsettings.Development.json` это включает
`BrokerMonitoring:Enabled=true`. Ключ — произвольная локальная строка, одинаковая во всех трёх
процессах; реальные секреты сюда не помещайте. Открыть `http://localhost:5080` (готовый UI)
или `http://localhost:5173` (Vite dev-сервер).

Без `BrokerMonitoring__IngestionKey` Web сохраняет локальный журнал и снимки, а воркеры
выключают HTTP delivery с warning в логе: в журнале будут только события самого Web.

Поток заказа описан в §4; индикатор в шапке означает подключение SignalR уведомлений и не
подтверждает доступность обоих брокеров. Страницы мониторинга — read-only диагностика,
а не проверка end-to-end гарантий.

## 15. Структура решения

```
PetKafkaRabbitMQApp.slnx
├── docker-compose.yml / README.md / spec.md / dev_notes.md
├── Shop.Contracts/                контракты сообщений и конфигурационные типы
├── Shop.Observability/            MassTransit observers, sanitizer, bounded sink, HTTP exporter
├── Shop.Orders/                   Worker, SQLite orders.db, Rabbit consumers, Kafka producer
├── Shop.Notifications/            Worker, SQLite notifications.db, Kafka consumer, Rabbit publish
├── Shop.Tests/                    xUnit-тесты backend и диагностического контура
└── Shop.Web/
    ├── Controllers/, Diagnostics/, Hubs/, Consumers/, Views/
    ├── wwwroot/                   app.js/app.css — артефакты сборки React
    └── clientapp/                 React 19, TypeScript, Vite, Tailwind 4, Vitest,
                                   src/ (main, App, api, signalr, components/, diagnostics/)
```

## 16. Идеи развития

Транзакционный outbox · сага «заказ → оплата → доставка» (MassTransit Saga) · вторая
консьюмер-группа Kafka для аудита · OpenTelemetry + Jaeger · xUnit-тесты консьюмеров ·
health-checks UI · ключи вместо guest/guest · кластеризация Kafka.
