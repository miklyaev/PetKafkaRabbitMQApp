# Dev Notes

Краткий журнал ключевых изменений проекта PetKafkaRabbitMQApp (пет-проект «MiniShop»).

## Журнал изменений

<!-- Новые записи добавлять сверху -->

### 2026-10-20 — Read-only мониторинг RabbitMQ/Kafka и живой журнал обмена

**Область:** Architecture | Backend | Frontend | Infra | Testing

**Что изменилось:**
- Новый проект `Shop.Observability`: MassTransit observers (`send/publish/consume/receive`, Kafka produce через `KafkaSendContext`), санитайзер (маскирование `CustomerName`/`Text`/`Reason`/`Message`/stack, allowlist headers, payload ≤8 KiB, адрес без credentials) и bounded sink (2048, неблокирующая запись, отбрасывание нового при переполнении). Worker-экспортёр шлёт батчи ≤100 событий / ≤256 KiB без retry и redirect. Ошибки диагностики изолированы от бизнес-обработки.
- Наблюдение opt-in: только `Development` + `BrokerMonitoring:Enabled=true`. `appsettings.Development.json` всех трёх сервисов включает флаг (в Web ещё `AllowedOrigins` для Vite `localhost:5173`/`127.0.0.1:5173`); вне dev или при `false` observers не регистрируются, а диагностические endpoints отвечают 404.
- `Shop.Orders` и `Shop.Notifications` отправляют события на `BrokerMonitoring:CollectorUrl` (по умолчанию `http://localhost:5080/internal/diagnostics/events`) с `X-Diagnostics-Key`; общий `BrokerMonitoring:IngestionKey` задаётся только серверной конфигурацией/окружением (`BrokerMonitoring__IngestionKey`) и не попадает в репозиторий. Без ключа Web сохраняет локальный журнал и снимки, а worker-delivery выключается с warning в логе.
- `Shop.Web`: `DiagnosticJournal` (2000 событий / 30 минут, cursor, dedupe по `EventId`, gap и dropped), ingestion endpoint `/internal/diagnostics/events` (fixed-time проверка ключа, ≤1 MiB, ≤100 событий), `DiagnosticsHub` `/hubs/diagnostics` с `DiagnosticsChanged`, локальный sink Web. Страницы, API чтения и хаб защищены loopback + same-origin/`AllowedOrigins` guard; ingestion требует ключ и не принимает браузерный `Origin`. Добавлены read-only снимки:
  - RabbitMQ — management GET queues/exchanges/bindings/consumers (vhost encoded), без peek/consume; `RabbitMqManagementUsername/Password` опциональны с fallback на AMQP-учётку; rate nullable → в UI «—»; при сбое показываются последние данные и `available=false`;
  - Kafka — metadata, committed offsets и watermarks без входа в consumer group; lag = high − committed, отложенный commit не означает потерю, offset упорядочен только внутри partition;
  - Kafka messages — ручной `Assign`, уникальный временный group id, без `Subscribe`/`Commit`/`StoreOffset`; лимиты 50 записей, 2 с, 256 KiB, value 8 KiB.
- Frontend: маршруты `/brokers/rabbitmq`, `/brokers/kafka`, `/brokers/flow`, навигация в header, таблицы снимков и журнал с фильтрами, паузой, SignalR-обновлением и резервным polling раз в 5 с. Сырые Kafka-значения помечены как потенциально содержащие персональные данные; индикатор в шапке относится только к каналу уведомлений, не к здоровью брокеров.
- Документация: README теперь содержит фактическую инструкцию запуска (Docker → `npm build` → ключ в трёх терминалах → три `dotnet run`, готовые страницы); `spec.md` обновлён (6 проектов, config/API/semantics мониторинга), устаревшее «1 партиция» исправлено на 3.

**Проверки (фактические результаты):**
- Release solution build — PASS, 0 warnings/0 errors; backend — 51/51, включая native Kafka config regression; React `tsc` + `vite build` — PASS; Vitest — 10 файлов, 40/40.
- Runtime в изолированных vhost/topic и отдельной БД: create+complete прошли всю цепочку со всеми тремя сервисами; журнал маскирует raw customer, dropped = 0; Kafka raw read вернул 2 события, offsets до/после не изменились; HTTP с evil Origin — 404, Vite — 200, missing ingestion key — 401, invalid limit — 400.
- Runtime в Production (порт 5092) при `Enabled=true`: `/api/diagnostics/config`, `/brokers/*` и `/hubs/diagnostics` — 404, `/api/catalog` — 200 (Development-guard работает).
- Browser QA: прямые URL, навигация, фильтры, пауза, reconnect, JSON-детали и mobile-страницы брокеров проверены; ошибок консоли нет. Исправлено прежнее переполнение страницы заказов на mobile: явная `grid-cols-1`, корректная desktop-сетка. Проверка `dotnet format whitespace` новых C#-файлов пройдена.

