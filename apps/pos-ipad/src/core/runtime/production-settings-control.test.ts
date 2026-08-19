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
  hasFulfilmentInFlight: false,
  hasSyncOrAuditInFlight: false,
  paymentConfigurationSensitiveOrderCount: 0,
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
        resetRegistration: async () => "completed",
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

test("普通危险动作持有目录独占门，支付配置单独进入全局 transition", async () => {
  let exclusiveCalls = 0;
  let paymentTransitionCalls = 0;
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
      paymentConfigurationTransition: {
        run: async (operation) => {
          paymentTransitionCalls += 1;
          return operation();
        },
      },
      device: {
        reregister: async () => undefined,
        resetRegistration: async () => "completed",
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

  assert.equal(exclusiveCalls, 2);
  assert.equal(paymentTransitionCalls, 1);
});

test("App 重启不预拿普通目录门，避免 transition 等待自身 operation 自锁", async () => {
  let restartCalls = 0;
  const subject = new ProductionSettingsControl(
    deps({
      pendingData: { read: async () => CLEAR },
      catalog: {
        getRefreshState: () => ({ kind: "idle" }),
        subscribeRefresh: () => () => undefined,
        runExclusive: async () => {
          throw new Error("restart must not acquire ordinary catalog lease");
        },
        download: async () => {
          throw new Error("not implemented");
        },
        reset: async () => {
          throw new Error("not implemented");
        },
      },
      appUpdate: {
        check: async () => {
          throw new Error("not implemented");
        },
        restart: async () => {
          restartCalls += 1;
          return true;
        },
      },
    }),
  );

  assert.deepEqual(
    await subject.executeDangerousAction(
      { kind: "restart-app" },
      new AbortController().signal,
    ),
    { status: "completed", kind: "restart-app" },
  );
  assert.equal(restartCalls, 1);
});

test("支付配置由 transition 直接进入 guarded 路径，不嵌套普通目录门", async () => {
  const events: string[] = [];
  const dependencies = deps({
    pendingData: {
      read: async () => {
        events.push("pending");
        return CLEAR;
      },
    },
    catalog: {
      getRefreshState: () => ({ kind: "idle" }),
      subscribeRefresh: () => () => undefined,
      runExclusive: async () => {
        throw new Error("payment transition must not nest catalog lease");
      },
      download: async () => {
        throw new Error("not implemented");
      },
      reset: async () => {
        throw new Error("not implemented");
      },
    },
    paymentConfiguration: {
      save: async () => {
        events.push("save");
      },
    },
    runtimeReload: {
      reload: async () => {
        events.push("reload");
      },
    },
  }) as ProductionSettingsControlDependencies & {
    paymentConfigurationTransition: Readonly<{
      run<T>(operation: () => Promise<T>): Promise<T>;
    }>;
  };
  dependencies.paymentConfigurationTransition = {
    run: async (operation) => {
      events.push("transition:start");
      const result = await operation();
      events.push("transition:end");
      return result;
    },
  };
  const subject = new ProductionSettingsControl(dependencies);

  assert.deepEqual(
    await subject.executeDangerousAction(
      paymentSettingsAction(),
      new AbortController().signal,
    ),
    { status: "completed", kind: "change-payment-settings" },
  );
  assert.deepEqual(events, [
    "transition:start",
    "pending",
    "save",
    "reload",
    "transition:end",
  ]);
});

test("transition 等待后 cashier 已替换时，支付配置在保存前 fail closed", async () => {
  const events: string[] = [];
  let sessionActive = true;
  const dependencies = deps({
    pendingData: {
      read: async () => {
        events.push("pending");
        sessionActive = false;
        return CLEAR;
      },
    },
    paymentConfiguration: {
      save: async () => {
        events.push("save");
      },
    },
    runtimeReload: {
      reload: async () => {
        events.push("reload");
      },
    },
  }) as ProductionSettingsControlDependencies & {
    paymentConfigurationTransition: Readonly<{
      run<T>(operation: () => Promise<T>): Promise<T>;
    }>;
  };
  dependencies.paymentConfigurationTransition = {
    run: (operation) => operation(),
  };
  const subject = new ProductionSettingsControl(dependencies);
  const executeWithLeaseCheck = subject.executeDangerousAction.bind(
    subject,
  ) as (
    action: SettingsDangerousConfirmation,
    signal: AbortSignal,
    assertActive: () => void,
  ) => Promise<unknown>;

  await assert.rejects(
    () =>
      executeWithLeaseCheck(
        paymentSettingsAction(),
        new AbortController().signal,
        () => {
          if (!sessionActive) throw new Error("SESSION_REPLACED");
        },
      ),
    /SESSION_REPLACED/u,
  );
  assert.deepEqual(events, ["pending"]);
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

test("支付配置变更允许与通道无关的销售、退货或耐久写入待处理", async (t) => {
  const cases = [
    {
      name: "待同步销售",
      pending: { ...CLEAR, pendingSaleCount: 1 },
    },
    {
      name: "待同步退货",
      pending: { ...CLEAR, pendingReturnCount: 1 },
    },
    {
      name: "待完成耐久写入",
      pending: { ...CLEAR, pendingDurableWriteCount: 1 },
    },
  ] as const;

  for (const { name, pending } of cases) {
    await t.test(name, async () => {
      const events: string[] = [];
      const subject = new ProductionSettingsControl(
        deps({
          pendingData: { read: async () => pending },
          paymentConfiguration: {
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
          },
          new AbortController().signal,
        ),
        { status: "completed", kind: "change-payment-settings" },
      );
      assert.deepEqual(events, ["save", "reload"]);
    });
  }
});

test("支付配置变更仍被内存交易、通道敏感订单或在途外部动作阻断", async (t) => {
  const cases = [
    {
      name: "活动购物车",
      pending: { ...CLEAR, hasActiveCart: true },
    },
    {
      name: "未决支付",
      pending: { ...CLEAR, unresolvedPaymentCount: 1 },
    },
    {
      name: "依赖当前支付环境的待同步订单",
      pending: {
        ...CLEAR,
        paymentConfigurationSensitiveOrderCount: 1,
      },
    },
    {
      name: "同步或审计正在执行",
      pending: { ...CLEAR, hasSyncOrAuditInFlight: true },
    },
    {
      name: "履约硬件正在执行",
      pending: { ...CLEAR, hasFulfilmentInFlight: true },
    },
  ] as const;

  for (const { name, pending } of cases) {
    await t.test(name, async () => {
      const events: string[] = [];
      const subject = new ProductionSettingsControl(
        deps({
          pendingData: { read: async () => pending },
          paymentConfiguration: {
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
          },
          new AbortController().signal,
        ),
        { status: "blocked", reason: "pending-local-data" },
      );
      assert.deepEqual(events, []);
    });
  }
});

test("支付配置之外的危险动作仍由完整待处理数据门禁阻断", async () => {
  const events: string[] = [];
  const subject = new ProductionSettingsControl(
    deps({
      pendingData: {
        read: async () => ({
          ...CLEAR,
          pendingSaleCount: 1,
        }),
      },
      apiConfiguration: {
        probe: async () => {
          events.push("api:probe");
          return true;
        },
        save: async () => {
          events.push("api:save");
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
      device: {
        reregister: async () => {
          events.push("device:reregister");
        },
        resetRegistration: async () => "completed",
      },
      appUpdate: {
        check: async () => {
          throw new Error("not implemented");
        },
        restart: async () => {
          events.push("app:restart");
          return true;
        },
      },
    }),
  );
  const signal = new AbortController().signal;
  const actions = [
    {
      kind: "change-api-address",
      apiBaseUrl: "https://next.example.test/pos",
    },
    { kind: "reset-catalog" },
    { kind: "reregister-device", targetStoreCode: "S2" },
    { kind: "restart-app" },
  ] satisfies readonly SettingsDangerousConfirmation[];

  for (const action of actions) {
    assert.deepEqual(
      await subject.executeDangerousAction(action, signal),
      { status: "blocked", reason: "pending-local-data" },
    );
  }
  assert.deepEqual(events, []);
});

test("Linkly 配对复用 payment configuration transition、pending-data gate，且不重试 unknown", async () => {
  const events: string[] = [];
  let pairResult: "completed" | "unknown" = "completed";
  const subject = new ProductionSettingsControl(
    deps({
      linklySetup: {
        pair: async (
          environment: "Sandbox" | "Production",
          pairCode: string,
        ) => {
          events.push(`pair:${environment}:${pairCode}`);
          return { status: pairResult };
        },
      },
      paymentConfigurationTransition: {
        run: async (operation) => {
          events.push("transition:start");
          const result = await operation();
          events.push("transition:end");
          return result;
        },
      },
      pendingData: {
        read: async () => {
          events.push("pending");
          return CLEAR;
        },
      },
    }),
  );
  const action = {
    kind: "pair-linkly" as const,
    environment: "Sandbox" as const,
    pairCode: "123456",
  };

  assert.deepEqual(
    await subject.executeDangerousAction(
      action,
      new AbortController().signal,
    ),
    { status: "completed", kind: "pair-linkly" },
  );
  assert.deepEqual(events, [
    "transition:start",
    "pending",
    "pair:Sandbox:123456",
    "transition:end",
  ]);

  events.length = 0;
  pairResult = "unknown";
  assert.deepEqual(
    await subject.executeDangerousAction(
      { ...action, pairCode: "654321" },
      new AbortController().signal,
    ),
    { status: "unknown", kind: "pair-linkly" },
  );
  assert.deepEqual(events, [
    "transition:start",
    "pending",
    "pair:Sandbox:654321",
    "transition:end",
  ]);

  events.length = 0;
  const blocked = new ProductionSettingsControl(
    deps({
      linklySetup: {
        pair: async () => {
          events.push("unexpected-pair");
          return { status: "completed" as const };
        },
      },
      pendingData: {
        read: async () => ({
          ...CLEAR,
          paymentConfigurationSensitiveOrderCount: 1,
        }),
      },
    }),
  );
  assert.deepEqual(
    await blocked.executeDangerousAction(
      action,
      new AbortController().signal,
    ),
    { status: "blocked", reason: "pending-local-data" },
  );
  assert.deepEqual(events, []);
});

test("Linkly 配对和支付切换允许普通耐久队列但阻断敏感订单", async () => {
  let pending: SettingsPendingDataSnapshot = {
    ...CLEAR,
    pendingDurableWriteCount: 1,
    pendingReturnCount: 1,
    pendingSaleCount: 1,
  };
  let pairCalls = 0;
  let saveCalls = 0;
  const subject = new ProductionSettingsControl(
    deps({
      linklySetup: {
        pair: async () => {
          pairCalls += 1;
          return { status: "completed" as const };
        },
      },
      paymentConfiguration: {
        save: async () => {
          saveCalls += 1;
        },
      },
      pendingData: { read: async () => pending },
    }),
  );
  const signal = new AbortController().signal;

  assert.deepEqual(
    await subject.executeDangerousAction(
      {
        kind: "pair-linkly",
        environment: "Production",
        pairCode: "123456",
      },
      signal,
    ),
    { status: "completed", kind: "pair-linkly" },
  );
  assert.deepEqual(
    await subject.executeDangerousAction(paymentSettingsAction(), signal),
    { status: "completed", kind: "change-payment-settings" },
  );
  assert.equal(pairCalls, 1);
  assert.equal(saveCalls, 1);

  pending = { ...CLEAR, paymentConfigurationSensitiveOrderCount: 1 };
  assert.deepEqual(
    await subject.executeDangerousAction(
      {
        kind: "pair-linkly",
        environment: "Production",
        pairCode: "654321",
      },
      signal,
    ),
    { status: "blocked", reason: "pending-local-data" },
  );
  assert.deepEqual(
    await subject.executeDangerousAction(paymentSettingsAction(), signal),
    { status: "blocked", reason: "pending-local-data" },
  );
  assert.equal(pairCalls, 1);
  assert.equal(saveCalls, 1);
});

test("Linkly 配对 POST 返回后 signal 取消仍保留不可逆 completed 终态", async () => {
  const controller = new AbortController();
  let releasePair!: () => void;
  const pairStarted = new Promise<void>((resolve) => {
    releasePair = resolve;
  });
  const subject = new ProductionSettingsControl(
    deps({
      linklySetup: {
        pair: async () => {
          await pairStarted;
          controller.abort();
          return { status: "completed" as const };
        },
      },
      pendingData: { read: async () => CLEAR },
    }),
  );

  const resultPromise = subject.executeDangerousAction(
    {
      kind: "pair-linkly",
      environment: "Production",
      pairCode: "123456",
    },
    controller.signal,
  );
  releasePair();

  assert.deepEqual(await resultPromise, {
    status: "completed",
    kind: "pair-linkly",
  });
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

test("清除设备注册必须通过全量待处理门禁且不会调用重置服务", async () => {
  let resetCalls = 0;
  const subject = new ProductionSettingsControl(
    deps({
      pendingData: {
        read: async () => ({ ...CLEAR, pendingSaleCount: 1 }),
      },
      device: {
        reregister: async () => undefined,
        resetRegistration: async () => {
          resetCalls += 1;
          return "completed" as const;
        },
      },
    }),
  );

  assert.deepEqual(
    await subject.executeDangerousAction(
      { kind: "reset-device-registration" },
      new AbortController().signal,
      "EMPLOYEE-BARCODE",
    ),
    { status: "blocked", reason: "pending-local-data" },
  );
  assert.equal(resetCalls, 0);
});

test("清除设备注册仅把瞬时员工条码交给重置协调器，成功后重载运行时", async () => {
  const events: string[] = [];
  const subject = new ProductionSettingsControl(
    deps({
      pendingData: { read: async () => CLEAR },
      device: {
        reregister: async () => undefined,
        resetRegistration: async (barcode) => {
          events.push(`reset:${barcode}`);
          return "completed" as const;
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
      { kind: "reset-device-registration" },
      new AbortController().signal,
      "EMPLOYEE-BARCODE",
    ),
    { status: "completed", kind: "reset-device-registration" },
  );
  assert.deepEqual(events, ["reset:EMPLOYEE-BARCODE", "reload"]);
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
    paymentConfigurationTransition: {
      run: (operation) => operation(),
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
    receiptProfile: {
      load: unavailable,
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
      resetRegistration: unavailable,
    },
    ...overrides,
  };
}

function paymentSettingsAction(): SettingsDangerousConfirmation {
  return {
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
  };
}
