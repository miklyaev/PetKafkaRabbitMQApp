/// <reference types="vitest" />
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

// Адрес Kestrel из launchSettings.json (Shop.Web).
const backend = 'http://localhost:5080';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    proxy: {
      '/api': backend,
      // SignalR работает на WebSocket — проксируем с ws.
      '/hubs': { target: backend, ws: true },
    },
  },
  build: {
    outDir: '../wwwroot',
    emptyOutDir: false,
    rollupOptions: {
      // Энтрипоинт — main.tsx (index.html не билдим): фиксированные имена app.js/app.css,
      // чтобы MVC-вью (_Layout.cshtml) ссылался на стабильные пути.
      input: 'src/main.tsx',
      output: {
        entryFileNames: 'app.js',
        chunkFileNames: 'assets/[name].js',
        assetFileNames: 'app[extname]',
      },
    },
  },
  // Настройки Vitest (юнит-тесты компонентов). Vitest читает этот же конфиг,
  // поэтому отдельный vitest.config.ts не нужен.
  test: {
    // DOM-окружение: React-компонентам нужен document/window (по умолчанию — node).
    environment: 'jsdom',
    // Явные импорты describe/it/expect вместо глобальных: tsconfig держит
    // "types": ["vite/client"], и расширять его под vitest/globals не хочется.
    globals: false,
    // Матчеры jest-dom (toBeInTheDocument и др.) + авто-cleanup между тестами.
    setupFiles: ['src/test/setup.ts'],
    // Tailwind-стили в тестах не тянем: классы проверяем строками, не пикселями.
    css: false,
  },
});
