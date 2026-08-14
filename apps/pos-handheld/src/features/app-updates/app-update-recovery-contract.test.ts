import assert from "node:assert/strict";
import test from "node:test";

import {
  combineAppUpdateRecoverySnapshot,
  createAppUpdateRecoveryRuntimeSnapshot,
  serializeAppUpdateRecoverySnapshot,
} from "./app-update-recovery-contract";

test("更新恢复快照只保留方案冻结的诊断白名单字段", () => {
  const unsafeRuntimeInput = {
    appVersion: "1.2.3",
    buildNumber: "101",
    runtimeVersion: "1.2.3",
    channel: "pos-handheld-production",
    apiOrigin: "https://pos.example",
    deviceCode: "POS-SECRET-9876",
    storeCode: "001",
    storeName: "Brisbane",
  };
  const runtime =
    createAppUpdateRecoveryRuntimeSnapshot(unsafeRuntimeInput);
  const snapshot = combineAppUpdateRecoverySnapshot(runtime, {
    backendState: "reachable",
    deviceState: "authorized-online",
  });
  const parsed = JSON.parse(
    serializeAppUpdateRecoverySnapshot(snapshot),
  ) as Record<string, unknown>;

  assert.deepEqual(Object.keys(parsed), [
    "appVersion",
    "buildNumber",
    "runtimeVersion",
    "channel",
    "apiOrigin",
    "backendState",
    "deviceState",
  ]);
  const serialized = JSON.stringify(parsed);
  for (const forbidden of [
    "POS-SECRET",
    "authorization",
    "hardwareId",
    "order",
    "payment",
    "audit",
    "token",
  ]) {
    assert.equal(serialized.includes(forbidden), false);
  }
});

test("缺失运行时字段使用稳定的非敏感展示值", () => {
  assert.deepEqual(
    createAppUpdateRecoveryRuntimeSnapshot({
      appVersion: "",
      buildNumber: null,
      runtimeVersion: undefined,
      channel: "",
      apiOrigin: "",
    }),
    {
      appVersion: "unknown",
      buildNumber: "unknown",
      runtimeVersion: "unknown",
      channel: "unknown",
      apiOrigin: "unknown",
    },
  );
});
