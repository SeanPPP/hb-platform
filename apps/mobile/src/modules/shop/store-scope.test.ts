import {
  getAssignedStoresForSession,
  getManageableStoresForSession,
  resolveScopedStoreCode,
} from "./store-scope";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

const assignedStores = [
  { storeCode: "1006", storeName: "HB WARE HOUSE", isPrimary: false },
  { storeCode: "1004", storeName: "Campbelltown", isPrimary: true },
];

assertEqual(
  getAssignedStoresForSession({
    deviceBoundStore: null,
    isDeviceMode: false,
    stores: assignedStores,
  }).length,
  2,
  "account sessions keep all assigned stores for read access"
);

assertEqual(
  getManageableStoresForSession({
    deviceBoundStore: null,
    isAdmin: false,
    isDeviceMode: false,
    stores: assignedStores,
  }).map((store) => store.storeCode).join(","),
  "1004",
  "non-admin account sessions only manage isPrimary stores"
);

const deviceScopedStores = getAssignedStoresForSession({
  deviceBoundStore: { storeCode: "1024", storeName: "Bankstown" },
  isDeviceMode: true,
  stores: assignedStores,
});

assertEqual(deviceScopedStores.length, 1, "device sessions expose only the bound store");
assertEqual(deviceScopedStores[0]?.storeCode, "1024", "device sessions ignore account stores");

assertEqual(
  resolveScopedStoreCode({
    currentStoreCode: "1006",
    deviceBoundStoreCode: "1024",
    isDeviceMode: true,
    stores: assignedStores,
  }),
  "1024",
  "device sessions lock selection to the bound store"
);

assertEqual(
  resolveScopedStoreCode({
    currentStoreCode: "1004",
    deviceBoundStoreCode: null,
    isDeviceMode: false,
    stores: assignedStores,
  }),
  "1004",
  "account sessions keep an assigned current selection"
);
