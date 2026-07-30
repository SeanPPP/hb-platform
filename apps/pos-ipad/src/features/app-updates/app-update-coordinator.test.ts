import assert from "node:assert/strict";
import test from "node:test";

import {
  AppUpdateCoordinator,
  decideAppUpdateRestart,
} from "./app-update-coordinator";

import type {
  PosIpadUpdatePolicy,
  PosIpadUpdatePolicyStorePort,
} from "@/core/contracts/app-updates";

const metadata = Object.freeze({
  version: "1.2.3",
  build: "42",
  runtimeVersion: "1.2.3",
});

const enabledPolicy: PosIpadUpdatePolicy = Object.freeze({
  enabled: true,
  minimumSupportedVersion: "1.2.0",
  latestVersion: "1.3.0",
  forceUpdate: false,
  appStoreUrl: null,
  releaseMessage: null,
});

class MemoryPolicyStore implements PosIpadUpdatePolicyStorePort {
  public saves = 0;

  public constructor(private value: PosIpadUpdatePolicy | null = null) {}

  public async get(): Promise<PosIpadUpdatePolicy | null> {
    return this.value;
  }

  public async save(policy: PosIpadUpdatePolicy): Promise<PosIpadUpdatePolicy> {
    this.saves += 1;
    this.value = policy;
    return policy;
  }
}

test("启动、前台和联网刷新共享 single-flight，并向订阅者发布可开始交易的门禁", async () => {
  let calls = 0;
  let release!: () => void;
  const pending = new Promise<void>((resolve) => {
    release = resolve;
  });
  const coordinator = new AppUpdateCoordinator({
    metadata,
    policyStore: new MemoryPolicyStore(),
    remote: {
      async getPolicy(actualMetadata) {
        calls += 1;
        assert.equal(actualMetadata, metadata);
        await pending;
        return enabledPolicy;
      },
    },
  });
  const observed: string[] = [];
  const unsubscribe = coordinator.subscribe((gate) => observed.push(gate.state));

  const startup = coordinator.refreshOnStartup();
  const foreground = coordinator.refreshOnForeground();
  const network = coordinator.refreshOnNetworkAvailable();
  assert.equal(startup, foreground);
  assert.equal(startup, network);
  assert.equal(calls, 1);
  assert.deepEqual(coordinator.getGate(), {
    state: "unchecked",
    canStartNewTransaction: false,
    canContinueRecovery: true,
  });
  release();
  await startup;

  assert.deepEqual(observed, ["unchecked", "enabled"]);
  assert.deepEqual(coordinator.getGate(), {
    state: "enabled",
    canStartNewTransaction: true,
    canContinueRecovery: true,
  });
  unsubscribe();
});

test("首次策略检查完成前阻止新交易，旧 enabled false 在验证后不再阻止", async () => {
  const coordinator = new AppUpdateCoordinator({
    metadata,
    policyStore: new MemoryPolicyStore(),
    remote: {
      async getPolicy() {
        return { ...enabledPolicy, enabled: false };
      },
    },
  });

  assert.deepEqual(coordinator.getGate(), {
    state: "unchecked",
    canStartNewTransaction: false,
    canContinueRecovery: true,
  });
  await coordinator.refreshOnStartup();
  assert.deepEqual(coordinator.getGate(), {
    state: "enabled",
    canStartNewTransaction: true,
    canContinueRecovery: true,
  });
});

test("网络刷新失败回退合法缓存；无有效缓存时保持未检查门禁", async () => {
  const cachedForcePolicy: PosIpadUpdatePolicy = Object.freeze({
    ...enabledPolicy,
    forceUpdate: true,
  });
  const fromCache = new AppUpdateCoordinator({
    metadata,
    policyStore: new MemoryPolicyStore(cachedForcePolicy),
    remote: { async getPolicy() { throw new Error("offline"); } },
  });
  await fromCache.refreshOnNetworkAvailable();
  assert.deepEqual(fromCache.getGate(), {
    state: "force-update",
    canStartNewTransaction: false,
    canContinueRecovery: true,
  });

  const withoutCache = new AppUpdateCoordinator({
    metadata,
    policyStore: new MemoryPolicyStore(),
    remote: { async getPolicy() { throw new Error("offline"); } },
  });
  await withoutCache.refreshOnStartup();
  assert.deepEqual(withoutCache.getGate(), {
    state: "unchecked",
    canStartNewTransaction: false,
    canContinueRecovery: true,
  });

  const malformedCache = new AppUpdateCoordinator({
    metadata,
    policyStore: {
      async get() {
        return {
          enabled: true,
          forceUpdate: false,
          accessToken: "forbidden",
        } as unknown as PosIpadUpdatePolicy;
      },
      async save(value) {
        return value;
      },
    },
    remote: { async getPolicy() { throw new Error("offline"); } },
  });
  await malformedCache.refreshOnStartup();
  assert.equal(malformedCache.getGate().state, "unchecked");
});

