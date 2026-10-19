const rub = new Intl.NumberFormat('ru-RU', {
  style: 'currency',
  currency: 'RUB',
  maximumFractionDigits: 0,
});

/** Форматирует сумму в рублях: 6 990 ₽. */
export const formatRub = (value: number): string => rub.format(value);
