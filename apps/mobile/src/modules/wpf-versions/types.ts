export type WpfReleaseChannel = "production" | "preview" | string;
export type WpfInstallerType = "exe" | "msi" | null;
export type WpfTargetScope = "all" | "stores" | "devices";

export interface WpfStoreSummary {
  storeGuid: string;
  storeCode: string;
  storeName: string;
}

export interface WpfDeviceSummary {
  deviceRegistrationId: number;
  systemDeviceNumber: string;
  storeCode: string;
  storeName: string;
  remarks: string | null;
}

export interface WpfRelease {
  id: string;
  version: string;
  channel: WpfReleaseChannel;
  fileName: string;
  fileSize: number | null;
  sha256: string | null;
  installerType: WpfInstallerType;
  installerArguments: string | null;
  downloadUrl: string | null;
  objectKey: string | null;
  releaseNotes: string | null;
  isActive: boolean;
  isCurrent: boolean;
  isRollback: boolean;
  forceUpdate: boolean;
  minimumSupportedVersion: string | null;
  targetVersion: string | null;
  targetScope: WpfTargetScope;
  targetStoreGuids: string[];
  targetDeviceRegistrationIds: number[];
  targetStoreSummaries: WpfStoreSummary[];
  targetDeviceSummaries: WpfDeviceSummary[];
  policyUpdatedAt: string | null;
  policyUpdatedBy: string | null;
  createdAt: string | null;
  updatedAt: string | null;
}

export interface WpfReleasePage {
  items: WpfRelease[];
  total: number;
  page: number;
  pageSize: number;
}

export interface WpfReleaseQuery {
  channel: string;
  includeDisabled: boolean;
  page: number;
  pageSize: number;
}

export interface WpfReleaseUpdateRequest {
  downloadUrl?: string | null;
  sha256?: string | null;
  installerType?: Exclude<WpfInstallerType, null> | null;
  installerArguments?: string | null;
  releaseNotes?: string | null;
  isActive?: boolean;
}

export interface WpfReleasePolicyRequest {
  channel: string;
  targetVersion: string;
  minimumSupportedVersion: string;
  forceUpdate: boolean;
  isRollback: boolean;
  rollbackConfirmed?: boolean;
  targetScope: WpfTargetScope;
  targetStoreGuids: string[];
  targetDeviceRegistrationIds: number[];
}

export type WpfStoreOption = WpfStoreSummary;
export type WpfDeviceOption = WpfDeviceSummary;

export interface WpfDevicePage {
  items: WpfDeviceOption[];
  total: number;
  page: number;
  pageSize: number;
}

export interface WpfPolicySummary {
  channel: string;
  targetVersion: string;
  minimumSupportedVersion: string;
  forceUpdate: boolean;
  targetScope: WpfTargetScope;
  targetStoreGuids: string[];
  targetDeviceRegistrationIds: number[];
  targetStoreSummaries: WpfStoreSummary[];
  targetDeviceSummaries: WpfDeviceSummary[];
  policyUpdatedAt: string | null;
  policyUpdatedBy: string | null;
}
