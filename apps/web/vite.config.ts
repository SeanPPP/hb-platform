import { defineConfig, loadEnv } from 'vite'
import type { Plugin } from 'vite'
import react from '@vitejs/plugin-react'
import { fileURLToPath } from 'node:url'

export interface CenterLogBuildEnvironment {
  VITE_CENTER_LOG_KEY?: string
  VITE_CENTER_LOG_PROJECT?: string
  VITE_CENTER_LOG_ENVIRONMENT?: string
  VITE_CENTER_LOG_SERVICE_NAME?: string
}

const PDF_CHUNK_DEPENDENCIES = ['jspdf', 'html2canvas', 'dompurify'] as const
const BUNDLE_DEPENDENCY_GROUPS = [
  { id: 'excel', patterns: ['/node_modules/exceljs/'] },
  { id: 'pdf', patterns: PDF_CHUNK_DEPENDENCIES.map((dependency) => `/node_modules/${dependency}/`) },
  { id: 'leaflet', patterns: ['/node_modules/leaflet/'] },
  { id: 'zxing', patterns: ['/node_modules/@zxing/'] },
] as const

export function resolveWebBundleDependencyGroups(moduleIds: string[]) {
  const normalizedIds = moduleIds.map((id) => id.replace(/\\/g, '/').toLowerCase())
  return BUNDLE_DEPENDENCY_GROUPS
    .filter((group) => group.patterns.some((pattern) => normalizedIds.some((id) => id.includes(pattern))))
    .map((group) => group.id)
}

export function createWebBundleDependencyMetadataPlugin(): Plugin {
  return {
    name: 'hb-web-bundle-dependency-metadata',
    apply: 'build',
    generateBundle(_options, bundle) {
      const chunks = Object.fromEntries(
        Object.values(bundle)
          .filter((output) => output.type === 'chunk')
          .sort((left, right) => left.fileName.localeCompare(right.fileName))
          .map((chunk) => {
            const dependencies = resolveWebBundleDependencyGroups(Object.keys(chunk.modules))
            return [chunk.fileName, dependencies]
          }),
      )

      this.emitFile({
        type: 'asset',
        fileName: '.vite/bundle-dependencies.json',
        source: `${JSON.stringify({ schemaVersion: 'WebBundleDependencyMapV1', chunks }, null, 2)}\n`,
      })
    },
  }
}

export function resolveWebManualChunk(id: string) {
  const normalizedId = id.replace(/\\/g, '/')

  if (normalizedId.includes('/node_modules/exceljs/')) {
    return 'excel'
  }

  if (PDF_CHUNK_DEPENDENCIES.some((dependency) => normalizedId.includes(`/node_modules/${dependency}/`))) {
    return 'pdf'
  }

  return undefined
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
      createWebBundleDependencyMetadataPlugin(),
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
      // 重型导出依赖保留稳定名称，其余共享依赖由 Rollup 按动态入口自然切分。
      chunkSizeWarningLimit: 1000,
      rollupOptions: {
        input: {
          index: fileURLToPath(new URL('index.html', import.meta.url)),
          'initial-app-runtime': fileURLToPath(new URL('src/App.tsx', import.meta.url)),
          'initial-runtime-dependencies': fileURLToPath(
            new URL('src/initial-runtime-dependencies.ts', import.meta.url),
          ),
          'initial-i18n-zh': fileURLToPath(new URL('src/i18n/initial-zh.ts', import.meta.url)),
          'initial-i18n-en': fileURLToPath(new URL('src/i18n/initial-en.ts', import.meta.url)),
        },
        output: {
          manualChunks: resolveWebManualChunk,
          onlyExplicitManualChunks: true,
        },
      },
    },
  }
})
