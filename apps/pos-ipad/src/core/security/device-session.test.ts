import assert from "node:assert/strict";
import test from "node:test";

import { DeviceSessionCoordinator, type DeviceSessionApi } from "./device-session";
import {
  DeviceCredentialStore,
  DeviceLockStore,
  DevicePresentationStore,
  InMemorySecureStore,
  InstallationIdentityStore,
} from "./secure-storage";

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

test("旧 verify 的迟到响应不能覆盖已完成换绑的凭据、展示名称或状态", async () => {
  const secureStore = new InMemorySecureStore();
  const installation = new InstallationIdentityStore(
    secureStore,
    () => "INSTALL-001",
  );
  const credentials = new DeviceCredentialStore(secureStore);
  let releaseOldVerify: ((response: Awaited<ReturnType<DeviceSessionApi["verify"]>>) => void) | undefined;
  let oldVerifyStarted: (() => void) | undefined;
  const api: DeviceSessionApi = {
    async register() {
      throw new Error("not used");
    },
    verify() {
      return new Promise((resolve) => {
        releaseOldVerify = resolve;
        oldVerifyStarted?.();
      });
    },
    async reregister() {
      return {
        deviceCode: "NEW",
        storeCode: "S2",
        storeName: "New Store",
        deviceStatus: 1,
        isAllowed: true,
        authorizationCode: "new-secret",
      };
    },
  };
  const coordinator = new DeviceSessionCoordinator(api, installation, credentials);
  const verifyStarted = new Promise<void>((resolve) => {
    oldVerifyStarted = resolve;
  });

  const oldVerify = coordinator.verify({ deviceCode: "OLD", storeCode: "S1" });
  await verifyStarted;
  const reregistered = await coordinator.reregister({ targetStoreCode: "S2" });
  if (!releaseOldVerify) {
    throw new Error("旧 verify 未建立延迟响应控制点。");
  }
  releaseOldVerify({
    deviceCode: "OLD",
    storeCode: "S1",
    storeName: "Old Store",
    deviceStatus: 1,
    isAllowed: true,
    authorizationCode: "old-secret",
  });

  assert.deepEqual(await oldVerify, reregistered);
  assert.deepEqual(coordinator.getState(), reregistered);
  assert.deepEqual(await coordinator.getRequestHeaders(), {
    Authorization: "Bearer new-secret",
    "X-HBPOS-Device-Code": "NEW",
    "X-HBPOS-Store-Code": "S2",
    "X-HBPOS-Hardware-Id": "INSTALL-001",
  });
  assert.deepEqual(await coordinator.getDevicePresentation(), {
    deviceCode: "NEW",
    storeCode: "S2",
    storeName: "New Store",
  });
});

test("旧 verify 的延迟锁定不能在换绑后重新锁住新设备会话", async () => {
  class DelayedLockStore extends DeviceLockStore {
    private releaseLock: (() => void) | undefined;
    private notifyLockStarted: (() => void) | undefined;
    public readonly lockStarted = new Promise<void>((resolve) => {
      this.notifyLockStarted = resolve;
    });

    public override async lock(reason: string): Promise<void> {
      this.notifyLockStarted?.();
      await new Promise<void>((resolve) => {
        this.releaseLock = resolve;
      });
      await super.lock(reason);
    }

    public release(): void {
      if (!this.releaseLock) {
        throw new Error("延迟锁尚未开始。");
      }
      this.releaseLock();
    }
  }

  const secureStore = new InMemorySecureStore();
  const installation = new InstallationIdentityStore(
    secureStore,
    () => "INSTALL-001",
  );
  const credentials = new DeviceCredentialStore(secureStore);
  await credentials.save({
    deviceCode: "OLD",
    storeCode: "S1",
    hardwareId: "INSTALL-001",
    authorizationCode: "old-secret",
  });
  const lockStore = new DelayedLockStore(secureStore);
  const api: DeviceSessionApi = {
    async register() {
      throw new Error("not used");
    },
    async verify() {
      return {
        deviceCode: "OLD",
        storeCode: "S1",
        deviceStatus: 0,
        isAllowed: false,
        message: "Device is disabled.",
      };
    },
    async reregister() {
      return {
        deviceCode: "NEW",
        storeCode: "S2",
        storeName: "New Store",
        deviceStatus: 1,
        isAllowed: true,
        authorizationCode: "new-secret",
      };
    },
  };
  const coordinator = new DeviceSessionCoordinator(
    api,
    installation,
    credentials,
    lockStore,
  );

  const oldVerify = coordinator.verify({ deviceCode: "OLD", storeCode: "S1" });
  await lockStore.lockStarted;
  const reregister = coordinator.reregister({ targetStoreCode: "S2" });
  // 让未序列化实现中的换绑先完成 unlock；序列化实现则在此轮事件循环中继续等待旧锁。
  await new Promise<void>((resolve) => setImmediate(resolve));
  lockStore.release();
  await Promise.all([oldVerify, reregister]);

  assert.deepEqual(await coordinator.getRequestHeaders(), {
    Authorization: "Bearer new-secret",
    "X-HBPOS-Device-Code": "NEW",
    "X-HBPOS-Store-Code": "S2",
    "X-HBPOS-Hardware-Id": "INSTALL-001",
  });
  assert.deepEqual(await coordinator.getDevicePresentation(), {
    deviceCode: "NEW",
    storeCode: "S2",
    storeName: "New Store",
  });
  assert.equal(await lockStore.isLocked(), false);
  assert.deepEqual(coordinator.getState(), {
    status: "authorized",
    deviceCode: "NEW",
    storeCode: "S2",
  });
});

