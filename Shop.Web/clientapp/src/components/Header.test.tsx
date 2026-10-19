import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { Header } from './Header';

describe('Header', () => {
  it('сообщает «на связи», когда SignalR подключён', () => {
    render(<Header connected />);
    // role="status" — индикатор живости уведомлений; проверяем через роль,
    // а не className: текст — это и есть поведение компонента.
    expect(screen.getByRole('status')).toHaveTextContent('Уведомления: на связи');
  });

  it('сообщает «нет связи», когда подключения нет', () => {
    render(<Header connected={false} />);
    expect(screen.getByRole('status')).toHaveTextContent('Уведомления: нет связи');
  });
});
