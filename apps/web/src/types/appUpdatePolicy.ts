export type AppUpdateApp = 'mobile-ios' | 'pos-ipad' | 'pos-handheld'
export type AppUpdateTargetScope = 'all' | 'stores'

export interface IosAppStoreRelease {
  id: string
  app: AppUpdateApp
  appStoreId: string
  bundleIdentifier: string
  version: string
  buildNumber: string
  storefront: string
  appStoreUrl: string
  appleVerifiedAtUtc: string
  createdAt: string
  createdBy: string | null
}

export interface IosAppStoreReleaseCreateRequest {
  app: AppUpdateApp
  appStoreId: string
  buildNumber: string
  storefront: string
}

export interface NativeUpdatePolicyRequest {
  expectedPolicyVersion: number
  enabled: boolean
  releaseId: string | null
  minimumSupportedVersion: string | null
  releaseMessage: string | null
}

export interface PosIpadNativeUpdatePolicyRequest extends NativeUpdatePolicyRequest {
  minimumSupportedBuildNumber: number | null
  targetScope: AppUpdateTargetScope
  targetStoreGuids: string[]
}

export interface NativeUpdatePolicy {
  id: string | null
  enabled: boolean
  policyVersion: number
  releaseId: string | null
  latestVersion: string | null
  minimumSupportedVersion: string | null
  minimumSupportedBuildNumber: number | null
  appStoreUrl: string | null
  releaseMessage: string | null
  targetScope: AppUpdateTargetScope
  targetStoreGuids: string[]
  updatedAt: string | null
  updatedBy: string | null
}

export interface AppUpdateTargetStoreOption {
  storeGuid: string
  storeCode: string
  storeName: string
}

export interface PosIpadOtaRelease {
  id: string
  environment: string
  updateGroupId: string
  iosUpdateId: string
  channel: string
  runtimeVersion: string
  gitCommitHash: string | null
  dashboardUrl: string | null
  publishedAtUtc: string
  isRollback: boolean
  rollbackOfReleaseId: string | null
  createdAt: string
  createdBy: string | null
}

export interface PosIpadOtaRolloutRequest {
  expectedPolicyVersion: number
  enabled: boolean
  releaseId: string | null
  forceUpdate: boolean
  targetScope: AppUpdateTargetScope
  targetStoreGuids: string[]
  releaseMessage: string | null
}

export interface PosIpadOtaRollout {
  id: string | null
  enabled: boolean
  policyVersion: number
  releaseId: string | null
  forceUpdate: boolean
  targetScope: AppUpdateTargetScope
  targetStoreGuids: string[]
  releaseMessage: string | null
  release: PosIpadOtaRelease | null
  updatedAt: string | null
  updatedBy: string | null
}