test("注册、验证和换店授权成功后均缓存 trim 后的门店名称", async (context) => {
  for (const operation of ["register", "verify", "reregister"] as const) {
    await context.test(operation, async () => {
      const secureStore = new InMemorySecureStore();
      const installation = new InstallationIdentityStore(
        secureStore,
        () => "INSTALL-001",
      );
      const credentials = new DeviceCredentialStore(secureStore);
      const response = {
        deviceCode: "POS_1003_1011",
        storeCode: "1003",
        storeName: "  Chermside  ",
        deviceStatus: 1,
        isAllowed: true,
        authorizationCode: "device-secret",
      };
      const api: DeviceSessionApi = {
        async register() {
          return response;
        },
        async verify() {
          return response;
        },
        async reregister() {
          return response;
        },
      };
      const coordinator = new DeviceSessionCoordinator(
        api,
        installation,
        credentials,
      );

      if (operation === "register") {
        await coordinator.register({ storeCode: "1003" });
      } else if (operation === "verify") {
        await coordinator.verify({
          deviceCode: "POS_1003_1011",
          storeCode: "1003",
        });
      } else {
        await coordinator.reregister({ targetStoreCode: "1003" });
      }

      assert.deepEqual(await coordinator.getDevicePresentation(), {
        deviceCode: "POS_1003_1011",
        storeCode: "1003",
        storeName: "Chermside",
      });
      assert.deepEqual(await coordinator.getDeviceIdentity(), {
        deviceCode: "POS_1003_1011",
        storeCode: "1003",
      });
    });
  }
});

test("响应缺少名称时同范围保留缓存，换绑范围则清除旧名称", async () => {
  const secureStore = new InMemorySecureStore();
  const installation = new InstallationIdentityStore(
    secureStore,
    () => "INSTALL-001",
  );
  const credentials = new DeviceCredentialStore(secureStore);
  const presentation = new DevicePresentationStore(secureStore);
  await credentials.save({
    deviceCode: "POS-1",
    storeCode: "S1",
    hardwareId: "INSTALL-001",
    authorizationCode: "old-secret",
  });
  await presentation.save({
    deviceCode: "POS-1",
    storeCode: "S1",
    storeName: "Original Store",
  });
  const api: DeviceSessionApi = {
    async register() {
      throw new Error("not used");
    },
    async verify() {
      return {
        deviceCode: "POS-1",
        storeCode: "S1",
        deviceStatus: 1,
        isAllowed: true,
        authorizationCode: "same-scope-secret",
      };
    },
    async reregister() {
      return {
        deviceCode: "POS-2",
        storeCode: "S2",
        storeName: " ",
        deviceStatus: 1,
        isAllowed: true,
        authorizationCode: "new-scope-secret",
      };
    },
  };
  const coordinator = new DeviceSessionCoordinator(
    api,
    installation,
    credentials,
    undefined,
    undefined,
    presentation,
  );

  await coordinator.verify({ deviceCode: "POS-1", storeCode: "S1" });
  assert.equal(
    (await coordinator.getDevicePresentation())?.storeName,
    "Original Store",
  );

  await coordinator.reregister({ targetStoreCode: "S2" });
  assert.deepEqual(await coordinator.getDevicePresentation(), {
    deviceCode: "POS-2",
    storeCode: "S2",
    storeName: null,
  });
  assert.equal(await presentation.load(), null);
});

