import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig(() => {
  const isProfile = process.env.VITE_PROFILE === 'true';

  return {
    plugins: [react()],
    // `npm run dev` serves on 5173 but the spend API and the SSE stream live on the
    // gateway's port. Without this the dashboard 404s on every /api call in dev.
    server: {
      proxy: {
        '/api': { target: 'http://localhost:4000', changeOrigin: true },
      },
    },
    resolve: {
      alias: isProfile ? {
        'react-dom/client': 'react-dom/profiling',
      } : undefined,
    },
    build: {
      outDir: '../wwwroot',
      emptyOutDir: true,
    }
  };
})
