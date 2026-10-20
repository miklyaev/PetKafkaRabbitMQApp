import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mergeEvents, useJournal, MAX_EVENTS } from "./useJournal";
import type { DiagnosticEvent, JournalEntry, JournalPage } from "./api";
const hub = vi.hoisted(() => ({
  handlers: new Map<string, () => void>(),
  reconnected: () => {},
  start: vi.fn(),
  stop: vi.fn(),
}));
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
        on: (name: string, callback: () => void) =>
          hub.handlers.set(name, callback),
        onclose: vi.fn(),
        onreconnecting: vi.fn(),
        onreconnected: (callback: () => void) => {
          hub.reconnected = callback;
        },
        start: hub.start,
        stop: hub.stop,
      };
    }
  },
}));
const entry = (
  sequence: number,
  eventId = `event-${sequence}`,
): JournalEntry => ({ sequence, event: { eventId } as DiagnosticEvent });
const page = (
  events: JournalEntry[],
  nextCursor: number,
  gap = false,
): JournalPage => ({
  events,
  nextCursor,
  gap,
  oldestCursor: 1,
  droppedEvents: 0,
});
let fetchMock: ReturnType<typeof vi.fn>;
const flush = async () => {
  await act(async () => {
    await Promise.resolve();
    await Promise.resolve();
  });
};
beforeEach(() => {
  vi.useFakeTimers();
  hub.handlers.clear();
  hub.start.mockReset().mockResolvedValue(undefined);
  hub.stop.mockReset().mockResolvedValue(undefined);
  fetchMock = vi.fn(async (input: RequestInfo | URL) => ({
    ok: true,
    status: 200,
    json: async () =>
      page(
        [],
        Number(
          new URL(String(input), "http://localhost").searchParams.get(
            "after",
          ) ?? 0,
        ),
      ),
  }));
  vi.stubGlobal("fetch", fetchMock);
});
afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});
const respond = (data: JournalPage) =>
  fetchMock.mockResolvedValueOnce({
    ok: true,
    status: 200,
    json: async () => data,
  });

describe("журнал диагностики", () => {
  it("дедуплицирует eventId и ограничивает клиентский буфер 2000 событий", () => {
    const result = mergeEvents(
      Array.from({ length: MAX_EVENTS }, (_, i) => entry(i + 1)),
      [entry(2001), entry(2002), entry(2003, "event-2002")],
    );
    expect(result).toHaveLength(MAX_EVENTS);
    expect(result[0].sequence).toBe(3);
    expect(result.at(-1)?.event.eventId).toBe("event-2002");
  });
  it("использует cursor на уведомлении, dedup на reconnect и сбрасывает cursor при рестарте сервера", async () => {
    respond(page([entry(10)], 10));
    const { result } = renderHook(() => useJournal("kafka", "", false));
    await flush();
    expect(result.current.events.map((e) => e.sequence)).toEqual([10]);
    respond(page([entry(10), entry(11)], 11));
    await act(async () => {
      hub.reconnected();
    });
    await flush();
    expect(fetchMock).toHaveBeenLastCalledWith(
      "/api/diagnostics/events?after=10&broker=kafka",
      expect.anything(),
    );
    expect(result.current.events.map((e) => e.sequence)).toEqual([10, 11]);
    respond(page([entry(1, "new-server")], 1, true));
    await act(async () => {
      hub.handlers.get("DiagnosticsChanged")?.();
    });
    await flush();
    expect(result.current.gap).toBe(true);
    expect(result.current.events.map((e) => e.event.eventId)).toEqual([
      "new-server",
    ]);
    await act(async () => {
      hub.handlers.get("DiagnosticsChanged")?.();
    });
    await flush();
    expect(fetchMock).toHaveBeenLastCalledWith(
      "/api/diagnostics/events?after=1&broker=kafka",
      expect.anything(),
    );
  });
  it("пауза останавливает только запросы ленты, продолжение читает с сохранённого cursor", async () => {
    respond(page([entry(5)], 5));
    const { result, rerender } = renderHook(
      ({ paused }) => useJournal("rabbitmq", "", paused),
      { initialProps: { paused: false } },
    );
    await flush();
    rerender({ paused: true });
    const calls = fetchMock.mock.calls.length;
    await act(async () => {
      hub.handlers.get("DiagnosticsChanged")?.();
      await vi.advanceTimersByTimeAsync(15000);
    });
    expect(fetchMock).toHaveBeenCalledTimes(calls);
    expect(hub.stop).not.toHaveBeenCalled();
    respond(page([entry(6)], 6));
    rerender({ paused: false });
    await flush();
    expect(fetchMock).toHaveBeenLastCalledWith(
      "/api/diagnostics/events?after=5&broker=rabbitmq",
      expect.anything(),
    );
    expect(result.current.events.map((e) => e.sequence)).toEqual([5, 6]);
  });
  it("повторяет первоначальное подключение SignalR и опрашивает журнал при отсутствии уведомлений", async () => {
    hub.start.mockRejectedValueOnce(new Error("offline"));
    const { result } = renderHook(() => useJournal(undefined, "", false));
    await flush();
    expect(result.current.connected).toBe(false);
    const initialFetches = fetchMock.mock.calls.length;
    await act(async () => {
      await vi.advanceTimersByTimeAsync(5000);
    });
    await flush();
    expect(hub.start).toHaveBeenCalledTimes(2);
    expect(result.current.connected).toBe(true);
    expect(fetchMock.mock.calls.length).toBeGreaterThan(initialFetches);
  });
});
