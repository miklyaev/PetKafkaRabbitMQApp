import { useCallback, useEffect, useRef, useState } from "react";
import {
  BrowserRouter,
  Link,
  Route,
  Routes,
  useLocation,
} from "react-router-dom";
import { DiagnosticsPage } from "./diagnostics/DiagnosticsPage";
import {
  changeOrderStatus,
  createOrder,
  fetchCatalog,
  fetchOrders,
} from "./api";
import { formatRub } from "./format";
import { useNotificationHub } from "./signalr";
import type {
  NotificationPayload,
  OrderStatus,
  OrderSummary,
  Product,
} from "./types";
import { Header } from "./components/Header";
import { OrderForm } from "./components/OrderForm";
import { OrderList } from "./components/OrderList";
import { Toasts, type Toast } from "./components/Toasts";

const STATUS_LABEL: Record<OrderStatus, string> = {
  Accepted: "Заказ принят",
  Completed: "Заказ завершён",
  Cancelled: "Заказ отменён",
};

export default function App() {
  return (
    <BrowserRouter>
      <ShopApp />
    </BrowserRouter>
  );
}

function ShopApp() {
  const { pathname } = useLocation();
  const onOrdersPage = pathname === "/";
  const [products, setProducts] = useState<Product[]>([]);
  const [orders, setOrders] = useState<OrderSummary[]>([]);
  const [toasts, setToasts] = useState<Toast[]>([]);
  const toastId = useRef(0);

  const pushToast = useCallback((toast: Omit<Toast, "id">) => {
    const id = ++toastId.current;
    setToasts((prev) => [...prev, { ...toast, id }]);
    setTimeout(
      () => setToasts((prev) => prev.filter((t) => t.id !== id)),
      6000,
    );
  }, []);

  const refreshOrders = useCallback(async () => {
    if (!onOrdersPage) return;
    try {
      setOrders(await fetchOrders());
    } catch {
      // сервис заказов ещё поднимается — молча попробуем на следующем тике
    }
  }, [onOrdersPage]);

  useEffect(() => {
    if (!onOrdersPage) return;
    fetchCatalog()
      .then(setProducts)
      .catch(() =>
        pushToast({
          kind: "danger",
          title: "Каталог недоступен",
          text: "Не удалось загрузить товары. Проверьте, что Shop.Orders запущен.",
        }),
      );
    void refreshOrders();
    const interval = setInterval(() => void refreshOrders(), 15_000);
    return () => clearInterval(interval);
  }, [onOrdersPage, pushToast, refreshOrders]);

  const handleNotification = useCallback(
    (n: NotificationPayload) => {
      pushToast({
        kind:
          n.status === "Completed"
            ? "success"
            : n.status === "Cancelled"
              ? "danger"
              : "info",
        title: STATUS_LABEL[n.status],
        text: n.text,
      });
      void refreshOrders();
    },
    [pushToast, refreshOrders],
  );

  const connected = useNotificationHub(handleNotification);

  const handleCreate = useCallback(
    async (
      customerName: string,
      items: { productName: string; quantity: number }[],
    ) => {
      const result = await createOrder(customerName, items);
      pushToast({
        kind: "info",
        title: "Заказ оформлен",
        text: `№ ${result.orderId.slice(0, 8)} · ${formatRub(result.totalAmount)} · ответ по RabbitMQ`,
      });
      await refreshOrders();
    },
    [pushToast, refreshOrders],
  );

  const handleStatusChange = useCallback(
    async (id: string, action: "complete" | "cancel") => {
      try {
        await changeOrderStatus(id, action);
      } catch (error) {
        pushToast({
          kind: "danger",
          title: "Не удалось изменить статус",
          text: error instanceof Error ? error.message : "Неизвестная ошибка",
        });
      }
    },
    [pushToast],
  );

  const handleCloseToast = useCallback((id: number) => {
    setToasts((prev) => prev.filter((t) => t.id !== id));
  }, []);

  return (
    <div className="min-h-screen bg-zinc-950 text-zinc-50 font-sans selection:bg-orange-500/30">
      <Header connected={connected} />
      <Routes>
        <Route
          path="/"
          element={
            <main className="mx-auto grid max-w-6xl grid-cols-1 gap-6 px-4 py-8 lg:grid-cols-[400px_minmax(0,1fr)]">
              <OrderForm products={products} onSubmit={handleCreate} />
              <OrderList orders={orders} onChangeStatus={handleStatusChange} />
            </main>
          }
        />
        <Route
          path="/brokers/rabbitmq"
          element={<DiagnosticsPage key="rabbitmq" broker="rabbitmq" />}
        />
        <Route
          path="/brokers/kafka"
          element={<DiagnosticsPage key="kafka" broker="kafka" />}
        />
        <Route path="/brokers/flow" element={<DiagnosticsPage key="flow" />} />
        <Route
          path="*"
          element={
            <main className="mx-auto max-w-6xl p-8">
              <h1>Страница не найдена</h1>
              <Link to="/">Перейти к заказам</Link>
            </main>
          }
        />
      </Routes>
      <Toasts toasts={toasts} onClose={handleCloseToast} />
    </div>
  );
}
