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

  // 只运行实际 API 模块的请求逻辑，不启动 Expo 原生环境。
  mockModule("expo-router", { router: { replace: () => undefined } });
  mockModule("react-native", {
    AppState: { addEventListener: () => ({ remove: () => undefined }) },
    NativeModules: {},
    Platform: {
      OS: "ios",
      select: <T>(values: { ios?: T; default?: T }) => values.ios ?? values.default,
    },
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
    default: {
      getItem: async () => null,
      setItem: async () => undefined,
      removeItem: async () => undefined,
    },
  });

  const { apiClient } = await import("../../shared/api/client");
  const { useDeviceStore } = await import("../../store/device-store");
  const { ensureStorePrice } = await import("./api");
  const originalPost = apiClient.post;
  const initialSession = useDeviceStore.getState().session;
  const requests: { url: string; body: unknown; config: unknown }[] = [];
  let reply: unknown = null;
  let requestError: Error | null = null;

  apiClient.post = (async (url: string, body: unknown, config: unknown) => {
    requests.push({ url, body, config });
    if (requestError) throw requestError;
    return { data: reply };
  }) as typeof apiClient.post;

  try {
    useDeviceStore.setState({
      session: { hardwareId: "device-1", authCode: "code-1", storeCode: "S-1" },
    });
    const validReply = {
      Uuid: "price-1",
      ProductCode: "P/1",
      StoreCode: "S-1",
      PurchasePrice: "3.50",
      RetailPrice: "9.90",
      DiscountRate: "1",
      IsActive: true,
    };
    reply = validReply;

    const price = await ensureStorePrice("P/1", "S-1");
    assert.deepEqual(requests[0], {
      url: "/react/v1/store-product-maintenance/P%2F1/ensure-store-price",
      body: null,
      config: {
        headers: { "X-Device-Id": "device-1", "X-Auth-Code": "code-1" },
        params: { storeCode: "S-1" },
      },
    });
    assert.equal(price.uuid, "price-1");
    assert.equal(price.productCode, "P/1");
    assert.equal(price.storeCode, "S-1");
    assert.equal(price.purchasePrice, 3.5);
    assert.equal(price.retailPrice, 9.9);
    assert.equal(price.discountRate, 1);

    for (const invalid of [
      { ...validReply, Uuid: "" },
      { ...validReply, Uuid: "   " },
      { ...validReply, ProductCode: "another-product" },
      { ...validReply, StoreCode: "S-2" },
      null,
    ]) {
      reply = invalid;
      await assert.rejects(
        ensureStorePrice("P/1", "S-1"),
        /INVALID_STORE_PRICE_RESPONSE/,
      );
    }

    requestError = new Error("request failed");
    await assert.rejects(ensureStorePrice("P/1", "S-1"), requestError);

    requestError = null;
    useDeviceStore.setState({ session: null });
    reply = { Uuid: "price-2", ProductCode: "P-2", StoreCode: "S-1" };
    await ensureStorePrice("P-2", "S-1");
    assert.deepEqual(requests.at(-1)?.config, { params: { storeCode: "S-1" } });
  } finally {
    apiClient.post = originalPost;
    useDeviceStore.setState({ session: initialSession });
  }
}

void run().then(() => console.log("ensure-store-price-api.test.ts: ok"));
