import assert from "node:assert/strict";
import test from "node:test";
import { HbposApiError } from "../api/hbpos-api";

import {
  DeviceSessionCoordinator,
  subscribeDeviceScopeChange,
  type DeviceScopeChange,
  type DeviceSessionApi,
} from "./device-session";
import {
  DeviceCredentialStore,
  DeviceLockStore,
  DevicePresentationStore,
  InMemorySecureStore,
  InstallationIdentityStore,
  PendingDeviceActivationCodeStore,
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

test("换店凭据提交后才发布 scope 切换，重新注册失败不误发", async () => {
  const secureStore = new InMemorySecureStore();
  const installation = new InstallationIdentityStore(
    secureStore,
    () => "INSTALL-001",
  );
  const credentials = new DeviceCredentialStore(secureStore);
  await credentials.save({
    deviceCode: "OLD-DEVICE",
    storeCode: "S1",
    hardwareId: "INSTALL-001",
    authorizationCode: "old-secret",
  });
  let shouldFail = false;
  const api: DeviceSessionApi = {
    async register() {
      throw new Error("not used");
    },
    async verify() {
      throw new Error("not used");
    },
    async reregister() {
      if (shouldFail) throw new Error("reregister failed");
      return {
        deviceCode: "NEW-DEVICE",
        storeCode: "S2",
        deviceStatus: 1,
        isAllowed: true,
        authorizationCode: "new-secret",
      };
    },
  };
  const coordinator = new DeviceSessionCoordinator(api, installation, credentials);
  const published: Readonly<{
    previousStoreCode: string;
    nextStoreCode: string;
    authorization: string | null;
  }>[] = [];
  const unsubscribe = subscribeDeviceScopeChange((change) => {
    published.push({
      previousStoreCode: change.previous.storeCode,
      nextStoreCode: change.current.storeCode,
      authorization: null,
    });
  });

  try {
    await coordinator.reregister({ targetStoreCode: "S2" });
    published[0] = {
      ...published[0]!,
      authorization: (await coordinator.getRequestHeaders())?.Authorization ?? null,
    };
    shouldFail = true;
    await assert.rejects(() => coordinator.reregister({ targetStoreCode: "S3" }));
  } finally {
    unsubscribe();
  }

  assert.deepEqual(published, [
    {
      previousStoreCode: "S1",
      nextStoreCode: "S2",
      authorization: "Bearer new-secret",
    },
  ]);
});

test("同设备 scope 重新注册轮换授权后发布一次，首次 register 与 verify 不误发", async () => {
  const secureStore = new InMemorySecureStore();
  const installation = new InstallationIdentityStore(
    secureStore,
    () => "INSTALL-001",
  );
  const credentials = new DeviceCredentialStore(secureStore);
  await credentials.save({
    deviceCode: "IPAD-1",
    storeCode: "S001",
    hardwareId: "INSTALL-001",
    authorizationCode: "old-secret",
  });
  const coordinator = new DeviceSessionCoordinator(
    {
      async register() {
        return {
          deviceCode: "IPAD-NEW",
          storeCode: "S002",
          deviceStatus: 1,
          isAllowed: true,
          authorizationCode: "register-secret",
        };
      },
      async verify() {
        return {
          deviceCode: "IPAD-NEW",
          storeCode: "S002",
          deviceStatus: 1,
          isAllowed: true,
          authorizationCode: "verify-secret",
        };
      },
      async reregister() {
        return {
          deviceCode: "IPAD-1",
          storeCode: "S001",
          deviceStatus: 1,
          isAllowed: true,
          authorizationCode: "rotated-secret",
        };
      },
    },
    installation,
    credentials,
  );
  const changes: Readonly<{
    previous: Readonly<{ deviceCode: string; storeCode: string }>;
    current: Readonly<{ deviceCode: string; storeCode: string }>;
  }>[] = [];
  const unsubscribe = subscribeDeviceScopeChange((change) => changes.push(change));

  try {
    await coordinator.reregister({ targetStoreCode: "S001" });
    assert.equal(
      (await coordinator.getRequestHeaders())?.Authorization,
      "Bearer rotated-secret",
      "事件发布前新凭据必须已可读取",
    );
    await coordinator.register({ storeCode: "S002" });
    await coordinator.verify({ deviceCode: "IPAD-NEW", storeCode: "S002" });
  } finally {
    unsubscribe();
  }

  assert.deepEqual(changes, [
    {
      previous: { deviceCode: "IPAD-1", storeCode: "S001" },
      current: { deviceCode: "IPAD-1", storeCode: "S001" },
    },
  ]);
});

test("重新注册凭据保存失败时不发布 scope 变更", async () => {
  const secureStore = new InMemorySecureStore();
  const originalSet = secureStore.set.bind(secureStore);
  let failWrites = false;
  secureStore.set = async (...args) => {
    if (failWrites) throw new Error("credential save failed");
    await originalSet(...args);
  };
  const installation = new InstallationIdentityStore(
    secureStore,
    () => "INSTALL-001",
  );
  await installation.getOrCreate();
  const credentials = new DeviceCredentialStore(secureStore);
  await credentials.save({
    deviceCode: "IPAD-1",
    storeCode: "S001",
    hardwareId: "INSTALL-001",
    authorizationCode: "old-secret",
  });
  const coordinator = new DeviceSessionCoordinator(
    {
      async register() { throw new Error("not used"); },
      async verify() { throw new Error("not used"); },
      async reregister() {
        return {
          deviceCode: "IPAD-1",
          storeCode: "S001",
          deviceStatus: 1,
          isAllowed: true,
          authorizationCode: "rotated-secret",
        };
      },
    },
    installation,
    credentials,
  );
  let changes = 0;
  const unsubscribe = subscribeDeviceScopeChange(() => { changes += 1; });

  try {
    failWrites = true;
    await assert.rejects(
      () => coordinator.reregister({ targetStoreCode: "S001" }),
      /credential save failed/,
    );
  } finally {
    unsubscribe();
  }

  assert.equal(changes, 0);
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

test("连续换绑在前次保存后变 stale 时仍按 durable 顺序发布并保留最终状态", async () => {
  const secureStore = new InMemorySecureStore();
  const installation = new InstallationIdentityStore(
    secureStore,
    () => "INSTALL-001",
  );
  await installation.getOrCreate();
  const credentials = new DeviceCredentialStore(secureStore);
  await credentials.save({
    deviceCode: "IPAD-0",
    storeCode: "S0",
    hardwareId: "INSTALL-001",
    authorizationCode: "secret-0",
  });
  const originalSave = credentials.save.bind(credentials);
  let saveCalls = 0;
  let notifyFirstSaveStarted: (() => void) | undefined;
  let releaseFirstSave: (() => void) | undefined;
  const firstSaveStarted = new Promise<void>((resolve) => {
    notifyFirstSaveStarted = resolve;
  });
  const firstSaveRelease = new Promise<void>((resolve) => {
    releaseFirstSave = resolve;
  });
  credentials.save = async (next) => {
    saveCalls += 1;
    if (saveCalls === 1) {
      notifyFirstSaveStarted?.();
      await firstSaveRelease;
    }
    await originalSave(next);
  };
  const coordinator = new DeviceSessionCoordinator(
    {
      async register() { throw new Error("not used"); },
      async verify() { throw new Error("not used"); },
      async reregister(input) {
        const suffix = input.targetStoreCode.slice(1);
        return {
          deviceCode: `IPAD-${suffix}`,
          storeCode: input.targetStoreCode,
          storeName: `Store ${suffix}`,
          deviceStatus: 1,
          isAllowed: true,
          authorizationCode: `secret-${suffix}`,
        };
      },
    },
    installation,
    credentials,
  );
  const changes: Readonly<{
    previous: string;
    current: string;
  }>[] = [];
  let s0ListenerCalls = 0;
  const unsubscribe = subscribeDeviceScopeChange((change) => {
    changes.push({
      previous: `${change.previous.storeCode}/${change.previous.deviceCode}`,
      current: `${change.current.storeCode}/${change.current.deviceCode}`,
    });
    if (
      change.previous.storeCode === "S0" &&
      change.previous.deviceCode === "IPAD-0"
    ) {
      s0ListenerCalls += 1;
    }
  });

  try {
    const first = coordinator.reregister({ targetStoreCode: "S1" });
    await firstSaveStarted;
    const second = coordinator.reregister({ targetStoreCode: "S2" });
    if (!releaseFirstSave) {
      throw new Error("首个凭据保存未进入可控延迟点。");
    }
    releaseFirstSave();
    await Promise.all([first, second]);
  } finally {
    unsubscribe();
  }

  assert.deepEqual(changes, [
    { previous: "S0/IPAD-0", current: "S1/IPAD-1" },
    { previous: "S1/IPAD-1", current: "S2/IPAD-2" },
  ]);
  assert.equal(s0ListenerCalls, 1, "S0 runtime 必须收到不可逆的首段换绑事件");
  assert.deepEqual(coordinator.getState(), {
    status: "authorized",
    deviceCode: "IPAD-2",
    storeCode: "S2",
  });
  assert.deepEqual(await coordinator.getDevicePresentation(), {
    deviceCode: "IPAD-2",
    storeCode: "S2",
    storeName: "Store 2",
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

test("预览不落盘，兑换仅在网络不确定时保留开通码并在确定结果后清除", async () => {
  const secureStore = new InMemorySecureStore();
  const installation = new InstallationIdentityStore(
    secureStore,
    () => "INSTALL-001",
  );
  const credentials = new DeviceCredentialStore(secureStore);
  const pendingActivation = new PendingDeviceActivationCodeStore(secureStore);
  const activationCode =
    "HBDEV1-0123456789ABCDEFGHJKMNPQRS-STVWXYZ0123456789ABCDEFGHJ";
  let redeemOutcome:
    | "network"
    | "rate-limited"
    | "http"
    | "http-auth"
    | "http-forbidden"
    | "http-not-found"
    | "http-conflict"
    | "transport-other"
    | "envelope"
    | "rejected-without-reason"
    | "rejected-private-reason"
    | "allowed-without-reason"
    | "allowed-private-reason"
    | "incomplete"
    | "empty"
    | "rejected"
    | "allowed" = "network";
  let receivedRedeem: unknown;
  const coordinator = new DeviceSessionCoordinator(
    {
      async register() { throw new Error("not used"); },
      async verify() { throw new Error("not used"); },
      async reregister() { throw new Error("not used"); },
      async previewActivationCode(input) {
        return {
          isAllowed: true,
          storeCode: "1042",
          storeName: "Sunnybank",
          deviceSystem: "iPadOS",
          expiresAtUtc: "2026-08-27T12:00:00.000Z",
          message: null,
          ...input,
        };
      },
      async redeemActivationCode(input) {
        receivedRedeem = input;
        if (redeemOutcome === "network") {
          throw new HbposApiError("network timeout", {
            kind: "transport",
            code: "NO_HTTP_RESPONSE",
          });
        }
        if (redeemOutcome === "http") {
          throw new HbposApiError("invalid request", { kind: "http", status: 400 });
        }
        if (redeemOutcome === "http-auth") {
          throw new HbposApiError("authentication state unknown", {
            kind: "http",
            status: 401,
          });
        }
        if (redeemOutcome === "http-forbidden") {
          throw new HbposApiError("authorization state unknown", {
            kind: "http",
            status: 403,
          });
        }
        if (redeemOutcome === "http-not-found") {
          throw new HbposApiError("route or grant state unknown", {
            kind: "http",
            status: 404,
          });
        }
        if (redeemOutcome === "http-conflict") {
          throw new HbposApiError("concurrent grant state unknown", {
            kind: "http",
            status: 409,
          });
        }
        if (redeemOutcome === "transport-other") {
          throw new HbposApiError("unclassified transport failure", {
            kind: "transport",
          });
        }
        if (redeemOutcome === "rate-limited") {
          throw new HbposApiError("retry later", { kind: "http", status: 429 });
        }
        if (redeemOutcome === "envelope") {
          throw new HbposApiError("response data missing", { kind: "envelope" });
        }
        if (redeemOutcome === "rejected-without-reason") {
          return { isAllowed: false, deviceStatus: 1, message: "rejected" };
        }
        if (redeemOutcome === "rejected-private-reason") {
          return {
            isAllowed: false,
            reasonCode: "USED",
            deviceStatus: 1,
            message: "rejected",
          };
        }
        if (redeemOutcome === "incomplete") {
          return { isAllowed: true, deviceStatus: 1 };
        }
        if (redeemOutcome === "empty") {
          return undefined as never;
        }
        if (
          redeemOutcome === "allowed-without-reason" ||
          redeemOutcome === "allowed-private-reason"
        ) {
          return {
            isAllowed: true,
            ...(redeemOutcome === "allowed-private-reason"
              ? { reasonCode: "USED" }
              : {}),
            deviceCode: "IPAD-1042-01",
            storeCode: "1042",
            storeName: "Sunnybank",
            deviceStatus: 1,
            authorizationCode: "device-secret",
          };
        }
        if (redeemOutcome === "rejected") {
          return {
            isAllowed: false,
            reasonCode: "ACTIVATION_CODE_NOT_AVAILABLE",
            deviceStatus: 1,
            message: "Activation code was already used.",
          };
        }
        return {
          isAllowed: true,
          reasonCode: "ACTIVATED",
          deviceCode: "IPAD-1042-01",
          storeCode: "1042",
          storeName: "Sunnybank",
          deviceStatus: 1,
          authorizationCode: "device-secret",
        };
      },
    },
    installation,
    credentials,
    undefined,
    undefined,
    undefined,
    pendingActivation,
  );

  const preview = await coordinator.previewActivationCode(activationCode);
  assert.equal(preview.storeCode, "1042");
  assert.equal(await coordinator.restorePendingActivationCode(), null);
  await assert.rejects(
    () => coordinator.redeemActivationCode({ activationCode }),
    /network timeout/,
  );
  assert.equal(await pendingActivation.load(), activationCode);

  redeemOutcome = "rate-limited";
  await assert.rejects(
    () => coordinator.redeemActivationCode({ activationCode }),
    /retry later/,
  );
  assert.equal(await pendingActivation.load(), activationCode);

  for (const uncertainOutcome of [
    "envelope",
    "http-auth",
    "http-forbidden",
    "http-not-found",
    "http-conflict",
    "transport-other",
    "rejected-without-reason",
    "rejected-private-reason",
    "allowed-without-reason",
    "allowed-private-reason",
    "incomplete",
    "empty",
  ] as const) {
    redeemOutcome = uncertainOutcome;
    await assert.rejects(() =>
      coordinator.redeemActivationCode({ activationCode }),
    );
    assert.equal(
      await pendingActivation.load(),
      activationCode,
      `${uncertainOutcome} 不能误清状态不确定的开通码`,
    );
  }

  redeemOutcome = "http";
  await assert.rejects(
    () => coordinator.redeemActivationCode({ activationCode }),
    /invalid request/,
  );
  assert.equal(await pendingActivation.load(), null);

  redeemOutcome = "rejected";
  const rejected = await coordinator.redeemActivationCode({ activationCode });
  assert.equal(rejected.status, "denied");
  assert.equal(await pendingActivation.load(), null);

  redeemOutcome = "allowed";
  const state = await coordinator.redeemActivationCode({ activationCode });

  assert.equal(state.status, "authorized");
  assert.deepEqual(receivedRedeem, {
    activationCode,
    hardwareId: "INSTALL-001",
  });
  assert.equal((await credentials.load())?.storeCode, "1042");
  assert.equal(await pendingActivation.load(), null);
});

test("兑换成功但设备凭据落盘失败时保留开通码用于启动恢复", async () => {
  const secureStore = new InMemorySecureStore();
  const originalSet = secureStore.set.bind(secureStore);
  secureStore.set = async (key, value, options) => {
    if (key === "hbpos.ipad.device-credentials.v1") {
      throw new Error("credential save failed");
    }
    await originalSet(key, value, options);
  };
  const installation = new InstallationIdentityStore(
    secureStore,
    () => "INSTALL-IPAD-001",
  );
  const credentials = new DeviceCredentialStore(secureStore);
  const pendingActivation = new PendingDeviceActivationCodeStore(secureStore);
  const activationCode =
    "HBDEV1-0123456789ABCDEFGHJKMNPQRS-STVWXYZ0123456789ABCDEFGHJ";
  const coordinator = new DeviceSessionCoordinator(
    {
      async register() { throw new Error("not used"); },
      async verify() { throw new Error("not used"); },
      async reregister() { throw new Error("not used"); },
      async redeemActivationCode() {
        return {
          isAllowed: true,
          reasonCode: "ACTIVATED",
          deviceCode: "IPAD-1042-01",
          storeCode: "1042",
          storeName: "Sunnybank",
          deviceStatus: 1,
          authorizationCode: "device-secret",
        };
      },
    },
    installation,
    credentials,
    undefined,
    undefined,
    undefined,
    pendingActivation,
  );

  await assert.rejects(
    () => coordinator.redeemActivationCode({ activationCode }),
    /credential save failed/,
  );

  assert.equal(await pendingActivation.load(), activationCode);
  assert.equal(await credentials.load(), null);
});

test("已有待恢复开通意图时不同码或模式不得覆盖原记录", async () => {
  const secureStore = new InMemorySecureStore();
  const installation = new InstallationIdentityStore(secureStore, () => "INSTALL-001");
  const credentials = new DeviceCredentialStore(secureStore);
  const pendingActivation = new PendingDeviceActivationCodeStore(secureStore);
  const activationCode =
    "HBDEV1-0123456789ABCDEFGHJKMNPQRS-STVWXYZ0123456789ABCDEFGHJ";
  const otherActivationCode =
    "HBDEV1-1123456789ABCDEFGHJKMNPQRS-STVWXYZ0123456789ABCDEFGHJ";
  let rebindCalls = 0;
  await credentials.save({
    deviceCode: "IPAD-OLD",
    storeCode: "1003",
    hardwareId: "INSTALL-001",
    authorizationCode: "old-secret",
  });
  await pendingActivation.save(activationCode, "redeem", {
    apiPartition: "https://hotbargain.vip/pos-api",
    hardwareId: "INSTALL-001",
  });
  const coordinator = new DeviceSessionCoordinator(
    {
      async register() { throw new Error("not used"); },
      async verify() { throw new Error("not used"); },
      async reregister() { throw new Error("not used"); },
      async rebindActivationCode() {
        rebindCalls += 1;
        throw new Error("rebind must not start");
      },
    },
    installation,
    credentials,
    undefined,
    undefined,
    undefined,
    pendingActivation,
  );

  await assert.rejects(
    () => coordinator.rebindActivationCode({ activationCode: otherActivationCode }),
    /pending.*conflict/i,
  );
  assert.equal(rebindCalls, 0);
  assert.deepEqual(await pendingActivation.loadPending(), {
    activationCode,
    mode: "redeem",
    apiPartition: "https://hotbargain.vip/pos-api",
    hardwareId: "INSTALL-001",
  });
});

test("开通意图本地 staging 失败必须保留原 pending、零 API 且零 clear", async (t) => {
  const activationCode =
    "HBDEV1-0123456789ABCDEFGHJKMNPQRS-STVWXYZ0123456789ABCDEFGHJ";
  const pendingKey = "hbpos.ipad.pending-device-activation-code.v1";
  const cases = [
    { kind: "legacy" as const, seed: activationCode },
    { kind: "corrupt" as const, seed: "{not-json" },
    { kind: "load-error" as const, seed: null },
    { kind: "save-error" as const, seed: null },
  ];

  for (const scenario of cases) {
    await t.test(scenario.kind, async () => {
      const secureStore = new InMemorySecureStore();
      const originalGet = secureStore.get.bind(secureStore);
      const originalSet = secureStore.set.bind(secureStore);
      const originalRemove = secureStore.remove.bind(secureStore);
      if (scenario.seed !== null) {
        await originalSet(pendingKey, scenario.seed, {
          requireThisDeviceOnly: true,
        });
      }
      let pendingReads = 0;
      if (scenario.kind === "load-error") {
        secureStore.get = async (key) => {
          if (key === pendingKey && ++pendingReads === 2) {
            throw new Error("pending load failed");
          }
          return originalGet(key);
        };
      }
      if (scenario.kind === "save-error") {
        secureStore.set = async (key, value, options) => {
          await originalSet(key, value, options);
          if (key === pendingKey) throw new Error("pending save failed");
        };
      }
      let clearCalls = 0;
      secureStore.remove = async (key) => {
        if (key === pendingKey) clearCalls += 1;
        await originalRemove(key);
      };
      let apiCalls = 0;
      const pendingActivation = new PendingDeviceActivationCodeStore(
        secureStore,
      );
      const coordinator = new DeviceSessionCoordinator(
        {
          async register() { throw new Error("not used"); },
          async verify() { throw new Error("not used"); },
          async reregister() { throw new Error("not used"); },
          async redeemActivationCode() {
            apiCalls += 1;
            throw new Error("API must not start");
          },
        },
        new InstallationIdentityStore(secureStore, () => "INSTALL-001"),
        new DeviceCredentialStore(secureStore),
        undefined,
        undefined,
        undefined,
        pendingActivation,
      );

      await assert.rejects(() =>
        coordinator.redeemActivationCode({ activationCode }),
      );
      secureStore.get = originalGet;
      secureStore.set = originalSet;

      assert.equal(apiCalls, 0);
      assert.equal(clearCalls, 0);
      if (scenario.seed !== null) {
        assert.equal(await originalGet(pendingKey), scenario.seed);
      } else {
        assert.equal(await pendingActivation.load(), activationCode);
      }
    });
  }
});

test("重绑成功响应后的设备凭据落盘失败时保留重绑开通码和旧凭据", async () => {
  const secureStore = new InMemorySecureStore();
  const installation = new InstallationIdentityStore(secureStore, () => "INSTALL-001");
  const credentials = new DeviceCredentialStore(secureStore);
  const pendingActivation = new PendingDeviceActivationCodeStore(secureStore);
  const activationCode =
    "HBDEV1-0123456789ABCDEFGHJKMNPQRS-STVWXYZ0123456789ABCDEFGHJ";
  const oldCredentials = {
    deviceCode: "IPAD-OLD",
    storeCode: "1003",
    hardwareId: "INSTALL-001",
    authorizationCode: "old-secret",
  };
  await credentials.save(oldCredentials);
  credentials.save = async () => {
    throw new Error("credential save failed");
  };
  const coordinator = new DeviceSessionCoordinator(
    {
      async register() { throw new Error("not used"); },
      async verify() { throw new Error("not used"); },
      async reregister() { throw new Error("not used"); },
      async rebindActivationCode() {
        return {
          isAllowed: true,
          reasonCode: "ACTIVATED",
          deviceCode: "IPAD-NEW",
          storeCode: "1042",
          deviceStatus: 1,
          authorizationCode: "new-secret",
        };
      },
    },
    installation,
    credentials,
    undefined,
    undefined,
    undefined,
    pendingActivation,
  );

  await assert.rejects(
    () => coordinator.rebindActivationCode({ activationCode }),
    /credential save failed/i,
  );
  assert.deepEqual(await pendingActivation.loadPending(), {
    activationCode,
    mode: "rebind",
    apiPartition: "https://hotbargain.vip/pos-api",
    hardwareId: "INSTALL-001",
  });
  assert.deepEqual(await credentials.load(), oldCredentials);
});

test("已注册设备用开通码换店：网络不确定保留、业务拒绝清除、成功提交新 scope", async () => {
  const secureStore = new InMemorySecureStore();
  const installation = new InstallationIdentityStore(secureStore, () => "INSTALL-001");
  const credentials = new DeviceCredentialStore(secureStore);
  const pendingActivation = new PendingDeviceActivationCodeStore(secureStore);
  await credentials.save({
    deviceCode: "IPAD-OLD",
    storeCode: "1003",
    hardwareId: "INSTALL-001",
    authorizationCode: "old-secret",
  });
  const activationCode =
    "HBDEV1-0123456789ABCDEFGHJKMNPQRS-STVWXYZ0123456789ABCDEFGHJ";
  let outcome:
    | "network"
    | "http"
    | "rejected"
    | "allowed-without-reason"
    | "allowed-private-reason"
    | "allowed" = "network";
  const changes: DeviceScopeChange[] = [];
  const unsubscribe = subscribeDeviceScopeChange((change) => changes.push(change));
  const coordinator = new DeviceSessionCoordinator(
    {
      async register() { throw new Error("not used"); },
      async verify() { throw new Error("not used"); },
      async reregister() { throw new Error("legacy route must not be used"); },
      async previewActivationCode() {
        return { isAllowed: true, storeCode: "1042", storeName: "Sunnybank", deviceSystem: "iPadOS" };
      },
      async rebindActivationCode(input) {
        assert.deepEqual(input, { activationCode, terminalName: "Front iPad" });
        if (outcome === "network") {
          throw new HbposApiError("network timeout", {
            kind: "transport",
            code: "NO_HTTP_RESPONSE",
          });
        }
        if (outcome === "http") {
          throw new HbposApiError("invalid request", { kind: "http", status: 400 });
        }
        if (outcome === "rejected") {
          return {
            isAllowed: false,
            reasonCode: "ACTIVATION_CODE_NOT_AVAILABLE",
            deviceStatus: 1,
            message: "used",
          };
        }
        if (
          outcome === "allowed-without-reason" ||
          outcome === "allowed-private-reason"
        ) {
          return {
            isAllowed: true,
            ...(outcome === "allowed-private-reason"
              ? { reasonCode: "USED" }
              : {}),
            deviceCode: "IPAD-NEW",
            storeCode: "1042",
            storeName: "Sunnybank",
            deviceStatus: 1,
            authorizationCode: "new-secret",
          };
        }
        return {
          isAllowed: true,
          reasonCode: "ACTIVATED",
          deviceCode: "IPAD-NEW",
          storeCode: "1042",
          storeName: "Sunnybank",
          deviceStatus: 1,
          authorizationCode: "new-secret",
        };
      },
    },
    installation,
    credentials,
    undefined,
    undefined,
    undefined,
    pendingActivation,
  );

  try {
    const preview = await coordinator.previewActivationCode(activationCode);
    assert.equal(preview.storeCode, "1042");
    assert.equal(await pendingActivation.load(), null);

    await assert.rejects(
      () => coordinator.rebindActivationCode({ activationCode, terminalName: "Front iPad" }),
      /network timeout/,
    );
    assert.equal(await pendingActivation.load(), activationCode);

    outcome = "http";
    await assert.rejects(
      () => coordinator.rebindActivationCode({ activationCode, terminalName: "Front iPad" }),
      /invalid request/,
    );
    assert.equal(await pendingActivation.load(), null);

    outcome = "rejected";
    const rejected = await coordinator.rebindActivationCode({ activationCode, terminalName: "Front iPad" });
    assert.equal(rejected.status, "denied");
    assert.equal(await pendingActivation.load(), null);
    assert.equal((await credentials.load())?.storeCode, "1003");

    for (const uncertainSuccess of [
      "allowed-without-reason",
      "allowed-private-reason",
    ] as const) {
      outcome = uncertainSuccess;
      await assert.rejects(() =>
        coordinator.rebindActivationCode({
          activationCode,
          terminalName: "Front iPad",
        }),
      );
      assert.equal(await pendingActivation.load(), activationCode);
      assert.equal((await credentials.load())?.storeCode, "1003");
    }

    outcome = "allowed";
    const rebound = await coordinator.rebindActivationCode({ activationCode, terminalName: "Front iPad" });
    assert.equal(rebound.status, "authorized");
    assert.equal((await credentials.load())?.storeCode, "1042");
    assert.equal(await pendingActivation.load(), null);
    assert.deepEqual(changes, [{
      previous: { deviceCode: "IPAD-OLD", storeCode: "1003" },
      current: { deviceCode: "IPAD-NEW", storeCode: "1042" },
    }]);
  } finally {
    unsubscribe();
  }
});

test("恢复意图的 API 分区或 HardwareId 漂移时零请求且保留待开通记录", async () => {
  const activationCode =
    "HBDEV1-0123456789ABCDEFGHJKMNPQRS-STVWXYZ0123456789ABCDEFGHJ";
  const CoordinatorWithPartition = DeviceSessionCoordinator as unknown as new (
    ...args: readonly unknown[]
  ) => DeviceSessionCoordinator;

  for (const drift of [
    {
      currentPartition: "https://staging.hotbargain.vip/pos-api",
      currentHardwareId: "INSTALL-IPAD-001",
    },
    {
      currentPartition: "https://hotbargain.vip/pos-api",
      currentHardwareId: "INSTALL-IPAD-002",
    },
  ]) {
    const secureStore = new InMemorySecureStore();
    const pendingActivation = new PendingDeviceActivationCodeStore(secureStore);
    await pendingActivation.save(activationCode, "redeem", {
      apiPartition: "https://hotbargain.vip/pos-api",
      hardwareId: "INSTALL-001",
    });
    let redeemCalls = 0;
    const coordinator = new CoordinatorWithPartition(
      {
        async register() { throw new Error("not used"); },
        async verify() { throw new Error("verify must not start"); },
        async reregister() { throw new Error("not used"); },
        async redeemActivationCode() {
          redeemCalls += 1;
          throw new Error("recovery API must not start");
        },
      },
      new InstallationIdentityStore(secureStore, () => drift.currentHardwareId),
      new DeviceCredentialStore(secureStore),
      undefined,
      undefined,
      undefined,
      pendingActivation,
      drift.currentPartition,
    );

    await assert.rejects(() => coordinator.poll(), /intent|partition|hardware/i);
    assert.equal(redeemCalls, 0);
    assert.equal(await pendingActivation.load(), activationCode);
  }
});

test("本地 installation ID 失败不会留下可恢复开通码", async () => {
  const secureStore = new InMemorySecureStore();
  const pendingActivation = new PendingDeviceActivationCodeStore(secureStore);
  const activationCode =
    "HBDEV1-0123456789ABCDEFGHJKMNPQRS-STVWXYZ0123456789ABCDEFGHJ";
  await pendingActivation.save(activationCode, "redeem", {
    apiPartition: "https://hotbargain.vip/pos-api",
    hardwareId: "INSTALL-001",
  });
  const coordinator = new DeviceSessionCoordinator(
    {
      async register() { throw new Error("not used"); },
      async verify() { throw new Error("not used"); },
      async reregister() { throw new Error("not used"); },
      async redeemActivationCode() { throw new Error("request must not start"); },
    },
    new InstallationIdentityStore(secureStore, () => {
      throw new Error("installation ID failed");
    }),
    new DeviceCredentialStore(secureStore),
    undefined,
    undefined,
    undefined,
    pendingActivation,
  );

  await assert.rejects(
    () => coordinator.redeemActivationCode({ activationCode }),
    /installation ID failed/,
  );
  assert.equal(await pendingActivation.load(), activationCode);
});

test("重绑未到服务端时启动使用旧凭据重试 rebind，不误走匿名 redeem", async () => {
  const secureStore = new InMemorySecureStore();
  const installation = new InstallationIdentityStore(secureStore, () => "INSTALL-001");
  const credentials = new DeviceCredentialStore(secureStore);
  const pendingActivation = new PendingDeviceActivationCodeStore(secureStore);
  const activationCode =
    "HBDEV1-0123456789ABCDEFGHJKMNPQRS-STVWXYZ0123456789ABCDEFGHJ";
  let rebindCalls = 0;
  let redeemCalls = 0;
  await credentials.save({
    deviceCode: "IPAD-OLD",
    storeCode: "1003",
    hardwareId: "INSTALL-001",
    authorizationCode: "old-secret",
  });
  await pendingActivation.save(activationCode, "rebind", {
    apiPartition: "https://hotbargain.vip/pos-api",
    hardwareId: "INSTALL-001",
  });
  const coordinator = new DeviceSessionCoordinator(
    {
      async register() { throw new Error("not used"); },
      async reregister() { throw new Error("not used"); },
      async verify() { throw new Error("verify must not start"); },
      async rebindActivationCode(input) {
        rebindCalls += 1;
        assert.deepEqual(input, { activationCode });
        return {
          isAllowed: true,
          reasonCode: "ACTIVATED",
          deviceCode: "IPAD-NEW",
          storeCode: "1042",
          storeName: "Sunnybank",
          deviceStatus: 1,
          authorizationCode: "new-secret",
        };
      },
      async redeemActivationCode() {
        redeemCalls += 1;
        throw new Error("anonymous redeem must not start");
      },
    },
    installation,
    credentials,
    undefined,
    undefined,
    undefined,
    pendingActivation,
  );

  const recovered = await coordinator.poll();
  assert.equal(recovered.status, "authorized");
  assert.equal((await credentials.load())?.storeCode, "1042");
  assert.equal(rebindCalls, 1);
  assert.equal(redeemCalls, 0);
  assert.equal(await pendingActivation.load(), null);
});

test("重绑已提交但丢响应时，启动在旧凭据 403 后才匿名恢复新凭据", async () => {
  const secureStore = new InMemorySecureStore();
  const installation = new InstallationIdentityStore(secureStore, () => "INSTALL-001");
  const credentials = new DeviceCredentialStore(secureStore);
  const pendingActivation = new PendingDeviceActivationCodeStore(secureStore);
  const activationCode =
    "HBDEV1-0123456789ABCDEFGHJKMNPQRS-STVWXYZ0123456789ABCDEFGHJ";
  let rebindCalls = 0;
  let redeemCalls = 0;
  let recoveryReason: string | undefined = "ACTIVATED";
  let recoveryAllowed = true;
  let recoveryCredentialsComplete = true;
  let oldBindingFailure: "http" | "envelope" = "http";
  await credentials.save({
    deviceCode: "IPAD-OLD",
    storeCode: "1003",
    hardwareId: "INSTALL-001",
    authorizationCode: "old-secret",
  });
  await pendingActivation.save(activationCode, "rebind", {
    apiPartition: "https://hotbargain.vip/pos-api",
    hardwareId: "INSTALL-001",
  });
  const coordinator = new DeviceSessionCoordinator(
    {
      async register() { throw new Error("not used"); },
      async reregister() { throw new Error("not used"); },
      async verify() { throw new Error("verify must not start"); },
      async rebindActivationCode() {
        rebindCalls += 1;
        if (oldBindingFailure === "envelope") {
          throw new HbposApiError("old binding disabled", {
            kind: "envelope",
            code: "DEVICE_DISABLED",
          });
        }
        throw new HbposApiError("old binding disabled", {
          kind: "http",
          status: 403,
        });
      },
      async redeemActivationCode(input, options) {
        redeemCalls += 1;
        assert.deepEqual(input, { activationCode, hardwareId: "INSTALL-001" });
        assert.deepEqual(options, { recoveryOnly: true });
        return {
          isAllowed: recoveryAllowed,
          ...(recoveryReason ? { reasonCode: recoveryReason } : {}),
          deviceCode: "IPAD-NEW",
          storeCode: "1042",
          storeName: "Sunnybank",
          deviceStatus: 1,
          ...(recoveryCredentialsComplete
            ? { authorizationCode: "new-secret" }
            : {}),
        };
      },
    },
    installation,
    credentials,
    undefined,
    undefined,
    undefined,
    pendingActivation,
  );

  for (const invalidReason of ["ACTIVATED", undefined] as const) {
    recoveryReason = invalidReason;
    await assert.rejects(() => coordinator.poll(), /ACTIVATION_RECOVERED|recovery/i);
    assert.equal((await credentials.load())?.deviceCode, "IPAD-OLD");
    assert.equal(await pendingActivation.load(), activationCode);
  }
  recoveryAllowed = false;
  recoveryReason = "ACTIVATION_CODE_NOT_AVAILABLE";
  await assert.rejects(() => coordinator.poll(), /ACTIVATION_RECOVERED|recovery/i);
  assert.equal((await credentials.load())?.deviceCode, "IPAD-OLD");
  assert.equal(await pendingActivation.load(), activationCode);

  recoveryAllowed = true;
  oldBindingFailure = "envelope";
  recoveryReason = "ACTIVATION_RECOVERED";
  recoveryCredentialsComplete = false;
  await assert.rejects(() => coordinator.poll(), /incomplete/i);
  assert.equal((await credentials.load())?.deviceCode, "IPAD-OLD");
  assert.equal(await pendingActivation.load(), activationCode);

  recoveryCredentialsComplete = true;
  const recovered = await coordinator.poll();
  assert.equal(recovered.status, "authorized");
  assert.equal((await credentials.load())?.deviceCode, "IPAD-NEW");
  assert.equal(rebindCalls, 5);
  assert.equal(redeemCalls, 5);
  assert.equal(await pendingActivation.load(), null);
});

test("启动先用 pending 开通码恢复首次兑换；确定拒绝后才回到旧设备锁机，断网则保留", async () => {
  const secureStore = new InMemorySecureStore();
  const installation = new InstallationIdentityStore(secureStore, () => "INSTALL-001");
  const credentials = new DeviceCredentialStore(secureStore);
  const lock = new DeviceLockStore(secureStore);
  const pendingActivation = new PendingDeviceActivationCodeStore(secureStore);
  const activationCode =
    "HBDEV1-0123456789ABCDEFGHJKMNPQRS-STVWXYZ0123456789ABCDEFGHJ";
  let outcome: "recovered" | "rejected" | "network" = "recovered";
  let verifyCalls = 0;
  const coordinator = new DeviceSessionCoordinator(
    {
      async register() { throw new Error("not used"); },
      async reregister() { throw new Error("not used"); },
      async redeemActivationCode(input) {
        assert.deepEqual(input, { activationCode, hardwareId: "INSTALL-001" });
        if (outcome === "network") {
          throw new HbposApiError("offline", {
            kind: "transport",
            code: "NO_HTTP_RESPONSE",
          });
        }
        if (outcome === "rejected") {
          return {
            isAllowed: false,
            reasonCode: "ACTIVATION_CODE_NOT_AVAILABLE",
            deviceStatus: 1,
            message: "used",
          };
        }
        return {
          isAllowed: true,
          reasonCode: "ACTIVATION_RECOVERED",
          deviceCode: "IPAD-NEW",
          storeCode: "1042",
          storeName: "Sunnybank",
          deviceStatus: 1,
          authorizationCode: "new-secret",
        };
      },
      async verify() {
        verifyCalls += 1;
        return {
          isAllowed: false,
          deviceCode: "IPAD-OLD",
          storeCode: "1003",
          deviceStatus: 0,
          message: "disabled",
        };
      },
    },
    installation,
    credentials,
    lock,
    undefined,
    undefined,
    pendingActivation,
  );
  const seedOldLockedDevice = async () => {
    await credentials.save({
      deviceCode: "IPAD-OLD",
      storeCode: "1003",
      hardwareId: "INSTALL-001",
      authorizationCode: "old-secret",
    });
    await lock.lock("old binding disabled");
    await pendingActivation.save(activationCode, "redeem", {
      apiPartition: "https://hotbargain.vip/pos-api",
      hardwareId: "INSTALL-001",
    });
  };

  await seedOldLockedDevice();
  const recovered = await coordinator.poll();
  assert.equal(recovered.status, "authorized");
  assert.equal((await credentials.load())?.deviceCode, "IPAD-NEW");
  assert.equal(await lock.isLocked(), false);
  assert.equal(await pendingActivation.load(), null);
  assert.equal(verifyCalls, 0);

  outcome = "rejected";
  await seedOldLockedDevice();
  const rejected = await coordinator.poll();
  assert.equal(rejected.status, "disabled");
  assert.equal(await pendingActivation.load(), null);
  assert.equal(await lock.isLocked(), true);
  assert.equal(verifyCalls, 1);

  outcome = "network";
  await pendingActivation.save(activationCode, "redeem", {
    apiPartition: "https://hotbargain.vip/pos-api",
    hardwareId: "INSTALL-001",
  });
  await assert.rejects(() => coordinator.poll(), /offline/);
  assert.equal(await pendingActivation.load(), activationCode);
  assert.equal(verifyCalls, 1);
});
