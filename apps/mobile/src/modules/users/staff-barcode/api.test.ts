import assert from "node:assert/strict";
import Module from "node:module";

async function run() {
  Object.assign(globalThis, { __DEV__: false });
  const mockModule = (name: string, exports: object) => {
    const filename = require.resolve(name);
    const module = new Module(filename);
    module.filename = filename;
    module.loaded = true;
    module.exports = exports;
    require.cache[filename] = module;
  };
  mockModule("expo-router", { router: { replace: () => undefined } });
  mockModule("react-native", { AppState: { addEventListener: () => ({ remove: () => undefined }) }, NativeModules: {}, Platform: { OS: "ios", select: () => undefined } });
  mockModule("expo-secure-store", { getItemAsync: async () => null, setItemAsync: async () => undefined, deleteItemAsync: async () => undefined });
  mockModule("expo-location", { hasStartedLocationUpdatesAsync: async () => false, stopLocationUpdatesAsync: async () => undefined });
  mockModule("@react-native-async-storage/async-storage", { default: { getItem: async () => null, setItem: async () => undefined, removeItem: async () => undefined } });

  const { apiClient } = await import("../../../shared/api/client");
  const { getStaffCashierBarcodeApi, ensureStaffCashierBarcodeApi, confirmStaffCashierBarcodePrintApi } = await import("../api");
  const calls: { method: string; url: string; data?: unknown; config?: unknown }[] = [];
  const originalGet = apiClient.get;
  const originalPost = apiClient.post;
  apiClient.get = (async (url: string, config?: unknown) => { calls.push({ method: "get", url, config }); return { data: { exists: false } }; }) as typeof apiClient.get;
  apiClient.post = (async (url: string, data?: unknown) => {
    calls.push({ method: "post", url, data });
    return { data: { success: true, data: { Exists: true, Barcode: "2912345678906", PrintCount: 2 } } };
  }) as typeof apiClient.post;
  try {
    assert.equal((await getStaffCashierBarcodeApi("user/a", "S001")).exists, false);
    assert.equal((await ensureStaffCashierBarcodeApi("user/a", "S001")).barcode, "2912345678906");
    assert.equal((await confirmStaffCashierBarcodePrintApi("user/a", "S001", " 2912345678906 ", " attempt-1 ")).printCount, 2);
    assert.deepEqual(calls, [
      { method: "get", url: "/react/v1/store-users/user%2Fa/cashier-barcode", config: { params: { storeCode: "S001" } } },
      { method: "post", url: "/react/v1/store-users/user%2Fa/cashier-barcode/ensure", data: { storeCode: "S001" } },
      { method: "post", url: "/react/v1/store-users/user%2Fa/cashier-barcode/print-confirmation", data: { storeCode: "S001", barcode: "2912345678906", printAttemptId: "attempt-1" } },
    ]);
  } finally {
    apiClient.get = originalGet;
    apiClient.post = originalPost;
  }
}

void run();
