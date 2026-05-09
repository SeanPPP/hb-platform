import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'
import { VitePWA } from 'vite-plugin-pwa'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, '.', '')
  const proxyTarget = env.VITE_DEV_PROXY_TARGET || 'http://localhost:5001'

  return {
    plugins: [
      react(),
      VitePWA({
        registerType: 'autoUpdate',
        includeAssets: [
          'pwa/icon-192.png',
          'pwa/icon-512.png',
          'pwa/icon-maskable-512.png',
          'pwa/apple-touch-icon.png',
        ],
        manifest: {
          id: '/shop',
          name: 'HB Shop Ordering',
          short_name: 'HB Shop',
          description: 'HB store ordering PWA for Android and iOS',
          start_url: '/shop',
          scope: '/',
          display: 'standalone',
          background_color: '#f5f7fb',
          theme_color: '#1677ff',
          icons: [
            {
              src: '/pwa/icon-192.png',
              sizes: '192x192',
              type: 'image/png',
            },
            {
              src: '/pwa/icon-512.png',
              sizes: '512x512',
              type: 'image/png',
            },
            {
              src: '/pwa/icon-maskable-512.png',
              sizes: '512x512',
              type: 'image/png',
              purpose: 'maskable',
            },
          ],
        },
        workbox: {
          globPatterns: ['**/*.{js,css,html,ico,png,svg,webp}'],
          navigateFallback: 'index.html',
          maximumFileSizeToCacheInBytes: 5 * 1024 * 1024,
          runtimeCaching: [
            {
              urlPattern: /\/api\/.*/i,
              handler: 'NetworkOnly',
            },
          ],
        },
      }),
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
  }
})
