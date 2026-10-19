import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { Product } from '../types';
import { OrderForm } from './OrderForm';

// Тестовый «каталог»: те же товары, что даёт реальный GET /api/catalog,
// но локально — OrderForm не должен знать, откуда пришёл список продуктов.
const products: Product[] = [
  { name: 'Кофе в зёрнах, 1 кг', price: 1490, icon: '☕' },
  { name: 'Мышь беспроводная', price: 1990, icon: '🖱️' },
];

// Кнопки «+/−» адресуются по aria-label («Добавить «имя»»/«Убрать «имя»») —
// это и есть контракты доступности компонента, заодно стабильные селекторы.
const plus = (name: string) => screen.getByRole('button', { name: `Добавить «${name}»` });
const minus = (name: string) => screen.getByRole('button', { name: `Убрать «${name}»` });

describe('OrderForm', () => {
  it('рисует каталог, кнопки «−» заблокированы при нуле, сабмит — без товаров', () => {
    render(<OrderForm products={products} onSubmit={vi.fn()} />);
    expect(screen.getByText('Кофе в зёрнах, 1 кг')).toBeInTheDocument();
    expect(screen.getByText('Мышь беспроводная')).toBeInTheDocument();
    // из каталога ещё не выбрано ничего → «Убрать» неактивна, «Оформить» disabled
    expect(minus(products[0].name)).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Оформить заказ' })).toBeDisabled();
  });

  it('отклоняет заказ без имени клиента', async () => {
    const onSubmit = vi.fn();
    const user = userEvent.setup();
    render(<OrderForm products={products} onSubmit={onSubmit} />);

    // товар выбран, имени нет → кнопка активна, но валидация внутри должна откатить
    await user.click(plus(products[0].name));
    await user.click(screen.getByRole('button', { name: 'Оформить заказ' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Укажите имя клиента.');
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('отклоняет заказ без позиций, даже если имя заполнено', async () => {
    const onSubmit = vi.fn();
    const user = userEvent.setup();
    const { container } = render(<OrderForm products={products} onSubmit={onSubmit} />);

    await user.type(screen.getByLabelText('Имя клиента'), 'Иван');

    // Товаров нет → кнопка disabled, кликнуть её нельзя. Но submit всё равно
    // достижим: Enter в текстовом поле отправляет форму (браузер не смотрит,
    // disabled ли submit-кнопка). Поэтому дёргаем событие формы напрямую.
    fireEvent.submit(container.querySelector('form')!);

    expect(await screen.findByRole('alert')).toHaveTextContent('Добавьте хотя бы один товар.');
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('успешный заказ: верные аргументы onSubmit, пересчёт итога, очистка формы', async () => {
    const onSubmit = vi.fn().mockResolvedValue(undefined);
    const user = userEvent.setup();
    render(<OrderForm products={products} onSubmit={onSubmit} />);

    await user.type(screen.getByLabelText('Имя клиента'), 'Иван');
    await user.click(plus(products[0].name)); // 1 × кофе
    await user.click(plus(products[1].name)); // 1 × мышь
    await user.click(plus(products[1].name)); // 2 × мышь

    // Итог пересчитывается по количествам (useMemo): 1490 + 1990×2 = 5 470.
    // В Intl-строке неразрывный пробел (U+00A0) — он покрыт \s, поэтому регекс.
    expect(screen.getByText(/5\s470\s₽/)).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Оформить заказ' }));

    await waitFor(() =>
      expect(onSubmit).toHaveBeenCalledWith('Иван', [
        { productName: 'Кофе в зёрнах, 1 кг', quantity: 1 },
        { productName: 'Мышь беспроводная', quantity: 2 },
      ]),
    );
    // после успеха форма готова к следующему заказу
    expect(screen.getByLabelText('Имя клиента')).toHaveValue('');
    expect(minus(products[0].name)).toBeDisabled();
  });

  it('ошибка onSubmit показывается, введённые данные не теряются', async () => {
    // Пользователь не должен перепечатывать заказ из-за 500-й бэкенда:
    // при ошибке форма сохраняет имя и корзину.
    const onSubmit = vi.fn().mockRejectedValue(new Error('Сервис заказов недоступен'));
    const user = userEvent.setup();
    render(<OrderForm products={products} onSubmit={onSubmit} />);

    await user.type(screen.getByLabelText('Имя клиента'), 'Иван');
    await user.click(plus(products[0].name));
    await user.click(screen.getByRole('button', { name: 'Оформить заказ' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Сервис заказов недоступен');
    expect(screen.getByLabelText('Имя клиента')).toHaveValue('Иван');
    expect(minus(products[0].name)).toBeEnabled();
  });

  it('ограничивает количество сверху MAX_QTY = 99', () => {
    render(<OrderForm products={products} onSubmit={vi.fn()} />);
    const btn = plus(products[0].name);

    // 99 кликов по «+»: счётчик доходит до MAX_QTY и кнопка блокируется.
    // fireEvent, а не userEvent: сотня «умных» кликов медленная, а семантика
    // (клик по enabled-кнопке) здесь ровно та же.
    for (let i = 0; i < 99; i++) fireEvent.click(btn);

    expect(btn).toBeDisabled();
    expect(screen.getByText('99')).toBeInTheDocument();
  });
});
