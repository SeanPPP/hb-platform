import assert from "node:assert/strict";
import test from "node:test";

import { prepareMobileDeviceActivationCommitRequest } from "./device-activation-request";
import type { PendingMobileDeviceActivation } from "./types";

const rebindPending: PendingMobileDeviceActivation = {
  version: 1,
  mode: "rebind",
  activationCode:
    "HBDEV1-0123456789ABCDEFGHJKMNPQRS-STVWXYZ0123456789ABCDEFGHJ",
  apiHost: "api.example.com",
  hardwareId: "old-hardware",
  deviceSystem: "Android",
  credential: "new-credential",
  credentialVerifier: "b".repeat(64),
  deviceName: "Store handheld",
  currentHardwareId: "old-hardware",
  currentCredential: "old-credential",
};

test("正常 rebind 始终发送旧绑定双重凭据，并优先使用 exchange JWT", async () => {
  const request = await prepareMobileDeviceActivationCommitRequest(
    rebindPending,
    false,
    async (identity) => {
      assert.deepEqual(identity, {
        hardwareId: "old-hardware",
        credential: "old-credential",
        apiHost: "api.example.com",
      });
      return { accessToken: "bound-jwt" };
    },
  );

  assert.deepEqual(request, {
    body: {
      activationCode: rebindPending.activationCode,
      credentialVerifier: "b".repeat(64),
      deviceName: "Store handheld",
      currentHardwareId: "old-hardware",
      currentCredential: "old-credential",
    },
    accessToken: "bound-jwt",
    skipAuthentication: false,
    recoveryOnly: false,
  });
});

test("旧账号失效导致 exchange 失败时仍匿名提交正常 rebind", async () => {
  const request = await prepareMobileDeviceActivationCommitRequest(
    rebindPending,
    false,
    async () => {
      throw new Error("disabled-account");
    },
  );

  assert.equal(request.accessToken, null);
  assert.equal(request.skipAuthentication, true);
  assert.equal(request.recoveryOnly, false);
  assert.deepEqual(request.body, {
    activationCode: rebindPending.activationCode,
    credentialVerifier: "b".repeat(64),
    deviceName: "Store handheld",
    currentHardwareId: "old-hardware",
    currentCredential: "old-credential",
  });
});

test("recovery-only 不重新 exchange，但保留同一旧绑定证明", async () => {
  const request = await prepareMobileDeviceActivationCommitRequest(
    rebindPending,
    true,
    async () => assert.fail("恢复请求不得重新 exchange"),
  );

  assert.equal(request.accessToken, null);
  assert.equal(request.skipAuthentication, true);
  assert.equal(request.recoveryOnly, true);
  assert.equal(request.body.currentHardwareId, "old-hardware");
  assert.equal(request.body.currentCredential, "old-credential");
});
