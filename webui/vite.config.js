import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// Vue 控制台构建配置：相对路径 base（适配 OpenList / 任意子路径挂载）、/api 代理到 Python 核心
export default defineConfig({
  base: './',
  plugins: [vue()],
  server: {
    port: 5173,
    host: '127.0.0.1',
    proxy: {
      '/api': {
        target: 'http://127.0.0.1:18760',
        changeOrigin: true,
        // 核心未启动时返回结构化错误，前端据此降级显示占位，避免白屏
        onError: (_err, _req, res) => {
          res.statusCode = 502
          res.end(JSON.stringify({ ok: false, error: 'core-unreachable' }))
        },
      },
    },
  },
  build: {
    outDir: 'dist',
    assetsDir: 'assets',
    emptyOutDir: true,
  },
})
