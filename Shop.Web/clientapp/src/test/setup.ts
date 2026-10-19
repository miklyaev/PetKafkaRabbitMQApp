// Настройка окружения тестов (подключается через test.setupFiles в vite.config.ts).
import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterEach } from 'vitest';

// RTL выполняет авто-cleanup только при наличии ГЛОБАЛЬНОГО afterEach.
// Vitest у нас запущен с globals: false — глобального хука нет, поэтому
// размонтируем компоненты вручную: иначе DOM от одного теста «протекал» бы
// в следующий (getByText находил бы элементы чужого рендера).
afterEach(() => cleanup());
