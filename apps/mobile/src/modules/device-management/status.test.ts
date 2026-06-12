import {
  DEVICE_STATUS,
  DEVICE_STATUS_VALUES,
  getDeviceStatusKey,
  isKnownDeviceStatus,
  normalizeDeviceStatus,
} from "./status";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

assertEqual(DEVICE_STATUS.PENDING_CONFIRMATION, -1, "pending confirmation status value");
assertEqual(DEVICE_STATUS.DISABLED, 0, "disabled status value");
assertEqual(DEVICE_STATUS.ACTIVE, 1, "active status value");
assertEqual(DEVICE_STATUS.LOCKED, 2, "locked status value");
assertEqual(DEVICE_STATUS.UNREGISTERED, 3, "unregistered status value");

assertEqual(DEVICE_STATUS_VALUES.length, 5, "exports all known status values");
assertEqual(normalizeDeviceStatus("-1"), -1, "normalizes numeric string");
assertEqual(normalizeDeviceStatus(2), 2, "normalizes known number");
assertEqual(normalizeDeviceStatus("unknown"), 3, "unknown value falls back to unregistered");
assertEqual(isKnownDeviceStatus(0), true, "detects known disabled status");
assertEqual(isKnownDeviceStatus(99), false, "rejects unknown status");
assertEqual(getDeviceStatusKey(-1), "pendingConfirmation", "pending key");
assertEqual(getDeviceStatusKey(0), "disabled", "disabled key");
assertEqual(getDeviceStatusKey(1), "active", "active key");
assertEqual(getDeviceStatusKey(2), "locked", "locked key");
assertEqual(getDeviceStatusKey(3), "unregistered", "unregistered key");
