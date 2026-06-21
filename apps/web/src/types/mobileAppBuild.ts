export interface MobileAppBuild {
  id: string
  easBuildId?: string | null
  appName?: string | null
  platform?: string | null
  status?: string | null
  buildProfile?: string | null
  distribution?: string | null
  channel?: string | null
  runtimeVersion?: string | null
  appVersion?: string | null
  appBuildVersion?: string | null
  artifactUrl?: string | null
  buildDetailsPageUrl?: string | null
  gitCommitHash?: string | null
  gitCommitMessage?: string | null
  createdAt?: string | null
  completedAt?: string | null
  expirationDate?: string | null
  receivedAt?: string | null
}

export interface MobileAppBuildPagedResult {
  items: MobileAppBuild[]
  total: number
  page: number
  pageSize: number
}
