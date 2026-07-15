import assert from "node:assert/strict";
import Module from "node:module";

interface RequestRecord {
  body?: unknown;
  method: "delete" | "get" | "put";
  url: string;
}

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

  // Node 测试只验证 API 与纯 query key，不加载 Expo 原生运行时。
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
    fetchStoreUserPosTerminalPermissions,
    normalizeStoreUserPosTerminalPermissions,
    restoreStoreUserPosTerminalPermissions,
    updateStoreUserPosTerminalPermissions,
  } = await import("./pos-terminal-permissions-api");
  const { storeUserPosTerminalPermissionsQueryKey } = await import(
    "./pos-terminal-permissions-hooks"
  );

  const requests: RequestRecord[] = [];
  const originalGet = apiClient.get;
  const originalPut = apiClient.put;
  const originalDelete = apiClient.delete;
  const rawPermissions = {
    mode: "override",
    assignablePermissions: [
      {
        code: "PosTerminal.Sales.AddItem",
        name: "添加商品",
        group: "销售",
        description: "允许添加商品",
      },
      null,
      { code: 123, name: "无效权限", group: "销售", description: "" },
    ],
    inheritedPermissionCodes: ["inherited", 123],
    overriddenPermissionCodes: ["overridden", null],
    grantedPermissionCodes: ["granted", false],
    effectivePermissionCodes: ["effective", {}],
  };

  apiClient.get = (async (url: string) => {
    requests.push({ method: "get", url });
    return { data: { success: true, data: rawPermissions } };
  }) as typeof apiClient.get;
  apiClient.put = (async (url: string, body?: unknown) => {
    requests.push({ body, method: "put", url });
    return { data: rawPermissions };
  }) as typeof apiClient.put;
  apiClient.delete = (async (url: string) => {
    requests.push({ method: "delete", url });
    return { data: { success: true, data: rawPermissions } };
  }) as typeof apiClient.delete;

  try {
    const userGuid = "user/a? b";
    const storeGuid = "store/#1";
    const expectedPath =
      "/Users/guid/user%2Fa%3F%20b/stores/store%2F%231/pos-terminal-permissions";

    const fetched = await fetchStoreUserPosTerminalPermissions(
      userGuid,
      storeGuid
    );
    const updated = await updateStoreUserPosTerminalPermissions({
      userGuid,
      storeGuid,
      grantedPermissionCodes: ["PosTerminal.Sales.AddItem"],
    });
    const restored = await restoreStoreUserPosTerminalPermissions(
      userGuid,
      storeGuid
    );

    assert.deepEqual(requests, [
      { method: "get", url: expectedPath },
      {
        method: "put",
        url: expectedPath,
        body: { grantedPermissionCodes: ["PosTerminal.Sales.AddItem"] },
      },
      { method: "delete", url: expectedPath },
    ]);
    requests.forEach(({ url }) => {
      assert.equal(url.startsWith("/api"), false, `${url} 不应重复添加 /api`);
      assert.equal(url.includes("/api/api"), false, `${url} 不应包含重复 /api`);
    });

    const expectedNormalized = {
      mode: "override",
      assignablePermissions: [rawPermissions.assignablePermissions[0]],
      inheritedPermissionCodes: ["inherited"],
      overriddenPermissionCodes: ["overridden"],
      grantedPermissionCodes: ["granted"],
      effectivePermissionCodes: ["effective"],
    };
    assert.deepEqual(fetched, expectedNormalized, "GET 应兼容一层 envelope 并规范化");
    assert.deepEqual(updated, expectedNormalized, "PUT 应规范化已解包响应");
    assert.deepEqual(restored, expectedNormalized, "DELETE 应兼容 envelope 并规范化");
    assert.throws(
      () =>
        normalizeStoreUserPosTerminalPermissions({
          success: false,
          message: "denied",
          data: null,
        }),
      /denied/,
      "失败 envelope 必须抛出服务端错误，不能规范化为空权限状态"
    );
    assert.throws(
      () =>
        normalizeStoreUserPosTerminalPermissions({
          success: false,
          data: null,
        }),
      /Request failed/,
      "失败 envelope 缺少 message 时应使用稳定错误信息"
    );
    assert.deepEqual(
      normalizeStoreUserPosTerminalPermissions(null),
      {
        mode: "",
        assignablePermissions: [],
        inheritedPermissionCodes: [],
        overriddenPermissionCodes: [],
        grantedPermissionCodes: [],
        effectivePermissionCodes: [],
      },
      "非对象响应应安全回退为空权限状态"
    );
    assert.deepEqual(
      normalizeStoreUserPosTerminalPermissions({ mode: "inherited" }),
      {
        mode: "inherited",
        assignablePermissions: [],
        inheritedPermissionCodes: [],
        overriddenPermissionCodes: [],
        grantedPermissionCodes: [],
        effectivePermissionCodes: [],
      },
      "缺失数组时应使用空数组"
    );

    assert.deepEqual(
      storeUserPosTerminalPermissionsQueryKey(storeGuid, userGuid),
      ["storeUserPosTerminalPermissions", storeGuid, userGuid]
    );
    assert.deepEqual(storeUserPosTerminalPermissionsQueryKey(null, undefined), [
      "storeUserPosTerminalPermissions",
      "",
      "",
    ]);

    const originalError = new Error("API failed");
    apiClient.get = (async () => {
      throw originalError;
    }) as typeof apiClient.get;

    let caughtError: unknown;
    try {
      await fetchStoreUserPosTerminalPermissions(userGuid, storeGuid);
    } catch (error) {
      caughtError = error;
    }
    assert.equal(caughtError, originalError, "API 错误必须原样透传");
  } finally {
    apiClient.get = originalGet;
    apiClient.put = originalPut;
    apiClient.delete = originalDelete;
  }
}

void run();
