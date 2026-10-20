import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { DiagnosticsPage } from "./DiagnosticsPage";

vi.mock("@microsoft/signalr", () => ({
  LogLevel: { Warning: 3 },
  HubConnectionBuilder: class {
    withUrl() {
      return this;
    }
    withAutomaticReconnect() {
      return this;
    }
    configureLogging() {
      return this;
    }
    build() {
      return {
        on: vi.fn(),
        onclose: vi.fn(),
        onreconnecting: vi.fn(),
        onreconnected: vi.fn(),
        start: vi.fn().mockResolvedValue(undefined),
        stop: vi.fn().mockResolvedValue(undefined),
      };
    }
  },
}));
const response = (body: unknown, status = 200) =>
  ({ ok: status === 200, status, json: async () => body }) as Response;
const config = {
  enabled: true,
  rabbitMqManagementUrl: null,
  kafkaUiUrl: "http://localhost:8080",
};
const kafka = {
  available: true,
  error: null,
  observedAt: "2026-10-20T10:00:00Z",
  lastSuccessAt: "2026-10-20T10:00:00Z",
  topic: "order-status",
  consumerGroup: "notifications",
  partitions: [
    {
      partition: 0,
      leader: 1,
      lowOffset: 0,
      highOffset: 15,
      committedOffset: 12,
      lag: 3,
    },
  ],
};
let fetchMock: ReturnType<typeof vi.fn>;
beforeEach(() => {
  fetchMock = vi.fn(async (input: RequestInfo | URL) => {
    const path = String(input);
    if (path.endsWith("/config")) return response(config);
    if (path.endsWith("/kafka/snapshot")) return response(kafka);
    if (path.includes("/events?"))
      return response({
        events: [],
        nextCursor: 0,
        oldestCursor: 0,
        gap: false,
        droppedEvents: 0,
      });
    if (path.includes("/kafka/messages?"))
      return response({
        available: true,
        error: null,
        topic: "order-status",
        partition: 0,
        lowOffset: 0,
        highOffset: 15,
        nextOffset: 8,
        truncated: false,
        messages: [
          {
            partition: 0,
            offset: 7,
            timestamp: null,
            key: "key-7",
            value: '{"status":"Accepted"}',
            headers: { type: "OrderStatusChanged" },
            truncated: true,
          },
        ],
      });
    return response(null, 404);
  });
  vi.stubGlobal("fetch", fetchMock);
});
afterEach(() => vi.unstubAllGlobals());

describe("страницы мониторинга", () => {
  it("404 конфигурации показывает выключенный мониторинг без запуска чтения брокера", async () => {
    fetchMock.mockResolvedValue(response(null, 404));
    render(<DiagnosticsPage broker="rabbitmq" />);
    expect(
      await screen.findByRole("heading", { name: "Мониторинг выключен" }),
    ).toBeInTheDocument();
    expect(screen.getByText(/BrokerMonitoring:Enabled/)).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });
  it("сетевую ошибку можно повторить и получить реальные метрики", async () => {
    fetchMock.mockRejectedValueOnce(new Error("Сеть недоступна"));
    const user = userEvent.setup();
    render(<DiagnosticsPage broker="kafka" />);
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Сеть недоступна",
    );
    await user.click(screen.getByRole("button", { name: "Повторить" }));
    expect(await screen.findByText("order-status")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Kafka UI ↗" })).toHaveAttribute(
      "rel",
      "noopener noreferrer",
    );
  });
  it("RabbitMQ показывает недоступность отдельно от SignalR и сохраняет последние метрики", async () => {
    const previous = fetchMock.getMockImplementation() as (
      input: RequestInfo | URL,
    ) => Promise<Response>;
    fetchMock.mockImplementation(async (input: RequestInfo | URL) => {
      if (String(input).endsWith("/rabbitmq/snapshot"))
        return response({
          available: false,
          error: "Management API: timeout",
          observedAt: "2026-10-20T10:00:00Z",
          lastSuccessAt: "2026-10-20T09:59:00Z",
          queues: [
            {
              name: "create-order",
              messages: 3,
              messagesReady: 2,
              messagesUnacknowledged: 1,
              consumers: 1,
              publishRate: null,
              deliverRate: 0,
              ackRate: 0,
            },
          ],
          exchanges: [],
          bindings: [],
          consumers: [],
        });
      return previous(input);
    });
    render(<DiagnosticsPage broker="rabbitmq" />);
    expect(await screen.findByText("create-order")).toBeInTheDocument();
    expect(screen.getByText("Management API: timeout")).toBeInTheDocument();
    expect(
      screen.getByText(/Показаны последние доступные данные/),
    ).toBeInTheDocument();
    expect(screen.getByText("недоступен")).toBeInTheDocument();
    expect(
      screen.getByText(/Безопасного просмотра тела очереди/),
    ).toBeInTheDocument();
    expect(screen.getByText("—")).toBeInTheDocument();
  });
  it("Kafka читает сообщения только по запросу выбранного offset с limit 50", async () => {
    const user = userEvent.setup();
    render(<DiagnosticsPage broker="kafka" />);
    await screen.findByRole("button", { name: "Прочитать до 50 сообщений" });
    expect(
      fetchMock.mock.calls.some(([url]) => String(url).includes("/messages?")),
    ).toBe(false);
    fireEvent.change(screen.getByLabelText("Offset"), {
      target: { value: "7" },
    });
    await user.click(
      screen.getByRole("button", { name: "Прочитать до 50 сообщений" }),
    );
    expect(await screen.findByText("key-7")).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/diagnostics/kafka/messages?partition=0&offset=7&limit=50",
      { signal: undefined },
    );
    await user.click(screen.getByText("Показать сообщение"));
    expect(screen.getByText("Сообщение усечено.")).toBeVisible();
    expect(
      screen.getByText(/исходные сообщения могут содержать/),
    ).toBeInTheDocument();
  });
  it("схема не имитирует успешную обработку и валидирует orderId", async () => {
    const user = userEvent.setup();
    render(<DiagnosticsPage />);
    expect(
      await screen.findByRole("list", { name: "Цепочка обработки заказа" }),
    ).toBeInTheDocument();
    expect(
      screen.getByText(/Статическая схема архитектуры/),
    ).toBeInTheDocument();
    expect(screen.getByText("RabbitMQ: NotificationSent")).toBeInTheDocument();
    expect(
      screen.getByText("Shop.Web: обработка уведомления"),
    ).toBeInTheDocument();
    await user.type(
      screen.getByLabelText("Фильтр журнала: orderId"),
      "not-guid",
    );
    await user.click(screen.getByRole("button", { name: "Применить фильтр" }));
    expect(screen.getByRole("alert")).toHaveTextContent(
      "Введите orderId в формате GUID",
    );
    expect(
      fetchMock.mock.calls.some(([url]) =>
        String(url).includes("orderId=not-guid"),
      ),
    ).toBe(false);
    await user.clear(screen.getByLabelText("Фильтр журнала: orderId"));
    await user.type(
      screen.getByLabelText("Фильтр журнала: orderId"),
      "122bf004-fa2a-4298-a9c6-bfd4284a89bf",
    );
    await user.click(screen.getByRole("button", { name: "Применить фильтр" }));
    await waitFor(() =>
      expect(
        fetchMock.mock.calls.some(([url]) =>
          String(url).includes("orderId=122bf004-fa2a-4298-a9c6-bfd4284a89bf"),
        ),
      ).toBe(true),
    );
  });
});