**Ограничения:**
- Журнал volatile: рестарт Web теряет события, переполнение даёт gap/dropped; payload — callback-представление, а не полный wire envelope.
- Мониторинг доступен только в Development + loopback; отдельной аутентификации нет, кроме общего ingestion key; в Production endpoints отвечают 404.
- Raw Kafka view может содержать `CustomerName` и другие персональные данные; RabbitMQ не даёт безопасного peek, поэтому тела очередей не показываются.
- Снимки могут быть устаревшими; lag не доказывает обработку или потерю; диагностика lossy и не меняет бизнес-гарантии (outbox-риск остаётся прежним).

**Затронутые пути:**
- `Shop.Observability/` (новый проект), `Shop.Orders/Program.cs`, `Shop.Notifications/Program.cs`, `Shop.Tests/`
- `Shop.Web/Program.cs`, `Shop.Web/Controllers/DiagnosticsController.cs`, `Shop.Web/Diagnostics/`
- `Shop.Web/appsettings.json`, `Shop.Web/appsettings.Development.json`, dev-settings воркеров
- `Shop.Web/clientapp/src/App.tsx`, `src/components/Header.tsx`, `src/diagnostics/`, `src/index.css`
- `README.md`, `spec.md`, `dev_notes.md`

**Зачем / контекст:** Пет-проекту нужны наглядные read-only страницы RabbitMQ/Kafka и живой журнал пути сообщения, при этом диагностика не должна влиять на доставку и бизнес-гарантии. Проверки подтвердили маскирование, неизменность Kafka offsets при чтении и loopback/key-защиту.

### 2026-10-16 — Обновление дизайна UI (Swiss Style & Dark Mode Minimal)

**Область:** Frontend | UI/UX

**Что изменилось:**
- Внедрен строгий минимализм в стиле "Swiss Style" / Dark Mode Minimal.
- Основной фон изменен на глубокий графитовый (`bg-zinc-950`), текст на контрастный белый (`text-zinc-50`).
- Убраны скругления (используется `rounded-none`) и стандартные тени.
- Цвет акцентов (кнопки, активные элементы) изменен на терракотовый/ярко-оранжевый (`bg-orange-500`).
- В шапке (`Header`) логотип сделан в виде брутального квадрата с кинетическим hover-эффектом (наклон, масштабирование). Иконка корзины заменена на строгий текст.
- `OrderForm`: форма оформления лишилась теней и скруглений. Кнопки счетчиков товаров стали строгими квадратами. Инпуты получили темный фон (`bg-zinc-900`) с оранжевым фокусом.
- `OrderList`: переработана таблица заказов. Добавлены тонкие разделители (`divide-zinc-800`), hover-состояния строк (`hover:bg-zinc-800/50`). Кнопки действий (Отменить / Завершить) стали появляться по наведению.
- `StatusBadge`: бейджи переведены в стилистику темной темы (полупрозрачные фоны `bg-orange-500/10` и рамки `border-zinc-700`).

### 2026-10-16 — Расширение ассортимента магазина

**Область:** Backend | Data

**Что изменилось:**
- В `Shop.Contracts/Catalog.cs` добавлены 5 новых современных товаров, подходящих по смыслу к существующей электронике: "Умная колонка", "Монитор 27' 4K", "Эргономичное кресло", "Коврик для мыши XXL", "Внешний аккумулятор". Бэкенд-логика не менялась.

**Затронутые пути:**
- `Shop.Web/clientapp/src/index.css`
- `Shop.Web/clientapp/src/App.tsx`
- `Shop.Web/clientapp/src/components/Header.tsx`
- `Shop.Web/clientapp/src/components/OrderForm.tsx`
- `Shop.Web/clientapp/src/components/OrderList.tsx`
- `Shop.Web/clientapp/src/components/StatusBadge.tsx`
- `Shop.Contracts/Catalog.cs`

**Зачем / контекст:** Редизайн для портфолио с целью показать владение современными трендами (брутализм, кинетическая типографика, минимализм). Расширение каталога для большего визуального разнообразия UI.

### 2026-10-16 — Покрытие OrdersController тестами

**Область:** Backend | Testing

**Что изменилось:**
- В `Shop.Tests` добавлены unit-тесты `OrdersController`: валидация имени клиента, создание заказа и преобразование позиций, успешный список заказов, завершение/отмена и маппинг отказов в HTTP-коды/ProblemDetails.
- Подключён Moq для изоляции MassTransit `IRequestClient` без реального RabbitMQ.
- Проверено, что `GET /api/catalog` возвращает HTTP 200 и корректный JSON-массив из 5 товаров.
- Устаревшее упоминание `/api/data` удалено из команд тестирования `.claude` и `.kodik`; проверка направлена на существующий `GET /api/catalog`.

