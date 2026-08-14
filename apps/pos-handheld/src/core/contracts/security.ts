export const DEVICE_SYSTEM_IOS = "iOS" as const;
export const DEVICE_SYSTEM_ANDROID = "Android" as const;
export type DeviceSystem =
  | typeof DEVICE_SYSTEM_IOS
  | typeof DEVICE_SYSTEM_ANDROID;

export type DeviceIdentity = Readonly<{
  installationId: string;
  deviceCode: string;
  storeCode: string;
  authorizationCode: string;
  deviceSystem: DeviceSystem;
}>;

export type CashierSession = Readonly<{
  cashierId: string;
  cashierName: string;
  userBarcode: string;
  allowedStoreCodes: readonly string[];
  permissions: readonly string[];
  authorizationTicket: string;
}>;

export type DeviceRegistrationState =
  | "unregistered"
  | "registering"
  | "pending-approval"
  | "verifying"
  | "authorized"
  | "denied"
  | "disabled";
