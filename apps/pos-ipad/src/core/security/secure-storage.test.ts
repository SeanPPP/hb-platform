import assert from "node:assert/strict";
import test from "node:test";

import {
  CashierAuthorizationStore,
  CashierSessionCache,
  DeviceCredentialStore,
  InMemorySecureStore,
  PendingDeviceRegistrationStore,
  type CashierSessionKeyHasher,
} from "./secure-storage";

const localOnly = { requireThisDeviceOnly: true };
const cacheKeyHasher: CashierSessionKeyHasher = {
  async sha256Hex() {
    return "b".repeat(64);
  },
};

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

test("收银员缓存保存时必须与门店和设备绑定，不能把另一设备的票据写入当前键", async () => {
  const cache = new CashierSessionCache(new InMemorySecureStore(), cacheKeyHasher);

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
  const cache = new CashierSessionCache(secureStore, cacheKeyHasher);
  const key = `hbpos.ipad.cashier.v2.${"b".repeat(64)}`;

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
  const cache = new CashierSessionCache(secureStore, hasher);
  const barcode = "CASHIER-PRIVATE-BARCODE";

  await cache.save(" S1 ", " POS-1 ", ` ${barcode} `, {
    cashierId: "C1",
    userGuid: "U1",
    cashierName: "Alice",
    storeCode: " S1 ",
    deviceCode: " POS-1 ",
  });

  assert.deepEqual(hasherInputs, [`S1\nPOS-1\n${barcode}`]);
  assert.equal(secureStore.lastWriteKey?.includes(barcode), false);
  assert.equal(secureStore.lastWriteKey, `hbpos.ipad.cashier.v2.${"c".repeat(64)}`);
  assert.deepEqual(secureStore.lastWriteOptions, localOnly);
});

test("收银员 bearer 使用版本化到期记录，活动期间到期后立即从 Keychain 清除", async () => {
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
  });
  assert.equal(await authorization.get(), "cashier-ticket");

  nowEpochMs = 2_000;
  systemUptimeMs = 11_000;
  assert.equal(await authorization.get(), null);
  assert.equal(
    await secureStore.get(
      "hbpos.ipad.active-cashier-authorization.v1",
    ),
    null,
  );
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
  });

  assert.equal(await authorization.get(), null);
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
  assert.equal(await authorization.get(), null);
  assert.equal(await secureStore.get(key), null);

  await authorization.set({
    authorizationToken: "new-ticket",
    expiresAtEpochMs: 2_000,
    source: "online",
  });
  systemUptimeMs = 9_999;
  assert.equal(await authorization.get(), null);
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
    systemUptimeMs: 10_000,
    trustedNowEpochMs: 1_000,
  });
  wallEpochMs = 0;
  systemUptimeMs = 69_999;
  assert.equal(await authorization.get(), "HBPOSE2-signed");

  systemUptimeMs = 70_000;
  assert.equal(await authorization.get(), null);
});
