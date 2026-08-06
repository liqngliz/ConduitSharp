import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig(() => {
  const isProfile = process.env.VITE_PROFILE === 'true';

  return {
    plugins: [react()],
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
