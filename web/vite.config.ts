import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// In development the API is proxied to a running bot (set VITE_API to its address).
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: process.env.VITE_API || 'http://127.0.0.1:8080', changeOrigin: false },
      '^/c/[^/]+/api/': { target: process.env.VITE_API || 'http://127.0.0.1:8080', changeOrigin: false },
    },
  },
  build: { outDir: 'dist', sourcemap: false, rollupOptions: { output: { manualChunks: { charts: ['recharts'], react: ['react', 'react-dom', 'react-router-dom'] } } } },
  preview: { port: 4173 },
})
