import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  base: './',
  build: { outDir: 'dist', assetsDir: 'assets' },
  // 开发服务器：API 走代理转发到 Python 核心（默认 18760，被占用时会自动顺延）
  server: {
    port: 5173,
    host: '127.0.0.1',
    proxy: {
      '/api': {
        target: 'http://127.0.0.1:18760',
        changeOrigin: true,
        // 开机晚于前端时，不要把 502 直接抛给页面
        onError: (err, _req, res) => {
          res.statusCode = 502
          res.end(JSON.stringify({ ok: false, error: 'core-unreachable' }))
        },
      },
    },
  }
})
