import { useCallback, useEffect, useState, type ReactNode } from "react";
import {
  diagnosticsGet,
  type Broker,
  type KafkaMessages,
  type KafkaSnapshot,
  type RabbitSnapshot,
  type Snapshot,
} from "./api";
import { JsonDetail } from "./Journal";

function Table({
  caption,
  headers,
  rows,
}: {
  caption: string;
  headers: string[];
  rows: ReactNode[][];
}) {
  return (
    <div className="overflow-x-auto">
      <table className="monitor-table">
        <caption className="py-3 text-left font-semibold">{caption}</caption>
        <thead>
          <tr>
            {headers.map((header) => (
              <th key={header} scope="col">
                {header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, index) => (
            <tr key={index}>
              {row.map((cell, i) => (
                <td key={i}>{cell ?? "—"}</td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
      {!rows.length && <p className="text-zinc-400">Нет данных.</p>}
    </div>
  );
}
function Availability({ snapshot }: { snapshot: Snapshot }) {
  return (
    <div className="space-y-1 text-sm">
      <p>
        Брокер:{" "}
        <span
          className={snapshot.available ? "text-emerald-400" : "text-rose-400"}
        >
          {snapshot.available ? "доступен" : "недоступен"}
        </span>
      </p>
      {snapshot.error && (
        <p role="alert" className="text-rose-400">
          {snapshot.error}
        </p>
      )}
      <p className="text-zinc-400">
        Наблюдение: {snapshot.observedAt} · Последний успех:{" "}
        {snapshot.lastSuccessAt ?? "ещё не было"}
      </p>
      {!snapshot.available && snapshot.lastSuccessAt && (
        <p className="text-amber-400">
          Показаны последние доступные данные; они могут быть устаревшими.
        </p>
      )}
    </div>
  );
}
function Rabbit({ snapshot }: { snapshot: RabbitSnapshot }) {
  return (
    <>
      <p className="text-sm text-zinc-400">
        RabbitMQ удаляет сообщение после подтверждения (ack). Безопасного
        просмотра тела очереди (peek) нет: здесь показаны метрики и журнал
        приложения, без чтения сообщений из очереди.
      </p>
      <Table
        caption="Очереди"
        headers={[
          "Очередь",
          "Всего",
          "Готовы",
          "Без ack",
          "Consumers",
          "Publish / с",
          "Deliver / с",
          "Ack / с",
        ]}
        rows={snapshot.queues.map((q) => [
          q.name,
          q.messages,
          q.messagesReady,
          q.messagesUnacknowledged,
          q.consumers,
          q.publishRate,
          q.deliverRate,
          q.ackRate,
        ])}
      />
      <Table
        caption="Exchanges"
        headers={["Имя", "Тип", "Durable"]}
        rows={snapshot.exchanges.map((e) => [
          e.name || "(default)",
          e.type,
          e.durable ? "да" : "нет",
        ])}
      />
      <Table
        caption="Bindings"
        headers={["Источник", "Назначение", "Тип", "Routing key"]}
        rows={snapshot.bindings.map((b) => [
          b.source || "(default)",
          b.destination,
          b.destinationType,
          b.routingKey,
        ])}
      />
      <Table
        caption="Consumers"
        headers={[
          "Очередь",
          "Consumer tag",
          "Ack обязателен",
          "Prefetch",
          "Активен",
        ]}
        rows={snapshot.consumers.map((c) => [
          c.queue,
          c.consumerTag,
          c.ackRequired ? "да" : "нет",
          c.prefetchCount,
          c.active ? "да" : "нет",
        ])}
      />
    </>
  );
}
function Kafka({ snapshot }: { snapshot: KafkaSnapshot }) {
  const [partition, setPartition] = useState("");
  const [offset, setOffset] = useState("0");
  const [result, setResult] = useState<KafkaMessages | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);
  const selected =
    snapshot.partitions.find((p) => String(p.partition) === partition) ??
    snapshot.partitions[0];
  const read = async () => {
    if (
      !selected ||
      !/^\d+$/.test(offset) ||
      !Number.isSafeInteger(Number(offset))
    ) {
      setError(
        "Укажите неотрицательный целый offset в безопасном диапазоне JavaScript.",
      );
      return;
    }
    setLoading(true);
    setError("");
    setResult(null);
    try {
      const query = new URLSearchParams({
        partition: String(selected.partition),
        offset,
        limit: "50",
      });
      const data = await diagnosticsGet<KafkaMessages>(
        `kafka/messages?${query}`,
      );
      setResult(data);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Ошибка чтения Kafka");
    } finally {
      setLoading(false);
    }
  };
  return (
    <>
      <p>
        Топик: <strong>{snapshot.topic}</strong> · Группа:{" "}
        <strong>{snapshot.consumerGroup}</strong>
      </p>
      <p className="text-sm text-zinc-400">
        Lag может временно расти из-за отложенных commit и не означает потерю
        сообщений. Offset упорядочен только внутри partition; общего порядка
        между partition нет.
      </p>
      <Table
        caption="Partitions / offsets / lag"
        headers={[
          "Partition",
          "Leader",
          "Low offset",
          "High offset",
          "Committed",
          "Lag",
        ]}
        rows={snapshot.partitions.map((p) => [
          p.partition,
          p.leader,
          p.lowOffset,
          p.highOffset,
          p.committedOffset,
          p.lag,
        ])}
      />
      <section
        aria-labelledby="messages-title"
        className="space-y-3 border-t border-zinc-800 pt-4"
      >
        <h2 id="messages-title" className="text-xl font-semibold">
          Сообщения Kafka
        </h2>
        <p className="text-sm text-zinc-400">
          Чтение только по запросу, до 50 записей. Consumer group и её offset не
          изменяются. Данные показаны как получены.
        </p>
        <p className="text-sm text-amber-400">
          Внимание: исходные сообщения могут содержать имя клиента и другие
          персональные данные. Просмотр предназначен только для локального
          обучения, не для production.
        </p>
        <form
          className="flex flex-wrap items-end gap-3"
          onSubmit={(event) => {
            event.preventDefault();
            void read();
          }}
        >
          <label className="grid gap-1">
            Partition
            <select
              className="monitor-input"
              value={selected?.partition ?? ""}
              onChange={(event) => {
                setPartition(event.target.value);
                setResult(null);
              }}
              disabled={loading || !snapshot.partitions.length}
            >
              {snapshot.partitions.map((p) => (
                <option key={p.partition} value={p.partition}>
                  {p.partition}
                </option>
              ))}
            </select>
          </label>
          <label className="grid gap-1">
            Offset
            <input
              className="monitor-input"
              type="number"
              min="0"
              step="1"
              required
              value={offset}
              onChange={(event) => setOffset(event.target.value)}
              disabled={loading}
            />
          </label>
          <button className="monitor-button" disabled={loading || !selected}>
            {loading ? "Чтение…" : "Прочитать до 50 сообщений"}
          </button>
        </form>
        {error && (
          <p role="alert" className="text-rose-400">
            {error}
          </p>
        )}
        {result && (
          <div className="space-y-3">
            <p>
              Ответ: {result.topic}, partition {result.partition}, диапазон [
              {result.lowOffset}, {result.highOffset}), следующий offset{" "}
              {result.nextOffset}
            </p>
            {!result.available && (
              <p role="alert" className="text-rose-400">
                {result.error ?? "Kafka недоступна"}
              </p>
            )}
            {result.truncated && (
              <p className="text-amber-400">Ответ ограничен по размеру.</p>
            )}
            {!result.messages.length && (
              <p>Сообщений в выбранном диапазоне нет.</p>
            )}
            <Table
              caption="Прочитанные сообщения"
              headers={[
                "Partition / Offset",
                "Время",
                "Key",
                "JSON / Value / Headers",
              ]}
              rows={result.messages.map((message) => [
                `${message.partition} / ${message.offset}`,
                message.timestamp,
                message.key,
                <details>
                  <summary className="cursor-pointer text-orange-400">
                    Показать сообщение
                  </summary>
                  <JsonDetail value={message} />
                  {message.truncated && (
                    <p className="text-amber-400">Сообщение усечено.</p>
                  )}
                </details>,
              ])}
            />
            {result.messages.length > 0 && (
              <button
                className="monitor-button"
                onClick={() => setOffset(String(result.nextOffset))}
              >
                Подставить следующий offset
              </button>
            )}
          </div>
        )}
      </section>
    </>
  );
}
export function BrokerSnapshot({ broker }: { broker: Broker }) {
  const [snapshot, setSnapshot] = useState<
    RabbitSnapshot | KafkaSnapshot | null
  >(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);
  const [revision, setRevision] = useState(0);
  const reload = useCallback(() => setRevision((value) => value + 1), []);
  useEffect(() => {
    let disposed = false;
    let busy = false;
    const controller = new AbortController();
    const refresh = async () => {
      if (busy) return;
      busy = true;
      setLoading(true);
      try {
        const data = await diagnosticsGet<RabbitSnapshot | KafkaSnapshot>(
          `${broker}/snapshot`,
          controller.signal,
        );
        if (!disposed) {
          setSnapshot(data);
          setError("");
        }
      } catch (e) {
        if (!disposed)
          setError(e instanceof Error ? e.message : "Ошибка снимка");
      } finally {
        busy = false;
        if (!disposed) setLoading(false);
      }
    };
    void refresh();
    const timer = setInterval(() => void refresh(), 10000);
    return () => {
      disposed = true;
      controller.abort();
      clearInterval(timer);
    };
  }, [broker, revision]);
  return (
    <section
      aria-label="Снимок брокера"
      className="space-y-4 rounded border border-zinc-800 bg-zinc-900/40 p-4"
    >
      <div className="flex flex-wrap items-center gap-4">
        <h2 className="text-xl font-semibold">Состояние брокера</h2>
        <button className="monitor-button" onClick={reload} disabled={loading}>
          Обновить снимок
        </button>
        <span className="text-sm text-zinc-400">Обновление каждые 10 с</span>
      </div>
      {loading && <p role="status">Загрузка снимка…</p>}
      {error && (
        <p role="alert" className="text-rose-400">
          {error}
        </p>
      )}
      {error && snapshot && (
        <p className="text-amber-400">
          Обновление не удалось; ниже предыдущий снимок.
        </p>
      )}
      {snapshot && (
        <>
          <Availability snapshot={snapshot} />
          {broker === "rabbitmq" ? (
            <Rabbit snapshot={snapshot as RabbitSnapshot} />
          ) : (
            <Kafka snapshot={snapshot as KafkaSnapshot} />
          )}
        </>
      )}
    </section>
  );
}
