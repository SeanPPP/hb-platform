export const DEVICE_SYSTEM_IPAD = "iPadOS" as const;
export type DeviceSystem = typeof DEVICE_SYSTEM_IPAD;

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
