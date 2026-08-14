import { readFileSync } from 'node:fs'
import {
  buildPosHandheldPolicyConfirmationSummary,
  buildPosHandheldPolicyRequest,
  filterPosHandheldCandidates,
  getPosHandheldCandidateKey,
  getPosHandheldCandidateLabel,
  getPosHandheldCandidateEffectiveStatus,
  getPosHandheldPolicySelectionState,
  isPosHandheldPolicyCandidateActive,
  mergePosHandheldPolicyCandidates,
} from './posHandheldUpdatePolicyLogic'
import { isValidPosHandheldBuildNumber } from './appUpdatePolicyLogic'
import type { PosHandheldReleaseCandidate } from '../../../types/posHandheldUpdatePolicy'

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

const candidates: PosHandheldReleaseCandidate[] = [
  {
    id: '101',
    lane: 'android-native',
    platform: 'android',
    kind: 'native',
    version: '1.2.0',
    buildNumber: '42',
    runtimeVersion: null,
    channel: null,
    updateId: null,
    updateGroupId: null,
    downloadUrl: 'https://download.example/handheld-42.apk',
    appStoreUrl: null,
    artifactSha256: 'a'.repeat(64),
    createdAt: '2026-08-14T00:00:00Z',
    createdBy: 'eas-webhook',
    activatable: true,
    blockedReason: null,
  },
  {
    id: 'ios-release-1',
    lane: 'ios-native',
    platform: 'ios',
    kind: 'native',
    version: '1.2.0',
    buildNumber: '17',
    runtimeVersion: null,
    channel: null,
    updateId: null,
    updateGroupId: null,
    downloadUrl: null,
    appStoreUrl: 'https://apps.apple.com/au/app/id1234567890',
    artifactSha256: null,
    createdAt: '2026-08-14T01:00:00Z',
    createdBy: 'release-bot',
    activatable: true,
    blockedReason: null,
  },
  {
    id: '202',
    lane: 'android-ota',
    platform: 'android',
    kind: 'ota',
    version: null,
    buildNumber: null,
    runtimeVersion: '1.2.0',
    channel: 'pos-handheld-production',
    updateId: 'android-update-42',
    updateGroupId: '11111111-1111-1111-1111-111111111111',
    downloadUrl: null,
    appStoreUrl: null,
    artifactSha256: null,
    createdAt: '2026-08-14T02:00:00Z',
    createdBy: 'ota-script',
    activatable: false,
    blockedReason: 'OTA_CANDIDATE_NOT_CHANNEL_HEAD',
  },
]

assertDeepEqual(
  buildPosHandheldPolicyRequest({
    enabled: false,
    required: true,
    candidateId: '101',
    minimumSupportedVersion: '1.1.0',
    minimumSupportedBuildNumber: 40,
    releaseMessage: '旧说明',
  }, 'android-native', 7),
  {
    expectedPolicyVersion: 7,
    enabled: false,
    required: false,
    candidateId: null,
    minimumSupportedVersion: null,
    minimumSupportedBuildNumber: null,
    releaseMessage: null,
  },
  '停用 lane 时必须清空候选、强制门槛和说明，不能残留隐式发布目标',
)

assertDeepEqual(
  buildPosHandheldPolicyRequest({
    enabled: true,
    required: true,
    candidateId: ' 101 ',
    minimumSupportedVersion: ' 1.1.0 ',
    minimumSupportedBuildNumber: 40,
    releaseMessage: ' 请升级 ',
  }, 'android-native', 8),
  {
    expectedPolicyVersion: 8,
    enabled: true,
    required: true,
    candidateId: '101',
    minimumSupportedVersion: '1.1.0',
    minimumSupportedBuildNumber: 40,
    releaseMessage: '请升级',
  },
  'Android 原生策略必须保留显式 required 与最低版本/build',
)

assertDeepEqual(
  buildPosHandheldPolicyRequest({
    enabled: true,
    required: false,
    candidateId: '101',
    minimumSupportedVersion: null,
    minimumSupportedBuildNumber: 40,
    releaseMessage: null,
  }, 'android-native', 9),
  {
    expectedPolicyVersion: 9,
    enabled: true,
    required: false,
    candidateId: '101',
    minimumSupportedVersion: null,
    minimumSupportedBuildNumber: 40,
    releaseMessage: null,
  },
  '原生策略必须允许只按 build 设置最低门槛',
)

