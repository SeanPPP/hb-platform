export const DEVICE_STATUS = {
  PENDING_CONFIRMATION: -1,
  DISABLED: 0,
  ACTIVE: 1,
  LOCKED: 2,
  UNREGISTERED: 3,
} as const;

export type DeviceStatus = (typeof DEVICE_STATUS)[keyof typeof DEVICE_STATUS];

export type DeviceStatusKey =
  | "pendingConfirmation"
  | "disabled"
  | "active"
  | "locked"
  | "unregistered";

export const DEVICE_STATUS_VALUES = [
  DEVICE_STATUS.PENDING_CONFIRMATION,
  DEVICE_STATUS.DISABLED,
  DEVICE_STATUS.ACTIVE,
  DEVICE_STATUS.LOCKED,
  DEVICE_STATUS.UNREGISTERED,
] as const;

const DEVICE_STATUS_KEYS: Record<DeviceStatus, DeviceStatusKey> = {
  [DEVICE_STATUS.PENDING_CONFIRMATION]: "pendingConfirmation",
  [DEVICE_STATUS.DISABLED]: "disabled",
  [DEVICE_STATUS.ACTIVE]: "active",
  [DEVICE_STATUS.LOCKED]: "locked",
  [DEVICE_STATUS.UNREGISTERED]: "unregistered",
};

export function isKnownDeviceStatus(value: unknown): value is DeviceStatus {
  return typeof value === "number" && DEVICE_STATUS_VALUES.includes(value as DeviceStatus);
}

export function normalizeDeviceStatus(
  value: unknown,
  fallback: DeviceStatus = DEVICE_STATUS.UNREGISTERED
): DeviceStatus {
  const numericValue = typeof value === "string" && value.trim() ? Number(value) : value;
  return isKnownDeviceStatus(numericValue) ? numericValue : fallback;
}

export function getDeviceStatusKey(value: unknown): DeviceStatusKey {
  return DEVICE_STATUS_KEYS[normalizeDeviceStatus(value)];
}
