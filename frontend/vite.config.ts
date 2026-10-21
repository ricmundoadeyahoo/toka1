import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// En desarrollo (npm run dev) las llamadas /api se envían al API Gateway expuesto en el host.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:5000',
      '/connect': 'http://localhost:5000',
      '/.well-known': 'http://localhost:5000'
    }
  }
});
