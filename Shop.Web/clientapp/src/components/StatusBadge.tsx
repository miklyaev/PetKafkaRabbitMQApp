import type { OrderStatus } from '../types';

const BADGES: Record<OrderStatus, { label: string; className: string }> = {
  Accepted: { label: 'Принят', className: 'border-indigo-200 bg-indigo-50 text-indigo-700' },
  Completed: { label: 'Завершён', className: 'border-emerald-200 bg-emerald-50 text-emerald-700' },
  Cancelled: { label: 'Отменён', className: 'border-rose-200 bg-rose-50 text-rose-700' },
};

export function StatusBadge({ status }: { status: OrderStatus }) {
  const badge = BADGES[status] ?? BADGES.Accepted;
  return (
    <span className={`inline-flex items-center gap-1.5 rounded-full border px-2.5 py-0.5 text-xs font-medium ${badge.className}`}>
      <span className="h-1.5 w-1.5 rounded-full bg-current" aria-hidden="true" />
      {badge.label}
    </span>
  );
}
