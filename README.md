# PetKafkaRabbitMQApp — MiniShop

Пет-проект на **.NET 10**: заказы ходят через **RabbitMQ** (команды, request/response) и **Kafka**
(событийный поток), транспорт — **MassTransit 8.5.11**. UI — **React 19 + TypeScript + Vite + Tailwind 4**,
данные — **SQLite**, инфраструктура — **Docker**. Дополнительно есть read-only страницы мониторинга
брокеров и живой журнал обмена сообщениями.

## Что смотреть в готовом UI

| URL | Страница |
|---|---|
| `http://localhost:5080/` | витрина: оформление и список заказов |
| `http://localhost:5080/brokers/rabbitmq` | снимок RabbitMQ: очереди, exchanges, bindings, consumers, rates |
| `http://localhost:5080/brokers/kafka` | Kafka: partitions/offsets/lag и чтение до 50 сообщений по offset |
| `http://localhost:5080/brokers/flow` | статическая схема пути сообщения + живой журнал |

Страницы `/brokers/*` только читают метрики и сообщения, не меняют топологию и offset'ы
и доступны лишь в Development с loopback-адреса.

## Запуск (Windows 10/11, PowerShell)

Нужны .NET 10 SDK, Node.js и запущенный Docker Desktop.

```powershell
# 1. Брокеры: RabbitMQ (:5672, UI :15672), Kafka (:9092), Kafka UI (:8080)
docker compose up -d

# 2. Сборка React в Shop.Web/wwwroot (после изменений UI повторять)
npm --prefix Shop.Web/clientapp install
npm --prefix Shop.Web/clientapp run build
# hot reload вместо сборки: npm --prefix Shop.Web/clientapp run dev (Vite :5173)
```

Дальше — **три терминала из корня репозитория**. В каждом перед `dotnet run` задайте
**один и тот же** локальный ключ диагностики (подставьте свою случайную строку; это не
реальный секрет и его не нужно коммитить):

```powershell
# Терминал 1
$env:BrokerMonitoring__IngestionKey='<одинаковый случайный локальный ключ>'
dotnet run --project Shop.Orders

# Терминал 2
$env:BrokerMonitoring__IngestionKey='<одинаковый случайный локальный ключ>'
dotnet run --project Shop.Notifications

# Терминал 3
$env:BrokerMonitoring__IngestionKey='<одинаковый случайный локальный ключ>'
dotnet run --project Shop.Web
```

`dotnet run` наследует launch-профили: `Shop.Web` — `ASPNETCORE_ENVIRONMENT=Development`,
воркеры — `DOTNET_ENVIRONMENT=Development`, а `appsettings.Development.json` включает
`BrokerMonitoring:Enabled=true`. Затем откройте `http://localhost:5080` (или `:5173` в dev-режиме Vite).

**Если ключ не задан:** в `Shop.Web` снимки брокеров и локальный журнал работают, а воркеры
не отправляют диагностику по HTTP — в их логе будет warning, и журнал покажет только события
самого Web. Бизнес-поток заказов от мониторинга не зависит.

## Проверка и тесты

```powershell
dotnet test                       # backend: xUnit
dotnet build                      # сборка решения
npm --prefix Shop.Web/clientapp run test   # frontend: Vitest + React Testing Library
```

Подробности архитектуры, конфигурации и семантики мониторинга — в `spec.md`; журнал
ключевых изменений — в `dev_notes.md`.