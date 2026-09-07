export type AppDownloadsSection =
  | "mobile-native"
  | "mobile-ota"
  | "ipad-native"
  | "ipad-ota"
  | "pos-handheld";

export type AppDownloadEnvironment = "production" | "preview";
export type AppDownloadPlatform = "android" | "ios";
export type AppDownloadAppKey = "mobile" | "pos-handheld";
export type AppUpdateApp = "mobile-ios" | "pos-ipad" | "pos-handheld";
export type TargetScope = "all" | "stores";
export type PolicyLane =
  | "android-native"
  | "ios-native"
  | "android-ota"
  | "ios-ota";

export interface AppBuild {
  id: string;
  appKey: AppDownloadAppKey;
  appVersion: string | null;
  appBuildVersion: string | null;
  platform: string | null;
  status: string | null;
  buildProfile: string | null;
  channel: string | null;
  runtimeVersion: string | null;
  artifactUrl: string | null;
  originalArtifactUrl: string | null;
  cosArtifactUrl: string | null;
  cosObjectKey: string | null;
  cosMirrorStatus: string | null;
  cosMirrorError: string | null;
  cosMirroredAt: string | null;
  gitCommitHash: string | null;
  buildDetailsPageUrl: string | null;
  createdAt: string | null;
  completedAt: string | null;
  expirationDate: string | null;
}

export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface OtaRelease {
  id: string;
  appKey: "mobile" | "pos-handheld";
  environment: AppDownloadEnvironment;
  platform: AppDownloadPlatform;
  clientChannel: string;
  releaseChannel: string;
  runtimeVersion: string;
  updateGroupId: string;
  updateId: string;
  message: string | null;
  dashboardUrl: string | null;
  publishedAtUtc: string;
  isRollback: boolean;
}

export interface OtaPolicy {
  id: string | null;
  environment: AppDownloadEnvironment;
  platform: AppDownloadPlatform;
  enabled: boolean;
  required: boolean;
  policyVersion: number;
  targetReleaseId: string | null;
  targetRuntimeVersion: string | null;
  releaseMessage: string | null;
  targetRelease: OtaRelease | null;
}

export interface NativeRelease {
  id: string;
  app: AppUpdateApp;
  appStoreId: string;
  bundleIdentifier: string;
  version: string;
  buildNumber: string;
  storefront: string;
  appStoreUrl: string;
  appleVerifiedAtUtc: string;
  createdAt: string;
}

export interface NativePolicy {
  id: string | null;
  enabled: boolean;
  policyVersion: number;
  releaseId: string | null;
  latestVersion: string | null;
  minimumSupportedVersion: string | null;
  minimumSupportedBuildNumber: number | null;
  appStoreUrl: string | null;
  releaseMessage: string | null;
  targetScope: TargetScope;
  targetStoreGuids: string[];
  updatedAt: string | null;
  updatedBy: string | null;
}

export interface StoreOption {
  storeGuid: string;
  storeCode: string;
  storeName: string;
}

export interface IpadOtaRelease {
  id: string;
  environment: string;
  updateGroupId: string;
  iosUpdateId: string;
  channel: string;
  runtimeVersion: string;
  dashboardUrl: string | null;
  publishedAtUtc: string;
  isRollback: boolean;
}

export interface IpadOtaRollout {
  id: string | null;
  enabled: boolean;
  policyVersion: number;
  releaseId: string | null;
  forceUpdate: boolean;
  targetScope: TargetScope;
  targetStoreGuids: string[];
  releaseMessage: string | null;
  release: IpadOtaRelease | null;
}

export interface HandheldCandidate {
  id: string;
  lane: PolicyLane;
  platform: AppDownloadPlatform;
  kind: "native" | "ota";
  version: string | null;
  buildNumber: string | null;
  runtimeVersion: string | null;
  channel: string | null;
  updateId: string | null;
  updateGroupId: string | null;
  message: string | null;
  dashboardUrl: string | null;
  downloadUrl: string | null;
  appStoreUrl: string | null;
  createdAt: string;
  activatable: boolean;
  blockedReason: string | null;
}

export interface HandheldPolicy {
  id: string | null;
  lane: PolicyLane;
  enabled: boolean;
  required: boolean;
  policyVersion: number;
  candidateId: string | null;
  candidateValid: boolean;
  blockedReason: string | null;
  candidate: HandheldCandidate | null;
  minimumSupportedVersion: string | null;
  minimumSupportedBuildNumber: number | null;
  releaseMessage: string | null;
}

export interface Revision {
  id: string;
  policyVersion: number;
  operation: string;
  snapshotJson: string;
  createdAt: string;
  createdBy: string | null;
  lane?: PolicyLane;
}

export interface NativePolicyForm {
  enabled: boolean;
  required?: boolean;
  releaseId: string;
  minimumSupportedVersion: string;
  minimumSupportedBuildNumber: string;
  releaseMessage: string;
  targetScope: TargetScope;
  targetStoreGuids: string[];
}

export interface OtaPolicyForm {
  enabled: boolean;
  required: boolean;
  targetReleaseId: string;
  releaseMessage: string;
  targetScope?: TargetScope;
  targetStoreGuids?: string[];
}
