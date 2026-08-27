import {
  createPosHandheldUpdatePolicyService,
  type PosHandheldUpdatePolicyTransport,
} from './posHandheldUpdatePolicyService'

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

type TransportCall = {
  method: 'get' | 'put'
  url: string
  params?: Record<string, unknown>
  payload?: unknown
  signal?: AbortSignal
}

const calls: TransportCall[] = []
const transport: PosHandheldUpdatePolicyTransport = {
  async get(url, options) {
    calls.push({ method: 'get', url, params: options?.params, signal: options?.signal })
    if (url.endsWith('/candidates/native/android')) {
      return {
        success: true,
        data: [{
          id: 101,
          lane: 'android-native',
          platform: 'android',
          kind: 'native',
          version: '1.2.0',
          buildNumber: '42',
          artifactUrl: 'https://download.example/handheld-42.apk',
          sha256: 'a'.repeat(64),
          activatable: true,
          publishedAtUtc: '2026-08-14T00:00:00Z',
        }],
      }
    }
    if (url.endsWith('/candidates/native/ios')) {
      return {
        success: true,
        data: [{
          id: '11111111-2222-3333-4444-555555555555',
          lane: 'ios-native',
          platform: 'iOS',
          kind: 'native',
          version: '1.2.0',
          buildNumber: '17',
          artifactUrl: 'https://apps.apple.com/au/app/id1234567890',
          activatable: true,
          publishedAtUtc: '2026-08-14T01:00:00Z',
        }],
      }
    }
    if (url.endsWith('/candidates/ota')) {
      return {
        success: true,
        data: [{
          id: 202,
          lane: `${options?.params?.platform}-ota`,
          platform: options?.params?.platform,
          kind: 'ota',
          runtimeVersion: '1.2.0',
          clientChannel: 'pos-handheld-production',
          releaseChannel: `pos-handheld-production-${options?.params?.platform}-release-20260827`,
          updateId: `${options?.params?.platform}-update-42`,
          updateGroupId: '11111111-1111-1111-1111-111111111111',
          releaseBatchId: 'batch-1',
          releaseMessage: '手持 OTA',
          gitCommitHash: 'abcdef12',
          dashboardUrl: 'https://expo.dev/update/handheld-42',
          factFingerprint: 'f'.repeat(64),
          legacy: false,
          isRollback: true,
          rollbackOfReleaseId: '33333333-3333-3333-3333-333333333333',
          registrationSource: 'pos-handheld-release-script',
          registeredBy: 'release-bot',
          activatable: false,
          blockedReason: 'POS_HANDHELD_OTA_CANDIDATE_NOT_CHANNEL_HEAD',
          publishedAtUtc: '2026-08-14T00:00:00Z',
        }],
      }
    }
    if (url.endsWith('/revisions')) {
      return {
        success: true,
        data: [{
          id: 9,
          lane: options?.params?.lane,
          policyVersion: 3,
          action: 'save',
          snapshot: { enabled: true },
          createdAt: '2026-08-14T03:00:00Z',
          createdBy: 'admin',
        }],
      }
    }
    return {
      success: true,
      data: [{
        id: 1,
        lane: 'android-native',
        managed: true,
        enabled: true,
        required: false,
        policyVersion: 2,
        candidateId: 101,
        minimumSupportedVersion: null,
        minimumSupportedBuildNumber: null,
        releaseMessage: '可选更新',
        candidateValid: false,
        blockedReason: 'POS_HANDHELD_UPDATE_CANDIDATE_FINGERPRINT_MISMATCH',
        candidate: {
          id: 101,
          lane: 'android-native',
          platform: 'Android',
          kind: 'native',
          version: '2.0.0',
          buildNumber: '200',
          artifactUrl: 'https://downloads.example/handheld.apk',
          sha256: 'b'.repeat(64),
          publishedAtUtc: '2026-08-14T02:00:00Z',
          activatable: true,
        },
        updatedAt: '2026-08-14T02:00:00Z',
        updatedBy: 'admin',
      }],
    }
  },
  async put(url, payload, options) {
    calls.push({ method: 'put', url, payload, signal: options?.signal })
    return {
      success: true,
      data: {
        id: 1,
        lane: 'android-native',
        managed: true,
        enabled: true,
        required: true,
        policyVersion: 3,
        candidateId: 101,
        minimumSupportedVersion: '1.1.0',
        minimumSupportedBuildNumber: 40,
        releaseMessage: '请升级',
        candidate: null,
        updatedAt: '2026-08-14T03:00:00Z',
        updatedBy: 'admin',
      },
    }
  },
}

