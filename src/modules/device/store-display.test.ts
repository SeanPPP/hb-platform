import { resolveDeviceStoreDisplayName } from "./store-display";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

assertEqual(
  resolveDeviceStoreDisplayName({
    deviceStoreCode: "1004",
    deviceStoreName: "Sunnybank",
    stores: [],
    fallback: "Select store",
  }),
  "Sunnybank",
  "device session store name is preferred"
);

assertEqual(
  resolveDeviceStoreDisplayName({
    deviceStoreCode: "1004",
    deviceStoreName: null,
    stores: [{ storeCode: "1004", storeName: "Sunnybank" }],
    fallback: "Select store",
  }),
  "Sunnybank",
  "matching store list supplies the display name when session only has a code"
);

assertEqual(
  resolveDeviceStoreDisplayName({
    deviceStoreCode: "1004",
    deviceStoreName: null,
    stores: [],
    fallback: "Select store",
  }),
  "1004",
  "store code remains the fallback when no name is available"
);
