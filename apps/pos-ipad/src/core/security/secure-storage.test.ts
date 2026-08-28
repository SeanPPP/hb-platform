import assert from "node:assert/strict";
import test from "node:test";

import {
  CashierAuthorizationStore,
  CashierSessionCache,
  DeviceCredentialStore,
  DevicePresentationStore,
  DeviceRegistrationResetMarkerStore,
  InMemorySecureStore,
  PendingDeviceActivationCodeStore,
  PendingDeviceRegistrationStore,
  type CashierSessionKeyHasher,
} from "./secure-storage";

const localOnly = { requireThisDeviceOnly: true };
const cacheKeyHasher: CashierSessionKeyHasher = {
  async sha256Hex() {
    return "b".repeat(64);
  },
};
const cashierCacheNamespace = {
  async getAuthorizationFingerprint() {
    return "device-auth-fingerprint";
  },
};

const promptReadTimeout = Symbol("prompt-read-timeout");

test("待兑换开通码仅以 ThisDeviceOnly 暂存并可在崩溃后恢复", async () => {
  const secureStore = new InMemorySecureStore();
  const pending = new PendingDeviceActivationCodeStore(secureStore);
  const activationCode =
    "HBDEV1-0123456789ABCDEFGHJKMNPQRS-STVWXYZ0123456789ABCDEFGHJ";
  const otherActivationCode =
    "HBDEV1-1123456789ABCDEFGHJKMNPQRS-STVWXYZ0123456789ABCDEFGHJ";
  const intent = {
    apiPartition: "https://hotbargain.vip/pos-api",
    hardwareId: "INSTALL-IPAD-001",
  } as const;

  await pending.save(`  ${activationCode.toLowerCase()}\n`, "redeem", intent);

  assert.equal(await pending.load(), activationCode);
  assert.deepEqual(await pending.loadPending(), {
    activationCode,
    mode: "redeem",
    ...intent,
  });
  await pending.save(activationCode, "redeem", intent);
  await assert.rejects(
    () => pending.save(activationCode, "rebind", intent),
    /pending.*conflict/i,
  );
  await assert.rejects(
    () => pending.save(otherActivationCode, "redeem", intent),
    /pending.*conflict/i,
  );
  assert.deepEqual(await pending.loadPending(), {
    activationCode,
    mode: "redeem",
    ...intent,
  });
  assert.equal(secureStore.lastWriteOptions?.requireThisDeviceOnly, true);
  await pending.clear();
  await pending.save(activationCode, "rebind", intent);
  assert.deepEqual(await pending.loadPending(), {
    activationCode,
    mode: "rebind",
    ...intent,
  });
  await pending.clear();
  assert.equal(await pending.load(), null);
  await assert.rejects(
    () => pending.save("HBDEV1-invalid", "redeem", intent),
    /invalid/i,
  );
});

test("待开通码损坏或安全存储读取失败时保留记录并失败关闭", async () => {
  const key = "hbpos.ipad.pending-device-activation-code.v1";
  const secureStore = new InMemorySecureStore();
  const pending = new PendingDeviceActivationCodeStore(secureStore);
  await secureStore.set(key, "not-json", localOnly);

  await assert.rejects(() => pending.loadPending(), /invalid/i);
  assert.equal(await secureStore.get(key), "not-json", "不得删除状态不确定的恢复记录");

  const readFailure = new Error("secure store unavailable");
  secureStore.get = async (requestedKey) => {
    if (requestedKey === key) throw readFailure;
    return null;
  };
  await assert.rejects(() => pending.loadPending(), (error) => error === readFailure);
});

