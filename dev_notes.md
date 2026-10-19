# Dev Notes

Краткий журнал ключевых изменений проекта PetKafkaRabbitMQApp (пет-проект «MiniShop»).

## Журнал изменений

<!-- Новые записи добавлять сверху -->

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
