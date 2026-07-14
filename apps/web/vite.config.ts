import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'

export interface CenterLogBuildEnvironment {
  VITE_CENTER_LOG_KEY?: string
  VITE_CENTER_LOG_PROJECT?: string
  VITE_CENTER_LOG_ENVIRONMENT?: string
  VITE_CENTER_LOG_SERVICE_NAME?: string
}

const EXPECTED_CENTER_LOG_BUILD_VALUES = {
  VITE_CENTER_LOG_PROJECT: 'hbweb_rv',
  VITE_CENTER_LOG_ENVIRONMENT: 'Production',
  VITE_CENTER_LOG_SERVICE_NAME: 'hbweb_rv-web',
} as const

export function getCenterLogBuildConfigurationStatus(env: CenterLogBuildEnvironment) {
  const invalidVariables: string[] = []

  if (!env.VITE_CENTER_LOG_KEY?.trim()) {
    invalidVariables.push('VITE_CENTER_LOG_KEY')
  }

  for (const [name, expectedValue] of Object.entries(EXPECTED_CENTER_LOG_BUILD_VALUES)) {
    if (env[name as keyof typeof EXPECTED_CENTER_LOG_BUILD_VALUES]?.trim() !== expectedValue) {
      invalidVariables.push(name)
    }
  }

  return {
    configured: invalidVariables.length === 0,
    invalidVariables,
  }
}

export function assertCenterLogProductionBuildConfig(
  command: string,
  mode: string,
  env: CenterLogBuildEnvironment,
) {
  if (command !== 'build' || mode !== 'production') {
    return
  }

  const status = getCenterLogBuildConfigurationStatus(env)
  if (!status.configured) {
    // 只报告变量名，禁止把中心日志密钥或其他配置值写入构建日志。
    throw new Error(`中心日志 production 构建配置不完整：${status.invalidVariables.join(', ')}`)
  }
}

export default defineConfig(({ command, mode }) => {
  const env = loadEnv(mode, '.', '')
  const proxyTarget = env.VITE_DEV_PROXY_TARGET || 'http://localhost:5002'
  const centerLogBuildStatus = getCenterLogBuildConfigurationStatus(env)

  assertCenterLogProductionBuildConfig(command, mode, env)

  return {
    define: {
      // 页面只需要配置是否齐全，不能把中心日志密钥注入额外的运行时对象。
      __CENTER_LOG_BUILD_CONFIGURED__: JSON.stringify(centerLogBuildStatus.configured),
    },
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
