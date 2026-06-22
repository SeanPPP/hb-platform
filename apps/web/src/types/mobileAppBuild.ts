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

export interface MobileAppOtaUpdate {
  id: string
  updateGroupId?: string | null
  androidUpdateId?: string | null
  channel?: string | null
  branch?: string | null
  platform?: string | null
  runtimeVersion?: string | null
  message?: string | null
  gitCommitHash?: string | null
  dashboardUrl?: string | null
  publishedAt?: string | null
  isRollback: boolean
  rollbackOfGroupId?: string | null
  createdAt?: string | null
  updatedAt?: string | null
}

export interface MobileAppOtaUpdatePagedResult {
  items: MobileAppOtaUpdate[]
  total: number
  page: number
  pageSize: number
}

export interface MobileAppOtaRollbackCommand {
  updateGroupId: string
  command: string
  warning?: string | null
}
