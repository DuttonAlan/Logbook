import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// In development the UI runs on Vite (5173) and proxies API calls to Logbook.Api (5080).
// In production the built files are served by Logbook.Api from wwwroot.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:5080',
      '/health': 'http://localhost:5080',
    },
  },
})
