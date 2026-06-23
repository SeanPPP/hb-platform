import enLocale from '../../../i18n/locales/en.json'
import zhLocale from '../../../i18n/locales/zh.json'
import {
  APP_DOWNLOAD_PROFILES,
  DEFAULT_APP_DOWNLOAD_PROFILE,
  buildAppDownloadQuery,
  buildAppDownloadOtaQuery,
  normalizeAppDownloadProfile,
  normalizeRuntimeVersionFilter,
  resolveAppDownloadMirrorStatus,
  resolveAppDownloadSource,
  resolveAppDownloadContentState,
} from './logic'

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

assertEqual(DEFAULT_APP_DOWNLOAD_PROFILE, 'production', 'App 下载页默认应展示 production APK')
assertDeepEqual(
  APP_DOWNLOAD_PROFILES,
  ['production', 'preview'],
  'App 下载页应同时支持 production 和 preview profile',
)

assertEqual(
  normalizeAppDownloadProfile('preview'),
  'preview',
  'preview profile 应保持为 preview',
)
assertEqual(
  normalizeAppDownloadProfile(' Production '),
  'production',
  'profile 应忽略大小写和首尾空白',
)
assertEqual(
  normalizeAppDownloadProfile('development'),
  'production',
  '未知 profile 应回落到 production',
)

assertDeepEqual(
  buildAppDownloadQuery('preview', 2.8, 20.2),
  { profile: 'preview', page: 2, pageSize: 20 },
  '构建查询参数时应保留当前 profile 并规范分页参数',
)
assertDeepEqual(
  buildAppDownloadQuery(null, 0, 0),
  { profile: 'production', page: 1, pageSize: 10 },
  '空 profile 和无效分页应使用安全默认值',
)
assertDeepEqual(
  buildAppDownloadOtaQuery('PREVIEW', 2.8, 20.2, ' 1.0.1 '),
  { channel: 'preview', page: 2, pageSize: 20, runtimeVersion: '1.0.1' },
  'OTA 查询应复用 profile/channel 并清理 runtimeVersion 空白',
)
assertDeepEqual(
  buildAppDownloadOtaQuery('preview', 1, 10, '   '),
  { channel: 'preview', page: 1, pageSize: 10 },
  '空 runtimeVersion 应从 OTA 查询参数中省略，表示查询全部 runtime',
)
assertEqual(
  normalizeRuntimeVersionFilter(' 1.0.1 '),
  '1.0.1',
  'runtimeVersion 筛选值应去除首尾空白',
)

assertEqual(
  resolveAppDownloadContentState(true, true, 1),
  'error',
  '加载失败时应优先展示错误态，避免继续显示旧数据',
)
assertEqual(
  resolveAppDownloadContentState(false, false, 0),
  'empty',
  '无最新 APK 且无历史记录时应展示空态',
)
assertEqual(
  resolveAppDownloadContentState(false, true, 0),
  'ready',
  '存在最新 APK 时应展示可用态',
)
assertEqual(
  resolveAppDownloadContentState(false, false, 1),
  'ready',
  '存在历史记录时应展示可用态',
)
assertEqual(
  resolveAppDownloadMirrorStatus({
    artifactUrl: 'https://cos.example/hb.apk',
    originalArtifactUrl: 'https://expo.dev/artifacts/hb.apk',
    cosArtifactUrl: 'https://cos.example/hb.apk',
    cosMirrorStatus: 'succeeded',
  }),
  'succeeded',
  '存在 COS 地址时镜像状态应为已镜像',
)
assertEqual(
  resolveAppDownloadMirrorStatus({
    artifactUrl: 'https://expo.dev/artifacts/hb.apk',
    originalArtifactUrl: 'https://expo.dev/artifacts/hb.apk',
    cosMirrorStatus: 'running',
  }),
  'running',
  '后端 running 状态应直接展示为镜像中',
)
assertEqual(
  resolveAppDownloadMirrorStatus({
    artifactUrl: 'https://expo.dev/artifacts/hb.apk',
    originalArtifactUrl: 'https://expo.dev/artifacts/hb.apk',
    cosMirrorStatus: 'unsafe',
    cosMirrorError: 'UNSAFE_ARTIFACT: bad content',
  }),
  'unsafe',
  '后端 unsafe 状态应展示为不安全',
)
assertEqual(
  resolveAppDownloadMirrorStatus({
    artifactUrl: 'https://expo.dev/artifacts/hb.apk',
    originalArtifactUrl: 'https://expo.dev/artifacts/hb.apk',
    cosMirrorError: 'timeout',
  }),
  'failed',
  '存在镜像错误时镜像状态应为失败',
)
assertEqual(
  resolveAppDownloadMirrorStatus({
    artifactUrl: 'https://expo.dev/artifacts/hb.apk',
    originalArtifactUrl: 'https://expo.dev/artifacts/hb.apk',
  }),
  'pending',
  '有原始下载地址但没有 COS 结果时应展示等待镜像',
)
assertEqual(
  resolveAppDownloadSource({
    artifactUrl: 'https://cos.example/hb.apk',
    cosArtifactUrl: 'https://cos.example/hb.apk',
  }),
  'cos',
  '后端 artifactUrl 指向 COS 地址时下载源应显示 COS 镜像',
)
assertEqual(
  resolveAppDownloadSource({
    artifactUrl: 'https://expo.dev/artifacts/hb.apk',
    cosArtifactUrl: 'https://cos.example/hb.apk',
  }),
  'eas',
  '后端 artifactUrl 未指向 COS 地址时下载源应显示 EAS 回退',
)

assertEqual(
  zhLocale.system.appDownloads.loadFailedDescription,
  '请确认数据库表已迁移且后端服务可访问，然后点击刷新重试。',
  '中文加载失败说明应提示迁移和后端可访问性',
)
assertEqual(
  enLocale.system.appDownloads.loadFailedDescription,
  'Confirm the database migration is applied and the backend is reachable, then refresh.',
  '英文加载失败说明应提示迁移和后端可访问性',
)
assertEqual(
  zhLocale.system.appDownloads.profiles.preview,
  '预览版',
  '中文 preview profile 文案应存在',
)
assertEqual(
  enLocale.system.appDownloads.profiles.production,
  'Production',
  '英文 production profile 文案应存在',
)
assertEqual(
  zhLocale.system.appDownloads.runtime,
  'Runtime',
  '中文 Runtime 描述项文案应存在',
)
assertEqual(
  zhLocale.system.appDownloads.downloadSources.cos,
  'COS 镜像',
  '中文 COS 下载源文案应存在',
)
assertEqual(
  zhLocale.system.appDownloads.mirrorStatuses.failed,
  '镜像失败',
  '中文镜像失败状态文案应存在',
)
assertEqual(
  enLocale.system.appDownloads.mirrorStatuses.pending,
  'Pending Mirror',
  '英文等待镜像状态文案应存在',
)
assertEqual(
  zhLocale.system.appDownloads.mirrorStatuses.unsafe,
  '不安全',
  '中文不安全状态文案应存在',
)
assertEqual(
  zhLocale.system.appDownloads.ota.rollbackLatestOnly,
  '只能回撤当前最新 OTA',
  '中文 OTA 回撤限制提示应存在',
)
assertEqual(
  enLocale.system.appDownloads.ota.runtimePlaceholder,
  'Runtime version (all)',
  '英文 OTA runtime 筛选占位文案应存在',
)

console.log('AppDownloads logic tests: ok')
