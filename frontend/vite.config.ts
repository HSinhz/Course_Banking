import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Vite dev server tại :5180 (tránh xung đột 5173 của HCM), proxy /api tới backend :5199
// (cùng origin qua proxy -> khỏi lo CORS khi dev). strictPort: báo lỗi thay vì trôi port.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5180,
    strictPort: true,
    proxy: {
      '/api': 'http://localhost:5199',
    },
  },
});
