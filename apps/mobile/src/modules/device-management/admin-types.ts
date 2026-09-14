export type DeviceActivationSystem = "Windows" | "iPadOS" | "Android" | "iOS";
export type MobileActivationSystem = Extract<DeviceActivationSystem, "Android" | "iOS">;
export type DeviceActivationStatus = "Available" | "Expired" | "Revoked" | "Consumed";

export interface DeviceRegistrationDetail {
  id: number;
  hardwareId: string;
  systemDeviceNumber: string;
  storeCode: string | null;
  storeName: string | null;
  deviceType: string;
  deviceSystem: string;
  status: number;
  statusDescription: string;
  allowTransactions: boolean;
  remark: string | null;
  createdAt: string | null;
  lastModified: string | null;
  createdBy: string | null;
  lastModifiedBy: string | null;
  isOnline: boolean;
  lastHeartbeatAt: string | null;
  currentCashierId: string | null;
  currentCashierName: string | null;
  cashierLoginAt: string | null;
}

export interface UpdateDeviceRegistrationPayload {
  deviceType: string;
  deviceSystem: string;
  allowTransactions: boolean;
  remark: string;
}

export interface DeviceActivationGrant {
  grantId: string;
  storeCode: string;
  storeName: string | null;
  deviceSystem: DeviceActivationSystem;
  status: DeviceActivationStatus;
  createdAtUtc: string;
  createdBy: string;
  reason: string;
  expiresAtUtc: string;
  revokedAtUtc: string | null;
  revokedBy: string | null;
  revokeReason: string | null;
  consumedAtUtc: string | null;
  consumedHardwareId: string | null;
  consumedDeviceCode: string | null;
  consumptionKind: "Initial" | "Rebind" | null;
  previousStoreCode: string | null;
  previousDeviceCode: string | null;
  targetUserGuid: string | null;
  targetUsername: string | null;
  targetFullName: string | null;
}

export interface DeviceActivationGrantList {
  items: DeviceActivationGrant[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface ManageableStore {
  storeCode: string;
  storeName: string;
}

export interface ManageableAccount {
  userGuid: string;
  username: string;
  fullName: string | null;
}

export interface ActivationCodeCreatePayload {
  storeCode: string;
  deviceSystem: DeviceActivationSystem;
  validForMinutes: 30 | 120 | 1440;
  reason: string;
}

export interface MobileActivationCodeCreatePayload extends ActivationCodeCreatePayload {
  deviceSystem: MobileActivationSystem;
  targetUserGuid: string;
}

export interface ActivationCodeCreateResult {
  grant: DeviceActivationGrant;
  activationCode: string;
}

export interface EmergencyLoginGrant {
  grantId: string;
  storeCode: string;
  businessDate: string;
  keyId: string;
  permissionProfile: "AllPosTerminal";
  issuedBy: string;
  reason: string;
  issuedAtUtc: string;
  expiresAtUtc: string;
  revokedBy: string | null;
  revokedAtUtc: string | null;
  revokeReason: string | null;
  status: "Active" | "Expired" | "Revoked";
}

export interface EmergencyLoginGrantCreateResult {
  grant: EmergencyLoginGrant;
  token: string;
}

export function isDeviceRegistrationId(value: unknown): value is number {
  return typeof value === "number" && Number.isSafeInteger(value) && value > 0;
}

export function supportsTransactionControl(deviceType: string, deviceSystem: string) {
  return deviceType === "POS" && ["Android", "iOS", "iPadOS"].includes(deviceSystem);
}
