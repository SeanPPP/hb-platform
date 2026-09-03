import * as buildConfigModule from '../../../../vite.config'

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`)
  }
}

const assertProductionConfig = (
  buildConfigModule as unknown as {
    assertCenterLogProductionBuildConfig?: (
      command: string,
      mode: string,
      env: Record<string, string | undefined>,
    ) => void
  }
).assertCenterLogProductionBuildConfig

const getBuildStatus = (
  buildConfigModule as unknown as {
    getCenterLogBuildConfigurationStatus?: (env: Record<string, string | undefined>) => {
      configured: boolean
    }
  }
).getCenterLogBuildConfigurationStatus

const resolveWebManualChunk = (
  buildConfigModule as unknown as {
    resolveWebManualChunk?: (id: string) => string | undefined
  }
).resolveWebManualChunk

const resolveWebBundleDependencyGroups = (
  buildConfigModule as unknown as {
    resolveWebBundleDependencyGroups?: (moduleIds: string[]) => string[]
  }
).resolveWebBundleDependencyGroups

assertEqual(typeof assertProductionConfig, 'function', 'build config should expose production center-log guard')
assertEqual(typeof getBuildStatus, 'function', 'build config should expose center-log status helper')
assertEqual(typeof resolveWebManualChunk, 'function', 'build config should expose manual chunk resolver')
assertEqual(
  typeof resolveWebBundleDependencyGroups,
  'function',
  'build config should expose bundle dependency classifier',
)

const validEnv = {
  VITE_CENTER_LOG_KEY: 'test-only-secret',
  VITE_CENTER_LOG_PROJECT: 'hbweb_rv',
  VITE_CENTER_LOG_ENVIRONMENT: 'Production',
  VITE_CENTER_LOG_SERVICE_NAME: 'hbweb_rv-web',
}

assertProductionConfig?.('serve', 'production', {})
assertProductionConfig?.('build', 'test', {})
assertProductionConfig?.('build', 'production', validEnv)
assertEqual(getBuildStatus?.(validEnv).configured, true, 'valid web build config is complete')
assertEqual(getBuildStatus?.({}).configured, false, 'missing web build config is incomplete')

let missingError = ''
try {
  assertProductionConfig?.('build', 'production', {})
} catch (error) {
  missingError = error instanceof Error ? error.message : String(error)
}
assertEqual(missingError.includes('VITE_CENTER_LOG_KEY'), true, 'production guard lists missing key name')

let mismatchError = ''
try {
  assertProductionConfig?.('build', 'production', {
    ...validEnv,
    VITE_CENTER_LOG_PROJECT: 'wrong-project',
  })
} catch (error) {
  mismatchError = error instanceof Error ? error.message : String(error)
}
assertEqual(mismatchError.includes('VITE_CENTER_LOG_PROJECT'), true, 'production guard rejects wrong project')
assertEqual(mismatchError.includes(validEnv.VITE_CENTER_LOG_KEY), false, 'production guard never prints the key value')

assertEqual(resolveWebManualChunk?.('/repo/node_modules/exceljs/lib/exceljs.js'), 'excel', 'Excel uses async chunk')
assertEqual(
  resolveWebManualChunk?.('C:\\repo\\node_modules\\exceljs\\lib\\exceljs.js'),
  'excel',
  'Excel chunk matching supports Windows paths',
)
assertEqual(resolveWebManualChunk?.('/repo/node_modules/jspdf/dist/jspdf.es.min.js'), 'pdf', 'jsPDF uses PDF chunk')
assertEqual(resolveWebManualChunk?.('/repo/node_modules/html2canvas/dist/html2canvas.js'), 'pdf', 'html2canvas uses PDF chunk')
assertEqual(resolveWebManualChunk?.('/repo/node_modules/dompurify/dist/purify.es.mjs'), 'pdf', 'DOMPurify uses PDF chunk')
assertEqual(resolveWebManualChunk?.('/repo/node_modules/react/index.js'), undefined, 'React is left to Rollup')
assertEqual(resolveWebManualChunk?.('/repo/node_modules/antd/es/index.js'), undefined, 'AntD is left to Rollup')
assertEqual(
  resolveWebBundleDependencyGroups?.([
    '/repo/node_modules/leaflet/dist/leaflet-src.esm.js',
    'C:\\repo\\node_modules\\@zxing\\browser\\esm\\index.js',
  ]).join(','),
  'leaflet,zxing',
  'dependency metadata detects Leaflet and ZXing inside generic chunks on Unix and Windows',
)
assertEqual(
  resolveWebBundleDependencyGroups?.(['/repo/node_modules/antd/es/index.js']).length,
  0,
  'dependency metadata does not label unrelated shared chunks',
)

const testViteConfig = (
  buildConfigModule.default as unknown as (env: { command: string; mode: string }) => {
    plugins?: Array<{ name?: string }>
    build?: {
      chunkSizeWarningLimit?: number
      rollupOptions?: {
        input?: Record<string, string>
        output?: {
          manualChunks?: unknown
          onlyExplicitManualChunks?: boolean
        }
      }
    }
  }
)({ command: 'serve', mode: 'test' })
const rollupOutput = testViteConfig.build?.rollupOptions?.output
const rollupInput = testViteConfig.build?.rollupOptions?.input

assertEqual(testViteConfig.build?.chunkSizeWarningLimit, 1000, 'chunk warning limit is 1000 KiB')
assertEqual(rollupInput?.index.endsWith('/index.html'), true, 'HTML remains the main build entry')
assertEqual(
  rollupInput?.['initial-app-runtime'].endsWith('/src/App.tsx'),
  true,
  'App runtime is an explicit static entry',
)
assertEqual(
  rollupInput?.['initial-runtime-dependencies'].endsWith('/src/initial-runtime-dependencies.ts'),
  true,
  'shared runtime dependencies use an explicit static entry',
)
assertEqual(
  rollupInput?.['initial-i18n-zh'].endsWith('/src/i18n/initial-zh.ts'),
  true,
  'Chinese resources use an explicit static entry',
)
assertEqual(
  rollupInput?.['initial-i18n-en'].endsWith('/src/i18n/initial-en.ts'),
  true,
  'English resources use an explicit static entry',
)
assertEqual(rollupOutput?.manualChunks, resolveWebManualChunk, 'Rollup uses the tested chunk resolver')
assertEqual(rollupOutput?.onlyExplicitManualChunks, true, 'Rollup only groups explicitly matched dependencies')
assertEqual(
  testViteConfig.plugins?.some((plugin) => plugin.name === 'hb-web-bundle-dependency-metadata'),
  true,
  'Vite emits dependency metadata for deterministic initial-closure checks',
)

console.log('centerLogs.viteConfig.test: ok')
