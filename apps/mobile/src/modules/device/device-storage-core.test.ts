import assert from "node:assert/strict";
import test from "node:test";

import {
  createDeviceStorage,
  type DeviceStorageKeyValuePort,
} from "./device-storage-core";

class MemoryPort implements DeviceStorageKeyValuePort {
  readonly values = new Map<string, string>();
  async getItem(key: string) {
    return this.values.get(key) ?? null;
  }
  async setItem(key: string, value: string) {
    this.values.set(key, value);
  }
  async removeItem(key: string) {
    this.values.delete(key);
  }
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((nextResolve) => {
    resolve = nextResolve;
  });
  return { promise, resolve };
}

test("会话资料尚在读取时即并行读取安全凭据", async () => {
  const presentation = new MemoryPort();
  const sensitive = new MemoryPort();
  const pendingPresentation = deferred<string | null>();
  const reads: string[] = [];
  presentation.getItem = async () => {
    reads.push("presentation");
    return pendingPresentation.promise;
  };
  sensitive.getItem = async () => {
    reads.push("sensitive");
    return "secure-auth";
  };
  const storage = createDeviceStorage({
    presentation,
    sensitive,
    generateInstallationId: () => "unused",
  });

  const sessionPromise = storage.getSession();
  try {
    await Promise.resolve();
    assert.deepEqual(reads, ["presentation", "sensitive"]);
  } finally {
    pendingPresentation.resolve(JSON.stringify({ hardwareId: "hardware-1", storeCode: "BNE01" }));
  }
  assert.equal((await sessionPromise)?.authCode, "secure-auth");
});

test("缺失或无效会话资料时，安全凭据读取失败仍返回 null", async () => {
  for (const raw of [null, "bad-json", JSON.stringify({ hardwareId: "hardware-1" })]) {
    const presentation = new MemoryPort();
    const sensitive = new MemoryPort();
    if (raw !== null) presentation.values.set("hbweb_device_session", raw);
    sensitive.getItem = async () => {
      throw new Error("secure unavailable");
    };
    const storage = createDeviceStorage({
      presentation,
      sensitive,
      generateInstallationId: () => "unused",
    });
    assert.equal(await storage.getSession(), null);
  }

  // presentation 已经返回 null 后，安全读取才失败也必须被收敛。
  const presentation = new MemoryPort();
  const sensitive = new MemoryPort();
  let rejectSecure!: (error: Error) => void;
  sensitive.getItem = () => new Promise((_, reject) => {
    rejectSecure = reject;
  });
  const storage = createDeviceStorage({
    presentation,
    sensitive,
    generateInstallationId: () => "unused",
  });
  assert.equal(await storage.getSession(), null);
  rejectSecure(new Error("late secure failure"));
  await new Promise<void>((resolve) => setImmediate(resolve));
});

test("有效会话资料时，安全凭据读取失败仍向调用方抛错", async () => {
  const presentation = new MemoryPort();
  const sensitive = new MemoryPort();
  presentation.values.set(
    "hbweb_device_session",
    JSON.stringify({ hardwareId: "hardware-1", storeCode: "BNE01", authCode: "legacy-auth" }),
  );
  const failure = new Error("secure unavailable");
  sensitive.getItem = async () => {
    throw failure;
  };
  const storage = createDeviceStorage({
    presentation,
    sensitive,
    generateInstallationId: () => "unused",
  });

  await assert.rejects(storage.getSession(), (error) => error === failure);
  assert.equal(sensitive.values.has("hbmobile.legacy-device-auth-code.v1"), false);
  assert.equal(presentation.values.get("hbweb_device_session")?.includes("legacy-auth"), true);
});

test("旧 authCode 迁移先写入安全存储，再去除明文资料", async () => {
  const presentation = new MemoryPort();
  const sensitive = new MemoryPort();
  const writes: string[] = [];
  const pendingSecureWrite = deferred<void>();
  presentation.values.set(
    "hbweb_device_session",
    JSON.stringify({ hardwareId: "hardware-1", storeCode: "BNE01", authCode: "legacy-auth" }),
  );
  sensitive.values.set("hbmobile.legacy-device-auth-code.v1", "older-auth");
  sensitive.setItem = async (key, value) => {
    writes.push("secure");
    await pendingSecureWrite.promise;
    sensitive.values.set(key, value);
  };
  presentation.setItem = async (key, value) => {
    writes.push("presentation");
    presentation.values.set(key, value);
  };
  const storage = createDeviceStorage({
    presentation,
    sensitive,
    generateInstallationId: () => "unused",
  });

  const sessionPromise = storage.getSession();
  try {
    await new Promise<void>((resolve) => setImmediate(resolve));
    assert.deepEqual(writes, ["secure"]);
    assert.equal(presentation.values.get("hbweb_device_session")?.includes("legacy-auth"), true);
  } finally {
    pendingSecureWrite.resolve();
  }
  assert.equal((await sessionPromise)?.authCode, "legacy-auth");
  assert.deepEqual(writes, ["secure", "presentation"]);
  assert.equal(sensitive.values.get("hbmobile.legacy-device-auth-code.v1"), "legacy-auth");
  assert.equal(presentation.values.get("hbweb_device_session")?.includes("legacy-auth"), false);
});

