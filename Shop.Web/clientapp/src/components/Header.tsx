import { NavLink } from "react-router-dom";

export function Header({ connected }: { connected: boolean }) {
  return (
    <header className="sticky top-0 z-20 border-b border-zinc-800 bg-zinc-950/80 backdrop-blur-md">
      <div className="mx-auto flex h-20 max-w-6xl items-center justify-between px-4">
        <div className="group flex items-center gap-4 cursor-default">
          <div
            className="relative flex h-12 w-12 items-center justify-center overflow-hidden border border-zinc-700 bg-zinc-950 group-hover:border-orange-500/50 transition-colors duration-500"
            aria-hidden="true"
          >
            {/* AI Grid Background */}
            <div className="absolute inset-0 bg-[radial-gradient(circle_at_center,rgba(255,255,255,0.15)_1px,transparent_1px)] bg-[size:4px_4px]" />

            {/* AI Scan Line (activates on hover) */}
            <div className="absolute left-0 -top-full h-full w-full bg-gradient-to-b from-transparent via-orange-500/10 to-orange-500/30 border-b border-orange-500/50 group-hover:translate-y-[200%] transition-transform duration-[1500ms] ease-in-out" />

            {/* Letter with Glow */}
            <span className="relative z-10 text-2xl font-black text-zinc-100 group-hover:text-orange-400 transition-colors duration-300 drop-shadow-[0_0_8px_rgba(255,255,255,0.4)] group-hover:drop-shadow-[0_0_12px_rgba(249,115,22,0.8)]">
              M
            </span>

            {/* Processing Indicators */}
            <div className="absolute bottom-1 right-1 flex gap-[2px]">
              <div className="h-1 w-1 bg-orange-500 animate-ping [animation-duration:1.5s]" />
              <div className="h-1 w-1 bg-orange-500 animate-ping [animation-duration:1.5s] [animation-delay:400ms]" />
            </div>
          </div>
          <div>
            <h1 className="text-xl font-bold tracking-tight text-zinc-50 flex items-center gap-2">
              MINISHOP
              <span className="rounded bg-orange-500/10 px-1.5 py-0.5 text-[9px] font-mono text-orange-500 border border-orange-500/20 uppercase tracking-widest flex items-center gap-1.5 shadow-[0_0_10px_rgba(249,115,22,0.1)]">
                <span className="relative flex h-1.5 w-1.5">
                  <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-orange-400 opacity-75"></span>
                  <span className="relative inline-flex rounded-full h-1.5 w-1.5 bg-orange-500"></span>
                </span>
                AI Core
              </span>
            </h1>
            <p className="text-xs font-medium tracking-wide text-zinc-500 uppercase mt-0.5 flex items-center gap-1.5">
              <span>Kafka</span>
              <span className="text-zinc-700">×</span>
              <span>RabbitMQ</span>
              <span className="text-zinc-700">·</span>
              <span>MassTransit</span>
            </p>
          </div>
        </div>

        <div
          className={`flex items-center gap-2.5 rounded-none border px-4 py-1.5 text-xs font-bold uppercase tracking-wider ${
            connected
              ? "border-zinc-800 bg-zinc-900 text-zinc-400"
              : "border-rose-900/50 bg-rose-950/30 text-rose-500"
          }`}
          role="status"
        >
          <span
            className={`h-2 w-2 ${connected ? "bg-orange-500" : "animate-pulse bg-rose-500"}`}
            aria-hidden="true"
          />
          {connected ? "Уведомления: на связи" : "Уведомления: нет связи"}
        </div>
      </div>
      <nav
        aria-label="Основная навигация"
        className="mx-auto flex max-w-6xl flex-wrap gap-2 px-4 pb-3"
      >
        {[
          ["/", "Заказы"],
          ["/brokers/rabbitmq", "RabbitMQ"],
          ["/brokers/kafka", "Kafka"],
          ["/brokers/flow", "Путь сообщения"],
        ].map(([to, label]) => (
          <NavLink
            key={to}
            to={to}
            end
            className={({ isActive }) =>
              `px-3 py-2 text-sm border focus-visible:outline-2 focus-visible:outline-orange-400 ${isActive ? "border-orange-500 text-orange-400" : "border-zinc-800 text-zinc-300 hover:border-zinc-500"}`
            }
          >
            {label}
          </NavLink>
        ))}
      </nav>
    </header>
  );
}
