export const APP_DOWNLOAD_PROFILES = ['production', 'preview'] as const

export type AppDownloadProfile = (typeof APP_DOWNLOAD_PROFILES)[number]

export const DEFAULT_APP_DOWNLOAD_PROFILE: AppDownloadProfile = 'production'

export interface AppDownloadQuery {
  page: number
  pageSize: number
  profile: AppDownloadProfile
}

export type AppDownloadContentState = 'error' | 'empty' | 'ready'

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