test("只读预读不迁移旧凭据，真正读取时才迁移", async () => {
  const presentation = new MemoryPort();
  const sensitive = new MemoryPort();
  presentation.values.set(
    "hbweb_device_session",
    JSON.stringify({ hardwareId: "hardware-1", storeCode: "BNE01", authCode: "legacy-auth" }),
  );
  const storage = createDeviceStorage({ presentation, sensitive, generateInstallationId: () => "unused" });

  const snapshot = await storage.peekSession();
  assert.equal(snapshot.session?.authCode, "legacy-auth");
  assert.equal(snapshot.requiresMigration, true);
  assert.equal(sensitive.values.size, 0);
  assert.equal(presentation.values.get("hbweb_device_session")?.includes("legacy-auth"), true);

  assert.equal((await storage.getSession())?.authCode, "legacy-auth");
  assert.equal(storage.isSessionSnapshotCurrent(snapshot), false);
  assert.equal(sensitive.values.get("hbmobile.legacy-device-auth-code.v1"), "legacy-auth");
});

test("预读后设备会话写入或清除会使快照失效", async () => {
  const presentation = new MemoryPort();
  const sensitive = new MemoryPort();
  const storage = createDeviceStorage({ presentation, sensitive, generateInstallationId: () => "unused" });
  await storage.setSession({ hardwareId: "hardware-1", storeCode: "BNE01", authCode: "auth-1" });
  const original = await storage.peekSession();
  assert.equal(original.requiresMigration, false);
  assert.equal(storage.isSessionSnapshotCurrent(original), true);

  const pendingWrite = deferred<void>();
  sensitive.setItem = async (key, value) => {
    await pendingWrite.promise;
    sensitive.values.set(key, value);
  };
  const nextWrite = storage.setSession({ hardwareId: "hardware-2", storeCode: "BNE02", authCode: "auth-2" });
  assert.equal(storage.isSessionSnapshotCurrent(original), false);
  const duringWrite = await storage.peekSession();
  assert.equal(storage.isSessionSnapshotCurrent(duringWrite), false);
  pendingWrite.resolve();
  await nextWrite;
  assert.equal(storage.isSessionSnapshotCurrent(original), false);

  const next = await storage.peekSession();
  assert.equal(next.session?.authCode, "auth-2");
  assert.equal(storage.isSessionSnapshotCurrent(next), true);
  await storage.clearSession();
  assert.equal(storage.isSessionSnapshotCurrent(next), false);
});

test("清除的一项失败时等待另一项删除结束后才结束 revision 写入", async () => {
  const presentation = new MemoryPort();
  const sensitive = new MemoryPort();
  const storage = createDeviceStorage({ presentation, sensitive, generateInstallationId: () => "unused" });
  await storage.setSession({ hardwareId: "hardware-1", storeCode: "BNE01", authCode: "auth-1" });
  const before = await storage.peekSession();
  const failure = new Error("presentation delete failed");
  const pendingSecureDelete = deferred<void>();
  presentation.removeItem = async () => { throw failure; };
  sensitive.removeItem = async (key) => {
    await pendingSecureDelete.promise;
    sensitive.values.delete(key);
  };

  const clearResult = storage.clearSession().then(() => null, (error: unknown) => error);
  assert.equal(storage.isSessionSnapshotCurrent(before), false);
  const during = await storage.peekSession();
  assert.equal(storage.isSessionSnapshotCurrent(during), false);
  pendingSecureDelete.resolve();
  assert.equal(await clearResult, failure);
  assert.equal(storage.isSessionSnapshotCurrent(before), false);
  assert.equal(storage.isSessionSnapshotCurrent(during), false);
  const after = await storage.peekSession();
  assert.equal(storage.isSessionSnapshotCurrent(after), true);
  assert.notEqual(after.revision, before.revision);
});

test("旧 AsyncStorage 设备标识和 authCode 首次读取后透明迁入安全存储", async () => {
  const presentation = new MemoryPort();
  const sensitive = new MemoryPort();
  presentation.values.set("hbweb_device_installation_id", "legacy-hardware");
  presentation.values.set(
    "hbweb_device_session",
    JSON.stringify({
      hardwareId: "legacy-hardware",
      authCode: "legacy-auth-code",
      storeCode: "BNE01",
      systemDeviceNumber: "MOB-001",
    }),
  );
  const storage = createDeviceStorage({
    presentation,
    sensitive,
    generateInstallationId: () => "generated-hardware",
  });

  assert.equal(await storage.getInstallationId(), "legacy-hardware");
  assert.equal((await storage.getSession())?.authCode, "legacy-auth-code");
  assert.equal(presentation.values.has("hbweb_device_installation_id"), false);
  assert.equal(
    presentation.values.get("hbweb_device_session")?.includes("legacy-auth-code"),
    false,
  );
  assert.equal(
    [...sensitive.values.values()].some((value) => value.includes("legacy-auth-code")),
    true,
  );
});

test("后端资料响应不再返回 authCode 时保留本机已有安全凭据", async () => {
  const presentation = new MemoryPort();
  const sensitive = new MemoryPort();
  const storage = createDeviceStorage({
    presentation,
    sensitive,
    generateInstallationId: () => "hardware-1",
  });
  await storage.setSession({
    hardwareId: "hardware-1",
    authCode: "existing-auth-code",
    storeCode: "BNE01",
    systemDeviceNumber: "MOB-001",
  });
  await storage.setSession({
    hardwareId: "hardware-1",
    authCode: "",
    storeCode: "BNE01",
    systemDeviceNumber: "MOB-001",
  });

  assert.equal((await storage.getSession())?.authCode, "existing-auth-code");
});
