export const APP_DOWNLOAD_PROFILES = ['production', 'preview'] as const

export type AppDownloadProfile = (typeof APP_DOWNLOAD_PROFILES)[number]

export const DEFAULT_APP_DOWNLOAD_PROFILE: AppDownloadProfile = 'production'

export interface AppDownloadQuery {
  page: number
  pageSize: number
  profile: AppDownloadProfile
}

export interface AppDownloadOtaQuery {
  page: number
  pageSize: number
  channel: AppDownloadProfile
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
): AppDownloadQuery {
  return {
    // 页面和接口共用这里的 profile 归一化，避免 production/preview 查询参数漂移。
    profile: normalizeAppDownloadProfile(profile),
    page: Math.max(1, Math.trunc(page || 1)),
    pageSize: Math.max(1, Math.trunc(pageSize || 10)),
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
): AppDownloadOtaQuery {
  const normalizedRuntimeVersion = normalizeRuntimeVersionFilter(runtimeVersion)
  return {
    // APK profile 与 OTA channel 使用同一个切换值，避免两个列表在不同环境之间漂移。
    channel: normalizeAppDownloadProfile(channel),
    page: Math.max(1, Math.trunc(page || 1)),
    pageSize: Math.max(1, Math.trunc(pageSize || 10)),
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