test("待开通恢复只允许同一码、模式、API 分区和硬件意图幂等", async () => {
  const key = "hbpos.ipad.pending-device-activation-code.v1";
  const secureStore = new InMemorySecureStore();
  const pending = new PendingDeviceActivationCodeStore(secureStore);
  const activationCode =
    "HBDEV1-0123456789ABCDEFGHJKMNPQRS-STVWXYZ0123456789ABCDEFGHJ";
  const saveIntent = pending.save.bind(pending) as unknown as (
    value: string,
    mode: "redeem" | "rebind",
    intent: Readonly<{ apiPartition: string; hardwareId: string }>,
  ) => Promise<void>;

  await saveIntent(activationCode, "redeem", {
    apiPartition: "https://hotbargain.vip/pos-api/",
    hardwareId: "INSTALL-IPAD-001",
  });
  const original = await secureStore.get(key);
  assert.deepEqual(await pending.loadPending(), {
    activationCode,
    mode: "redeem",
    apiPartition: "https://hotbargain.vip/pos-api",
    hardwareId: "INSTALL-IPAD-001",
  });
  await saveIntent(activationCode, "redeem", {
    apiPartition: "https://hotbargain.vip/pos-api",
    hardwareId: "INSTALL-IPAD-001",
  });
  for (const changed of [
    { apiPartition: "https://staging.hotbargain.vip/pos-api", hardwareId: "INSTALL-IPAD-001" },
    { apiPartition: "https://hotbargain.vip/pos-api", hardwareId: "INSTALL-IPAD-002" },
  ]) {
    await assert.rejects(
      () => saveIntent(activationCode, "redeem", changed),
      /pending.*conflict/i,
    );
    assert.equal(await secureStore.get(key), original);
  }
});

test("旧版只有码或 code+mode 的恢复记录必须保留并失败关闭", async () => {
  const key = "hbpos.ipad.pending-device-activation-code.v1";
  const secureStore = new InMemorySecureStore();
  const pending = new PendingDeviceActivationCodeStore(secureStore);
  const activationCode =
    "HBDEV1-0123456789ABCDEFGHJKMNPQRS-STVWXYZ0123456789ABCDEFGHJ";

  for (const legacy of [
    activationCode,
    JSON.stringify({ version: 2, activationCode, mode: "redeem" }),
  ]) {
    await secureStore.set(key, legacy, localOnly);
    await assert.rejects(() => pending.loadPending(), /invalid/i);
    assert.equal(await secureStore.get(key), legacy);
  }
});

async function assertPromptNull(
  operation: Promise<string | null>,
  message: string,
): Promise<void> {
  const result = await Promise.race([
    operation,
    new Promise<typeof promptReadTimeout>((resolve) => {
      setImmediate(() => resolve(promptReadTimeout));
    }),
  ]);
  assert.notEqual(result, promptReadTimeout, message);
  assert.equal(result, null);
}

async function flushBackgroundCleanup(): Promise<void> {
  await new Promise<void>((resolve) => setImmediate(resolve));
}

test("设备凭据仅接受四个完整非空字段，损坏或旧格式 Keychain 数据必须失败关闭", async () => {
  const secureStore = new InMemorySecureStore();
  const credentials = new DeviceCredentialStore(secureStore);

  for (const raw of [
    "{}",
    "[]",
    "not-json",
    JSON.stringify({
      deviceCode: "POS-1",
      storeCode: "S1",
      hardwareId: "",
      authorizationCode: "secret",
    }),
  ]) {
    await secureStore.set(
      "hbpos.ipad.device-credentials.v1",
      raw,
      localOnly,
    );
    await assert.rejects(
      credentials.load(),
      /Stored hbpos\.ipad\.device-credentials\.v1 is invalid/,
    );
  }
});

test("设备凭据保存和读取保持设备、门店、安装 UUID 与授权码绑定", async () => {
  const secureStore = new InMemorySecureStore();
  const credentials = new DeviceCredentialStore(secureStore);
  const expected = {
    deviceCode: "POS-1",
    storeCode: "S1",
    hardwareId: "INSTALL-1",
    authorizationCode: "device-secret",
  };

  await credentials.save(expected);

  assert.deepEqual(await credentials.load(), expected);
  assert.deepEqual(secureStore.lastWriteOptions, localOnly);
});

test("设备展示缓存使用独立 v1 Key 保存脱敏名称且仅限本机", async () => {
  const secureStore = new InMemorySecureStore();
  const presentation = new DevicePresentationStore(secureStore);

  await presentation.save({
    deviceCode: "POS-1",
    storeCode: "S1",
    storeName: "Chermside",
  });

  assert.deepEqual(await presentation.load(), {
    deviceCode: "POS-1",
    storeCode: "S1",
    storeName: "Chermside",
  });
  assert.equal(
    await secureStore.get("hbpos.ipad.device-presentation.v1"),
    JSON.stringify({
      version: 1,
      deviceCode: "POS-1",
      storeCode: "S1",
      storeName: "Chermside",
    }),
  );
  assert.equal(
    await secureStore.get("hbpos.ipad.device-credentials.v1"),
    null,
  );
  assert.deepEqual(secureStore.lastWriteOptions, localOnly);
});

