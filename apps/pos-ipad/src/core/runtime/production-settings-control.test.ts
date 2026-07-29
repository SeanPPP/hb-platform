import assert from "node:assert/strict";
import test from "node:test";

import type {
  SettingsDangerousConfirmation,
  SettingsPendingDataSnapshot,
} from "../../features/settings/settings-presenter";

import {
  ProductionSettingsControl,
  type ProductionSettingsControlDependencies,
} from "./production-settings-control";

const CLEAR: SettingsPendingDataSnapshot = {
  hasActiveCart: false,
  pendingDurableWriteCount: 0,
  pendingReturnCount: 0,
  pendingSaleCount: 0,
  unresolvedPaymentCount: 0,
};

test("测试 API 连接只探测候选 health，不保存、不重载", async () => {
  const events: string[] = [];
  const subject = new ProductionSettingsControl(
    deps({
      apiConfiguration: {
        probe: async (url: string) => {
          events.push(`probe:${url}`);
          return true;
        },
        save: async (url: string) => {
          events.push(`save:${url}`);
        },
      },
      runtimeReload: {
        reload: async () => {
          events.push("reload");
        },
      },
    }),
  );

  assert.equal(
    await subject.testApiAddress(
      "http://localhost:5159",
      new AbortController().signal,
    ),
    true,
  );
  assert.deepEqual(events, [
    "probe:http://localhost:5159/api/v1/health",
  ]);
});

test("API 切换先探测候选 health；失败不保存，成功只保存规范地址", async () => {
  const events: string[] = [];
  let reachable = false;
  const subject = new ProductionSettingsControl(
    deps({
      pendingData: { read: async () => CLEAR },
      apiConfiguration: {
        probe: async (url: string) => {
          events.push(`probe:${url}`);
          return reachable;
        },
        save: async (url: string) => {
          events.push(`save:${url}`);
        },
      },
      runtimeReload: {
        reload: async () => {
          events.push("reload");
        },
      },
    }),
  );

  const blocked = await subject.executeDangerousAction(
    {
      kind: "change-api-address",
      apiBaseUrl: "https://next.example.test/pos",
    },
    new AbortController().signal,
  );
  assert.deepEqual(blocked, {
    status: "blocked",
    reason: "candidate-unreachable",
  });
  assert.deepEqual(events, [
    "probe:https://next.example.test/pos/api/v1/health",
  ]);

  reachable = true;
  const completed = await subject.executeDangerousAction(
    {
      kind: "change-api-address",
      apiBaseUrl: "https://next.example.test/pos",
    },
    new AbortController().signal,
  );
  assert.equal(completed.status, "completed");
  assert.deepEqual(events.slice(-2), [
    "save:https://next.example.test/pos",
    "reload",
  ]);
});

test("目录刷新中 API 切换与目录重置均 fail closed，控制面透传共享状态订阅", async () => {
  const events: string[] = [];
  let refreshState: ReturnType<
    ProductionSettingsControl["getCatalogRefreshState"]
  > = { kind: "idle" };
  const listeners = new Set<() => void>();
  const subject = new ProductionSettingsControl(
    deps({
      pendingData: {
        read: async () => {
          events.push("pending");
          return CLEAR;
        },
      },
      apiConfiguration: {
        probe: async () => {
          events.push("probe");
          return true;
        },
        save: async () => {
          events.push("save");
        },
      },
      catalog: {
        getRefreshState: () => refreshState,
        subscribeRefresh: (listener) => {
          listeners.add(listener);
          return () => listeners.delete(listener);
        },
        runExclusive: (operation) => operation(),
        download: async () => {
          throw new Error("not implemented");
        },
        reset: async () => {
          events.push("reset");
          return {
            snapshotId: null,
            itemCount: 0,
            activatedAt: null,
          };
        },
      },
    }),
  );
  let notifications = 0;
  const unsubscribe = subject.subscribeCatalogRefresh(() => {
    notifications += 1;
  });
  refreshState = {
    kind: "running",
    storeCode: "S1",
    progress: {
      currentStep: "prepare",
      overallPercent: 0,
      elapsedMilliseconds: 12_000,
      steps: [
        { step: "prepare", percent: 0 },
        { step: "products", percent: 0 },
        { step: "promotions", percent: 0 },
        { step: "activate", percent: 0 },
      ],
    },
  };
  for (const listener of listeners) listener();

  assert.equal(subject.getCatalogRefreshState().kind, "running");
  assert.equal(notifications, 1);
  assert.deepEqual(
    await subject.executeDangerousAction(
      {
        kind: "change-api-address",
        apiBaseUrl: "https://next.example.test/pos",
      },
      new AbortController().signal,
    ),
    { status: "blocked", reason: "safety-check-failed" },
  );
  assert.deepEqual(
    await subject.executeDangerousAction(
      { kind: "reset-catalog" },
      new AbortController().signal,
    ),
    { status: "blocked", reason: "safety-check-failed" },
  );
  assert.deepEqual(events, []);

  unsubscribe();
  assert.equal(listeners.size, 0);
});