**Затронутые пути:**
- `Shop.Tests/OrdersControllerTests.cs`, `Shop.Tests/Shop.Tests.csproj`
- `dev_notes.md`

**Зачем / контекст:** До этого в backend-проекте был тест только для каталога. Устаревшая инструкция тестирования ссылалась на отсутствующий маршрут; теперь она проверяет действующий API каталога.

### 2026-10-15 — Исправление сборки Shop.Web из корня репозитория

**Область:** Infra | Frontend

**Что изменилось:**
- Исправлен повреждённый `Shop.Web/clientapp/tsconfig.json`: восстановлен валидный конфиг TypeScript с ES2022 и `noEmit`.
- Удалён ранее сгенерированный TypeScript-компилятором JavaScript рядом с исходниками `.ts/.tsx`; такие файлы добавлены в `.gitignore`.
- Исправлены пути артефактов сборки в `.gitignore` и инструкции запуска/структура проекта в `spec.md` после переноса проектов из `src/` в корень.
- Проверка: клиент собрался через `npm --prefix Shop.Web/clientapp run build`, 28 тестов прошли; решение собрано в Debug и Release.

**Затронутые пути:**
- `Shop.Web/clientapp/tsconfig.json`, `Shop.Web/clientapp/src/`, `Shop.Web/clientapp/vite.config.js`
- `.gitignore`, `spec.md`

**Зачем / контекст:** .NET-проекты собирались успешно, но повреждённый tsconfig и устаревшие исходные пути мешали собирать React-клиент и пользоваться инструкциями проекта.

### 2026-10-14 — Юнит-тесты фронтенда (Vitest + React Testing Library) и комментарии в Program.cs

**Область:** Frontend | Testing

**Что изменилось:**
- Подключён тестовый стек фронтенда: **Vitest 5** (Vite-аналог Jest — тот же API `describe/it/expect`) + **React Testing Library** + `@testing-library/user-event` + `@testing-library/jest-dom` + `jsdom`. 78 пакетов, 0 уязвимостей.
- **28 юнит-тестов, 8 файлов**: покрыты все компоненты `clientapp/src` — `OrderForm` (валидация, клэмп `MAX_QTY=99`, пересчёт итога, ошибка `onSubmit` без потери ввода), `OrderList` (пустое состояние, действия только для `Accepted`, контракт `onChangeStatus(id, action)`, формат даты), `StatusBadge` (в т.ч. fallback на «Принят» для неизвестного статуса), `Toasts` (`onClose(id)`, цвет по kind), `Header`, `Spinner`, `formatRub` и интеграционный `App.test.tsx` (мок `fetch`/`useNotificationHub`, полный цикл оформления заказа, обработка ProblemDetails-ошибок).
- Конфиг тестов — в том же `vite.config.ts` (`test: { environment: 'jsdom', globals: false, setupFiles, css: false }`), отдельный `vitest.config.ts` не заводили. Setup: `src/test/setup.ts` (jest-dom матчеры + ручной `cleanup()`).
- Скрипты: `npm test` → `vitest run`, `npm run test:watch` → `vitest`.
- Во все три `Program.cs` (Shop.Web, Shop.Orders, Shop.Notifications) добавлены развёрнутые комментарии «для джуна»: роли брокеров, консьюмер-группы Kafka, ретраи/дед-леттеры, идемпотентность, паттерн rider.

**Затронутые пути:**
- `src/Shop.Web/clientapp/package.json`, `package-lock.json` (devDeps + скрипты)
- `src/Shop.Web/clientapp/vite.config.ts` (секция `test`)
- `src/Shop.Web/clientapp/src/test/setup.ts` (новый)
- `src/Shop.Web/clientapp/src/*.test.ts(x)`: `format.test.ts`, `App.test.tsx`, `components/{Header,OrderForm,OrderList,Spinner,StatusBadge,Toasts}.test.tsx`
- `src/{Shop.Web,Shop.Orders,Shop.Notifications}/Program.cs` (только комментарии)

**Зачем / контекст:** покрытие фронтенда тестами — защита от регрессий в пропсах, обработчиках и рендеринге при дальнейших правках; Vitest выбран вместо Jest из-за Vite-стека (общий конфиг, без babel-пайплайна).

