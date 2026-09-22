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
  mockModule("react-native", {
    AppState: { addEventListener: () => ({ remove: () => undefined }) },
    NativeModules: {},
    Platform: { OS: "ios", select: <T>(values: { ios?: T; default?: T }) => values.ios ?? values.default },
  });
  mockModule("expo-secure-store", {
    getItemAsync: async () => null,
    setItemAsync: async () => undefined,
    deleteItemAsync: async () => undefined,
  });
  mockModule("expo-location", {
    hasStartedLocationUpdatesAsync: async () => false,
    stopLocationUpdatesAsync: async () => undefined,
  });
  mockModule("@react-native-async-storage/async-storage", {
    default: { getItem: async () => null, setItem: async () => undefined, removeItem: async () => undefined },
  });

  const { apiClient } = await import("../../shared/api/client");
  const { useDeviceStore } = await import("../../store/device-store");
  const { scanProductLabel } = await import("./api");
  const originalPost = apiClient.post;
  const initialSession = useDeviceStore.getState().session;
  let reply: unknown = {
    Candidates: [{ ProductCode: "P1", ProductName: "Tissue", MatchSource: "ProductBarcode", MatchValue: "9528503822107" }],
    Detail: {
      ProductCode: "P1",
      ProductName: "Tissue",
      Barcode: "9528503822107",
      StorePrice: { Uuid: "price-1", StoreCode: "1042", RetailPrice: "1.50", DiscountRate: "0.20" },
    },
    PrintTarget: {
      Kind: "product",
      Barcode: "9528503822107",
      RetailPrice: "1.50",
      DiscountRate: "0.20",
      CodeId: "price-1",
      ProductCode: "P1",
      StoreCode: "1042",
    },
  };
  const requests: { url: string; body: unknown; config: unknown }[] = [];
  apiClient.post = (async (url: string, body: unknown, config: unknown) => {
    requests.push({ url, body, config });
    return { data: reply };
  }) as typeof apiClient.post;
  try {
    useDeviceStore.setState({ session: { hardwareId: "device-1", authCode: "code-1", storeCode: "1042" } });
    const response = await scanProductLabel({ keyword: "9528503822107", storeCode: "1042" });
    assert.equal(requests.length, 1);
    assert.deepEqual(requests[0], {
      url: "/react/v1/store-product-maintenance/scan-label",
      body: { keyword: "9528503822107", storeCode: "1042" },
      config: {
        headers: { "X-Device-Id": "device-1", "X-Auth-Code": "code-1" },
        timeout: 10_000,
      },
    });
    assert.equal(response.candidates[0].productCode, "P1");
    assert.equal(response.detail?.storePrice?.retailPrice, 1.5);
    assert.equal(response.detail?.storePrice?.discountRate, 0.2);
    assert.equal(response.printTarget?.retailPrice, 1.5);
    assert.equal(response.printTarget?.discountRate, 0.2);
    reply = {};
    await assert.rejects(scanProductLabel({ keyword: "x", storeCode: "1042" }), /INVALID_SCAN_LABEL_RESPONSE/);
  } finally {
    apiClient.post = originalPost;
    useDeviceStore.setState({ session: initialSession });
  }
}

void run().then(() => console.log("scan-label-api.test.ts: ok"));
