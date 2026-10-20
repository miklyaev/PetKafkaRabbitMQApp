import { useState } from "react";
import type { Broker, DiagnosticEvent } from "./api";
import { useJournal } from "./useJournal";

export function JsonDetail({ value }: { value: unknown }) {
  return (
    <pre className="max-h-96 overflow-auto whitespace-pre-wrap break-all rounded border border-zinc-700 bg-zinc-950 p-3 text-xs">
      {JSON.stringify(value, null, 2)}
    </pre>
  );
}
function EventDetail({ event }: { event: DiagnosticEvent }) {
  return (
    <details>
      <summary className="cursor-pointer text-orange-400 focus-visible:outline-2">
        JSON / payload / headers
      </summary>
      <JsonDetail value={event} />
      {event.payloadTruncated && (
        <p className="text-amber-400">Payload усечён источником.</p>
      )}
    </details>
  );
}
export function Journal({
  broker,
  orderId,
}: {
  broker?: Broker;
  orderId: string;
}) {
  const [paused, setPaused] = useState(false);
  const journal = useJournal(broker, orderId, paused);
  const [filters, setFilters] = useState({
    service: "",
    operation: "",
    phase: "",
    messageType: "",
  });
  const visible = journal.events.filter(({ event }) =>
    Object.entries(filters).every(
      ([field, value]) =>
        !value || event[field as keyof typeof filters] === value,
    ),
  );
  return (
    <section
      aria-labelledby="journal-title"
      className="space-y-4 rounded border border-zinc-800 bg-zinc-900/40 p-4"
    >
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 id="journal-title" className="text-xl font-semibold">
          Живой журнал
        </h2>
        <button
          className="monitor-button"
          onClick={() => setPaused((value) => !value)}
          aria-pressed={paused}
        >
          {paused ? "Продолжить ленту" : "Приостановить ленту"}
        </button>
      </div>
      <p className="text-sm text-zinc-400">
        Канал диагностики:{" "}
        {journal.connected
          ? "подключён"
          : "нет связи; резервный опрос каждые 5 с"}
        . Это не статус брокера.{" "}
        {paused
          ? "Лента приостановлена; брокеры продолжают работать."
          : "Лента активна."}{" "}
        В интерфейсе хранятся последние 2000 событий.
      </p>
      <p className="text-sm text-zinc-400">
        Строки упорядочены по поступлению диагностики, а не по причинному
        порядку сообщений. Пакетная доставка и часы сервисов могут различаться;
        сопоставляйте этапы по OrderId и идентификаторам сообщений.
      </p>
      <div className="flex flex-wrap gap-3">
        {(
          [
            ["service", "Сервис"],
            ["operation", "Операция"],
            ["phase", "Фаза"],
            ["messageType", "Тип сообщения"],
          ] as const
        ).map(([field, label]) => (
          <label key={field} className="grid gap-1 text-sm">
            {label}
            <select
              className="monitor-input max-w-64"
              value={filters[field]}
              onChange={(event) =>
                setFilters((previous) => ({
                  ...previous,
                  [field]: event.target.value,
                }))
              }
            >
              <option value="">Все</option>
              {[...new Set(journal.events.map(({ event }) => event[field]))]
                .sort()
                .map((value) => (
                  <option key={value} value={value}>
                    {value}
                  </option>
                ))}
            </select>
          </label>
        ))}
      </div>
      <p className="text-sm text-zinc-400">
        Показано {visible.length} из {journal.events.length}; фильтры сервиса,
        операции, фазы и типа применяются к загруженной ленте.
      </p>
      {journal.gap && (
        <p role="alert" className="text-amber-400">
          В журнале есть пропуск: события вытеснены из буфера или сервер
          перезапущен. Получены доступные события.
        </p>
      )}
      {journal.dropped > 0 && (
        <p className="text-amber-400">
          Потери диагностики, сообщённые источниками: {journal.dropped}
        </p>
      )}
      {journal.error && (
        <p role="alert" className="text-rose-400">
          {journal.error}{" "}
          <button className="monitor-button" onClick={journal.refresh}>
            Повторить журнал
          </button>
        </p>
      )}
      {journal.loading && <p role="status">Загрузка журнала…</p>}
      {!journal.loading && !visible.length && <p>Событий пока нет.</p>}
      <div className="overflow-x-auto">
        <table className="monitor-table">
          <caption className="sr-only">События обмена сообщениями</caption>
          <thead>
            <tr>
              <th>№ / Время</th>
              <th>Брокер / Сервис</th>
              <th>Операция / Фаза</th>
              <th>Тип / Заказ</th>
              <th>Детали</th>
            </tr>
          </thead>
          <tbody>
            {visible.map(({ sequence, event }) => (
              <tr key={event.eventId}>
                <td>
                  {sequence}
                  <br />
                  {event.observedAt}
                </td>
                <td>
                  {event.broker}
                  <br />
                  {event.service}
                </td>
                <td>
                  {event.operation}
                  <br />
                  <span
                    className={event.phase === "faulted" ? "text-rose-400" : ""}
                  >
                    {event.phase}
                  </span>
                  {event.durationMs != null && (
                    <>
                      <br />
                      {event.durationMs} мс
                    </>
                  )}
                </td>
                <td className="break-all">
                  {event.messageType}
                  <br />
                  {event.orderId ?? "—"}
                </td>
                <td>
                  <EventDetail event={event} />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}
