import { useMemo, useState, type FormEvent } from 'react';
import { formatRub } from '../format';
import type { Product } from '../types';
import { Spinner } from './Spinner';

interface Props {
  products: Product[];
  onSubmit: (customerName: string, items: { productName: string; quantity: number }[]) => Promise<void>;
}

const MAX_QTY = 99;

export function OrderForm({ products, onSubmit }: Props) {
  const [customerName, setCustomerName] = useState('');
  const [quantities, setQuantities] = useState<Record<string, number>>({});
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const total = useMemo(
    () => products.reduce((sum, product) => sum + (quantities[product.name] ?? 0) * product.price, 0),
    [products, quantities],
  );
  const hasItems = Object.values(quantities).some(q => q > 0);

  const setQuantity = (name: string, value: number) => {
    setQuantities(prev => {
      const next = { ...prev, [name]: Math.max(0, Math.min(MAX_QTY, value)) };
      if (next[name] === 0) delete next[name];
      return next;
    });
  };

  const handleSubmit = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    if (submitting) return;

    if (!customerName.trim()) {
      setError('Укажите имя клиента.');
      return;
    }

    const items = Object.entries(quantities).map(([productName, quantity]) => ({ productName, quantity }));
    if (items.length === 0) {
      setError('Добавьте хотя бы один товар.');
      return;
    }

    setError(null);
    setSubmitting(true);
    try {
      await onSubmit(customerName, items);
      setQuantities({});
      setCustomerName('');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Не удалось оформить заказ.');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <form
      onSubmit={handleSubmit}
      className="flex h-fit flex-col gap-4 rounded-xl border border-slate-200 bg-white p-6 shadow-sm"
    >
      <div>
        <h2 className="text-base font-semibold">Новый заказ</h2>
        <p className="mt-0.5 text-sm text-slate-500">Команда — RabbitMQ, событие — Kafka.</p>
      </div>

      <label className="block">
        <span className="mb-1 block text-sm font-medium text-slate-700">Имя клиента</span>
        <input
          type="text"
          value={customerName}
          onChange={e => setCustomerName(e.target.value)}
          placeholder="Иван Петров"
          maxLength={100}
          autoComplete="off"
          className="w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm outline-none transition focus:border-indigo-500 focus:ring-2 focus:ring-indigo-200"
        />
      </label>

      <ul className="flex flex-col divide-y divide-slate-100">
        {products.map(product => {
          const qty = quantities[product.name] ?? 0;
          return (
            <li key={product.name} className="flex items-center gap-3 py-2.5">
              <span className="text-2xl" aria-hidden="true">
                {product.icon}
              </span>
              <div className="min-w-0 flex-1">
                <p className="truncate text-sm font-medium">{product.name}</p>
                <p className="text-xs text-slate-500">{formatRub(product.price)}</p>
              </div>
              <div className="flex items-center gap-1.5">
                <button
                  type="button"
                  onClick={() => setQuantity(product.name, qty - 1)}
                  disabled={qty === 0}
                  aria-label={`Убрать «${product.name}»`}
                  className="flex h-8 w-8 items-center justify-center rounded-lg border border-slate-200 text-lg leading-none text-slate-600 transition hover:bg-slate-50 disabled:opacity-40 focus-visible:outline focus-visible:outline-2 focus-visible:outline-indigo-500"
                >
                  −
                </button>
                <span className="w-6 text-center text-sm font-semibold tabular-nums">{qty}</span>
                <button
                  type="button"
                  onClick={() => setQuantity(product.name, qty + 1)}
                  disabled={qty === MAX_QTY}
                  aria-label={`Добавить «${product.name}»`}
                  className="flex h-8 w-8 items-center justify-center rounded-lg border border-slate-200 text-lg leading-none text-slate-600 transition hover:bg-slate-50 disabled:opacity-40 focus-visible:outline focus-visible:outline-2 focus-visible:outline-indigo-500"
                >
                  +
                </button>
              </div>
            </li>
          );
        })}
      </ul>

      <div className="flex items-center justify-between border-t border-slate-100 pt-4 text-sm">
        <span className="text-slate-500">Итого</span>
        <span className="text-lg font-bold tabular-nums">{formatRub(total)}</span>
      </div>

      {error && (
        <p role="alert" className="rounded-lg bg-rose-50 px-3 py-2 text-sm text-rose-700">
          {error}
        </p>
      )}

      <button
        type="submit"
        disabled={submitting || !hasItems}
        className="inline-flex items-center justify-center gap-2 rounded-lg bg-gradient-to-r from-indigo-600 to-violet-600 px-4 py-2.5 text-sm font-semibold text-white shadow-sm transition hover:from-indigo-700 hover:to-violet-700 disabled:cursor-not-allowed disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-indigo-600"
      >
        {submitting && <Spinner />}
        {submitting ? 'Оформляем…' : 'Оформить заказ'}
      </button>
    </form>
  );
}
