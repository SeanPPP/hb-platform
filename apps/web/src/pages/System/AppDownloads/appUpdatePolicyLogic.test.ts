import { readFileSync } from 'node:fs'
import {
  buildAppStoreReleaseRegistrationSummary,
  buildNativePolicyConfirmationSummary,
  buildNativeUpdatePolicyRequest,
  buildOtaPolicyConfirmationSummary,
  buildOtaRolloutRequest,
  resolveNativeReleaseStatus,
  resolveOtaReleaseStatus,
  validateTargetStores,
} from './appUpdatePolicyLogic'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}: expected ${String(expected)}, got ${String(actual)}`)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)
  if (actualJson !== expectedJson) {
    throw new Error(`${message}: expected ${expectedJson}, got ${actualJson}`)
  }
}

assertDeepEqual(
  buildAppStoreReleaseRegistrationSummary({
    appStoreId: ' 123456789 ',
    buildNumber: ' 200 ',
    storefront: ' AU ',
  }),
  {
    appStoreId: '123456789',
    buildNumber: '200',
    storefront: 'au',
  },
  'App Store 登记二次确认必须展示与最终 POST 完全一致的规范化事实',
)

assertDeepEqual(
  buildNativeUpdatePolicyRequest({
    enabled: true,
    releaseId: ' release-1 ',
    minimumSupportedVersion: ' 1.1.0 ',
    releaseMessage: ' 请升级 ',
    targetScope: 'stores',
    targetStoreGuids: [' store-2 ', 'store-1', 'store-2'],
  }, true),
  {
    enabled: true,
    releaseId: 'release-1',
    minimumSupportedVersion: '1.1.0',
    releaseMessage: '请升级',
    targetScope: 'stores',
    targetStoreGuids: ['store-2', 'store-1'],
  },
  'iPad 原生策略应清理字段并保留唯一分店 GUID',
)

assertDeepEqual(
  buildNativeUpdatePolicyRequest({
    enabled: true,
    releaseId: 'release-mobile',
    minimumSupportedVersion: '1.0.0',
    releaseMessage: '',
    targetScope: 'stores',
    targetStoreGuids: ['store-1'],
  }, false),
  {
    enabled: true,
    releaseId: 'release-mobile',
    minimumSupportedVersion: '1.0.0',
    releaseMessage: null,
  },
  'Mobile 策略必须保持全局，不得泄漏 iPad 分店字段',
)

assertDeepEqual(
  buildNativeUpdatePolicyRequest({
    enabled: true,
    releaseId: 'release-mobile',
    minimumSupportedVersion: ' ',
  }, false),
  {
    enabled: true,
    releaseId: 'release-mobile',
    minimumSupportedVersion: null,
    releaseMessage: null,
  },
  '原生策略最低版本留空时应仅提供可选更新，不得误启用强制门禁',
)

assertDeepEqual(
  buildNativeUpdatePolicyRequest({
    enabled: false,
    releaseId: 'release-1',
    minimumSupportedVersion: '1.1.0',
    releaseMessage: 'old',
    targetScope: 'stores',
    targetStoreGuids: ['store-1'],
  }, true),
  {
    enabled: false,
    releaseId: null,
    minimumSupportedVersion: null,
    releaseMessage: null,
    targetScope: 'all',
    targetStoreGuids: [],
  },
  '停用原生策略时必须清空发布和目标，避免误激活',
)

assertDeepEqual(
  buildOtaRolloutRequest({
    enabled: false,
    releaseId: 'ota-1',
    forceUpdate: true,
    targetScope: 'stores',
    targetStoreGuids: ['store-1'],
    releaseMessage: 'old',
  }),
  {
    enabled: false,
    releaseId: null,
    forceUpdate: false,
    targetScope: 'all',
    targetStoreGuids: [],
    releaseMessage: null,
  },
  '停用 rollout 时必须清空强制和定向字段',
)

assertDeepEqual(
  buildNativePolicyConfirmationSummary({
    enabled: true,
    releaseId: ' release-1 ',
    minimumSupportedVersion: ' 1.1.0 ',
    targetScope: 'stores',
    targetStoreGuids: [' store-2 ', 'store-1', 'store-2'],
  }, true),
  {
    releaseId: 'release-1',
    targetScope: 'stores',
    targetStoreGuids: ['store-2', 'store-1'],
    updateMode: 'required',
    minimumSupportedVersion: '1.1.0',
  },
  'iPad 原生二次确认必须展示规范化后的发布、分店范围和最低支持版本',
)

assertDeepEqual(
  buildNativePolicyConfirmationSummary({
    enabled: true,
    releaseId: 'release-mobile',
    minimumSupportedVersion: ' ',
    targetScope: 'stores',
    targetStoreGuids: ['store-1'],
  }, false),
  {
    releaseId: 'release-mobile',
    targetScope: 'all',
    targetStoreGuids: [],
    updateMode: 'optional',
    minimumSupportedVersion: null,
  },
  'Mobile 原生二次确认必须固定为全局范围，并明确可选更新',
)

assertDeepEqual(
  buildOtaPolicyConfirmationSummary({
    enabled: true,
    releaseId: ' ota-1 ',
    forceUpdate: true,
    targetScope: 'stores',
    targetStoreGuids: [' store-1 ', 'store-1'],
  }),
  {
    releaseId: 'ota-1',
    targetScope: 'stores',
    targetStoreGuids: ['store-1'],
    updateMode: 'required',
    minimumSupportedVersion: null,
  },
  'OTA 二次确认必须展示已登记发布、投放范围和强制状态',
)

assertEqual(validateTargetStores('all', []), true, '全部分店目标无需选择 GUID')
assertEqual(validateTargetStores('stores', []), false, '指定分店至少选择一项')
assertEqual(validateTargetStores('stores', ['store-1']), true, '指定分店有 GUID 时可保存')

assertEqual(
  resolveNativeReleaseStatus('release-1', 'release-1', true),
  'active',
  '当前启用策略引用的 App Store 发布应标记已激活',
)
assertEqual(
  resolveNativeReleaseStatus('release-2', 'release-1', true),
  'verified',
  '未激活的 App Store 发布仍应标记 Apple 已验证',
)
assertEqual(
  resolveOtaReleaseStatus('ota-1', 'ota-1', true),
  'active',
  '当前 rollout 发布应标记已激活',
)
assertEqual(
  resolveOtaReleaseStatus('ota-2', 'ota-1', true),
  'registered',
  '未投放 OTA 只标记已登记',
)

const panelSource = readFileSync(
  'src/pages/System/AppDownloads/AppUpdatePolicyPanel.tsx',
  'utf8',
)
const zhLocale = JSON.parse(readFileSync('src/i18n/locales/zh.json', 'utf8'))
const enLocale = JSON.parse(readFileSync('src/i18n/locales/en.json', 'utf8'))

for (const locale of [zhLocale, enLocale]) {
  const copy = locale.system.appDownloads.updatePolicy
  assertEqual(
    typeof copy.activateNativeConfirmDescription,
    'string',
    '原生更新必须使用独立的 Apple Lookup 二次确认文案',
  )
  assertEqual(
    typeof copy.activateOtaConfirmDescription,
    'string',
    'OTA 必须使用独立的 EAS rollout 二次确认文案',
  )
  assertEqual(
    copy.activateOtaConfirmDescription.includes('Apple Lookup'),
    false,
    'OTA 确认不得宣称已经 Apple Lookup 验证',
  )
}

assertEqual(
  panelSource.includes("kind === 'native'"),
  true,
  '原生与 OTA 激活确认必须按策略类型选择独立文案',
)
for (const key of ['confirmRelease', 'confirmScope', 'confirmUpdateMode']) {
  assertEqual(
    panelSource.includes(`updatePolicy.${key}`),
    true,
    `二次确认必须渲染 ${key} 摘要`,
  )
}
assertEqual(
  panelSource.includes('onFinish={handleRegisterRelease}'),
  true,
  'App Store 登记 Form 必须通过 onFinish 统一键盘提交',
)
assertEqual(
  panelSource.includes('onClick={() => registerForm.submit()}'),
  true,
  '登记 footer 按钮必须调用 Form.submit，与 Enter 共用提交流程',
)
const registerHandlerSource = panelSource.slice(
  panelSource.indexOf('function handleRegisterRelease'),
  panelSource.indexOf('function confirmPolicySave'),
)
assertEqual(
  registerHandlerSource.includes('Modal.confirm({'),
  true,
  'App Store 表单通过校验后必须先显示独立二次确认',
)
assertEqual(
  registerHandlerSource.includes('onOk:'),
  true,
  'App Store 不可变发布事实只能由二次确认的 onOk 发起登记',
)

console.log('appUpdatePolicyLogic.test.ts: ok')