test("安全检查期间目录刷新启动时，API 保存与目录重置仍不会穿过竞态窗口", async () => {
  let refreshRunning = false;
  const events: string[] = [];
  const subject = new ProductionSettingsControl(
    deps({
      pendingData: {
        read: async () => {
          events.push("pending");
          return CLEAR;
        },
      },
      catalog: {
        getRefreshState: () =>
          refreshRunning
            ? {
                kind: "running",
                storeCode: "S1",
                progress: {
                  currentStep: "prepare",
                  overallPercent: 0,
                  elapsedMilliseconds: 1_000,
                  steps: [
                    { step: "prepare", percent: 0 },
                    { step: "products", percent: 0 },
                    { step: "promotions", percent: 0 },
                    { step: "activate", percent: 0 },
                  ],
                },
              }
            : { kind: "idle" },
        subscribeRefresh: () => () => undefined,
        runExclusive: (operation) => operation(),
        download: async () => {
          throw new Error("not implemented");
        },
        reset: async () => {
          events.push("reset");
          return {
            snapshotId: null,
            itemCount: 0,
            activatedAt: null,
          };
        },
      },
      apiConfiguration: {
        probe: async () => {
          events.push("probe");
          refreshRunning = true;
          return true;
        },
        save: async () => {
          events.push("save");
        },
      },
      runtimeReload: {
        reload: async () => {
          events.push("reload");
        },
      },
    }),
  );

  assert.deepEqual(
    await subject.executeDangerousAction(
      {
        kind: "change-api-address",
        apiBaseUrl: "https://next.example.test/pos",
      },
      new AbortController().signal,
    ),
    { status: "blocked", reason: "safety-check-failed" },
  );
  assert.deepEqual(events, ["pending", "probe"]);

  refreshRunning = false;
  events.length = 0;
  const subjectReset = new ProductionSettingsControl(
    deps({
      pendingData: {
        read: async () => {
          events.push("pending");
          refreshRunning = true;
          return CLEAR;
        },
      },
      catalog: {
        getRefreshState: () =>
          refreshRunning
            ? {
                kind: "running",
                storeCode: "S1",
                progress: {
                  currentStep: "prepare",
                  overallPercent: 0,
                  elapsedMilliseconds: 1_000,
                  steps: [
                    { step: "prepare", percent: 0 },
                    { step: "products", percent: 0 },
                    { step: "promotions", percent: 0 },
                    { step: "activate", percent: 0 },
                  ],
                },
              }
            : { kind: "idle" },
        subscribeRefresh: () => () => undefined,
        runExclusive: (operation) => operation(),
        download: async () => {
          throw new Error("not implemented");
        },
        reset: async () => {
          events.push("reset");
          return {
            snapshotId: null,
            itemCount: 0,
            activatedAt: null,
          };
        },
      },
    }),
  );
  assert.deepEqual(
    await subjectReset.executeDangerousAction(
      { kind: "reset-catalog" },
      new AbortController().signal,
    ),
    { status: "blocked", reason: "safety-check-failed" },
  );
  assert.deepEqual(events, ["pending"]);
});

test("任一活动购物车、未决支付或耐久队列都会原子阻断危险动作", async () => {
  let executed = false;
  const subject = new ProductionSettingsControl(
    deps({
      pendingData: {
        read: async () => ({
          ...CLEAR,
          unresolvedPaymentCount: 1,
        }),
      },
      device: {
        reregister: async () => {
          executed = true;
        },
      },
    }),
  );

  const result = await subject.executeDangerousAction(
    {
      kind: "reregister-device",
      targetStoreCode: "S2",
    },
    new AbortController().signal,
  );

  assert.deepEqual(result, {
    status: "blocked",
    reason: "pending-local-data",
  });
  assert.equal(executed, false);
});

test("开发豁免只允许 API 切换跨过待处理门禁，其他危险动作仍阻断", async () => {
  const events: string[] = [];
  const subject = new ProductionSettingsControl(
    deps({
      pendingData: {
        read: async () => ({
          ...CLEAR,
          pendingDurableWriteCount: 3,
        }),
      },
      apiConfiguration: {
        allowSwitchWithPendingLocalData: true,
        probe: async (url: string) => {
          events.push(`probe:${url}`);
          return true;
        },
        save: async (url: string) => {
          events.push(`save:${url}`);
        },
      },
      runtimeReload: {
        reload: async () => {
          events.push("reload");
        },
      },
      catalog: {
        getRefreshState: () => ({ kind: "idle" }),
        subscribeRefresh: () => () => undefined,
        runExclusive: (operation) => operation(),
        download: async () => {
          throw new Error("not implemented");
        },
        reset: async () => {
          events.push("catalog:reset");
          return {
            snapshotId: null,
            itemCount: 0,
            activatedAt: null,
          };
        },
      },
    }),
  );

  assert.deepEqual(
    await subject.executeDangerousAction(
      {
        kind: "change-api-address",
        apiBaseUrl: "http://localhost:5159",
      },
      new AbortController().signal,
    ),
    { status: "completed", kind: "change-api-address" },
  );
  assert.deepEqual(events, [
    "probe:http://localhost:5159/api/v1/health",
    "save:http://localhost:5159",
    "reload",
  ]);

  assert.deepEqual(
    await subject.executeDangerousAction(
      { kind: "reset-catalog" },
      new AbortController().signal,
    ),
    { status: "blocked", reason: "pending-local-data" },
  );
  assert.equal(events.includes("catalog:reset"), false);
});

