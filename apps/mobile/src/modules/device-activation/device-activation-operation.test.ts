import assert from "node:assert/strict";
import test from "node:test";

import {
  DeviceActivationRejectedError,
  DeviceActivationRecoveryRequiredError,
  commitMobileDeviceActivation,
  recoverPendingMobileDeviceActivation,
} from "./device-activation-operation";
import type {
  MobileDeviceActivationBinding,
  PendingMobileDeviceActivation,
} from "./types";

const binding: MobileDeviceActivationBinding = {
  bindingId: "8b82e1d8-c435-4c1f-98fe-a4e513f4cc39",
  deviceRegistrationId: 11,
  deviceCode: "MOB-001",
  storeCode: "BNE01",
  storeName: "Brisbane",
  deviceSystem: "Android",
  targetUserGuid: "user-guid",
  targetUsername: "alice",
  targetFullName: "Alice",
  boundAtUtc: "2026-08-31T10:00:00Z",
};

const pending: PendingMobileDeviceActivation = {
  version: 1,
  mode: "redeem",
  activationCode:
    "HBDEV1-0123456789ABCDEFGHJKMNPQRS-STVWXYZ0123456789ABCDEFGHJ",
  apiHost: "api.example.com",
  hardwareId: "hardware-1",
  deviceSystem: "Android",
  credential: "candidate-secret",
  credentialVerifier: "b".repeat(64),
};

test("最终兑换先保存恢复意图，成功保存新绑定后才清除 pending", async () => {
  const calls: string[] = [];
  const result = await commitMobileDeviceActivation(pending, {
    savePending: async () => calls.push("save-pending"),
    clearPending: async () => calls.push("clear-pending"),
    saveBinding: async () => calls.push("save-binding"),
    saveLegacyDeviceSession: async (session) => {
      calls.push("save-legacy-session");
      assert.deepEqual(session, {
        hardwareId: "hardware-1",
        authCode: "candidate-secret",
        storeCode: "BNE01",
        storeName: "Brisbane",
        systemDeviceNumber: "MOB-001",
        status: 1,
        statusDescription: null,
        resolvedFromExisting: true,
      });
    },
    commit: async (_request, recoveryOnly) => {
      calls.push(`commit-${recoveryOnly}`);
      return { isAllowed: true, reasonCode: "OK", message: "ok", binding };
    },
  });

  assert.deepEqual(result, binding);
  assert.deepEqual(calls, [
    "save-pending",
    "commit-false",
    "save-binding",
    "save-legacy-session",
    "clear-pending",
  ]);
});

test("网络或 5xx 结果不确定时保留 pending 并进入精确恢复", async () => {
  let cleared = false;

  await assert.rejects(
    () =>
      commitMobileDeviceActivation(pending, {
        savePending: async () => undefined,
        clearPending: async () => {
          cleared = true;
        },
        saveBinding: async () => undefined,
        saveLegacyDeviceSession: async () => undefined,
        commit: async () => {
          throw new Error("network-lost");
        },
      }),
    DeviceActivationRecoveryRequiredError,
  );

  assert.equal(cleared, false);
});

test("commit 响应解析失败也视为结果不确定并保留 pending", async () => {
  let cleared = false;

  await assert.rejects(
    () =>
      commitMobileDeviceActivation(pending, {
        savePending: async () => undefined,
        clearPending: async () => {
          cleared = true;
        },
        saveBinding: async () => undefined,
        saveLegacyDeviceSession: async () => undefined,
        commit: async () => {
          throw new Error("MOBILE_DEVICE_ACTIVATION_RESPONSE_INVALID");
        },
      }),
    DeviceActivationRecoveryRequiredError,
  );

  assert.equal(cleared, false);
});

test("commit 声称成功但 binding 失形时保留 pending", async () => {
  let cleared = false;

  await assert.rejects(
    () =>
      commitMobileDeviceActivation(pending, {
        savePending: async () => undefined,
        clearPending: async () => {
          cleared = true;
        },
        saveBinding: async () => undefined,
        saveLegacyDeviceSession: async () => undefined,
        commit: async () => ({
          isAllowed: true,
          reasonCode: "OK",
          message: "committed",
          binding: null,
        }),
      }),
    DeviceActivationRecoveryRequiredError,
  );

  assert.equal(cleared, false);
});

test("服务端明确拒绝 activation 时清理 pending", async () => {
  let cleared = false;

  await assert.rejects(
    () =>
      commitMobileDeviceActivation(pending, {
        savePending: async () => undefined,
        clearPending: async () => {
          cleared = true;
        },
        saveBinding: async () => undefined,
        saveLegacyDeviceSession: async () => undefined,
        commit: async () => ({
          isAllowed: false,
          reasonCode: "notAvailable",
          message: "not available",
        }),
      }),
    DeviceActivationRejectedError,
  );

  assert.equal(cleared, true);
});

test("恢复只使用原 pending 和 recovery-only，成功后落盘并清理", async () => {
  const calls: string[] = [];
  const result = await recoverPendingMobileDeviceActivation(pending, {
    savePending: async () => assert.fail("恢复不得重写 pending"),
    clearPending: async () => calls.push("clear-pending"),
    saveBinding: async () => calls.push("save-binding"),
    saveLegacyDeviceSession: async () => calls.push("save-legacy-session"),
    commit: async (request, recoveryOnly) => {
      assert.deepEqual(request, pending);
      assert.equal(recoveryOnly, true);
      calls.push("recover");
      return { isAllowed: true, reasonCode: "RECOVERED", message: "ok", binding };
    },
  });

  assert.deepEqual(result, binding);
  assert.deepEqual(calls, [
    "recover",
    "save-binding",
    "save-legacy-session",
    "clear-pending",
  ]);
});