test("损坏的设备展示缓存按无缓存处理并最佳努力清理", async () => {
  const key = "hbpos.ipad.device-presentation.v1";
  const secureStore = new InMemorySecureStore();
  const presentation = new DevicePresentationStore(secureStore);

  for (const raw of [
    "not-json",
    JSON.stringify({
      version: 2,
      deviceCode: "POS-1",
      storeCode: "S1",
      storeName: "Chermside",
    }),
    JSON.stringify({
      version: 1,
      deviceCode: "POS-1",
      storeCode: "S1",
      storeName: " ",
    }),
  ]) {
    await secureStore.set(key, raw, localOnly);
    assert.equal(await presentation.load(), null);
    assert.equal(await secureStore.get(key), null);
  }

  class FailingRemoveSecureStore extends InMemorySecureStore {
    public override async remove(): Promise<void> {
      throw new Error("Keychain remove failed.");
    }
  }
  const failingStore = new FailingRemoveSecureStore();
  await failingStore.set(key, "not-json", localOnly);

  assert.equal(
    await new DevicePresentationStore(failingStore).load(),
    null,
  );
});

test("待审批记录缺少设备或门店时不得进入本地 pending 状态", async () => {
  const secureStore = new InMemorySecureStore();
  const pending = new PendingDeviceRegistrationStore(secureStore);
  await secureStore.set(
    "hbpos.ipad.pending-device-registration.v1",
    JSON.stringify({ deviceCode: "POS-PENDING" }),
    localOnly,
  );

  await assert.rejects(
    pending.load(),
    /Stored hbpos\.ipad\.pending-device-registration\.v1 is invalid/,
  );
});

test("设备注册重置恢复标记只保存精确设备身份、阶段和操作 UUID", async () => {
  const secureStore = new InMemorySecureStore();
  const markers = new DeviceRegistrationResetMarkerStore(secureStore);
  const expected = {
    version: 1 as const,
    operationId: "10000000-0000-4000-8000-000000000001",
    phase: "prepared" as const,
    deviceCode: "IPAD-1042-01",
    storeCode: "1042",
    hardwareId: "20000000-0000-4000-8000-000000000002",
    createdAtUtc: "2026-08-18T02:00:00.000Z",
  };

  await markers.save(expected);

  assert.deepEqual(await markers.load(), expected);
  assert.equal(
    await secureStore.get("hbpos.ipad.device-registration-reset.v1"),
    JSON.stringify(expected),
  );
  assert.deepEqual(secureStore.lastWriteOptions, localOnly);
});

test("损坏的设备注册重置恢复标记必须失败关闭且不能自动删除", async () => {
  const secureStore = new InMemorySecureStore();
  const key = "hbpos.ipad.device-registration-reset.v1";
  await secureStore.set(
    key,
    JSON.stringify({
      version: 1,
      operationId: "not-a-uuid",
      phase: "prepared",
      deviceCode: "IPAD-1",
      storeCode: "1042",
      hardwareId: "INSTALL-1",
      createdAtUtc: "2026-08-18T02:00:00.000Z",
    }),
    localOnly,
  );

  await assert.rejects(
    new DeviceRegistrationResetMarkerStore(secureStore).load(),
    /Stored hbpos\.ipad\.device-registration-reset\.v1 is invalid/,
  );
  assert.notEqual(await secureStore.get(key), null);
});

test("收银员缓存保存时必须与门店和设备绑定，不能把另一设备的票据写入当前键", async () => {
  const cache = new CashierSessionCache(
    new InMemorySecureStore(),
    cacheKeyHasher,
    cashierCacheNamespace,
  );

  await assert.rejects(
    cache.save("S1", "POS-1", "BARCODE-1", {
      cashierId: "C1",
      userGuid: "U1",
      cashierName: "Alice",
      storeCode: "S1",
      deviceCode: "POS-2",
      authorizationToken: "cashier-ticket",
    }),
    /Stored cashier session is invalid/,
  );
});

test("收银员离线缓存拒绝损坏字段和换绑内容，不把 JSON 类型断言当作认证", async () => {
  const secureStore = new InMemorySecureStore();
  const cache = new CashierSessionCache(
    secureStore,
    cacheKeyHasher,
    cashierCacheNamespace,
  );
  const key = `hbpos.ipad.cashier.v3.${"b".repeat(64)}`;

  for (const value of [
    { storeCode: "S1", deviceCode: "POS-1" },
    {
      cashierId: "C1",
      userGuid: "U1",
      cashierName: "Alice",
      storeCode: "S2",
      deviceCode: "POS-1",
    },
    {
      cashierId: "C1",
      userGuid: "U1",
      cashierName: "Alice",
      storeCode: "S1",
      deviceCode: "POS-1",
      permissionCodes: ["sale", 42],
    },
  ]) {
    await secureStore.set(key, JSON.stringify(value), localOnly);
    await assert.rejects(
      cache.load("S1", "POS-1", "BARCODE-1"),
      /Stored cashier session is invalid/,
    );
  }
});