async function run() {
  const service = createPosHandheldUpdatePolicyService(transport)
  const controller = new AbortController()

  const policies = await service.getPolicies(controller.signal)
  assertEqual(policies[0]?.candidateId, '101', '数值候选 ID 必须规范化为字符串')
  assertEqual(policies[0]?.candidateValid, false, '策略总览必须保留绑定候选校验结果')
  assertEqual(policies[0]?.candidate?.id, '101', '策略总览必须保留绑定候选快照')
  assertEqual(
    policies[0]?.blockedReason,
    'POS_HANDHELD_UPDATE_CANDIDATE_FINGERPRINT_MISMATCH',
    '策略总览必须保留失效原因',
  )
  assertDeepEqual(
    calls[calls.length - 1],
    {
      method: 'get',
      url: '/api/app-update-policies/pos-handheld',
      params: undefined,
      signal: controller.signal,
    },
    '策略总览必须使用固定受保护路径并透传 AbortSignal',
  )

  const androidNative = await service.getNativeCandidates('android')
  assertEqual(androidNative[0]?.artifactSha256?.length, 64, 'Android 候选必须保留 SHA-256')
  assertEqual(
    androidNative[0]?.createdAt,
    '2026-08-14T00:00:00Z',
    '候选必须把后台 PublishedAtUtc 归一化为目录时间',
  )
  assertEqual(
    calls[calls.length - 1]?.url,
    '/api/app-update-policies/pos-handheld/candidates/native/android',
    'Android 原生候选路径必须固定',
  )

  const iosNative = await service.getNativeCandidates('ios')
  assertEqual(
    calls[calls.length - 1]?.url,
    '/api/app-update-policies/pos-handheld/candidates/native/ios',
    'iOS 原生候选路径必须固定',
  )
  assertEqual(
    iosNative[0]?.appStoreUrl,
    'https://apps.apple.com/au/app/id1234567890',
    'iOS 候选应把后台 ArtifactUrl 识别为 App Store URL',
  )

  const ota = await service.getOtaCandidates('android')
  assertEqual(ota[0]?.activatable, false, '非 channel head OTA 必须保留不可激活状态')
  assertEqual(
    ota[0]?.channel,
    'pos-handheld-production-android-release-20260827',
    '新候选必须优先展示不可变发布事实的 releaseChannel',
  )
  assertEqual(ota[0]?.clientChannel, 'pos-handheld-production', '候选必须保留原生 client channel')
  assertEqual(ota[0]?.legacy, false, '候选必须区分 legacy fixed-channel 与新 release-channel')
  assertEqual(ota[0]?.releaseBatchId, 'batch-1', '候选必须保留跨平台审计 batch')
  assertEqual(ota[0]?.dashboardUrl, 'https://expo.dev/update/handheld-42', '候选必须保留可信 Dashboard 审计入口')
  assertEqual(ota[0]?.message, '手持 OTA', '候选必须保留不可变发布说明')
  assertEqual(ota[0]?.gitCommitHash, 'abcdef12', '候选必须保留发布 commit')
  assertEqual(ota[0]?.isRollback, true, '候选必须标识 rollback 发布事实')
  assertEqual(
    ota[0]?.rollbackOfReleaseId,
    '33333333-3333-3333-3333-333333333333',
    '候选必须保留 rollback 来源',
  )
  assertEqual(ota[0]?.createdBy, 'release-bot', '候选必须保留登记人')
  assertDeepEqual(
    calls[calls.length - 1]?.params,
    { platform: 'android' },
    'OTA 候选必须按精确平台隔离',
  )

  const payload = {
    expectedPolicyVersion: 2,
    enabled: true,
    required: true,
    candidateId: '101',
    minimumSupportedVersion: '1.1.0',
    minimumSupportedBuildNumber: 40,
    releaseMessage: '请升级',
  }
  const saved = await service.savePolicy('android-native', payload)
  assertEqual(saved.policyVersion, 3, '保存后必须使用服务端权威策略版本')
  assertDeepEqual(
    calls[calls.length - 1],
    {
      method: 'put',
      url: '/api/app-update-policies/pos-handheld/android-native',
      payload,
      signal: undefined,
    },
    '保存必须把完整 CAS 请求发送到精确 lane',
  )

  const revisions = await service.getRevisions('android-native')
  assertEqual(revisions[0]?.policyVersion, 3, '策略审计必须保留版本号')
  assertDeepEqual(
    calls[calls.length - 1]?.params,
    { lane: 'android-native' },
    '审计查询必须按 lane 隔离',
  )

  console.log('posHandheldUpdatePolicyService.test.ts: ok')
}

void run()
