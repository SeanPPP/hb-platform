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