test("收银员缓存 Key 使用规范化三元组的 SHA-256，绝不把条码写入 Keychain 项名", async () => {
  const secureStore = new InMemorySecureStore();
  const hasherInputs: string[] = [];
  const hasher: CashierSessionKeyHasher = {
    async sha256Hex(input) {
      hasherInputs.push(input);
      return "c".repeat(64);
    },
  };
  const cache = new CashierSessionCache(
    secureStore,
    hasher,
    cashierCacheNamespace,
  );
  const barcode = "CASHIER-PRIVATE-BARCODE";

  await cache.save(" S1 ", " POS-1 ", ` ${barcode} `, {
    cashierId: "C1",
    userGuid: "U1",
    cashierName: "Alice",
    storeCode: " S1 ",
    deviceCode: " POS-1 ",
  });

  assert.deepEqual(hasherInputs, [
    `device-auth-fingerprint\nS1\nPOS-1\n${barcode}`,
  ]);
  assert.equal(secureStore.lastWriteKey?.includes(barcode), false);
  assert.equal(secureStore.lastWriteKey, `hbpos.ipad.cashier.v3.${"c".repeat(64)}`);
  assert.deepEqual(secureStore.lastWriteOptions, localOnly);
});

test("重新注册轮换设备授权码后不能命中旧收银员缓存", async () => {
  const secureStore = new InMemorySecureStore();
  let authorizationFingerprint = "authorization-v1";
  const hasher: CashierSessionKeyHasher = {
    async sha256Hex(input) {
      return input.startsWith("authorization-v1\n")
        ? "1".repeat(64)
        : "2".repeat(64);
    },
  };
  const cache = new CashierSessionCache(secureStore, hasher, {
    async getAuthorizationFingerprint() {
      return authorizationFingerprint;
    },
  });
  await cache.save("S1", "POS-1", "BARCODE-1", {
    cashierId: "C1",
    userGuid: "U1",
    cashierName: "Alice",
    storeCode: "S1",
    deviceCode: "POS-1",
  });

  authorizationFingerprint = "authorization-v2";

  assert.equal(await cache.load("S1", "POS-1", "BARCODE-1"), null);
  assert.notEqual(
    await secureStore.get(`hbpos.ipad.cashier.v3.${"1".repeat(64)}`),
    null,
  );
});

test("收银员 bearer 活动期间可读，到期后立即 fail-close 并后台清除 Keychain", async () => {
  const secureStore = new InMemorySecureStore();
  let nowEpochMs = 1_000;
  let systemUptimeMs = 10_000;
  const authorization = new CashierAuthorizationStore(
    secureStore,
    {
      getSystemUptimeMilliseconds: () => systemUptimeMs,
      nowEpochMs: () => nowEpochMs,
    },
  );

  await authorization.set({
    authorizationToken: "cashier-ticket",
    expiresAtEpochMs: 2_000,
    source: "online",
    scope: { storeCode: "S1", deviceCode: "IPAD-1" },
  });
  assert.equal(
    await authorization.get({ storeCode: "S1", deviceCode: "IPAD-1" }),
    "cashier-ticket",
  );

  nowEpochMs = 2_000;
  systemUptimeMs = 11_000;
  assert.equal(
    await authorization.get({ storeCode: "S1", deviceCode: "IPAD-1" }),
    null,
  );
  await flushBackgroundCleanup();
  assert.equal(
    await secureStore.get(
      "hbpos.ipad.active-cashier-authorization.v1",
    ),
    null,
  );
});

