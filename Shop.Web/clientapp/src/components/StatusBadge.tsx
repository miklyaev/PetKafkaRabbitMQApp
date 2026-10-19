import type { OrderStatus } from '../types';

const BADGES: Record<OrderStatus, { label: string; className: string }> = {
  Accepted: { label: 'Принят', className: 'border-orange-500/30 bg-orange-500/10 text-orange-400' },
  Completed: { label: 'Завершён', className: 'border-zinc-700 bg-zinc-800 text-zinc-300' },
  Cancelled: { label: 'Отменён', className: 'border-rose-500/20 bg-rose-500/10 text-rose-400' },
};

export function StatusBadge({ status }: { status: OrderStatus }) {
  const badge = BADGES[status] ?? BADGES.Accepted;
  return (
    <span className={`inline-flex items-center gap-2 rounded-none border px-2.5 py-1 text-xs font-bold uppercase tracking-wider ${badge.className}`}>
      <span className="h-1.5 w-1.5 bg-current" aria-hidden="true" />
      {badge.label}
    </span>
  );
}
