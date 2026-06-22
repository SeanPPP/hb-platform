import enLocale from '../../../i18n/locales/en.json'
import zhLocale from '../../../i18n/locales/zh.json'
import {
  APP_DOWNLOAD_PROFILES,
  DEFAULT_APP_DOWNLOAD_PROFILE,
  buildAppDownloadQuery,
  normalizeAppDownloadProfile,
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

console.log('AppDownloads logic tests: ok')