assertEqual(isValidPosHandheldBuildNumber('1'), true, '手持 iOS build 1 应有效')
assertEqual(isValidPosHandheldBuildNumber('0'), false, '手持 iOS build 0 必须拒绝')
assertEqual(isValidPosHandheldBuildNumber('01'), false, '手持 iOS build 不得有前导零')
assertEqual(isValidPosHandheldBuildNumber('12a'), false, '手持 iOS build 必须是规范整数')
assertEqual(
  isValidPosHandheldBuildNumber('9007199254740991'),
  true,
  '手持 iOS 登记应接受 JavaScript 安全整数上界',
)
assertEqual(
  isValidPosHandheldBuildNumber('9007199254740992'),
  false,
  '手持 iOS 登记必须拒绝超出 JavaScript 安全整数的 build',
)

const driftedPolicy = {
  id: 'policy-1',
  lane: 'android-native' as const,
  managed: true,
  enabled: true,
  required: true,
  policyVersion: 2,
  candidateId: '101',
  candidateValid: false,
  blockedReason: 'POS_HANDHELD_UPDATE_CANDIDATE_FINGERPRINT_MISMATCH',
  candidate: null,
  minimumSupportedVersion: null,
  minimumSupportedBuildNumber: null,
  releaseMessage: null,
  updatedAt: null,
  updatedBy: null,
}
assertEqual(
  isPosHandheldPolicyCandidateActive(driftedPolicy),
  false,
  '候选指纹漂移后不得继续显示为 Active',
)
assertEqual(
  isPosHandheldPolicyCandidateActive({ ...driftedPolicy, candidateValid: true }),
  true,
  '只有已启用且指纹有效的精确候选才是 Active',
)
assertEqual(
  getPosHandheldPolicySelectionState(
    true,
    driftedPolicy.candidateId,
    driftedPolicy,
    candidates[0],
  ),
  'refreshable',
  '绑定候选仅指纹失配且当前仍可发布时，应允许管理员复核后重新发布',
)
assertEqual(
  getPosHandheldPolicySelectionState(
    true,
    driftedPolicy.candidateId,
    driftedPolicy,
    { ...candidates[0], activatable: false },
  ),
  'blocked',
  '指纹失配候选本身已不可发布时必须继续阻止保存',
)

const driftedKey = getPosHandheldCandidateKey(candidates[0])
assertEqual(
  getPosHandheldCandidateEffectiveStatus(
    candidates[0],
    new Set(),
    new Set([driftedKey]),
  ),
  'blocked',
  '绑定指纹失效必须覆盖候选自身的可发布状态',
)
assertDeepEqual(
  filterPosHandheldCandidates(candidates, {
    platform: 'android',
    kind: 'native',
    status: 'activatable',
    keyword: '',
  }, new Set(), new Set([driftedKey])),
  [],
  '绑定失效候选不得出现在可发布筛选中',
)
assertDeepEqual(
  filterPosHandheldCandidates(candidates, {
    platform: 'android',
    kind: 'native',
    status: 'blocked',
    keyword: '',
  }, new Set(), new Set([driftedKey])),
  [candidates[0]],
  '绑定失效候选必须出现在不可发布筛选中',
)

const catalogOverflowCandidate = {
  ...candidates[0],
  id: 'catalog-overflow',
  buildNumber: '1',
}
assertDeepEqual(
  mergePosHandheldPolicyCandidates(candidates, [{
    ...driftedPolicy,
    candidateId: catalogOverflowCandidate.id,
    candidateValid: true,
    blockedReason: null,
    candidate: catalogOverflowCandidate,
  }]),
  [...candidates, catalogOverflowCandidate],
  '目录上限外的绑定候选必须并入目录、筛选和二次确认数据源',
)

assertDeepEqual(
  buildPosHandheldPolicyRequest({
    enabled: true,
    required: true,
    candidateId: '202',
    minimumSupportedVersion: '1.1.0',
    minimumSupportedBuildNumber: 40,
    releaseMessage: 'OTA',
  }, 'android-ota', 3),
  {
    expectedPolicyVersion: 3,
    enabled: true,
    required: true,
    candidateId: '202',
    minimumSupportedVersion: null,
    minimumSupportedBuildNumber: null,
    releaseMessage: 'OTA',
  },
  'OTA 兼容边界是 runtimeVersion，请求不得混入原生最低版本/build',
)

