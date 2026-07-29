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
    "register",
    "reregister",
  ]);
  assert.equal("getRequestHeaders" in service, false);
  assert.equal("getTransportCredentials" in service, false);
  assert.equal("lockFromAuthorizationFailure" in service, false);
  assert.doesNotMatch(JSON.stringify(service), /authorization|hardware|bearer/i);
});
