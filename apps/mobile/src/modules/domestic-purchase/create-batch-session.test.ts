import assert from "node:assert/strict";
import Module from "node:module";
import React, { act } from "react";

type Deferred<T> = {
  promise: Promise<T>;
  resolve(value: T): void;
  reject(error: unknown): void;
};

function deferred<T>(): Deferred<T> {
  let resolve!: (value: T) => void;
  let reject!: (error: unknown) => void;
  const promise = new Promise<T>((nextResolve, nextReject) => {
    resolve = nextResolve;
    reject = nextReject;
  });
  return { promise, resolve, reject };
}

async function flushMicrotasks() {
  await Promise.resolve();
  await Promise.resolve();
}

async function run() {
  const translate = (key: string) => key;
  Object.assign(globalThis, { __DEV__: false, IS_REACT_ACT_ENVIRONMENT: true });

  const mockModule = (name: string, exports: object) => {
    const filename = require.resolve(name);
    const module = new Module(filename);
    module.filename = filename;
    module.loaded = true;
    module.exports = exports;
    require.cache[filename] = module;
  };

  // 仅加载 hook 的 React 生命周期；网络与翻译都在本测试内隔离。
  mockModule("expo-router", { router: { replace: () => undefined } });
  mockModule("react-native", {
    AppState: { addEventListener: () => ({ remove: () => undefined }) },
    Keyboard: { dismiss: () => undefined },
    NativeModules: {},
    Platform: { OS: "ios", select: <T>(values: { ios?: T; default?: T }) => values.ios ?? values.default },
    StyleSheet: { flatten: (style: unknown) => style },
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
  mockModule(require.resolve("../../shared/i18n/use-app-translation"), {
    useAppTranslation: () => ({
      language: "zh-CN",
      t: translate,
    }),
  });

  // pnpm 下 mobile 与根 workspace 各有一份 React；renderer 必须复用 Hook 使用的同一份实例。
  const rendererReactFilename = require.resolve("react", {
    paths: [require.resolve("test-renderer")],
  });
  if (rendererReactFilename !== require.resolve("react")) {
    mockModule(rendererReactFilename, React);
  }

  const { createRoot } = await import("test-renderer");
  const { apiClient } = await import("../../shared/api/client");
  const { useCreateBatch } = await import("./use-create-batch");

  const originalGet = apiClient.get;
  const originalPost = apiClient.post;
  const prefixRequests = new Map<string, Deferred<unknown>>();
  const templateRequests = new Map<string, Deferred<unknown>>();
  const postResponses: (unknown | Error)[] = [];
  const calls: { method: string; url: string; value?: unknown }[] = [];
  const created: unknown[] = [];
  let returnedToList = 0;

  apiClient.get = (async (url: string, config?: { params?: { supplierCode?: string } }) => {
    calls.push({ method: "GET", url, value: config });
    if (url.endsWith("/ChinaSuppliers/active")) {
      return {
        data: [
          { supplierCode: "SUP-A", supplierName: "供应商 A" },
          { supplierCode: "SUP-B", supplierName: "供应商 B" },
        ],
      } as never;
    }
    if (url.endsWith("/ProductPrefixCodes")) {
      const supplierCode = config?.params?.supplierCode ?? "";
      const request = prefixRequests.get(supplierCode);
      assert.ok(request, `应为 ${supplierCode} 创建前缀请求`);
      return request.promise as never;
    }
    if (url.endsWith("/domestic-product-creation/templates")) {
      const supplierCode = config?.params?.supplierCode ?? "";
      const request = templateRequests.get(supplierCode);
      assert.ok(request, `应为 ${supplierCode} 创建模板请求`);
      return request.promise as never;
    }
    // hook 提交成功后不应读取批次详情；如果误读，这个错误会让测试直接失败。
    throw new Error(`不应访问批次详情: ${url}`);
  }) as typeof apiClient.get;

  apiClient.post = (async (url: string, body?: unknown) => {
    calls.push({ method: "POST", url, value: body });
    if (url.endsWith("/domestic-product-creation/batch")) {
      const response = postResponses.shift();
      assert.ok(response !== undefined, "每次创建都必须有明确的模拟结果");
      if (response instanceof Error) throw response;
      return { data: response } as never;
    }
    throw new Error(`不应访问其他 POST: ${url}`);
  }) as typeof apiClient.post;

  const prefixA = deferred<unknown>();
  const prefixB = deferred<unknown>();
  const templatesA = deferred<unknown>();
  const templatesB = deferred<unknown>();
  prefixRequests.set("SUP-A", prefixA);
  prefixRequests.set("SUP-B", prefixB);
  templateRequests.set("SUP-A", templatesA);
  templateRequests.set("SUP-B", templatesB);

  const onDismiss = () => undefined;
  const onReturnToList = () => { returnedToList++; };
  const onCreated = (result: unknown) => created.push(result);
  type HookValue = ReturnType<typeof useCreateBatch>;
  let current!: HookValue;
  function HookHarness() {
    current = useCreateBatch({ onDismiss, onReturnToList, onCreated });
    return null;
  }

  // 使用 test-renderer 真实挂载/更新/卸载 Hook，确保 useEffect 与 state 更新都经过 React 调度。
  const renderer = createRoot();
  await act(async () => {
    renderer.render(React.createElement(HookHarness));
    await flushMicrotasks();
  });

  try {
    const waitForCondition = async (check: () => void, timeoutMs = 1000) => {
      const deadline = Date.now() + timeoutMs;
      let lastError: unknown;
      while (Date.now() < deadline) {
        try {
          check();
          return;
        } catch (error) {
          lastError = error;
          await new Promise((resolve) => setTimeout(resolve, 0));
        }
      }
      throw lastError ?? new Error("等待 React 状态更新超时");
    };

    await waitForCondition(() => assert.equal(current.suppliers.length, 2));

    await act(() => {
      current.selectSupplier(current.suppliers[0]!);
      current.setProducts((items) => [{ ...items[0]!, productName: "保留中的产品草稿" }]);
    });
    await act(() => {
      current.selectSupplier(current.suppliers[1]!);
    });

    await act(async () => {
      prefixB.resolve({ data: { items: [{ prefixCode: "B-001", prefixName: "B 前缀" }] } });
      templatesB.resolve({ data: [{ templateId: "template-b", supplierCode: "SUP-B", templateName: "B 模板", setProductName: "B 套装", isEnabled: true, setQuantity: 1 }] });
      await flushMicrotasks();
    });
    await waitForCondition(() => {
      assert.equal(current.prefixes[0]?.prefixCode, "B-001");
      assert.equal(current.templates[0]?.templateId, "template-b");
    });

    await act(async () => {
      prefixA.resolve({ data: { items: [{ prefixCode: "A-001", prefixName: "A 前缀" }] } });
      templatesA.resolve({ data: [{ templateId: "template-a", supplierCode: "SUP-A", templateName: "A 模板", setProductName: "A 套装", isEnabled: true, setQuantity: 1 }] });
      await flushMicrotasks();
    });
    assert.equal(current.prefixes[0]?.prefixCode, "B-001", "供应商 A 的慢前缀请求不能污染 B");
    assert.equal(current.templates[0]?.templateId, "template-b", "供应商 A 的慢模板请求不能污染 B");
    assert.equal(current.products[0]?.productName, "保留中的产品草稿", "切换供应商必须保留商品输入");

    // 本地草稿校验发生在网络写入前，允许修改后重试且不能进入结果未知终态。
    await act(() => {
      current.setProducts((items) => [{ ...items[0]!, privateLabelPrice: "invalid" }]);
    });
    await act(async () => { await current.submit(); });
    assert.equal(calls.filter((call) => call.method === "POST" && call.url.endsWith("/batch")).length, 0);
    assert.equal(current.creationUncertain, false, "本地校验失败不能锁死创建会话");
    await act(() => {
      current.setProducts((items) => [{ ...items[0]!, privateLabelPrice: "" }]);
    });

    // 明确的 4xx 业务拒绝保留草稿并允许用户修正后重试。
    postResponses.push(Object.assign(new Error("业务校验失败"), {
      response: { status: 400, data: { success: false, errorCode: "VALIDATION_ERROR" } },
    }));
    await act(async () => {
      await current.submit();
    });
    assert.equal(current.products[0]?.productName, "保留中的产品草稿", "业务失败后必须保留草稿");
    assert.equal(created.length, 0, "业务失败不能伪造成功通知");
    assert.equal(current.creationUncertain, false, "明确拒绝不能误判成结果未知");

    postResponses.push({ batchNumber: "BATCH-RETRY", totalCreated: 1, normalProductCount: 1, setProductCount: 0 });
    await act(() => {
      current.submit();
      current.submit();
    });
    await waitForCondition(() => assert.equal(created.length, 1, "用户显式重试成功后应通知外层"));
    assert.equal((created[0] as { batchNumber: string }).batchNumber, "BATCH-RETRY");
    assert.equal(calls.filter((call) => call.method === "POST" && call.url.endsWith("/batch")).length, 2, "连续点击只能产生一次重试 POST");
    await act(async () => { await current.submit(); });
    assert.equal(calls.filter((call) => call.method === "POST" && call.url.endsWith("/batch")).length, 2, "成功终态不能再次 POST");
    assert.equal(calls.some((call) => call.url.includes("/batch/") && call.method === "GET"), false, "提交成功不应在 hook 内读取详情");

    const uncertainErrors = [
      ["网络错误", Object.assign(new Error("Network Error"), { code: "ERR_NETWORK" })],
      ["超时", Object.assign(new Error("timeout"), { code: "ECONNABORTED" })],
      ["服务端 500", Object.assign(new Error("server error"), { response: { status: 500, data: { success: false, errorCode: "CREATE_BATCH_ERROR" } } })],
      ["取消", Object.assign(new Error("cancelled"), { code: "ERR_CANCELED" })],
      ["无法分类错误", new Error("unknown")],
    ] as const;
    for (const [label, error] of uncertainErrors) {
      // 每个错误使用独立表单会话，验证所有不确定结果都只能回列表核对。
      await act(async () => {
        renderer.render(React.createElement(HookHarness, { key: `uncertain-${label}` }));
        await flushMicrotasks();
      });
      await waitForCondition(() => assert.equal(current.suppliers.length, 2));
      await act(() => { current.selectSupplier(current.suppliers[1]!); });
      const postsBeforeUnknown = calls.filter((call) => call.method === "POST" && call.url.endsWith("/batch")).length;
      postResponses.push(error);
      await act(async () => { await current.submit(); });
      assert.equal(current.creationUncertain, true, `${label}必须进入结果未知终态`);
      assert.equal(current.notice, "wizard.creationResultUncertain");
      assert.equal(calls.filter((call) => call.method === "POST" && call.url.endsWith("/batch")).length, postsBeforeUnknown + 1);
      await act(async () => { await current.submit(); });
      assert.equal(calls.filter((call) => call.method === "POST" && call.url.endsWith("/batch")).length, postsBeforeUnknown + 1, `${label}后不能再次 POST`);
    }
    await act(() => { current.returnToList(); });
    assert.equal(returnedToList, 1, "结果未知时必须提供返回批次列表并刷新的入口");
  } finally {
    await act(async () => {
      renderer.unmount();
    });
    apiClient.get = originalGet;
    apiClient.post = originalPost;
  }

  console.log("create-batch-session.test.ts: ok");
}

void run();
