import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { Spinner } from './Spinner';

// Spinner не имеет пропсов и используется только внутри кнопки «Оформляем…».
// Единственное поведенческое требование — не орать в скринридер: рядом есть текст
// «Оформляем…», спиннер с ним конкурировать не должен (отсюда aria-hidden).
describe('Spinner', () => {
  it('скрыт от скринридеров (aria-hidden)', () => {
    const { container } = render(<Spinner />);
    expect(container.firstChild).toHaveAttribute('aria-hidden', 'true');
  });
});
