import assert from "node:assert/strict";
import test from "node:test";

import {
  createDeviceAccountStorage,
  type DeviceAccountKeyValueStorage,
} from "./device-account-storage";
import type {
  MobileDeviceActivationBinding,
  PendingMobileDeviceActivation,
} from "./types";

class MemoryStorage implements DeviceAccountKeyValueStorage {
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

const binding: MobileDeviceActivationBinding = {
  bindingId: "8b82e1d8-c435-4c1f-98fe-a4e513f4cc39",
  deviceRegistrationId: 11,
  deviceCode: "MOB-001",
  storeCode: "BNE01",
  storeName: "Brisbane",
  deviceSystem: "iOS",
  targetUserGuid: "user-guid",
  targetUsername: "alice",
  targetFullName: "Alice",
  boundAtUtc: "2026-08-31T10:00:00Z",
};

test("设备账号原始凭据只进入安全存储，展示缓存不含凭据", async () => {
  const secure = new MemoryStorage();
  const presentation = new MemoryStorage();
  const storage = createDeviceAccountStorage({ secure, presentation });

  await storage.saveBinding({
    binding,
    apiHost: "api.example.com",
    hardwareId: "hardware-1",
    credential: "private-credential",
  });

  assert.deepEqual(await storage.loadBinding(), {
    binding,
    apiHost: "api.example.com",
    hardwareId: "hardware-1",
    credential: "private-credential",
  });
  assert.equal(
    [...presentation.values.values()].some((value) =>
      value.includes("private-credential"),
    ),
    false,
  );
  assert.equal(
    [...secure.values.values()].some((value) => value.includes("private-credential")),
    true,
  );
  assert.equal(
    [...presentation.values.values()].some((value) =>
      value.includes("api.example.com"),
    ),
    true,
  );
});

test("未决开通意图在安全存储中保留精确恢复请求", async () => {
  const secure = new MemoryStorage();
  const presentation = new MemoryStorage();
  const storage = createDeviceAccountStorage({ secure, presentation });
  const pending: PendingMobileDeviceActivation = {
    version: 1,
    mode: "rebind",
    activationCode:
      "HBDEV1-0123456789ABCDEFGHJKMNPQRS-STVWXYZ0123456789ABCDEFGHJ",
    apiHost: "https://api.example.com",
    hardwareId: "hardware-1",
    deviceSystem: "iOS",
    credential: "next-private-credential",
    credentialVerifier: "a".repeat(64),
    currentHardwareId: "hardware-1",
    currentCredential: "current-private-credential",
  };

  await storage.savePending(pending);

  assert.deepEqual(await storage.loadPending(), pending);
  assert.equal(
    [...presentation.values.values()].some((value) => value.includes("HBDEV1")),
    false,
  );
  await storage.clearPending();
  assert.equal(await storage.loadPending(), null);
});

test("rebind 恢复意图必须同时保留旧 hardwareId 和原始凭据", async () => {
  const storage = createDeviceAccountStorage({
    secure: new MemoryStorage(),
    presentation: new MemoryStorage(),
  });

  await assert.rejects(
    () => storage.savePending({
      version: 1,
      mode: "rebind",
      activationCode:
        "HBDEV1-0123456789ABCDEFGHJKMNPQRS-STVWXYZ0123456789ABCDEFGHJ",
      apiHost: "api.example.com",
      hardwareId: "hardware-1",
      deviceSystem: "Android",
      credential: "next-private-credential",
      credentialVerifier: "b".repeat(64),
    }),
    /pending mobile device activation/i,
  );
});
