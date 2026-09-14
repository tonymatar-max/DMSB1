import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      // The SPA talks to a same-origin /api in every environment; the proxy keeps dev honest with
      // the production shape, where the API serves this build directly from wwwroot.
      '/api': { target: 'http://localhost:5210', changeOrigin: true },
    },
  },
})
