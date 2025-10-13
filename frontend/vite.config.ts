import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import path from 'path';

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  build: {
    outDir: path.resolve(__dirname, '../_output/UI'),
    emptyOutDir: true,
  },
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:1867',
        changeOrigin: true,
      },
      '/initialize.json': {
        target: 'http://localhost:1867',
        changeOrigin: true,
      },
    },
  },
})
