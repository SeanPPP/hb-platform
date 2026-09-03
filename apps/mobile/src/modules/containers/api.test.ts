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
  const {
    batchUpdateDetails,
    getContainerDetailPresence,
    heartbeatContainerDetailPresence,
    previewContainerDetailBatchAction,
  } = await import("./api");
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
        conflicts: [{
          hguid: "DETAIL-CONFLICT",
          field: "进口价格",
          code: "CONCURRENT_FIELD_UPDATE",
          message: "服务器已更新",
          serverValue: 4.2,
          submittedValue: 4.56,
          currentServerFieldToken: "current-token",
        }],
      },
    },
    {
      data: {
        totalUpdated: 1,
        totalRequested: 1,
      },
    },
    { data: { viewers: [], editors: [] } },
    { data: { viewers: [], editors: [] } },
    { data: { previewToken: "preview-token", affectedCount: 1, fieldSummary: ["进口价格"] } },
    { data: { totalUpdated: 2, totalRequested: 2 } },
  ];
  const requests: { url: string; body: unknown }[] = [];
  const originalGet = apiClient.get;

  apiClient.post = (async (url: string, body: unknown) => {
    requests.push({ url, body });
    const response = responses.shift();
    assert.ok(response, "每次调用都必须有对应的模拟响应");
    return response;
  }) as typeof apiClient.post;
  apiClient.get = (async (url: string) => {
    requests.push({ url, body: undefined });
    const response = responses.shift();
    assert.ok(response, "每次调用都必须有对应的模拟响应");
    return response;
  }) as typeof apiClient.get;

  try {
    const payload = [{
      hguid: "DETAIL-CHINESE",
      英文名称: "Large 草莓",
      进口价格: 4.56,
      expectedServerFieldTokens: { "进口价格": "baseline-token" },
      overrideAcknowledgements: { "进口价格": "current-token" },
    }];
    const validationResult = await batchUpdateDetails("CONTAINER-1", payload);
    assert.deepEqual(
      requests[0],
      {
        url: "/react/v1/containers/CONTAINER-1/batch-update-details",
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
          ExpectedServerFieldTokens: { "进口价格": "baseline-token" },
          OverrideAcknowledgements: { "进口价格": "current-token" },
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
        conflicts: [{
          hguid: "DETAIL-CONFLICT",
          field: "进口价格",
          code: "CONCURRENT_FIELD_UPDATE",
          message: "服务器已更新",
          serverValue: 4.2,
          submittedValue: 4.56,
          currentServerFieldToken: "current-token",
        }],
      },
      "必须保留完整字段校验错误，并丢弃无法安全展示的不完整条目",
    );

    const legacyResult = await batchUpdateDetails("CONTAINER-1", payload);
    assert.deepEqual(
      legacyResult,
      {
        totalUpdated: 1,
        totalRequested: 1,
        validationErrors: [],
        conflicts: [],
      },
      "旧响应缺少 validationErrors 时必须兼容为空数组",
    );

    const presence = await getContainerDetailPresence("CONTAINER-1");
    assert.deepEqual(requests[2], {
      url: "/react/v1/containers/CONTAINER-1/editing-presence",
      body: undefined,
    }, "活动用户查询必须走容器隔离端点");
    assert.deepEqual(presence, { viewers: [], editors: [] });

    await heartbeatContainerDetailPresence("CONTAINER-1", {
      clientSessionId: "mobile-session",
      state: "editing",
    });
    assert.deepEqual(requests[3], {
      url: "/react/v1/containers/CONTAINER-1/editing-presence/heartbeat",
      body: { clientSessionId: "mobile-session", state: "editing" },
    }, "心跳必须发送独立客户端会话和查看/编辑状态");

    const preview = await previewContainerDetailBatchAction("CONTAINER-1", {
      operation: "apply-prices",
      scope: { selectedHguids: ["DETAIL-1"] },
      parameters: { importPrice: 4.5 },
    });
    assert.deepEqual(requests[4], {
      url: "/react/v1/containers/CONTAINER-1/actions/preview",
      body: {
        operation: "apply-prices",
        scope: { selectedHguids: ["DETAIL-1"] },
        parameters: { importPrice: 4.5 },
      },
    }, "批量执行前必须先获取服务器签名预览令牌");
    assert.equal(preview.previewToken, "preview-token");

    await batchUpdateDetails("CONTAINER-1", [
      { hguid: "DETAIL-A", 国内价格: 8.8, expectedServerFieldTokens: { "国内价格": "price-token" } },
      { hguid: "DETAIL-B", 进口价格: 4.4, expectedServerFieldTokens: { "进口价格": "import-token" } },
    ]);
    const differentFieldBody = requests[5]?.body as Record<string, unknown>[];
    assert.deepEqual(differentFieldBody.map((item) => item.ExpectedServerFieldTokens), [
      { "国内价格": "price-token" },
      { "进口价格": "import-token" },
    ], "不同字段并发编辑时，客户端必须只提交各自字段的基线令牌，允许服务器自动合并");

    apiClient.post = (async () => {
      throw {
        response: {
          status: 428,
          data: { code: "CONCURRENCY_TOKEN_REQUIRED", message: "请升级" },
        },
      };
    }) as typeof apiClient.post;
    await assert.rejects(
      () => batchUpdateDetails("CONTAINER-1", payload),
      (error: unknown) => error instanceof Error && (error as Error & { code?: string }).code === "CONCURRENCY_TOKEN_REQUIRED",
      "旧客户端缺少令牌时必须保留稳定升级错误码",
    );
  } finally {
    apiClient.post = originalPost;
    apiClient.get = originalGet;
  }

  console.log("containers/api.test.ts: ok");
}

void run();
