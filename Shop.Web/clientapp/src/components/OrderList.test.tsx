import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { OrderSummary } from '../types';
import { OrderList } from './OrderList';

// createdAt — строка ISO БЕЗ суффикса Z (так DateTime отдаёт бэкенд):
// она парсится как локальное время, поэтому тест не зависит от TZ машины.
const base: OrderSummary = {
  id: 'o1',
  customerName: 'Иван',
  status: 'Accepted',
  totalAmount: 4970,
  createdAt: '2026-10-14T02:57:19.9042406',
  items: ['Кофе в зёрнах, 1 кг × 2', 'Мышь беспроводная × 1'],
};
const accepted = base;
const completed: OrderSummary = {
  ...base,
  id: 'o2',
  customerName: 'Мария',
  status: 'Completed',
  items: ['Наушники беспроводные × 1'],
};
const cancelled: OrderSummary = {
  ...base,
  id: 'o3',
  customerName: 'Пётр',
  status: 'Cancelled',
  items: ['SSD 1 ТБ × 3'],
};

describe('OrderList', () => {
  it('пустой список → пустое состояние и счётчик 0', () => {
    render(<OrderList orders={[]} onChangeStatus={vi.fn()} />);
    expect(screen.getByText('Заказов пока нет')).toBeInTheDocument();
    expect(screen.getByText('0')).toBeInTheDocument();
  });

  it('рисует строки всех заказов и счётчик количества', () => {
    render(<OrderList orders={[accepted, completed, cancelled]} onChangeStatus={vi.fn()} />);
    expect(screen.getByText('Иван')).toBeInTheDocument();
    expect(screen.getByText('Мария')).toBeInTheDocument();
    expect(screen.getByText('Пётр')).toBeInTheDocument();
    // позиции заказа склеены в одну ячейку (items.join(', ') в компоненте)
    expect(
      screen.getByText('Кофе в зёрнах, 1 кг × 2, Мышь беспроводная × 1'),
    ).toBeInTheDocument();
    expect(screen.getByText('3')).toBeInTheDocument();
  });

  it('дату приводит к «дд.мм, чч:мм», а не сырой ISO-строке', () => {
    render(<OrderList orders={[accepted]} onChangeStatus={vi.fn()} />);
    // Регекс вместо точной строки: час-минуты зависят от таймзоны машины,
    // а вот форма «14.10, 02:57» должна быть стабильной.
    expect(screen.getByText(/\d{2}\.\d{2}, \d{2}:\d{2}/)).toBeInTheDocument();
    expect(screen.queryByText(accepted.createdAt)).not.toBeInTheDocument();
  });

  it('действия доступны только активным заказам', () => {
    render(<OrderList orders={[accepted, completed, cancelled]} onChangeStatus={vi.fn()} />);
    expect(screen.getByRole('button', { name: 'Завершить' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Отменить' })).toBeEnabled();
    // Completed и Cancelled не имеют переходов → прочерк вместо кнопок
    expect(screen.getAllByText('—')).toHaveLength(2);
  });

  it('кнопки зовут onChangeStatus(id, action) — контракт App', async () => {
    const onChangeStatus = vi.fn();
    const user = userEvent.setup();
    render(<OrderList orders={[accepted]} onChangeStatus={onChangeStatus} />);

    await user.click(screen.getByRole('button', { name: 'Завершить' }));
    expect(onChangeStatus).toHaveBeenCalledWith('o1', 'complete');

    await user.click(screen.getByRole('button', { name: 'Отменить' }));
    expect(onChangeStatus).toHaveBeenCalledWith('o1', 'cancel');
  });
});