**Грабли, найденные при написании тестов (важно для будущих сессий):**
- RTL выполняет авто-cleanup только при наличии ГЛОБАЛЬНОГО `afterEach` — при `globals: false` нужно вручную `afterEach(() => cleanup())` в setup-файле.
- `Intl.NumberFormat('ru-RU')` вставляет неразрывный пробел (U+00A0) в разделитель разрядов: точное `toBe('6 990 ₽')` хрупко к версии ICU — проверять регулярками (`/5\s470\s₽/`, `\s` покрывает NBSP) или частями строки.
- Фикстуры через `{...base}` дают одинаковые `items` → `getByText` падает с «Found multiple elements» — у каждого заказа свой набор позиций.
- `createdAt` от бэка приходит без суффикса `Z` → парсится как локальное время: тесты с датами не должны завязываться на таймзону машины (проверять формат, не точные часы).

### 2026-10-13 — Инициализация: микросервисы MiniShop с Kafka/RabbitMQ через MassTransit

**Область:** Architecture | Backend | Frontend | Infra

**Что изменилось:**
- Решение `PetKafkaRabbitMQApp.slnx` (.NET 10, формат решений VS 2026), 4 проекта: `Shop.Web` (ASP.NET Core MVC + React SPA + SignalR), `Shop.Orders` и `Shop.Notifications` (Worker-хосты, EF Core + SQLite), `Shop.Contracts` (records-контракты сообщений, статический каталог, опции конфигурации).
- MassTransit 8.5.11 (последняя Apache-2.0 линейка): RabbitMQ — команды и request/response (`CreateOrder`, `ChangeOrderStatus`, `GetOrders`, `NotificationSent`), kebab-case очереди, экспоненциальный retry; Kafka-райдер — продюсер `OrderEvent` в топик `orders-events` (ключ партиции = OrderId) и консьюмер группы `notifications`.
- Подключения к брокерам берутся из appsettings: секции `MessageBus:RabbitMq` (Host/Port/Username/Password/VirtualHost) и `MessageBus:Kafka` (BootstrapServers/Topic/GroupId), классы `RabbitMqOptions`/`KafkaOptions` в `Shop.Contracts`; дефолты зашиты в опции.
- REST API `Shop.Web`: `POST /api/orders`, `GET /api/orders`, `POST /api/orders/{id}/complete|cancel`, `GET /api/catalog` (тонкие контроллеры, MassTransit request-клиенты).
- SignalR-хаб `/hubs/notifications`: консьюмер `NotificationSent` в `Shop.Web` пушит уведомления в React; тосты + живое обновление списка заказов.
- React-клиент `src/Shop.Web/clientapp`: Vite + React 19 + TypeScript + Tailwind CSS 4; сборка в `wwwroot/app.js` + `app.css` (фиксированные имена для MVC-вью), dev-режим `npm run dev` (порт 5173, proxy `/api` и `/hubs` на Kestrel 5080).
- Инфраструктура `docker-compose.yml`: RabbitMQ 3-management (:5672/:15672), Apache Kafka 3.9 KRaft (:9092, dual listeners), kafka-ui (:8080).
- Спецификация на русском — `spec.md` (архитектура, потоки, конфигурация, дизайн UI, запуск).

**Затронутые пути:**
- `PetKafkaRabbitMQApp.slnx`, `docker-compose.yml`, `spec.md`, `.editorconfig`, `.gitignore`
- `src/Shop.Contracts/` (Catalog.cs, Orders.cs, MessagingOptions.cs)
- `src/Shop.Orders/` (Program.cs, Data/, Consumers/)
- `src/Shop.Notifications/` (Program.cs, Data/, Consumers/)
- `src/Shop.Web/` (Program.cs, Controllers/, Consumers/, Hubs/, Views/, clientapp/)

**Зачем / контекст:** пет-проект для отработки интеграции MassTransit с двумя брокерами. Роли: RabbitMQ — «командная шина» (команды, request/response, уведомления), Kafka — событийный поток заказов.

**Грабли, найденные при сборке (важно для будущих сессий):**
- В .NET 10 Worker SDK **не** подключает `Microsoft.Extensions.Hosting` сам: нужен явный `PackageReference` (иначе CS0103 «Host не существует»).
- MassTransit 8.5.11: `GetResponse<T1,T2>` возвращает `Response<T1,T2>` — правильный паттерн `var response = await client.GetResponse<A,B>(...); response.Is(out Response<A>? ok)` (не кортеж `(accepted, rejected)`).
- Kafka-продюсер: `rider.AddProducer<Guid, OrderEvent>(topic)` + `ITopicProducer<Guid, OrderEvent>.Produce(orderId, message, ct)` — ключ идёт первым аргументом.
- `dotnet new sln` в .NET 10 SDK создаёт `.slnx` (XML-формат решений VS 2026), а не `.sln`.