test("已验证的内存更新策略优先于旧缓存，只有强制升级继续限制新交易", async () => {
  for (const [remotePolicy, expectedState, canStartNewTransaction] of [
    [{ ...enabledPolicy, enabled: false }, "enabled", true],
    [{ ...enabledPolicy, forceUpdate: true }, "force-update", false],
  ] as const) {
    let remoteCalls = 0;
    let cacheReads = 0;
    let cacheWrites = 0;
    const coordinator = new AppUpdateCoordinator({
      metadata,
      policyStore: {
        async get() {
          cacheReads += 1;
          return enabledPolicy;
        },
        async save() {
          cacheWrites += 1;
          throw new Error("disk full");
        },
      },
      remote: {
        async getPolicy() {
          remoteCalls += 1;
          if (remoteCalls === 1) return remotePolicy;
          throw new Error("offline");
        },
      },
    });

    assert.equal((await coordinator.refreshOnStartup()).source, "remote");
    assert.equal(coordinator.getGate().state, expectedState);
    assert.equal((await coordinator.refreshOnForeground()).source, "memory");
    assert.equal(coordinator.getGate().state, expectedState);
    assert.equal(
      coordinator.getGate().canStartNewTransaction,
      canStartNewTransaction,
    );
    assert.equal(coordinator.getGate().canContinueRecovery, true);
    assert.equal(cacheReads, 0);
    assert.equal(cacheWrites, 1);
  }
});

test("旧 enabled false 不再阻止交易，force-update 仍阻止且恢复永远开放", async () => {
  for (const [policy, canStartNewTransaction] of [
    [{ ...enabledPolicy, enabled: false }, true],
    [{ ...enabledPolicy, forceUpdate: true }, false],
  ] as const) {
    const coordinator = new AppUpdateCoordinator({
      metadata,
      policyStore: new MemoryPolicyStore(),
      remote: { async getPolicy() { return policy; } },
    });
    await coordinator.refreshOnStartup();
    assert.equal(
      coordinator.getGate().canStartNewTransaction,
      canStartNewTransaction,
    );
    assert.equal(coordinator.getGate().canContinueRecovery, true);
  }
});

test("重启决策由注入的风险快照控制，活动购物车、未决支付或耐久写入都不得重启", async () => {
  assert.deepEqual(
    decideAppUpdateRestart({
      hasActiveCart: true,
      hasUnresolvedPayment: false,
      hasPendingDurableWrite: false,
      hasRecoveryRequired: false,
      hasCatalogRefreshInFlight: false,
      hasSyncOrAuditInFlight: false,
      hasFulfilmentInFlight: false,
    }),
    { canRestart: false, reason: "active-cart" },
  );
  assert.deepEqual(
    decideAppUpdateRestart({
      hasActiveCart: false,
      hasUnresolvedPayment: true,
      hasPendingDurableWrite: false,
      hasRecoveryRequired: false,
      hasCatalogRefreshInFlight: false,
      hasSyncOrAuditInFlight: false,
      hasFulfilmentInFlight: false,
    }),
    { canRestart: false, reason: "unresolved-payment" },
  );
  assert.deepEqual(
    decideAppUpdateRestart({
      hasActiveCart: false,
      hasUnresolvedPayment: false,
      hasPendingDurableWrite: true,
      hasRecoveryRequired: false,
      hasCatalogRefreshInFlight: false,
      hasSyncOrAuditInFlight: false,
      hasFulfilmentInFlight: false,
    }),
    { canRestart: false, reason: "pending-durable-write" },
  );

  let restarts = 0;
  const coordinator = new AppUpdateCoordinator({
    metadata,
    policyStore: new MemoryPolicyStore(),
    remote: { async getPolicy() { return enabledPolicy; } },
    restart: {
      getSafetySnapshot() {
        return {
          hasActiveCart: false,
          hasUnresolvedPayment: false,
          hasPendingDurableWrite: false,
          hasRecoveryRequired: false,
          hasCatalogRefreshInFlight: false,
          hasSyncOrAuditInFlight: false,
          hasFulfilmentInFlight: false,
        };
      },
      async restart() {
        restarts += 1;
      },
    },
  });

  assert.deepEqual(await coordinator.restartIfSafe(), {
    canRestart: true,
    reason: null,
  });
  assert.equal(restarts, 1);
});

