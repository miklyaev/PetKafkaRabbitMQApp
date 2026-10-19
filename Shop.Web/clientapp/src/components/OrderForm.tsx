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
      className="flex h-fit flex-col gap-5 rounded-none border border-zinc-800 bg-zinc-900 p-8"
    >
      <div className="border-b border-zinc-800 pb-4">
        <h2 className="text-xl font-bold tracking-tight text-zinc-50 uppercase">Новый заказ</h2>
        <p className="mt-1 text-xs font-medium tracking-wide text-zinc-500 uppercase">Команда — RabbitMQ, событие — Kafka.</p>
      </div>

      <label className="block mt-2">
        <span className="mb-2 block text-xs font-bold uppercase tracking-wider text-zinc-400">Имя клиента</span>
        <input
          type="text"
          value={customerName}
          onChange={e => setCustomerName(e.target.value)}
          placeholder="Иван Петров"
          maxLength={100}
          autoComplete="off"
          className="w-full rounded-none border border-zinc-700 bg-zinc-950 px-4 py-3 text-sm text-zinc-50 outline-none transition placeholder:text-zinc-600 focus:border-orange-500 focus:ring-1 focus:ring-orange-500"
        />
      </label>

      <ul className="flex flex-col divide-y divide-zinc-800/50 mt-2">
        {products.map(product => {
          const qty = quantities[product.name] ?? 0;
          return (
            <li key={product.name} className="flex items-center gap-4 py-3">
              <span className="flex h-10 w-10 items-center justify-center bg-zinc-950 border border-zinc-800 text-lg grayscale transition-all group-hover:grayscale-0" aria-hidden="true">
                {product.icon}
              </span>
              <div className="min-w-0 flex-1">
                <p className="truncate text-sm font-semibold tracking-tight text-zinc-200">{product.name}</p>
                <p className="text-xs font-medium text-zinc-500">{formatRub(product.price)}</p>
              </div>
              <div className="flex items-center gap-2">
                <button
                  type="button"
                  onClick={() => setQuantity(product.name, qty - 1)}
                  disabled={qty === 0}
                  aria-label={`Убрать «${product.name}»`}
                  className="flex h-8 w-8 items-center justify-center rounded-none border border-zinc-700 bg-zinc-950 text-lg leading-none text-zinc-400 transition hover:border-zinc-500 hover:text-zinc-200 disabled:opacity-30 focus-visible:outline focus-visible:outline-2 focus-visible:outline-orange-500"
                >
                  −
                </button>
                <span className="w-8 text-center text-sm font-bold tabular-nums text-zinc-100">{qty}</span>
                <button
                  type="button"
                  onClick={() => setQuantity(product.name, qty + 1)}
                  disabled={qty === MAX_QTY}
                  aria-label={`Добавить «${product.name}»`}
                  className="flex h-8 w-8 items-center justify-center rounded-none border border-zinc-700 bg-zinc-950 text-lg leading-none text-zinc-400 transition hover:border-zinc-500 hover:text-zinc-200 disabled:opacity-30 focus-visible:outline focus-visible:outline-2 focus-visible:outline-orange-500"
                >
                  +
                </button>
              </div>
            </li>
          );
        })}
      </ul>

      <div className="flex items-center justify-between border-t border-zinc-800 pt-6 mt-2">
        <span className="text-xs font-bold uppercase tracking-wider text-zinc-500">Итого</span>
        <span className="text-xl font-bold tabular-nums tracking-tight text-orange-500">{formatRub(total)}</span>
      </div>

      {error && (
        <p role="alert" className="rounded-none border border-rose-900/50 bg-rose-950/30 px-4 py-3 text-sm text-rose-400">
          {error}
        </p>
      )}

      <button
        type="submit"
        disabled={submitting || !hasItems}
        className="mt-2 inline-flex w-full items-center justify-center gap-3 rounded-none bg-orange-600 px-5 py-3.5 text-sm font-bold uppercase tracking-wider text-white transition hover:bg-orange-500 disabled:cursor-not-allowed disabled:bg-zinc-800 disabled:text-zinc-500 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-orange-500"
      >
        {submitting && <Spinner />}
        {submitting ? 'Оформляем…' : 'Оформить заказ'}
      </button>
    </form>
  );
}
