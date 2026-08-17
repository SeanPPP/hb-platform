import {
  DEFAULT_MOBILE_APP_KEY,
  MOBILE_APP_KEYS,
  normalizeMobileAppKey,
  type MobileAppKey,
} from '../../../types/mobileAppBuild'

export const APP_DOWNLOAD_PROFILES = ['production', 'preview'] as const

export type AppDownloadProfile = (typeof APP_DOWNLOAD_PROFILES)[number]

export const DEFAULT_APP_DOWNLOAD_PROFILE: AppDownloadProfile = 'production'

export const APP_DOWNLOAD_APP_KEYS = MOBILE_APP_KEYS

export type AppDownloadAppKey = MobileAppKey

export const DEFAULT_APP_DOWNLOAD_APP_KEY = DEFAULT_MOBILE_APP_KEY

export const normalizeAppDownloadAppKey = normalizeMobileAppKey

export interface AppDownloadQuery {
  page: number
  pageSize: number
  profile: AppDownloadProfile
  appKey: AppDownloadAppKey
}

export interface AppDownloadOtaQuery {
  page: number
  pageSize: number
  channel: AppDownloadProfile | `pos-handheld-${AppDownloadProfile}`
  appKey: AppDownloadAppKey
  runtimeVersion?: string
}

export type AppDownloadContentState = 'error' | 'empty' | 'ready'
export type AppDownloadMirrorStatus = 'pending' | 'running' | 'succeeded' | 'failed' | 'unsafe' | 'unavailable'
export type AppDownloadSource = 'cos' | 'eas' | 'unknown'

export interface AppDownloadMirrorFields {
  artifactUrl?: string | null
  originalArtifactUrl?: string | null
  cosArtifactUrl?: string | null
  cosMirrorError?: string | null
  cosMirrorStatus?: string | null
}

export function normalizeAppDownloadProfile(value?: string | number | null): AppDownloadProfile {
  const normalized = String(value ?? DEFAULT_APP_DOWNLOAD_PROFILE).trim().toLowerCase()
  return APP_DOWNLOAD_PROFILES.includes(normalized as AppDownloadProfile)
    ? (normalized as AppDownloadProfile)
    : DEFAULT_APP_DOWNLOAD_PROFILE
}

export function buildAppDownloadQuery(
  profile: string | number | null | undefined,
  page: number,
  pageSize: number,
  appKey?: string | number | null,
): AppDownloadQuery {
  return {
    // 页面和接口共用受控 profile/AppKey，避免不同应用或环境的查询参数漂移。
    profile: normalizeAppDownloadProfile(profile),
    page: Math.max(1, Math.trunc(page || 1)),
    pageSize: Math.max(1, Math.trunc(pageSize || 10)),
    appKey: normalizeAppDownloadAppKey(appKey),
  }
}

export function normalizeRuntimeVersionFilter(value?: string | number | null) {
  return String(value ?? '').trim()
}

export function buildAppDownloadOtaQuery(
  channel: string | number | null | undefined,
  page: number,
  pageSize: number,
  runtimeVersion?: string | number | null,
  appKey?: string | number | null,
): AppDownloadOtaQuery {
  const normalizedRuntimeVersion = normalizeRuntimeVersionFilter(runtimeVersion)
  const normalizedProfile = normalizeAppDownloadProfile(channel)
  const normalizedAppKey = normalizeAppDownloadAppKey(appKey)
  return {
    // 手持 EAS 项目使用独立 channel；界面仍显示 production/preview，并在查询层做确定映射。
    channel:
      normalizedAppKey === 'pos-handheld'
        ? `pos-handheld-${normalizedProfile}`
        : normalizedProfile,
    page: Math.max(1, Math.trunc(page || 1)),
    pageSize: Math.max(1, Math.trunc(pageSize || 10)),
    appKey: normalizedAppKey,
    ...(normalizedRuntimeVersion ? { runtimeVersion: normalizedRuntimeVersion } : {}),
  }
}

export function resolveAppDownloadContentState(
  loadFailed: boolean,
  hasLatestArtifact: boolean,
  itemCount: number,
): AppDownloadContentState {
  if (loadFailed) {
    return 'error'
  }

  if (!hasLatestArtifact && itemCount <= 0) {
    return 'empty'
  }

  return 'ready'
}

export function resolveAppDownloadMirrorStatus(build?: AppDownloadMirrorFields | null): AppDownloadMirrorStatus {
  if (!build?.artifactUrl) {
    return 'unavailable'
  }

  const status = String(build.cosMirrorStatus ?? '').trim().toLowerCase()
  if (status === 'pending' || status === 'running' || status === 'succeeded' || status === 'failed' || status === 'unsafe') {
    return status
  }

  if (build.cosArtifactUrl) {
    return 'succeeded'
  }

  if (build.cosMirrorError?.startsWith('UNSAFE_ARTIFACT:')) {
    return 'unsafe'
  }

  if (build.cosMirrorError) {
    return 'failed'
  }

  // 有原始 artifact 但还没有 COS 结果时，展示为等待镜像，不影响 artifactUrl 下载。
  return build.originalArtifactUrl || build.artifactUrl ? 'pending' : 'unavailable'
}

export function resolveAppDownloadSource(build?: AppDownloadMirrorFields | null): AppDownloadSource {
  if (!build?.artifactUrl) {
    return 'unknown'
  }

  return build.cosArtifactUrl && build.artifactUrl === build.cosArtifactUrl ? 'cos' : 'eas'
}
