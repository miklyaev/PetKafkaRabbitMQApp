import { useEffect, useRef, useState } from "react";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import {
  diagnosticsGet,
  type Broker,
  type JournalEntry,
  type JournalPage,
} from "./api";

export const MAX_EVENTS = 2000;
export function mergeEvents(
  previous: JournalEntry[],
  incoming: JournalEntry[],
): JournalEntry[] {
  const seen = new Set<string>();
  return [...previous, ...incoming]
    .sort((a, b) => a.sequence - b.sequence)
    .filter(({ event }) => {
      if (seen.has(event.eventId)) return false;
      seen.add(event.eventId);
      return true;
    })
    .slice(-MAX_EVENTS);
}

export function useJournal(
  broker: Broker | undefined,
  orderId: string,
  paused: boolean,
) {
  const [events, setEvents] = useState<JournalEntry[]>([]);
  const [connected, setConnected] = useState(false);
  const [error, setError] = useState("");
  const [gap, setGap] = useState(false);
  const [dropped, setDropped] = useState(0);
  const [loading, setLoading] = useState(true);
  const refreshRef = useRef<() => void>(() => undefined);
  const cursor = useRef(0);
  const pausedRef = useRef(paused);
  pausedRef.current = paused;

  useEffect(() => {
    let disposed = false;
    let busy = false;
    let requested = false;
    const controller = new AbortController();
    cursor.current = 0;
    setEvents([]);
    setGap(false);
    setDropped(0);
    setLoading(true);
    setError("");
    const refresh = async () => {
      if (disposed || pausedRef.current) return;
      if (busy) {
        requested = true;
        return;
      }
      busy = true;
      try {
        do {
          requested = false;
          const query = new URLSearchParams({ after: String(cursor.current) });
          if (broker) query.set("broker", broker);
          if (orderId) query.set("orderId", orderId);
          const page = await diagnosticsGet<JournalPage>(
            `events?${query}`,
            controller.signal,
          );
          if (disposed) return;
          const restarted = page.gap && page.nextCursor < cursor.current;
          cursor.current = page.nextCursor;
          setEvents((previous) =>
            mergeEvents(restarted ? [] : previous, page.events),
          );
          setGap((previous) => previous || page.gap);
          setDropped(page.droppedEvents);
          setError("");
        } while (requested && !pausedRef.current);
      } catch (e) {
        if (!disposed)
          setError(e instanceof Error ? e.message : "Ошибка журнала");
      } finally {
        busy = false;
        if (!disposed) setLoading(false);
      }
    };
    refreshRef.current = () => {
      void refresh();
    };
    void refresh();
    const interval = setInterval(() => void refresh(), 5000);
    return () => {
      disposed = true;
      controller.abort();
      clearInterval(interval);
      refreshRef.current = () => undefined;
    };
  }, [broker, orderId]);

  useEffect(() => {
    if (!paused) refreshRef.current();
  }, [paused]);
  useEffect(() => {
    let disposed = false;
    let retry: ReturnType<typeof setTimeout> | undefined;
    const connection = new HubConnectionBuilder()
      .withUrl("/hubs/diagnostics")
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
    const start = async () => {
      try {
        await connection.start();
        if (disposed) {
          await connection.stop();
          return;
        }
        setConnected(true);
        refreshRef.current();
      } catch {
        if (!disposed) {
          setConnected(false);
          retry = setTimeout(() => void start(), 5000);
        }
      }
    };
    connection.on("DiagnosticsChanged", () => refreshRef.current());
    connection.onreconnecting(() => setConnected(false));
    connection.onreconnected(() => {
      setConnected(true);
      refreshRef.current();
    });
    connection.onclose(() => {
      if (!disposed) {
        setConnected(false);
        retry = setTimeout(() => void start(), 5000);
      }
    });
    void start();
    return () => {
      disposed = true;
      clearTimeout(retry);
      void connection.stop();
    };
  }, []);
  return {
    events,
    connected,
    error,
    gap,
    dropped,
    loading,
    refresh: () => refreshRef.current(),
  };
}
