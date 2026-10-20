export type Broker = "rabbitmq" | "kafka";
export interface MonitoringConfig {
  enabled: boolean;
  rabbitMqManagementUrl: string | null;
  kafkaUiUrl: string | null;
}
export interface Snapshot {
  available: boolean;
  error: string | null;
  observedAt: string;
  lastSuccessAt: string | null;
}
export interface RabbitSnapshot extends Snapshot {
  queues: {
    name: string;
    messages: number;
    messagesReady: number;
    messagesUnacknowledged: number;
    consumers: number;
    publishRate: number | null;
    deliverRate: number | null;
    ackRate: number | null;
  }[];
  exchanges: { name: string; type: string; durable: boolean }[];
  bindings: {
    source: string;
    destination: string;
    destinationType: string;
    routingKey: string;
  }[];
  consumers: {
    queue: string;
    consumerTag: string;
    ackRequired: boolean;
    prefetchCount: number;
    active: boolean;
  }[];
}
export interface KafkaSnapshot extends Snapshot {
  topic: string;
  consumerGroup: string;
  partitions: {
    partition: number;
    leader: number;
    lowOffset: number;
    highOffset: number;
    committedOffset: number | null;
    lag: number | null;
  }[];
}
export interface KafkaMessage {
  partition: number;
  offset: number;
  timestamp: string | null;
  key: string | null;
  value: string | null;
  headers: Record<string, string>;
  truncated: boolean;
}
export interface KafkaMessages {
  available: boolean;
  error: string | null;
  topic: string;
  partition: number;
  lowOffset: number;
  highOffset: number;
  nextOffset: number;
  messages: KafkaMessage[];
  truncated: boolean;
}
export interface DiagnosticEvent {
  eventId: string;
  service: string;
  instanceId: string;
  broker: Broker;
  operation: "send" | "publish" | "receive" | "consume" | "produce";
  phase: "started" | "completed" | "faulted";
  messageType: string;
  messageId: string | null;
  requestId: string | null;
  correlationId: string | null;
  conversationId: string | null;
  orderId: string | null;
  observedAt: string;
  durationMs: number | null;
  address: string | null;
  partition: number | null;
  offset: number | null;
  payload: string | null;
  payloadTruncated: boolean;
  headers: Record<string, string>;
  error: string | null;
}
export interface JournalEntry {
  sequence: number;
  event: DiagnosticEvent;
}
export interface JournalPage {
  events: JournalEntry[];
  nextCursor: number;
  oldestCursor: number;
  gap: boolean;
  droppedEvents: number;
}
export class MonitoringDisabledError extends Error {
  constructor() {
    super("Мониторинг выключен или API диагностики недоступен.");
  }
}
export async function diagnosticsGet<T>(
  path: string,
  signal?: AbortSignal,
): Promise<T> {
  const response = await fetch(`/api/diagnostics/${path}`, { signal });
  if (response.status === 404) throw new MonitoringDisabledError();
  if (!response.ok) {
    const body = await response.json().catch(() => null);
    throw new Error(
      body?.detail ??
        body?.title ??
        `Ошибка диагностики: HTTP ${response.status}`,
    );
  }
  return response.json() as Promise<T>;
}
