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
    <section className="rounded-none border border-zinc-800 bg-zinc-900 p-8 h-fit">
      <div className="mb-6 flex items-start justify-between border-b border-zinc-800 pb-4">
        <div>
          <h2 className="text-xl font-bold tracking-tight text-zinc-50 uppercase">Заказы</h2>
          <p className="mt-1 text-xs font-medium tracking-wide text-zinc-500 uppercase">Список обновляется по SignalR и раз в 15 секунд.</p>
        </div>
        <span className="rounded-none bg-zinc-800 px-3 py-1 text-xs font-bold text-zinc-300">
          {orders.length}
        </span>
      </div>

      {orders.length === 0 ? (
        <EmptyState />
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm whitespace-nowrap">
            <thead>
              <tr className="border-b border-zinc-800 text-xs font-bold uppercase tracking-wider text-zinc-500">
                <th className="py-3 pr-4">Клиент</th>
                <th className="py-3 pr-4">Позиции</th>
                <th className="py-3 pr-4">Сумма</th>
                <th className="py-3 pr-4">Статус</th>
                <th className="py-3 pr-4">Создан</th>
                <th className="py-3 text-right">Действия</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-zinc-800/50">
              {orders.map(order => (
                <tr key={order.id} className="transition-colors hover:bg-zinc-800/30 group">
                  <td className="py-4 pr-4 font-semibold text-zinc-200">{order.customerName}</td>
                  <td className="max-w-[200px] truncate py-4 pr-4 text-zinc-400" title={order.items.join(', ')}>
                    {order.items.join(', ')}
                  </td>
                  <td className="py-4 pr-4 font-bold tabular-nums text-zinc-100">{formatRub(order.totalAmount)}</td>
                  <td className="py-4 pr-4">
                    <StatusBadge status={order.status} />
                  </td>
                  <td className="py-4 pr-4 tabular-nums text-zinc-500 font-medium">
                    {timeFormat.format(new Date(order.createdAt))}
                  </td>
                  <td className="py-4 text-right">
                    {order.status === 'Accepted' ? (
                      <div className="inline-flex gap-2 opacity-0 transition-opacity group-hover:opacity-100">
                        <button
                          type="button"
                          onClick={() => void onChangeStatus(order.id, 'complete')}
                          className="rounded-none border border-zinc-700 bg-zinc-950 px-3 py-1.5 text-xs font-bold uppercase tracking-wider text-zinc-300 transition hover:border-zinc-500 hover:text-white focus-visible:outline focus-visible:outline-2 focus-visible:outline-orange-500"
                        >
                          Завершить
                        </button>
                        <button
                          type="button"
                          onClick={() => void onChangeStatus(order.id, 'cancel')}
                          className="rounded-none border border-rose-900/50 bg-zinc-950 px-3 py-1.5 text-xs font-bold uppercase tracking-wider text-rose-500 transition hover:border-rose-500 hover:bg-rose-500/10 focus-visible:outline focus-visible:outline-2 focus-visible:outline-rose-500"
                        >
                          Отменить
                        </button>
                      </div>
                    ) : (
                      <span className="text-xs text-zinc-600 font-bold">—</span>
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
    <div className="flex flex-col items-center gap-4 py-20 text-center">
      <div className="flex h-16 w-16 items-center justify-center border border-zinc-800 bg-zinc-950 text-2xl text-zinc-600 grayscale">
        🧺
      </div>
      <div>
        <p className="text-lg font-bold tracking-tight text-zinc-200">Заказов пока нет</p>
        <p className="mt-2 max-w-sm text-xs font-medium leading-relaxed text-zinc-500">
          Оформите первый заказ в форме слева — увидите полный цикл: RabbitMQ → Orders → Kafka → Notifications → SignalR.
        </p>
      </div>
    </div>
  );
}