test("收银员 bearer 必须精确匹配已验证的门店与设备 scope，旧 v2 envelope 不得猜测 scope", async () => {
  const secureStore = new InMemorySecureStore();
  const authorization = new CashierAuthorizationStore(secureStore, {
    getSystemUptimeMilliseconds: () => 10_000,
    nowEpochMs: () => 1_000,
  });
  const key = "hbpos.ipad.active-cashier-authorization.v1";

  await authorization.set({
    authorizationToken: "cashier-ticket",
    expiresAtEpochMs: 2_000,
    source: "online",
    scope: { storeCode: "S1", deviceCode: "IPAD-1" },
  });
  const reloadedAuthorization = new CashierAuthorizationStore(secureStore, {
    getSystemUptimeMilliseconds: () => 10_000,
    nowEpochMs: () => 1_000,
  });
  assert.equal(
    await reloadedAuthorization.get({ storeCode: "S2", deviceCode: "IPAD-2" }),
    null,
  );
  await flushBackgroundCleanup();
  assert.equal(await secureStore.get(key), null);

  await secureStore.set(
    key,
    JSON.stringify({
      activatedAtSystemUptimeMs: 10_000,
      authorizationToken: "legacy-ticket",
      expiresAtEpochMs: 2_000,
      expiresAtSystemUptimeMs: 11_000,
      source: "online",
      version: 2,
    }),
    localOnly,
  );
  assert.equal(
    await authorization.get({ storeCode: "S1", deviceCode: "IPAD-1" }),
    null,
  );
  await flushBackgroundCleanup();
  assert.equal(await secureStore.get(key), null);
});

test("scope 撤销在 Keychain 清理挂起或失败时仍同步阻断 bearer，随后成功登录才恢复", async () => {
  let releaseRemove: (() => void) | undefined;
  class DelayedRemoveSecureStore extends InMemorySecureStore {
    public override async remove(key: string): Promise<void> {
      await new Promise<void>((resolve) => {
        releaseRemove = resolve;
      });
      await super.remove(key);
    }
  }
  const secureStore = new DelayedRemoveSecureStore();
  const authorization = new CashierAuthorizationStore(secureStore, {
    getSystemUptimeMilliseconds: () => 10_000,
    nowEpochMs: () => 1_000,
  });
  const s1 = { storeCode: "S1", deviceCode: "IPAD-1" };
  const s2 = { storeCode: "S2", deviceCode: "IPAD-2" };

  await authorization.set({
    authorizationToken: "cashier-ticket",
    expiresAtEpochMs: 2_000,
    source: "online",
    scope: s1,
  });
  authorization.invalidateForDeviceScope();
  assert.equal(await authorization.get(s1), null);

  await new Promise<void>((resolve) => setImmediate(resolve));

  const restore = authorization.set({
    authorizationToken: "replacement-ticket",
    expiresAtEpochMs: 2_000,
    source: "online",
    scope: s2,
  });
  assert.ok(releaseRemove);
  releaseRemove?.();
  await restore;
  assert.equal(await authorization.get(s2), "replacement-ticket");
  await assertPromptNull(
    authorization.get(s1),
    "scope mismatch 不得等待后台 Keychain 清理",
  );
  assert.equal(await authorization.get(s2), null);
});

test("过期、损坏与无效请求 scope 在 Keychain 清理挂起或失败时立即 fail-close", async (context) => {
  class ControlledRemoveSecureStore extends InMemorySecureStore {
    public removeCalls = 0;

    public constructor(private readonly outcome: "pending" | "reject") {
      super();
    }

    public override async remove(): Promise<void> {
      this.removeCalls += 1;
      if (this.outcome === "pending") {
        await new Promise<void>(() => undefined);
        return;
      }
      throw new Error("Keychain cleanup unavailable.");
    }
  }
  const scope = { storeCode: "S1", deviceCode: "IPAD-1" };
  const key = "hbpos.ipad.active-cashier-authorization.v1";

  await context.test("expired + pending remove", async () => {
    let nowEpochMs = 1_000;
    let systemUptimeMs = 10_000;
    const secureStore = new ControlledRemoveSecureStore("pending");
    const authorization = new CashierAuthorizationStore(secureStore, {
      getSystemUptimeMilliseconds: () => systemUptimeMs,
      nowEpochMs: () => nowEpochMs,
    });
    await authorization.set({
      authorizationToken: "active-ticket",
      expiresAtEpochMs: 2_000,
      source: "online",
      scope,
    });
    nowEpochMs = 2_000;
    systemUptimeMs = 11_000;

    await assertPromptNull(
      authorization.get(scope),
      "过期授权不得等待后台 Keychain 清理",
    );
    await new Promise<void>((resolve) => setImmediate(resolve));
    assert.equal(secureStore.removeCalls, 1);
  });

  for (const scenario of [
    {
      name: "corrupt + pending remove",
      storedValue: "not-json",
      requestScope: scope,
      removeOutcome: "pending",
    },
    {
      name: "invalid request scope + pending remove",
      storedValue: JSON.stringify({
        activatedAtSystemUptimeMs: 10_000,
        authorizationToken: "active-ticket",
        expiresAtEpochMs: 2_000,
        expiresAtSystemUptimeMs: 11_000,
        scope,
        source: "online",
        version: 3,
      }),
      requestScope: { storeCode: "", deviceCode: "IPAD-1" },
      removeOutcome: "pending",
    },
    {
      name: "rejected remove does not escape background cleanup",
      storedValue: "not-json",
      requestScope: scope,
      removeOutcome: "reject",
    },
  ] as const) {
    await context.test(scenario.name, async () => {
      const secureStore = new ControlledRemoveSecureStore(
        scenario.removeOutcome,
      );
      const authorization = new CashierAuthorizationStore(secureStore, {
        getSystemUptimeMilliseconds: () => 10_000,
        nowEpochMs: () => 1_000,
      });
      await secureStore.set(
        key,
        scenario.storedValue,
        localOnly,
      );

      await assertPromptNull(
        authorization.get(scenario.requestScope),
        `${scenario.name} 不得等待后台 Keychain 清理`,
      );
      await new Promise<void>((resolve) => setImmediate(resolve));
      assert.equal(secureStore.removeCalls, 1);
    });
  }
});

