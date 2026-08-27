import assert from "node:assert/strict";
import test from "node:test";

import { createPublicDeviceSession } from "./public-device-session";

import type {
  DeviceSessionCoordinator,
  DeviceSessionState,
} from "@/core/security/device-session";

test("公开设备会话只保留注册与脱敏身份，不暴露授权头、硬件 ID 或锁机入口", async () => {
  const calls: string[] = [];
  const authorized: DeviceSessionState = {
    status: "authorized",
    deviceCode: "IPAD-1",
    storeCode: "S001",
  };
  const coordinator = {
    async register() {
      calls.push("register");
      return authorized;
    },
    async registerAppReview() {
      calls.push("app-review");
      return authorized;
    },
    async previewActivationCode() {
      calls.push("preview");
      return { isAllowed: true, storeCode: "S001", storeName: "Chermside" };
    },
    async redeemActivationCode() {
      calls.push("redeem");
      return authorized;
    },
    async rebindActivationCode() {
      calls.push("rebind");
      return authorized;
    },
    async restorePendingActivationCode() {
      calls.push("restore-activation");
      return "HBDEV1-RECOVERY";
    },
    async poll() {
      calls.push("poll");
      return authorized;
    },
    async reregister() {
      calls.push("reregister");
      return authorized;
    },
    async getDeviceIdentity() {
      calls.push("identity");
      return { deviceCode: "IPAD-1", storeCode: "S001" };
    },
    async getDevicePresentation() {
      calls.push("presentation");
      return {
        deviceCode: "IPAD-1",
        storeCode: "S001",
        storeName: "Chermside",
      };
    },
    async getRequestHeaders() {
      return { Authorization: "Bearer device-secret" };
    },
    async getTransportCredentials() {
      return {
        authorizationCode: "device-secret",
        deviceCode: "IPAD-1",
        storeCode: "S001",
        hardwareId: "hardware-secret",
      };
    },
    async lockFromAuthorizationFailure() {},
  } as unknown as DeviceSessionCoordinator;

  const service = createPublicDeviceSession(
    coordinator,
    async () => [{ storeCode: "S001", storeName: "Chermside" }],
  );
  assert.deepEqual(await service.listRegistrationStores(), [
    { storeCode: "S001", storeName: "Chermside" },
  ]);
  await service.register({ storeCode: "S001" });
  await service.registerAppReview({
    storeCode: "S001",
    provisioningCode: "APP-REVIEW-CODE",
  });
  await service.previewActivationCode("HBDEV1-CODE");
  await service.redeemActivationCode({ activationCode: "HBDEV1-CODE" });
  await service.rebindActivationCode({ activationCode: "HBDEV1-CODE" });
  await service.restorePendingActivationCode();
  await service.poll();
  await service.reregister({ targetStoreCode: "S002" });
  assert.deepEqual(await service.getDeviceIdentity(), {
    deviceCode: "IPAD-1",
    storeCode: "S001",
  });
  assert.deepEqual(await service.getDevicePresentation(), {
    deviceCode: "IPAD-1",
    storeCode: "S001",
    storeName: "Chermside",
  });

  assert.deepEqual(calls, [
    "register",
    "app-review",
    "preview",
    "redeem",
    "rebind",
    "restore-activation",
    "poll",
    "reregister",
    "identity",
    "presentation",
  ]);
  assert.deepEqual(Object.keys(service).sort(), [
    "getDeviceIdentity",
    "getDevicePresentation",
    "listRegistrationStores",
    "poll",
    "previewActivationCode",
    "rebindActivationCode",
    "redeemActivationCode",
    "register",
    "registerAppReview",
    "reregister",
    "restorePendingActivationCode",
  ]);
  assert.equal("getRequestHeaders" in service, false);
  assert.equal("getTransportCredentials" in service, false);
  assert.equal("lockFromAuthorizationFailure" in service, false);
  assert.doesNotMatch(JSON.stringify(service), /authorization|hardware|bearer/i);
});
