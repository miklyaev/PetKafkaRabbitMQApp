import type { CreateOrderResult, OrderSummary, Product } from './types';

interface ApiErrorBody {
  detail?: string;
  title?: string;
}

async function parse<T>(response: Promise<Response>): Promise<T> {
  const r = await response;
  if (!r.ok) {
    const body = (await r.json().catch(() => null)) as ApiErrorBody | null;
    throw new Error(body?.detail ?? body?.title ?? `Ошибка ${r.status}`);
  }
  return (await r.json()) as T;
}

/** Каталог товаров (GET /api/catalog). */
export function fetchCatalog(): Promise<Product[]> {
  return parse<Product[]>(fetch('/api/catalog'));
}

/** Список заказов (GET /api/orders). */
export function fetchOrders(): Promise<OrderSummary[]> {
  return parse<OrderSummary[]>(fetch('/api/orders'));
}

/** Создание заказа (POST /api/orders) — команда уходит в RabbitMQ. */
export function createOrder(
  customerName: string,
  items: { productName: string; quantity: number }[],
): Promise<CreateOrderResult> {
  return parse<CreateOrderResult>(
    fetch('/api/orders', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ customerName, items }),
    }),
  );
}

/** Завершение или отмена заказа (POST /api/orders/{id}/complete|cancel). */
export function changeOrderStatus(id: string, action: 'complete' | 'cancel'): Promise<void> {
  return parse<unknown>(fetch(`/api/orders/${id}/${action}`, { method: 'POST' })).then(() => undefined);
}
