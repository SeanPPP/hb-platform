import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { webcrypto } from 'node:crypto'
import {
  buildWpfPolicyPayload,
  calculateFileSha256,
  canSubmitWpfPolicy,
  getEffectiveWpfMinimumSupportedVersion,
  getWpfVersionErrorMessage,
  getWpfCurrentVersionText,
  getWpfPolicySummary,
  getWpfPolicyRangeError,
  isWpfRollbackTarget,
  isSupportedWpfInstallerFile,
  normalizeWpfReleaseChannel,
} from './logic'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)

  if (actualJson !== expectedJson) {
    throw new Error(`${message}. Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

function assertTruthy(value: unknown, message: string) {
  if (!value) {
    throw new Error(message)
  }
}

function getNestedValue(source: Record<string, unknown>, path: string) {
  return path.split('.').reduce<unknown>((current, segment) => {
    if (!current || typeof current !== 'object') {
      return undefined
    }
    return (current as Record<string, unknown>)[segment]
  }, source)
}

function collectWpfVersionLocaleKeys() {
  // 中文注释：直接读取页面源码里的 t('system.wpfVersions.*')，避免测试和页面文案引用脱节。
  const source = readFileSync(resolve(process.cwd(), 'src/pages/System/WpfVersions/index.tsx'), 'utf8')
  return [...new Set(
    [...source.matchAll(/t\('system\.wpfVersions\.([^']+)'/g)].map((match) => `system.wpfVersions.${match[1]}`),
  )].sort()
}

function loadLocale(localeName: 'en' | 'zh') {
  return JSON.parse(readFileSync(resolve(process.cwd(), `src/i18n/locales/${localeName}.json`), 'utf8')) as Record<string, unknown>
}

assertEqual(normalizeWpfReleaseChannel(' Preview '), 'preview', 'Channel should trim and lower-case')
assertEqual(normalizeWpfReleaseChannel(''), 'production', 'Empty channel should fall back to production')
assertEqual(isSupportedWpfInstallerFile('hbpos-1.2.3.msi'), true, 'MSI installers should be accepted')
assertEqual(isSupportedWpfInstallerFile('hbpos-1.2.3.exe'), true, 'EXE installers should be accepted')
assertEqual(isSupportedWpfInstallerFile('hbpos-1.2.3.zip'), false, 'Unsupported installer extensions should be rejected')

if (!globalThis.crypto?.subtle) {
  Object.defineProperty(globalThis, 'crypto', {
    value: webcrypto,
    configurable: true,
  })
}

assertEqual(
  await calculateFileSha256(new Blob(['hbpos-installer'])),
  'bde82e8a4fb92ddaebe22918141ddbf90e926a2e24946c90da3be1d6fd18d6fc',
  'Installer SHA-256 should be calculated from file contents',
)

assertDeepEqual(
  buildWpfPolicyPayload({
    channel: ' Preview ',
    targetVersion: ' 1.2.3 ',
    minimumSupportedVersion: ' 1.0.0 ',
    forceUpdate: true,
    isRollback: true,
    rollbackConfirmed: true,
  }),
  {
    channel: 'preview',
    targetVersion: '1.2.3',
    minimumSupportedVersion: '1.0.0',
    forceUpdate: true,
    isRollback: true,
    rollbackConfirmed: true,
  },
  'Rollback policy payload should keep target, minimum version, force flag, rollback flag, and confirmation',
)

assertEqual(
  isWpfRollbackTarget('1.1.0', [
    { version: '1.2.0', isCurrent: true, targetVersion: '1.2.0' },
    { version: '1.1.0', isCurrent: false, targetVersion: '1.2.0' },
  ]),
  true,
  'Choosing a version lower than the current target should require rollback confirmation',
)

assertEqual(
  isWpfRollbackTarget('1.2.0', [
    { version: '1.2.0', isCurrent: true, targetVersion: '1.2.0' },
    { version: '1.1.0', isCurrent: false, targetVersion: '1.2.0' },
  ]),
  false,
  'Choosing the current target should not require rollback confirmation',
)

assertEqual(
  isWpfRollbackTarget('1.1.0', [
    { version: '1.3.0', isCurrent: false, targetVersion: null },
    { version: '1.1.0', isCurrent: false, targetVersion: null },
  ]),
  false,
  'Choosing below the latest active release should not require rollback confirmation without a current policy',
)

assertEqual(
  isWpfRollbackTarget('1.1.0', [
    { version: '1.3.0', isCurrent: false, targetVersion: '1.2.0' },
    { version: '1.1.0', isCurrent: false, targetVersion: '1.2.0' },
  ]),
  true,
  'Policy target metadata should allow rollback confirmation when the current target is not in the page',
)

assertEqual(
  isWpfRollbackTarget('1.2.0', [
    { version: '1.3.0', isCurrent: false, targetVersion: null },
    { version: '1.1.0', isCurrent: false, targetVersion: null },
  ]),
  false,
  'Paged release lists without current policy metadata should not infer rollback from active releases',
)

assertEqual(
  isWpfRollbackTarget('1.1.0', [
    { version: '1.0.0', isCurrent: true, targetVersion: '1.2.0' },
    { version: '1.2.0', isCurrent: false, targetVersion: '1.2.0' },
  ]),
  true,
  'When a current release row carries targetVersion metadata, rollback checks should prioritize the current policy target',
)

assertEqual(
  getWpfCurrentVersionText([
    { version: '1.3.0', isCurrent: false, targetVersion: '1.2.0' },
    { version: '1.1.0', isCurrent: false, targetVersion: '1.2.0' },
  ]),
  '1.2.0',
  'Summary should show policy target version when the current release is not in the page',
)

assertEqual(
  getWpfCurrentVersionText([
    { version: '1.3.0', isCurrent: false, targetVersion: null },
    { version: '1.1.0', isCurrent: false, targetVersion: null },
  ]),
  null,
  'Summary should not infer current version from the first paged release without policy metadata',
)

assertDeepEqual(
  getWpfPolicySummary([
    {
      channel: 'Production',
      version: '1.3.0',
      isCurrent: false,
      targetVersion: '1.2.0',
      minimumSupportedVersion: '1.0.0',
      forceUpdate: true,
    },
  ]),
  {
    channel: 'production',
    targetVersion: '1.2.0',
    minimumSupportedVersion: '1.0.0',
    forceUpdate: true,
  },
  'Policy summary should preserve force-update metadata when the current target is not in the page',
)

assertEqual(
  canSubmitWpfPolicy({
    targetVersion: '1.2.3',
    minimumSupportedVersion: '1.0.0',
  }),
  true,
  'Policy can be submitted when target and minimum versions are present',
)

assertEqual(
  canSubmitWpfPolicy({
    targetVersion: '1.2.3',
    minimumSupportedVersion: '',
  }),
  false,
  'Policy should require a minimum supported version',
)

assertEqual(
  getWpfPolicyRangeError({
    targetVersion: '1.2.0',
    minimumSupportedVersion: '1.2.1',
  }),
  'INVALID_VERSION_RANGE',
  'Policy should reject a minimum supported version above the target version',
)

assertEqual(
  getWpfPolicyRangeError({
    targetVersion: '1.2.0',
    minimumSupportedVersion: '1.2.0',
  }),
  null,
  'Policy should allow a minimum supported version equal to the target version',
)

assertEqual(
  getEffectiveWpfMinimumSupportedVersion({
    targetVersion: '1.1.0',
    minimumSupportedVersion: '1.2.0',
  }),
  '1.1.0',
  'Rollback to a lower target version should clamp the effective minimum supported version to the target version',
)

assertEqual(
  getWpfVersionErrorMessage(new Error('WPF_RELEASE_EXISTS: WPF release version already exists.'), 'fallback'),
  'WPF_RELEASE_EXISTS: WPF release version already exists.',
  'WPF versions page should surface backend code and message from service errors',
)

assertEqual(
  getWpfVersionErrorMessage('plain failure', 'fallback'),
  'fallback',
  'WPF versions page should keep fallback text when the thrown value is not an Error',
)

const localeKeys = collectWpfVersionLocaleKeys()
const enLocale = loadLocale('en')
const zhLocale = loadLocale('zh')

assertTruthy(localeKeys.length > 0, 'WPF Versions page should reference at least one locale key')

for (const localeKey of localeKeys) {
  assertTruthy(
    typeof getNestedValue(enLocale, localeKey) === 'string',
    `English locale should define ${localeKey}`,
  )
  assertTruthy(
    typeof getNestedValue(zhLocale, localeKey) === 'string',
    `Chinese locale should define ${localeKey}`,
  )
}

console.log('WpfVersions logic.test: ok')