test("恢复、同步审计或外设动作仍在进行时不得进入完整更新门禁", () => {
  const safe = {
    hasActiveCart: false,
    hasUnresolvedPayment: false,
    hasPendingDurableWrite: false,
    hasRecoveryRequired: false,
    hasCatalogRefreshInFlight: false,
    hasSyncOrAuditInFlight: false,
    hasFulfilmentInFlight: false,
  };
  for (const [field, reason] of [
    ["hasRecoveryRequired", "recovery-required"],
    ["hasCatalogRefreshInFlight", "catalog-refresh-in-flight"],
    ["hasSyncOrAuditInFlight", "sync-audit-in-flight"],
    ["hasFulfilmentInFlight", "fulfilment-in-flight"],
  ] as const) {
    assert.deepEqual(
      decideAppUpdateRestart({
        ...safe,
        [field]: true,
      } as never),
      { canRestart: false, reason },
    );
  }
  assert.deepEqual(
    decideAppUpdateRestart({
      hasActiveCart: false,
      hasUnresolvedPayment: false,
      hasPendingDurableWrite: false,
      hasRecoveryRequired: false,
      hasSyncOrAuditInFlight: false,
      hasFulfilmentInFlight: false,
    } as never),
    { canRestart: false, reason: "invalid-safety-snapshot" },
  );
});

test("并发安全重启共享 single-flight，只执行一次 snapshot 与 restart", async () => {
  let safetyReads = 0;
  let restarts = 0;
  let release!: () => void;
  const pendingRestart = new Promise<void>((resolve) => {
    release = resolve;
  });
  const coordinator = new AppUpdateCoordinator({
    metadata,
    policyStore: new MemoryPolicyStore(),
    remote: { async getPolicy() { return enabledPolicy; } },
    restart: {
      getSafetySnapshot() {
        safetyReads += 1;
        return {
          hasActiveCart: false,
          hasUnresolvedPayment: false,
          hasPendingDurableWrite: false,
          hasRecoveryRequired: false,
          hasCatalogRefreshInFlight: false,
          hasSyncOrAuditInFlight: false,
          hasFulfilmentInFlight: false,
        };
      },
      async restart() {
        restarts += 1;
        await pendingRestart;
      },
    },
  });

  const first = coordinator.restartIfSafe();
  const second = coordinator.restartIfSafe();
  const sharedPromise = first === second;
  release();
  await Promise.all([first, second]);

  assert.equal(sharedPromise, true);
  assert.equal(safetyReads, 1);
  assert.equal(restarts, 1);
});

test("并发 restart 拒绝只执行一次，清理 single-flight 后允许显式重试", async () => {
  let restarts = 0;
  const coordinator = new AppUpdateCoordinator({
    metadata,
    policyStore: new MemoryPolicyStore(),
    remote: { async getPolicy() { return enabledPolicy; } },
    restart: {
      getSafetySnapshot() {
        return {
          hasActiveCart: false,
          hasUnresolvedPayment: false,
          hasPendingDurableWrite: false,
          hasRecoveryRequired: false,
          hasCatalogRefreshInFlight: false,
          hasSyncOrAuditInFlight: false,
          hasFulfilmentInFlight: false,
        };
      },
      async restart() {
        restarts += 1;
        if (restarts === 1) throw new Error("restart rejected");
      },
    },
  });

  const first = coordinator.restartIfSafe();
  const second = coordinator.restartIfSafe();
  const sharedPromise = first === second;
  const settled = await Promise.allSettled([first, second]);

  assert.equal(sharedPromise, true);
  assert.deepEqual(settled.map((item) => item.status), ["rejected", "rejected"]);
  assert.equal(restarts, 1);
  assert.deepEqual(await coordinator.restartIfSafe(), {
    canRestart: true,
    reason: null,
  });
  assert.equal(restarts, 2);
});
