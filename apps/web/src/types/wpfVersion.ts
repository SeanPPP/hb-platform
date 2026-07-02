export type WpfReleaseChannel = 'production' | 'preview' | string

export type WpfInstallerType = 'exe' | 'msi'

export interface WpfAppRelease {
  id: string
  version: string
  channel: WpfReleaseChannel
  fileName: string
  fileSize: number | null
  sha256: string | null
  installerType: WpfInstallerType | null
  installerArguments: string | null
  downloadUrl: string | null
  objectKey: string | null
  releaseNotes: string | null
  isActive: boolean
  isCurrent: boolean
  isRollback: boolean
  forceUpdate: boolean
  minimumSupportedVersion: string | null
  targetVersion: string | null
  createdAt: string | null
  updatedAt: string | null
}

export interface WpfAppReleasePagedResult {
  items: WpfAppRelease[]
  total: number
  page: number
  pageSize: number
}

export interface WpfReleaseUploadInitRequest {
  channel: WpfReleaseChannel
  version: string
  fileName: string
  fileSize: number
  sha256?: string | null
  contentType?: string | null
}

export interface WpfReleaseUploadInitRawResult {
  objectKey?: string | null
  cosObjectKey?: string | null
  downloadUrl?: string | null
  publicUrl?: string | null
  directUpload?: Record<string, unknown> | null
  multipartUpload?: Record<string, unknown> | null
}

export interface WpfReleaseUploadInitResult {
  uploadUrl: string
  uploadMethod: string
  objectKey: string
  downloadUrl: string
  headers: Record<string, string>
}

export interface CreateWpfAppReleaseRequest {
  version: string
  channel: WpfReleaseChannel
  fileName: string
  fileSize: number
  sha256?: string | null
  installerType: WpfInstallerType
  installerArguments?: string | null
  objectKey: string
  downloadUrl: string
  releaseNotes?: string | null
}

export interface WpfAppReleaseUpdateRequest {
  downloadUrl?: string | null
  sha256?: string | null
  installerType?: WpfInstallerType | null
  installerArguments?: string | null
  releaseNotes?: string | null
  isActive?: boolean | null
}

export interface WpfReleasePolicyRequest {
  channel: WpfReleaseChannel
  targetVersion: string
  minimumSupportedVersion: string
  forceUpdate: boolean
  isRollback: boolean
  rollbackConfirmed?: boolean
}

export interface WpfUpdateCheckResponse {
  updateAvailable: boolean
  forceUpdate: boolean
  isRollback: boolean
  currentVersion: string | null
  targetVersion: string | null
  minimumSupportedVersion: string | null
  downloadUrl: string | null
  fileName: string | null
  fileSize: number | null
  sha256: string | null
  installerType: WpfInstallerType | null
  installerArguments: string | null
  releaseNotes: string | null
}
