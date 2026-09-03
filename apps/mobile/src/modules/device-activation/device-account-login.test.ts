import assert from "node:assert/strict";
import test from "node:test";

import { loadDeviceAccountBindingForLogin } from "./device-account-login";
import type { StoredMobileDeviceAccountBinding } from "./types";

function binding(credential: string): StoredMobileDeviceAccountBinding {
  return {
    apiHost: "api.example.com",
    hardwareId: "hardware-1",
    credential,
    binding: {
      bindingId: "8b82e1d8-c435-4c1f-98fe-a4e513f4cc39",
      deviceRegistrationId: 11,
      deviceCode: "MOB-001",
      storeCode: "BNE01",
      storeName: "Brisbane",
      deviceSystem: "Android",
      targetUserGuid: "3ff60594-e237-4a80-8642-4dbb0d915b4d",
      targetUsername: "alice",
      targetFullName: "Alice",
      boundAtUtc: "2026-08-31T10:00:00Z",
    },
  };
}

test("设备账号登录先精确恢复 pending，再读取最新 binding", async () => {
  const calls: string[] = [];
  let storedBinding = binding("old-credential");

  const result = await loadDeviceAccountBindingForLogin({
    recoverPendingActivation: async () => {
      calls.push("recover-pending");
      storedBinding = binding("new-credential");
      return storedBinding.binding;
    },
    loadBinding: async () => {
      calls.push("load-binding");
      return storedBinding;
    },
  });

  assert.deepEqual(calls, ["recover-pending", "load-binding"]);
  assert.equal(result.credential, "new-credential");
});

test("恢复仍不确定时禁止用可能分裂的 binding 建立会话", async () => {
  let loaded = false;

  await assert.rejects(
    () =>
      loadDeviceAccountBindingForLogin({
        recoverPendingActivation: async () => {
          throw new Error("DEVICE_ACTIVATION_RECOVERY_REQUIRED");
        },
        loadBinding: async () => {
          loaded = true;
          return binding("stale-credential");
        },
      }),
    /DEVICE_ACTIVATION_RECOVERY_REQUIRED/,
  );
  assert.equal(loaded, false);
});
