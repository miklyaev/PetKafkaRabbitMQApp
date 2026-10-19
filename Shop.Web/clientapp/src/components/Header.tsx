export function Header({ connected }: { connected: boolean }) {
  return (
    <header className="sticky top-0 z-20 border-b border-slate-200 bg-white/80 backdrop-blur">
      <div className="mx-auto flex h-16 max-w-6xl items-center justify-between px-4">
        <div className="flex items-center gap-3">
          <div
            className="flex h-10 w-10 items-center justify-center rounded-xl bg-gradient-to-br from-indigo-600 to-violet-600 text-xl shadow-sm"
            aria-hidden="true"
          >
            🛒
          </div>
          <div>
            <h1 className="text-lg font-bold leading-tight">MiniShop</h1>
            <p className="text-xs text-slate-500">Kafka × RabbitMQ · MassTransit · SignalR</p>
          </div>
        </div>

        <div
          className={`flex items-center gap-2 rounded-full border px-3 py-1 text-xs font-medium ${
            connected ? 'border-emerald-200 bg-emerald-50 text-emerald-700' : 'border-rose-200 bg-rose-50 text-rose-700'
          }`}
          role="status"
        >
          <span
            className={`h-2 w-2 rounded-full ${connected ? 'bg-emerald-500' : 'animate-pulse bg-rose-500'}`}
            aria-hidden="true"
          />
          {connected ? 'Уведомления: на связи' : 'Уведомления: нет связи'}
        </div>
      </div>
    </header>
  );
}