test("普通离线缓存不因票据过期失效，但过期 bearer 不得保存或附加", async () => {
  const secureStore = new InMemorySecureStore();
  const authorization = new CashierAuthorizationStore(
    secureStore,
    {
      getSystemUptimeMilliseconds: () => 5_000,
      nowEpochMs: () => 10_000,
    },
  );

  await authorization.set({
    authorizationToken: "expired-ticket",
    expiresAtEpochMs: 9_999,
    source: "offline-cache",
    scope: { storeCode: "S1", deviceCode: "IPAD-1" },
  });

  assert.equal(
    await authorization.get({ storeCode: "S1", deviceCode: "IPAD-1" }),
    null,
  );
});

test("旧纯字符串、损坏 envelope 与进程 uptime 回退均 fail closed 并清除", async () => {
  const secureStore = new InMemorySecureStore();
  let systemUptimeMs = 10_000;
  const authorization = new CashierAuthorizationStore(
    secureStore,
    {
      getSystemUptimeMilliseconds: () => systemUptimeMs,
      nowEpochMs: () => 1_000,
    },
  );
  const key = "hbpos.ipad.active-cashier-authorization.v1";

  await secureStore.set(key, "legacy-raw-ticket", localOnly);
  assert.equal(
    await authorization.get({ storeCode: "S1", deviceCode: "IPAD-1" }),
    null,
  );
  await flushBackgroundCleanup();
  assert.equal(await secureStore.get(key), null);

  await authorization.set({
    authorizationToken: "new-ticket",
    expiresAtEpochMs: 2_000,
    source: "online",
    scope: { storeCode: "S1", deviceCode: "IPAD-1" },
  });
  systemUptimeMs = 9_999;
  assert.equal(
    await authorization.get({ storeCode: "S1", deviceCode: "IPAD-1" }),
    null,
  );
  await flushBackgroundCleanup();
  assert.equal(await secureStore.get(key), null);
});

test("紧急 bearer 只使用服务端可信时间与单调 uptime，墙钟回拨不能延长", async () => {
  const secureStore = new InMemorySecureStore();
  let wallEpochMs = 99_999_999;
  let systemUptimeMs = 10_000;
  const authorization = new CashierAuthorizationStore(
    secureStore,
    {
      getSystemUptimeMilliseconds: () => systemUptimeMs,
      nowEpochMs: () => wallEpochMs,
    },
  );

  await authorization.set({
    authorizationToken: "HBPOSE2-signed",
    expiresAtEpochMs: 61_000,
    source: "emergency-override",
    scope: { storeCode: "S1", deviceCode: "IPAD-1" },
    systemUptimeMs: 10_000,
    trustedNowEpochMs: 1_000,
  });
  wallEpochMs = 0;
  systemUptimeMs = 69_999;
  assert.equal(
    await authorization.get({ storeCode: "S1", deviceCode: "IPAD-1" }),
    "HBPOSE2-signed",
  );

  systemUptimeMs = 70_000;
  assert.equal(
    await authorization.get({ storeCode: "S1", deviceCode: "IPAD-1" }),
    null,
  );
});
