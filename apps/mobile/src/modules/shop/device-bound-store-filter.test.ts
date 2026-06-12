import {
  bindDeviceStoreFilter,
  getDeviceBoundStoreCode,
} from "./device-bound-store-filter";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

const accountFilters = bindDeviceStoreFilter(
  { supplierCode: "SUP01" },
  {
    isDeviceMode: false,
    selectedStoreCode: "S001",
    storeField: "storeCode",
  }
);

assertEqual(
  accountFilters.storeCode,
  undefined,
  "account sessions keep all-store filters empty"
);

const deviceFilters = bindDeviceStoreFilter(
  { supplierCode: "SUP01" },
  {
    isDeviceMode: true,
    selectedStoreCode: "S001",
    storeField: "storeCode",
  }
);

assertEqual(
  deviceFilters.storeCode,
  "S001",
  "device sessions force storeCode to the bound store"
);

const branchFilters = bindDeviceStoreFilter(
  { branchCode: undefined, status: "open" },
  {
    isDeviceMode: true,
    selectedStoreCode: "B002",
    storeField: "branchCode",
  }
);

assertEqual(
  branchFilters.branchCode,
  "B002",
  "device sessions force custom branch fields to the bound store"
);

const missingDeviceStore = bindDeviceStoreFilter(
  { storeCode: undefined },
  {
    isDeviceMode: true,
    selectedStoreCode: null,
    storeField: "storeCode",
  }
);

assertEqual(
  missingDeviceStore.storeCode,
  undefined,
  "device sessions without a selected store do not invent a store code"
);

assertEqual(
  getDeviceBoundStoreCode({ isDeviceMode: true, selectedStoreCode: "S003" }),
  "S003",
  "device bound store code is exposed when device mode is active"
);

assertEqual(
  getDeviceBoundStoreCode({ isDeviceMode: false, selectedStoreCode: "S003" }),
  undefined,
  "account sessions do not expose a device bound store code"
);