test("展示缓存写入失败不能把成功设备授权改判为失败", async () => {
  class FailingPresentationStore extends InMemorySecureStore {
    public override async set(): Promise<void> {
      throw new Error("Keychain presentation write failed.");
    }
  }
  const secureStore = new InMemorySecureStore();
  const installation = new InstallationIdentityStore(
    secureStore,
    () => "INSTALL-001",
  );
  const credentials = new DeviceCredentialStore(secureStore);
  const api: DeviceSessionApi = {
    async register() {
      throw new Error("not used");
    },
    async verify() {
      return {
        deviceCode: "POS-1",
        storeCode: "S1",
        storeName: "Store One",
        deviceStatus: 1,
        isAllowed: true,
        authorizationCode: "device-secret",
      };
    },
    async reregister() {
      throw new Error("not used");
    },
  };
  const coordinator = new DeviceSessionCoordinator(
    api,
    installation,
    credentials,
    undefined,
    undefined,
    new DevicePresentationStore(new FailingPresentationStore()),
  );

  assert.equal(
    (await coordinator.verify({
      deviceCode: "POS-1",
      storeCode: "S1",
    })).status,
    "authorized",
  );
  assert.equal(
    (await coordinator.getRequestHeaders())?.Authorization,
    "Bearer device-secret",
  );
});

test("展示名称仅精确匹配有效凭据，锁机或硬件不匹配时不返回展示身份", async () => {
  const secureStore = new InMemorySecureStore();
  const installation = new InstallationIdentityStore(
    secureStore,
    () => "INSTALL-001",
  );
  const credentials = new DeviceCredentialStore(secureStore);
  const presentation = new DevicePresentationStore(secureStore);
  await credentials.save({
    deviceCode: "POS-1",
    storeCode: "S1",
    hardwareId: "INSTALL-001",
    authorizationCode: "device-secret",
  });
  await presentation.save({
    deviceCode: "POS-OTHER",
    storeCode: "S1",
    storeName: "Wrong Device Store",
  });
  const api: DeviceSessionApi = {
    async register() {
      throw new Error("not used");
    },
    async verify() {
      throw new Error("not used");
    },
    async reregister() {
      throw new Error("not used");
    },
  };
  const coordinator = new DeviceSessionCoordinator(
    api,
    installation,
    credentials,
    undefined,
    undefined,
    presentation,
  );

  assert.deepEqual(await coordinator.getDevicePresentation(), {
    deviceCode: "POS-1",
    storeCode: "S1",
    storeName: null,
  });

  await coordinator.lockFromAuthorizationFailure("disabled");
  assert.equal(await coordinator.getDevicePresentation(), null);

  const invalidCredentials = new DeviceCredentialStore(
    new InMemorySecureStore(),
  );
  await invalidCredentials.save({
    deviceCode: "POS-1",
    storeCode: "S1",
    hardwareId: "OTHER-INSTALLATION",
    authorizationCode: "device-secret",
  });
  const invalidCoordinator = new DeviceSessionCoordinator(
    api,
    new InstallationIdentityStore(
      invalidCredentials.secureStore,
      () => "INSTALL-001",
    ),
    invalidCredentials,
  );

  assert.equal(
    await invalidCoordinator.getDevicePresentation(),
    null,
  );

  await invalidCredentials.secureStore.set(
    "hbpos.ipad.device-credentials.v1",
    "not-json",
    { requireThisDeviceOnly: true },
  );
  assert.equal(
    await invalidCoordinator.getDevicePresentation(),
    null,
  );
});
