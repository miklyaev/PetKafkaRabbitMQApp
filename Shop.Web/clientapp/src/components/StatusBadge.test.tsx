import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { OrderStatus } from '../types';
import { StatusBadge } from './StatusBadge';

// Статус приходит по сети (RabbitMQ ← Shop.Notifications), и тип OrderStatus —
// это лишь OUR ожидание: в рантайме строка может оказаться любой. Поэтому
// BADGES[status] ?? BADGES.Accepted в компоненте — не «лишний» код, а защита,
// и её мы покрываем тестом на неизвестный статус.
describe('StatusBadge', () => {
  it.each([
    ['Accepted', 'Принят'],
    ['Completed', 'Завершён'],
    ['Cancelled', 'Отменён'],
  ] as const)('статус %s рисует бейдж «%s»', (status, label) => {
    render(<StatusBadge status={status} />);
    expect(screen.getByText(label)).toBeInTheDocument();
  });

  it('неизвестный статус падает на безопасный дефолт «Принят»', () => {
    // Компонент получит строку вне Union-типа: каст «as unknown as» имитирует
    // ровно то, что происходит в рантайме при рассинхроне контрактов.
    render(<StatusBadge status={'Deleted' as unknown as OrderStatus} />);
    expect(screen.getByText('Принят')).toBeInTheDocument();
  });
});
