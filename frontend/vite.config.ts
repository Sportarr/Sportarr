import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import path from 'path';

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  build: {
    outDir: path.resolve(__dirname, '../_output/UI'),
    emptyOutDir: true,
    rollupOptions: {
      output: {
        // React, the router and the query client never change between
        // Sportarr releases, but they used to sit in the same file as the
        // app, so every update made the browser fetch all of them again.
        // Splitting them out lets an update download only what changed.
        // Matched on the resolved path, not the bare name: the app
        // imports react-dom/client, which a name list does not catch.
        manualChunks(id: string) {
          if (!id.includes('node_modules')) return undefined;
          if (/[\\/]node_modules[\\/](react|react-dom|scheduler|react-router|react-router-dom)[\\/]/.test(id)) {
            return 'vendor-react';
          }
          if (id.includes('node_modules/@tanstack/')) return 'vendor-query';
          if (id.includes('node_modules/axios/')) return 'vendor-http';
          return undefined;
        },
      },
    },
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
