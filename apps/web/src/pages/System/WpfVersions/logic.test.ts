import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { webcrypto } from 'node:crypto'
import {
  buildWpfPolicyPayload,
  calculateFileSha256,
  canSubmitWpfPolicy,
  getEffectiveWpfMinimumSupportedVersion,
  getDefaultWpfInstallerArguments,
  getWpfVersionErrorMessage,
  getWpfCurrentVersionText,
  getWpfPolicySummary,
  getWpfPolicyRangeError,
  isWpfRollbackTarget,
  isSupportedWpfInstallerFile,
  normalizeWpfReleaseChannel,
} from './logic'
import {
  createLatestRequestGuard,
  runLatestGuardedRequest,
} from '../../../utils/latestRequestGuard'

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

function createDeferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })

  return { promise, reject, resolve }
}

interface WpfReleaseQuery {
  page: number
  pageSize: number
  channel: string
  includeDisabled: boolean
  scopeRevision: number
}

async function verifyWpfScopeChangeUsesOneFirstPageRequest() {
  const requestGuard = createLatestRequestGuard()
  let desiredQuery: WpfReleaseQuery = {
    page: 3,
    pageSize: 10,
    channel: 'production',
    includeDisabled: false,
    scopeRevision: 0,
  }
  const state = { channel: '', page: 0 }
  const startedQueries: string[] = []

  const load = (query: WpfReleaseQuery, request: Promise<WpfReleaseQuery>) => {
    startedQueries.push(`${query.channel}:${query.page}`)
    return runLatestGuardedRequest(requestGuard, () => request, {
      onSuccess: (result) => {
        state.channel = result.channel
        state.page = result.page
      },
    })
  }

  const resetScope = (channel: string, includeDisabled: boolean) => {
    desiredQuery = {
      page: 1,
      pageSize: desiredQuery.pageSize,
      channel,
      includeDisabled,
      scopeRevision: desiredQuery.scopeRevision + 1,
    }
    requestGuard.invalidate()
  }

  const refreshAfterMutation = (expectedQuery: WpfReleaseQuery, targetChannel?: string) => {
    const scopeChanged = expectedQuery.scopeRevision !== desiredQuery.scopeRevision
    if (scopeChanged) {
      if (!targetChannel || targetChannel !== desiredQuery.channel) {
        return false
      }
    }
    if (targetChannel && targetChannel !== desiredQuery.channel) {
      resetScope(targetChannel, desiredQuery.includeDisabled)
      return false
    }
    return true
  }

  const staleList = createDeferred<WpfReleaseQuery>()
  const staleListTask = load(desiredQuery, staleList.promise)
  const mutation = createDeferred<void>()
  const mutationTask = mutation.promise.then(() => refreshAfterMutation({
    page: 3,
    pageSize: 10,
    channel: 'production',
    includeDisabled: false,
    scopeRevision: 0,
  }))

  resetScope('preview', false)
  const scopeList = createDeferred<WpfReleaseQuery>()
  const scopeListTask = load(desiredQuery, scopeList.promise)
  mutation.resolve()
  assertEqual(await mutationTask, false, '旧 mutation 不得在通道切换后重新开始列表请求')
  assertDeepEqual(startedQueries, ['production:3', 'preview:1'], '通道切换只应补发一次第一页请求')

  scopeList.resolve({ ...desiredQuery })
  await scopeListTask
  staleList.resolve({ page: 3, pageSize: 10, channel: 'production', includeDisabled: false, scopeRevision: 0 })
  await staleListTask
  assertEqual(state.channel, 'preview', '旧列表响应不得覆盖新通道')
  assertEqual(state.page, 1, '旧列表响应不得覆盖新通道的第一页')

  desiredQuery = {
    page: 3,
    pageSize: 10,
    channel: 'production',
    includeDisabled: false,
    scopeRevision: 0,
  }
  startedQueries.length = 0
  const targetMutation = createDeferred<void>()
  const targetMutationTask = targetMutation.promise.then(() => refreshAfterMutation({
    page: 3,
    pageSize: 10,
    channel: 'production',
    includeDisabled: false,
    scopeRevision: 0,
  }, 'preview'))

  targetMutation.resolve()
  assertEqual(await targetMutationTask, false, '目标通道变化应交由 scope effect 单次加载')
  assertEqual(desiredQuery.channel, 'preview', '目标通道变化应同步更新 desired query')
  assertEqual(desiredQuery.page, 1, '目标通道变化必须原子重置为第一页')

  const targetScopeList = createDeferred<WpfReleaseQuery>()
  const targetScopeTask = load(desiredQuery, targetScopeList.promise)
  assertDeepEqual(startedQueries, ['preview:1'], '目标通道变化不得再额外请求旧页码')
  targetScopeList.resolve({ ...desiredQuery })
  await targetScopeTask

  desiredQuery = {
    page: 3,
    pageSize: 10,
    channel: 'production',
    includeDisabled: false,
    scopeRevision: 10,
  }
  startedQueries.length = 0
  const conflictingMutation = createDeferred<void>()
  const mutationStartQuery = { ...desiredQuery }
  const conflictingMutationTask = conflictingMutation.promise.then(() => (
    refreshAfterMutation(mutationStartQuery, 'preview')
  ))

  resetScope('beta', false)
  const betaScopeList = createDeferred<WpfReleaseQuery>()
  const betaScopeTask = load(desiredQuery, betaScopeList.promise)
  conflictingMutation.resolve()

  assertEqual(await conflictingMutationTask, false, '旧 mutation 不得覆盖用户后来选择的其他通道')
  assertEqual(desiredQuery.channel, 'beta', '用户已切到 beta 时不得被旧 mutation 切回 preview')
  assertEqual(desiredQuery.page, 1, '用户切换 beta 后仍应停留第一页')
  assertDeepEqual(startedQueries, ['beta:1'], '旧 mutation 不得额外发出 preview 请求')
  betaScopeList.resolve({ ...desiredQuery })
  await betaScopeTask
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
assertEqual(
  getDefaultWpfInstallerArguments('hbpos-1.2.3.msi'),
  '/qn /norestart',
  'MSI releases should default to msiexec silent arguments',
)
assertEqual(
  getDefaultWpfInstallerArguments('hbpos-1.2.3.exe'),
  '/SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /NORESTARTAPPLICATIONS',
  'Inno EXE releases should default to Inno silent arguments',
)

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

await verifyWpfScopeChangeUsesOneFirstPageRequest()

const wpfVersionsPageSource = readFileSync(resolve(process.cwd(), 'src/pages/System/WpfVersions/index.tsx'), 'utf8')
const wpfQrActionGuardPattern = /\{record\.downloadUrl \? \(\s*<>\s*<Button[\s\S]*?href=\{record\.downloadUrl\}[\s\S]*?<Button[\s\S]*?onClick=\{\(\) => setQrRelease\(record\)\}[\s\S]*?<\/>\s*\) : null\}/
assertTruthy(
  wpfVersionsPageSource.includes('const resetReleaseScope = useCallback((nextChannel: string, nextIncludeDisabled: boolean) =>')
    && wpfVersionsPageSource.includes('page: 1,')
    && wpfVersionsPageSource.includes('latestReleaseQueryRef.current = nextQuery')
    && wpfVersionsPageSource.includes('releasesRequestGuardRef.current.invalidate()')
    && wpfVersionsPageSource.includes('scopeRevision: currentQuery.scopeRevision + 1'),
  'WPF 通道或筛选变更必须同步发布第一页 desired query 并使旧请求失效',
)
assertTruthy(
  wpfVersionsPageSource.includes('onChange={handleChannelChange}')
    && wpfVersionsPageSource.includes('onChange={handleIncludeDisabledChange}')
    && wpfVersionsPageSource.includes('onChange: handlePageChange,')
    && !wpfVersionsPageSource.includes('onChange={setChannel}')
    && !wpfVersionsPageSource.includes('onChange={setIncludeDisabled}'),
  'WPF 通道、筛选和分页必须通过原子 desired query handler 更新',
)
assertTruthy(
  wpfVersionsPageSource.includes('await refreshLatestReleaseQuery(payload.channel, refreshQuery)')
    && wpfVersionsPageSource.includes('await refreshLatestReleaseQuery(undefined, refreshQuery)')
    && wpfVersionsPageSource.includes('await refreshLatestReleaseQuery(uploadedChannel, refreshQuery)'),
  'WPF mutation 刷新必须携带启动时 query，避免晚完成覆盖新 scope',
)
assertTruthy(
  wpfVersionsPageSource.includes('expectedQuery.scopeRevision !== currentQuery.scopeRevision')
    && wpfVersionsPageSource.includes('if (!targetChannel || targetChannel !== currentQuery.channel) {'),
  'WPF 旧 mutation 的目标通道与当前 scope 不同时必须直接放弃刷新',
)
assertTruthy(
  wpfVersionsPageSource.includes('const [qrRelease, setQrRelease] = useState<WpfAppRelease | null>(null)')
    && wpfVersionsPageSource.includes('onClick={() => setQrRelease(record)}')
    && wpfVersionsPageSource.includes("t('system.wpfVersions.viewQrCode', '查看二维码')")
    && wpfQrActionGuardPattern.test(wpfVersionsPageSource),
  '只有带下载地址的 WPF 版本操作区才能打开当前行的下载二维码',
)
assertTruthy(
  wpfVersionsPageSource.includes('open={Boolean(qrRelease)}')
    && wpfVersionsPageSource.includes('onCancel={() => setQrRelease(null)}')
    && wpfVersionsPageSource.includes('<QRCode value={qrRelease.downloadUrl} size={220} />')
    && wpfVersionsPageSource.includes('<Text strong>{qrRelease.version}</Text>')
    && wpfVersionsPageSource.includes('<Text>{qrRelease.fileName}</Text>')
    && wpfVersionsPageSource.includes('copyable={{ text: qrRelease.downloadUrl }}')
    && wpfVersionsPageSource.includes('{qrRelease.downloadUrl}'),
  'WPF 下载二维码弹窗必须展示当前版本、文件名、二维码和可复制的同一下载链接',
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

assertEqual(
  getNestedValue(zhLocale, 'system.wpfVersions.setCurrent'),
  '设为发布目标',
  'WPF 中文操作文案应明确表示修改发布目标',
)
assertEqual(
  getNestedValue(enLocale, 'system.wpfVersions.setCurrent'),
  'Set as Release Target',
  'WPF English action copy should identify the release target',
)
assertEqual(
  getNestedValue(zhLocale, 'system.wpfVersions.setCurrentConfirm'),
  '将此版本设为发布目标？客户端将在下次检查更新时获取该版本。',
  'WPF 中文确认文案应说明客户端获取版本的时机',
)
assertEqual(
  getNestedValue(enLocale, 'system.wpfVersions.setCurrentConfirm'),
  'Set this version as the release target? Clients will receive it the next time they check for updates.',
  'WPF English confirmation copy should explain when clients receive the target',
)
assertTruthy(
  wpfVersionsPageSource.includes("t('system.wpfVersions.setCurrent', '设为发布目标')")
    && wpfVersionsPageSource.includes("t('system.wpfVersions.setCurrentConfirm', '将此版本设为发布目标？客户端将在下次检查更新时获取该版本。')"),
  'WPF 发布目标按钮的页面 fallback 文案必须与 locale 语义一致',
)

console.log('WpfVersions logic.test: ok')
