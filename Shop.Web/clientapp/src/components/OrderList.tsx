import { formatRub } from '../format';
import type { OrderSummary } from '../types';
import { StatusBadge } from './StatusBadge';

interface Props {
  orders: OrderSummary[];
  onChangeStatus: (id: string, action: 'complete' | 'cancel') => Promise<void>;
}

const timeFormat = new Intl.DateTimeFormat('ru-RU', {
  day: '2-digit',
  month: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
});

export function OrderList({ orders, onChangeStatus }: Props) {
  return (
    <section className="rounded-xl border border-slate-200 bg-white p-6 shadow-sm">
      <div className="mb-4 flex items-center justify-between">
        <div>
          <h2 className="text-base font-semibold">Заказы</h2>
          <p className="mt-0.5 text-sm text-slate-500">Список обновляется по SignalR и раз в 15 секунд.</p>
        </div>
        <span className="rounded-full bg-slate-100 px-2.5 py-0.5 text-xs font-medium text-slate-600">
          {orders.length}
        </span>
      </div>

      {orders.length === 0 ? (
        <EmptyState />
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead>
              <tr className="border-b border-slate-200 text-xs uppercase tracking-wider text-slate-500">
                <th className="py-2 pr-4 font-medium">Клиент</th>
                <th className="py-2 pr-4 font-medium">Позиции</th>
                <th className="py-2 pr-4 font-medium">Сумма</th>
                <th className="py-2 pr-4 font-medium">Статус</th>
                <th className="py-2 pr-4 font-medium">Создан</th>
                <th className="py-2 text-right font-medium">Действия</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {orders.map(order => (
                <tr key={order.id} className="transition-colors hover:bg-slate-50">
                  <td className="py-3 pr-4 font-medium">{order.customerName}</td>
                  <td className="max-w-56 truncate py-3 pr-4 text-slate-500">{order.items.join(', ')}</td>
                  <td className="py-3 pr-4 font-semibold tabular-nums">{formatRub(order.totalAmount)}</td>
                  <td className="py-3 pr-4">
                    <StatusBadge status={order.status} />
                  </td>
                  <td className="py-3 pr-4 tabular-nums text-slate-500">
                    {timeFormat.format(new Date(order.createdAt))}
                  </td>
                  <td className="py-3 text-right">
                    {order.status === 'Accepted' ? (
                      <div className="inline-flex gap-2">
                        <button
                          type="button"
                          onClick={() => void onChangeStatus(order.id, 'complete')}
                          className="rounded-lg border border-emerald-200 bg-emerald-50 px-3 py-1.5 text-xs font-semibold text-emerald-700 transition hover:bg-emerald-100 focus-visible:outline focus-visible:outline-2 focus-visible:outline-emerald-500"
                        >
                          Завершить
                        </button>
                        <button
                          type="button"
                          onClick={() => void onChangeStatus(order.id, 'cancel')}
                          className="rounded-lg border border-rose-200 bg-rose-50 px-3 py-1.5 text-xs font-semibold text-rose-700 transition hover:bg-rose-100 focus-visible:outline focus-visible:outline-2 focus-visible:outline-rose-500"
                        >
                          Отменить
                        </button>
                      </div>
                    ) : (
                      <span className="text-xs text-slate-400">—</span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}

function EmptyState() {
  return (
    <div className="flex flex-col items-center gap-3 py-16 text-center">
      <span className="text-4xl" aria-hidden="true">
        🧺
      </span>
      <p className="font-medium">Заказов пока нет</p>
      <p className="text-sm text-slate-500">
        Оформите первый заказ в форме слева — увидите полный цикл: RabbitMQ → Orders → Kafka → Notifications → SignalR.
      </p>
    </div>
  );
}
