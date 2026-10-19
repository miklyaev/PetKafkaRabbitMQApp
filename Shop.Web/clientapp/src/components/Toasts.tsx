export interface Toast {
  id: number;
  kind: 'info' | 'success' | 'danger';
  title: string;
  text: string;
}

const BORDER: Record<Toast['kind'], string> = {
  info: 'border-l-indigo-500',
  success: 'border-l-emerald-500',
  danger: 'border-l-rose-500',
};

interface Props {
  toasts: Toast[];
  onClose: (id: number) => void;
}

export function Toasts({ toasts, onClose }: Props) {
  return (
    <div className="pointer-events-none fixed bottom-4 right-4 z-50 flex w-80 flex-col gap-2" aria-live="polite">
      {toasts.map(toast => (
        <div
          key={toast.id}
          className={`animate-toast-in pointer-events-auto rounded-lg border border-slate-200 border-l-4 bg-white p-4 shadow-lg ${BORDER[toast.kind]}`}
        >
          <div className="flex items-start gap-3">
            <div className="min-w-0 flex-1">
              <p className="text-sm font-semibold text-slate-900">{toast.title}</p>
              <p className="mt-0.5 text-sm text-slate-600">{toast.text}</p>
            </div>
            <button
              type="button"
              onClick={() => onClose(toast.id)}
              aria-label="Закрыть уведомление"
              className="rounded p-0.5 text-slate-400 transition hover:text-slate-700 focus-visible:outline focus-visible:outline-2 focus-visible:outline-indigo-500"
            >
              <span aria-hidden="true">✕</span>
            </button>
          </div>
        </div>
      ))}
    </div>
  );
}
