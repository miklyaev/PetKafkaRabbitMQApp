import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { Toasts, type Toast } from './Toasts';

const makeToast = (over: Partial<Toast> = {}): Toast => ({
  id: 1,
  kind: 'info',
  title: 'Заказ оформлен',
  text: '№ 122bf004',
  ...over,
});

describe('Toasts', () => {
  it('рисует переданные уведомления', () => {
    render(
      <Toasts
        toasts={[makeToast(), makeToast({ id: 2, kind: 'success', title: 'Заказ завершён' })]}
        onClose={vi.fn()}
      />,
    );
    expect(screen.getByText('Заказ оформлен')).toBeInTheDocument();
    expect(screen.getByText('Заказ завершён')).toBeInTheDocument();
  });

  it('закрывает уведомление: onClose вызывается с id, а не событием', async () => {
    const onClose = vi.fn();
    const user = userEvent.setup();
    render(<Toasts toasts={[makeToast()]} onClose={onClose} />);

    await user.click(screen.getByRole('button', { name: 'Закрыть уведомление' }));
    // Контракт родителя (App.tsx): handleCloseToast(id: number).
    // Проверяем именно аргумент — защита от «onclick(e.target.id)»-регрессий.
    expect(onClose).toHaveBeenCalledTimes(1);
    expect(onClose).toHaveBeenCalledWith(1);
  });

  it('красит карточку по kind уведомления', () => {
    const { container } = render(
      <Toasts toasts={[makeToast({ kind: 'danger' })]} onClose={vi.fn()} />,
    );
    // kind → класс акцента на карточке (BORDER в компоненте).
    expect(container.querySelector('.border-l-rose-500')).not.toBeNull();
  });
});
