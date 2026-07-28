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

  // Node 契约测试只验证货柜 API，不加载 Expo 原生运行时。
  mockModule("expo-router", { router: { replace: () => undefined } });
  mockModule("react-native", {
    AppState: { addEventListener: () => ({ remove: () => undefined }) },
    NativeModules: {},
    Platform: {
      OS: "ios",
      select: <T>(values: { ios?: T; default?: T }) =>
        values.ios ?? values.default,
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
  const { batchUpdateDetails } = await import("./api");
  const originalPost = apiClient.post;
  const responses = [
    {
      data: {
        totalUpdated: 1,
        totalRequested: 1,
        validationErrors: [
          {
            hguid: "DETAIL-CHINESE",
            field: "英文名称",
            code: "CONTAINS_CHINESE",
            message: "英文名称不能包含中文",
          },
          {
            hguid: "DETAIL-INCOMPLETE",
            field: "英文名称",
            code: "CONTAINS_CHINESE",
          },
        ],
      },
    },
    {
      data: {
        totalUpdated: 1,
        totalRequested: 1,
      },
    },
  ];
  const requests: Array<{ url: string; body: unknown }> = [];

  apiClient.post = (async (url: string, body: unknown) => {
    requests.push({ url, body });
    const response = responses.shift();
    assert.ok(response, "每次调用都必须有对应的模拟响应");
    return response;
  }) as typeof apiClient.post;

  try {
    const payload = [{
      hguid: "DETAIL-CHINESE",
      英文名称: "Large 草莓",
      进口价格: 4.56,
    }];
    const validationResult = await batchUpdateDetails(payload);
    assert.deepEqual(
      requests[0],
      {
        url: "/react/v1/containers/batch-update-details",
        body: [{
          HGUID: "DETAIL-CHINESE",
          调整浮率: undefined,
          国内价格: undefined,
          进口价格: 4.56,
          运输成本: undefined,
          商品名称: undefined,
          英文名称: "Large 草莓",
          ClearEnglishName: undefined,
          贴牌价格: undefined,
          单件装箱数: undefined,
          中包数: undefined,
          单件体积: undefined,
          装柜数量: undefined,
          合计装柜体积: undefined,
          合计装柜金额: undefined,
          IsActive: undefined,
          SkipRelatedProductSync: undefined,
        }],
      },
      "批量更新必须继续调用同一 React 端点并保留现有 payload 映射",
    );
    assert.deepEqual(
      validationResult,
      {
        totalUpdated: 1,
        totalRequested: 1,
        validationErrors: [{
          hguid: "DETAIL-CHINESE",
          field: "英文名称",
          code: "CONTAINS_CHINESE",
          message: "英文名称不能包含中文",
        }],
      },
      "必须保留完整字段校验错误，并丢弃无法安全展示的不完整条目",
    );

    const legacyResult = await batchUpdateDetails(payload);
    assert.deepEqual(
      legacyResult,
      {
        totalUpdated: 1,
        totalRequested: 1,
        validationErrors: [],
      },
      "旧响应缺少 validationErrors 时必须兼容为空数组",
    );
  } finally {
    apiClient.post = originalPost;
  }

  console.log("containers/api.test.ts: ok");
}

void run();
