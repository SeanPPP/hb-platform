import assert from "node:assert/strict";
import test from "node:test";

import { DeviceSessionCoordinator, type DeviceSessionApi } from "./device-session";
import { InMemorySecureStore, InstallationIdentityStore, DeviceCredentialStore } from "./secure-storage";

test("设备验证成功后保存不可同步授权，并生成 iPad 认证头", async () => {
  const secureStore = new InMemorySecureStore();
  const installation = new InstallationIdentityStore(secureStore, () => "INSTALL-001");
  const credentials = new DeviceCredentialStore(secureStore);
  const api: DeviceSessionApi = {
    async register() {
      throw new Error("not used");
    },
    async verify() {
      return {
        deviceCode: "POS_1003_1011",
        storeCode: "1003",
        deviceStatus: 1,
        isAllowed: true,
        authorizationCode: "device-secret"
      };
    },
    async reregister() {
      throw new Error("not used");
    }
  };
  const coordinator = new DeviceSessionCoordinator(api, installation, credentials);

  const state = await coordinator.verify({ deviceCode: "POS_1003_1011", storeCode: "1003" });

  assert.equal(state.status, "authorized");
  assert.deepEqual(await coordinator.getRequestHeaders(), {
    Authorization: "Bearer device-secret",
    "X-HBPOS-Device-Code": "POS_1003_1011",
    "X-HBPOS-Store-Code": "1003",
    "X-HBPOS-Hardware-Id": "INSTALL-001"
  });
  assert.deepEqual(await coordinator.getDeviceIdentity(), {
    deviceCode: "POS_1003_1011",
    storeCode: "1003",
  });
  assert.doesNotMatch(
    JSON.stringify(await coordinator.getDeviceIdentity()),
    /device-secret|INSTALL-001/,
  );
  assert.equal(secureStore.lastWriteOptions?.requireThisDeviceOnly, true);
});

test("在线设备被禁用时锁定，即使本地仍有历史授权码", async () => {
  const secureStore = new InMemorySecureStore();
  const installation = new InstallationIdentityStore(secureStore, () => "INSTALL-001");
  const credentials = new DeviceCredentialStore(secureStore);
  await credentials.save({
    deviceCode: "POS_1003_1011",
    storeCode: "1003",
    hardwareId: "INSTALL-001",
    authorizationCode: "device-secret"
  });
  const api: DeviceSessionApi = {
    async register() {
      throw new Error("not used");
    },
    async verify() {
      return {
        deviceCode: "POS_1003_1011",
        storeCode: "1003",
        deviceStatus: 0,
        isAllowed: false,
        message: "Device is disabled."
      };
    },
    async reregister() {
      throw new Error("not used");
    }
  };
  const coordinator = new DeviceSessionCoordinator(api, installation, credentials);

  const state = await coordinator.verify({ deviceCode: "POS_1003_1011", storeCode: "1003" });

  assert.equal(state.status, "disabled");
  assert.equal(await coordinator.getRequestHeaders(), null);
});

test("已有设备授权在硬件不匹配等明确 verify 拒绝后必须锁机", async () => {
  const secureStore = new InMemorySecureStore();
  const installation = new InstallationIdentityStore(secureStore, () => "INSTALL-001");
  const credentials = new DeviceCredentialStore(secureStore);
  await credentials.save({
    deviceCode: "POS_1003_1011",
    storeCode: "1003",
    hardwareId: "INSTALL-001",
    authorizationCode: "device-secret",
  });
  const api: DeviceSessionApi = {
    async register() {
      throw new Error("not used");
    },
    async verify() {
      // 后端在 iPad hardware mismatch 时可保留原启用状态但明确 IsAllowed=false。
      return { deviceCode: "POS_1003_1011", storeCode: "1003", deviceStatus: 1, isAllowed: false, message: "Device hardware id does not match." };
    },
    async reregister() {
      throw new Error("not used");
    },
  };
  const coordinator = new DeviceSessionCoordinator(api, installation, credentials);

  const state = await coordinator.verify({ deviceCode: "POS_1003_1011", storeCode: "1003" });

  assert.equal(state.status, "disabled");
  assert.equal(await coordinator.getRequestHeaders(), null);
});

test("首次注册 pending 保存可轮询设备标识，不能因尚无授权码回到 unregistered", async () => {
  const secureStore = new InMemorySecureStore();
  const installation = new InstallationIdentityStore(secureStore, () => "INSTALL-001");
  const credentials = new DeviceCredentialStore(secureStore);
  const verifyCalls: { deviceCode: string; storeCode: string }[] = [];
  const api: DeviceSessionApi = {
    async register() {
      return { deviceCode: "POS_1003_1011", storeCode: "1003", deviceStatus: -1, isAllowed: false };
    },
    async verify(input) {
      verifyCalls.push({ deviceCode: input.deviceCode, storeCode: input.storeCode });
      return { deviceCode: input.deviceCode, storeCode: input.storeCode, deviceStatus: -1, isAllowed: false };
    },
    async reregister() {
      throw new Error("not used");
    },
  };
  const coordinator = new DeviceSessionCoordinator(api, installation, credentials);

  assert.equal((await coordinator.register({ storeCode: "1003" })).status, "pending-approval");
  assert.equal((await coordinator.poll()).status, "pending-approval");
  assert.deepEqual(verifyCalls, [{ deviceCode: "POS_1003_1011", storeCode: "1003" }]);
});
