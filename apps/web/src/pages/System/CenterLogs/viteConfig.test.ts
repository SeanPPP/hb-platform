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

assertEqual(typeof assertProductionConfig, 'function', 'build config should expose production center-log guard')
assertEqual(typeof getBuildStatus, 'function', 'build config should expose center-log status helper')

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

console.log('centerLogs.viteConfig.test: ok')
