export type MobileDeviceSystem = "Android" | "iOS";
export type MobileDeviceActivationMode = "redeem" | "rebind";

export interface MobileDeviceActivationPreview {
  isAllowed: boolean;
  reasonCode: string;
  storeCode?: string | null;
  storeName?: string | null;
  deviceSystem?: string | null;
  targetUsername?: string | null;
  targetFullName?: string | null;
  assignedStoreCount?: number | null;
  expiresAtUtc?: string | null;
  message: string;
}

export interface MobileDeviceActivationBinding {
  bindingId: string;
  deviceRegistrationId: number;
  deviceCode: string;
  storeCode: string;
  storeName: string;
  deviceSystem: string;
  targetUserGuid: string;
  targetUsername: string;
  targetFullName?: string | null;
  boundAtUtc: string;
}

export interface MobileDeviceActivationCommitResult {
  isAllowed: boolean;
  reasonCode: string;
  message: string;
  binding?: MobileDeviceActivationBinding | null;
}

export interface StoredMobileDeviceAccountBinding {
  binding: MobileDeviceActivationBinding;
  apiHost: string;
  hardwareId: string;
  credential: string;
}

export interface PendingMobileDeviceActivation {
  version: 1;
  mode: MobileDeviceActivationMode;
  activationCode: string;
  apiHost: string;
  hardwareId: string;
  deviceSystem: MobileDeviceSystem;
  credential: string;
  credentialVerifier: string;
  deviceName?: string;
  currentHardwareId?: string;
  currentCredential?: string;
}

export interface MobileDeviceAccountTokenResponse {
  accessToken: string;
  expiresAtUtc: string;
  tokenType: "Bearer" | string;
  sessionKind: "deviceAccount";
  user: MobileDeviceAccountSessionUser;
}

export interface MobileDeviceAccountSessionStore {
  storeGuid: string;
  storeCode: string;
  storeName: string;
  isPrimary: boolean;
}

export interface MobileDeviceAccountSessionUser {
  userGuid: string;
  username: string;
  fullName?: string | null;
  roles: string[];
  stores: MobileDeviceAccountSessionStore[];
}
