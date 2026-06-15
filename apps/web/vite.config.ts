import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, '.', '')
  const proxyTarget = env.VITE_DEV_PROXY_TARGET || 'http://localhost:5002'

  return {
    plugins: [
      react(),
    ],
    server: {
      proxy: {
        '/api': {
          target: proxyTarget,
          changeOrigin: true,
        },
        '/hangfire': {
          target: proxyTarget,
          changeOrigin: true,
        },
      },
    },
    build: {
      // 业务后台包含 AntD、Excel 和 PDF 生成依赖，分包后 vendor chunk 仍会超过 Vite 默认 500k 阈值。
      chunkSizeWarningLimit: 1600,
      rollupOptions: {
        output: {
          manualChunks: {
            react: ['react', 'react-dom', 'react-router-dom'],
            antd: ['antd', '@ant-design/icons'],
            excel: ['exceljs'],
            pdf: ['jspdf', 'html2canvas', 'dompurify'],
          },
        },
      },
    },
  }
})