assertDeepEqual(
  filterPosHandheldCandidates(candidates, {
    platform: 'android',
    kind: 'all',
    status: 'activatable',
    keyword: ' 42 ',
  }, new Set()),
  [candidates[0]],
  '候选目录必须组合平台、状态和关键词筛选',
)

assertDeepEqual(
  filterPosHandheldCandidates(candidates, {
    platform: 'all',
    kind: 'all',
    status: 'active',
    keyword: '',
  }, new Set(['ios-native:ios-release-1'])),
  [candidates[1]],
  '当前激活筛选必须按策略绑定候选 ID 判断',
)
assertEqual(
  getPosHandheldCandidateKey(candidates[0]),
  'android-native:101',
  '候选主键必须包含 lane，避免不同事实表的数值 ID 冲突',
)

assertEqual(
  getPosHandheldCandidateLabel(candidates[0]).includes('1.2.0'),
  true,
  '原生候选标签必须显示版本',
)
assertEqual(
  getPosHandheldCandidateLabel(candidates[2]).includes('android-update-42'),
  true,
  'OTA 候选标签必须显示精确 update ID',
)

assertDeepEqual(
  buildPosHandheldPolicyConfirmationSummary({
    enabled: true,
    required: true,
    candidateId: '101',
    minimumSupportedVersion: '1.1.0',
    minimumSupportedBuildNumber: 40,
    releaseMessage: '请升级',
  }, 'android-native', candidates[0]),
  {
    lane: 'android-native',
    enabled: true,
    updateMode: 'required',
    candidateId: '101',
    candidateLabel: '1.2.0 (42)',
    minimumSupportedVersion: '1.1.0',
    minimumSupportedBuildNumber: 40,
    releaseMessage: '请升级',
  },
  '二次确认必须展示 lane、精确候选、模式和最低门槛',
)

const parentPanelSource = readFileSync(
  'src/pages/System/AppDownloads/AppUpdatePolicyPanel.tsx',
  'utf8',
)
const handheldPanelSource = readFileSync(
  'src/pages/System/AppDownloads/PosHandheldUpdatePolicyTab.tsx',
  'utf8',
)
assertEqual(
  parentPanelSource.includes("key: 'pos-handheld'"),
  true,
  'App Downloads 更新策略必须提供独立 pos-handheld 标签页',
)
for (const lane of ['android-native', 'ios-native', 'android-ota', 'ios-ota']) {
  assertEqual(
    handheldPanelSource.includes(`'${lane}'`),
    true,
    `手持策略组件必须保留独立通道 ${lane}`,
  )
}
for (const filter of ['platform', 'kind', 'status', 'keyword']) {
  assertEqual(
    handheldPanelSource.includes(`${filter}:`),
    true,
    `手持候选目录必须接入 ${filter} 筛选`,
  )
}
assertEqual(
  handheldPanelSource.includes('savePolicyWithConflictReload('),
  true,
  '手持策略 409 必须重载权威状态且不得自动重放',
)
assertEqual(
  handheldPanelSource.includes('onRegisterIosRelease'),
  true,
  '手持页面必须允许通过后台 Apple Lookup 登记已存在的 iOS App Store 事实',
)
assertEqual(
  /EAS_TOKEN|EXPO_TOKEN|eas\s+update/i.test(handheldPanelSource),
  false,
  '浏览器组件不得包含 EAS 凭据或直接发布命令',
)

for (const localePath of ['src/i18n/locales/zh.json', 'src/i18n/locales/en.json']) {
  const locale = JSON.parse(readFileSync(localePath, 'utf8'))
  assertEqual(
    typeof locale.system.appDownloads.updatePolicy.tabs.posHandheld,
    'string',
    '中英文必须提供手持 POS 标签文案',
  )
  assertEqual(
    typeof locale.system.appDownloads.updatePolicy.posHandheld.boundaryDescription,
    'string',
    '中英文必须明确 Web 与 CI/EAS 发布边界',
  )
}

console.log('posHandheldUpdatePolicyLogic.test.ts: ok')