test("所有会重绑运行时的危险动作都持有目录协调器独占门闩", async () => {
  let exclusiveCalls = 0;
  const subject = new ProductionSettingsControl(
    deps({
      pendingData: { read: async () => CLEAR },
      catalog: {
        getRefreshState: () => ({ kind: "idle" }),
        subscribeRefresh: () => () => undefined,
        runExclusive: async (operation) => {
          exclusiveCalls += 1;
          return operation();
        },
        download: async () => {
          throw new Error("not implemented");
        },
        reset: async () => ({
          snapshotId: "catalog-reset",
          itemCount: 1,
          activatedAt: "2026-07-29T00:00:00.000Z",
        }),
      },
      apiConfiguration: {
        probe: async () => true,
        save: async () => undefined,
      },
      paymentConfiguration: {
        save: async () => undefined,
      },
      device: {
        reregister: async () => undefined,
      },
      appUpdate: {
        check: async () => {
          throw new Error("not implemented");
        },
        restart: async () => true,
      },
    }),
  );
  const signal = new AbortController().signal;

  await subject.executeDangerousAction(
    {
      kind: "change-api-address",
      apiBaseUrl: "https://next.example.test/pos",
    },
    signal,
  );
  await subject.executeDangerousAction(
    {
      kind: "change-payment-settings",
      input: {
        provider: "linkly",
        square: null,
        linkly: { environment: "Sandbox" },
      },
    },
    signal,
  );
  await subject.executeDangerousAction(
    { kind: "reregister-device", targetStoreCode: "S2" },
    signal,
  );
  await subject.executeDangerousAction({ kind: "restart-app" }, signal);
  await subject.executeDangerousAction({ kind: "reset-catalog" }, signal);

  assert.equal(exclusiveCalls, 4);
});

test("支付配置只把公开白名单传给保存端，Abort 后不执行", async () => {
  const saved: unknown[] = [];
  const subject = new ProductionSettingsControl(
    deps({
      pendingData: { read: async () => CLEAR },
      paymentConfiguration: {
        save: async (input: unknown) => {
          saved.push(input);
        },
      },
    }),
  );
  const action = {
    kind: "change-payment-settings",
    input: {
      provider: "square",
      square: {
        environment: "Sandbox",
        deviceId: "SQ-1",
        locationId: "LOC-1",
      },
      linkly: null,
    },
  } satisfies SettingsDangerousConfirmation;

  const aborted = new AbortController();
  aborted.abort();
  await assert.rejects(
    () => subject.executeDangerousAction(action, aborted.signal),
    /abort/i,
  );
  assert.deepEqual(saved, []);

  await subject.executeDangerousAction(
    action,
    new AbortController().signal,
  );
  assert.deepEqual(saved, [action.input]);
});

test("重启安全决策失败时映射 safety-check-failed", async () => {
  const subject = new ProductionSettingsControl(
    deps({
      pendingData: { read: async () => CLEAR },
      appUpdate: {
        check: async () => {
          throw new Error("not implemented");
        },
        restart: async () => false,
      },
    }),
  );

  assert.deepEqual(
    await subject.executeDangerousAction(
      { kind: "restart-app" },
      new AbortController().signal,
    ),
    { status: "blocked", reason: "safety-check-failed" },
  );
});

type DependencyOverrides = Partial<ProductionSettingsControlDependencies>;

function deps(
  overrides: DependencyOverrides = {},
): ProductionSettingsControlDependencies {
  const unavailable = async (): Promise<never> => {
    throw new Error("not implemented");
  };
  return {
    readSnapshot: unavailable,
    catalog: {
      getRefreshState: () => ({ kind: "idle" }),
      subscribeRefresh: () => () => undefined,
      runExclusive: (operation) => operation(),
      download: unavailable,
      reset: unavailable,
    },
    payments: {
      test: unavailable,
    },
    paymentConfiguration: {
      save: unavailable,
    },
    runtimeReload: {
      reload: async () => undefined,
    },
    printer: {
      saveSettings: unavailable,
      scan: unavailable,
      connect: unavailable,
      test: unavailable,
    },
    scanner: {
      test: unavailable,
    },
    display: {
      setEnabled: unavailable,
      test: unavailable,
    },
    appUpdate: {
      check: unavailable,
      restart: unavailable,
    },
    pendingData: {
      read: unavailable,
    },
    apiConfiguration: {
      probe: unavailable,
      save: unavailable,
    },
    device: {
      reregister: unavailable,
    },
    ...overrides,
  };
}
