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
