import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import App from './App';
import type { OrderSummary } from './types';

// Хаб SignalR не поднимаем: jsdom без WebSocket, а транспорт — не предмет
// этого теста (он проверен e2e в браузере). Подменяем хук синхронным false:
// тогда Header честно рисует «нет связи», и тест детерминирован.
vi.mock('./signalr', () => ({ useNotificationHub: vi.fn(() => false) }));

const products = [
  { name: 'Кофе в зёрнах, 1 кг', price: 1490, icon: '☕' },
  { name: 'Мышь беспроводная', price: 1990, icon: '🖱️' },
];
const order: OrderSummary = {
  id: '122bf004-fa2a-4298-a9c6-bfd4284a89bf',
  customerName: 'Иван',
  status: 'Accepted',
  totalAmount: 4970,
  createdAt: '2026-10-14T02:57:19.9042406',
  items: ['Кофе в зёрнах, 1 кг × 2'],
};

// Минимальный мок Response: api.ts использует только ok/status/json,
// поэтому полноценный undici Response для теста не нужен.
const ok = (body: unknown) => ({ ok: true, status: 200, json: async () => body }) as Response;
const bad = (body: unknown) => ({ ok: false, status: 400, json: async () => body }) as Response;

// GET /api/orders отвечает «текущим состоянием мира»: тест подменяет orders
// ДО render, чтобы список пришёл уже с первым опросом.
let orders: OrderSummary[] = [];
let fetchMock: ReturnType<typeof vi.fn>;

beforeEach(() => {
  orders = [];
  fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    if (url === '/api/catalog') return ok(products);
    if (url === '/api/orders' && init?.method !== 'POST') return ok(orders);
    if (url === '/api/orders' && init?.method === 'POST') {
      return ok({ orderId: order.id, status: 'Accepted', totalAmount: 4970 });
    }
    return bad({ title: 'Not Found' });
  });
  vi.stubGlobal('fetch', fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('App', () => {
  it('при старте грузит каталог и рисует пустое состояние заказов', async () => {
    render(<App />);
    // findBy*: первый рендер асинхронно делает GET /api/catalog — ждём.
    expect(await screen.findByText('Мышь беспроводная')).toBeInTheDocument();
    expect(screen.getByText('Заказов пока нет')).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('Уведомления: нет связи');
    // каталог реально пришёл из fetch, а не захардкожен в компоненте
    expect(fetchMock).toHaveBeenCalledWith('/api/catalog');
  });

  it('полный цикл: форма → POST /api/orders → тост успеха → форма чистая', async () => {
    const user = userEvent.setup();
    render(<App />);
    await screen.findByText('Мышь беспроводная');

    await user.type(screen.getByLabelText('Имя клиента'), 'Иван');
    await user.click(screen.getByRole('button', { name: 'Добавить «Кофе в зёрнах, 1 кг»' }));
    await user.click(screen.getByRole('button', { name: 'Оформить заказ' }));

    // тост об успехе ставит App.handleCreate (ответ от RabbitMQ → Shop.Orders)
    expect(await screen.findByText('Заказ оформлен')).toBeInTheDocument();

    // тело POST — camelCase, как ожидает ASP.NET Core (System.Text.Json)
    expect(fetchMock).toHaveBeenCalledWith('/api/orders', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        customerName: 'Иван',
        items: [{ productName: 'Кофе в зёрнах, 1 кг', quantity: 1 }],
      }),
    });

    // после успеха OrderForm очищается — готов к следующему заказу
    expect(screen.getByLabelText('Имя клиента')).toHaveValue('');
  });

  it('ошибка с бэкенда показывается в форме и не глотается App', async () => {
    // POST отвечает 400 + ProblemDetails.detail — как реальный Shop.Web
    // (см. OrdersController: «Товар … не найден в каталоге»).
    fetchMock.mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (url === '/api/catalog') return ok(products);
      if (url === '/api/orders') {
        return init?.method === 'POST'
          ? bad({ detail: 'Товар «Кофе» не найден в каталоге.' })
          : ok([]);
      }
      return bad({ title: 'Not Found' });
    });

    const user = userEvent.setup();
    render(<App />);
    await screen.findByText('Мышь беспроводная');

    await user.type(screen.getByLabelText('Имя клиента'), 'Иван');
    await user.click(screen.getByRole('button', { name: 'Добавить «Кофе в зёрнах, 1 кг»' }));
    await user.click(screen.getByRole('button', { name: 'Оформить заказ' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Товар «Кофе» не найден');
    // ошибка не стёрла введённое: пользователь правит и повторяет
    expect(screen.getByLabelText('Имя клиента')).toHaveValue('Иван');
  });

  it('заказы из GET /api/orders попадают в таблицу с действиями', async () => {
    orders = [order];
    render(<App />);

    // Строка таблицы: клиент, позиции и кнопка завершения активного заказа.
    expect(await screen.findByRole('button', { name: 'Завершить' })).toBeEnabled();
    expect(screen.getByText('Кофе в зёрнах, 1 кг × 2')).toBeInTheDocument();
    // счётчик заказов в шапке списка
    expect(screen.getByText('1')).toBeInTheDocument();
  });
});
