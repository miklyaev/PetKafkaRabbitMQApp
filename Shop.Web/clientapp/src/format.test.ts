import { describe, expect, it } from 'vitest';
import { formatRub } from './format';

// formatRub — обёртка над Intl.NumberFormat('ru-RU'): деньги, без копеек.
// Проверяем форму вывода, а не точную строку: Intl вставляет неразрывный
// пробел (U+00A0) в разделитель разрядов, и он отличается между версиями
// ICU в Node — точное toBe('6 990 ₽') сделало бы тест хрупким.
describe('formatRub', () => {
  it('добавляет знак рубля', () => {
    expect(formatRub(6990)).toContain('₽');
  });

  it('группирует разряды: «6 990», а не «6990»', () => {
    const formatted = formatRub(6990);
    expect(formatted).toContain('990');
    expect(formatted).not.toContain('6990');
  });

  it('не показывает копейки (maximumFractionDigits: 0)', () => {
    const formatted = formatRub(1490.5);
    expect(formatted).toContain('₽');
    expect(formatted).not.toContain(',');
  });
});
