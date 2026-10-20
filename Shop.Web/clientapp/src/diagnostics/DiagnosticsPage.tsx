import { useEffect, useState } from "react";
import { BrokerSnapshot } from "./BrokerSnapshot";
import { Journal } from "./Journal";
import {
  diagnosticsGet,
  MonitoringDisabledError,
  type Broker,
  type MonitoringConfig,
} from "./api";

function DisabledNotice() {
  return (
    <section className="space-y-3 border border-amber-700 bg-amber-950/20 p-5">
      <h2 className="text-xl text-amber-400">Мониторинг выключен</h2>
      <p>
        Включите BrokerMonitoring:Enabled в конфигурации сервисов и
        перезапустите их. Настройте адрес RabbitMQ Management API, Kafka
        bootstrap servers и общий BrokerMonitoring:IngestionKey на сервере.
      </p>
      <p className="text-sm text-zinc-400">
        Секреты задаются только на сервере и никогда не вводятся в браузере.
        Заказы и бизнес-уведомления работают независимо от мониторинга.
      </p>
    </section>
  );
}
function ExternalLink({
  url,
  children,
}: {
  url: string | null;
  children: string;
}) {
  if (!url) return null;
  try {
    if (!["http:", "https:"].includes(new URL(url).protocol)) return null;
  } catch {
    return null;
  }
  return (
    <a
      className="monitor-button inline-block"
      href={url}
      target="_blank"
      rel="noopener noreferrer"
    >
      {children} ↗
    </a>
  );
}
export function DiagnosticsPage({ broker }: { broker?: Broker }) {
  const [config, setConfig] = useState<MonitoringConfig | null>(null);
  const [error, setError] = useState("");
  const [disabled, setDisabled] = useState(false);
  const [revision, setRevision] = useState(0);
  const [orderInput, setOrderInput] = useState("");
  const [orderId, setOrderId] = useState("");
  const [filterError, setFilterError] = useState("");
  useEffect(() => {
    const controller = new AbortController();
    setError("");
    setConfig(null);
    setDisabled(false);
    diagnosticsGet<MonitoringConfig>("config", controller.signal)
      .then((data) => {
        if (!controller.signal.aborted) {
          setConfig(data);
          setDisabled(!data.enabled);
        }
      })
      .catch((e) => {
        if (controller.signal.aborted) return;
        if (e instanceof MonitoringDisabledError) setDisabled(true);
        else
          setError(
            e instanceof Error ? e.message : "Не удалось получить конфигурацию",
          );
      });
    return () => controller.abort();
  }, [revision]);
  const title =
    broker === "rabbitmq"
      ? "Мониторинг RabbitMQ"
      : broker === "kafka"
        ? "Мониторинг Kafka"
        : "Путь сообщения";
  return (
    <main className="mx-auto max-w-6xl space-y-6 px-4 py-8">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-bold">{title}</h1>
        {config?.enabled && (
          <div className="flex gap-3">
            <ExternalLink url={config.rabbitMqManagementUrl}>
              RabbitMQ Management
            </ExternalLink>
            <ExternalLink url={config.kafkaUiUrl}>Kafka UI</ExternalLink>
          </div>
        )}
      </div>
      {disabled ? (
        <>
          <DisabledNotice />
          <button
            className="monitor-button"
            onClick={() => setRevision((value) => value + 1)}
          >
            Проверить снова
          </button>
        </>
      ) : error ? (
        <div role="alert" className="text-rose-400">
          {error}{" "}
          <button
            className="monitor-button"
            onClick={() => setRevision((value) => value + 1)}
          >
            Повторить
          </button>
        </div>
      ) : !config ? (
        <p role="status">Загрузка конфигурации мониторинга…</p>
      ) : (
        <>
          {!broker && (
            <section
              aria-labelledby="flow-title"
              className="space-y-4 rounded border border-zinc-800 bg-zinc-900/40 p-4"
            >
              <h2 id="flow-title" className="text-xl font-semibold">
                Архитектура обмена
              </h2>
              <p className="text-sm text-zinc-400">
                Статическая схема архитектуры, а не индикация успеха отдельных
                этапов. Фактические операции и ошибки — в журнале ниже.
              </p>
              <ol
                aria-label="Цепочка обработки заказа"
                className="flex flex-wrap items-center gap-3"
              >
                {[
                  "Shop.Web: запрос",
                  "RabbitMQ: команда / ответ",
                  "Shop.Orders: заказ",
                  "Kafka: событие статуса",
                  "Shop.Notifications",
                  "RabbitMQ: NotificationSent",
                  "Shop.Web: обработка уведомления",
                  "SignalR: браузер",
                ].map((step, index) => (
                  <li key={step} className="border border-zinc-700 p-3">
                    <span className="text-orange-400">{index + 1}. </span>
                    {step}
                  </li>
                ))}
              </ol>
            </section>
          )}
          {broker && <BrokerSnapshot broker={broker} />}
          <form
            className="flex flex-wrap items-end gap-3"
            onSubmit={(event) => {
              event.preventDefault();
              const value = orderInput.trim();
              if (
                value &&
                !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(
                  value,
                )
              ) {
                setFilterError("Введите orderId в формате GUID.");
                return;
              }
              setFilterError("");
              setOrderId(value);
            }}
          >
            <label className="grid grow gap-1">
              Фильтр журнала: orderId
              <input
                className="monitor-input"
                placeholder="GUID заказа (необязательно)"
                value={orderInput}
                onChange={(event) => setOrderInput(event.target.value)}
              />
            </label>
            <button className="monitor-button">Применить фильтр</button>
            <button
              type="button"
              className="monitor-button"
              onClick={() => {
                setOrderInput("");
                setOrderId("");
                setFilterError("");
              }}
            >
              Сбросить
            </button>
            {filterError && (
              <p role="alert" className="w-full text-rose-400">
                {filterError}
              </p>
            )}
          </form>
          <Journal broker={broker} orderId={orderId} />
        </>
      )}
    </main>
  );
}
