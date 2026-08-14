import assert from "node:assert/strict";
import test from "node:test";

import {
  createExpoAttendanceRuntimeConfiguration,
  type ExpoAttendanceDeviceCredentials,
} from "./expo-attendance-runtime-configuration";

import type {
  AttendanceQrCachePort,
  AttendanceQrCryptoPort,
  AttendanceSchedulerPort,
  AttendanceSecurityRemotePort,
  OperationAuditReadPort,
} from "@/features/attendance-audit";

const credentials: ExpoAttendanceDeviceCredentials = Object.freeze({
  authorizationCode: "authorization-secret",
  deviceCode: "IPAD-1",
  hardwareId: "installation-1",
  storeCode: "S001",
});

test("Expo 考勤配置每次读取设备上下文都复核完整凭据与授权摘要", async () => {
  let current: ExpoAttendanceDeviceCredentials | null = credentials;
  const configuration = createExpoAttendanceRuntimeConfiguration({
    attendanceSecurity: security(),
    authorizationMarker: "MARKER:AUTHORIZATION-SECRET",
    connectivity: {
      currentOnline: () => true,
      async isOnline() {
        return true;
      },
    },
    credentials,
    localAudit: audit(),
    qrCache: cache(),
    qrCrypto: crypto(),
    readCurrentCredentials: async () => current,
    readStoreName: async () => " Brisbane CBD ",
    remoteAudit: audit(),
    scheduler: scheduler(),
    sha256Hex: async (value) => `marker:${value}`,
  });

  assert.deepEqual(
    await configuration.deviceContext.getDeviceContext(),
    {
      authorizationMarker: "MARKER:AUTHORIZATION-SECRET",
      deviceCode: "IPAD-1",
      hardwareId: "installation-1",
      isAllowed: true,
      storeCode: "S001",
      storeName: "Brisbane CBD",
    },
  );

  current = { ...credentials, authorizationCode: "rotated-secret" };
  assert.equal(
    await configuration.deviceContext.getDeviceContext(),
    null,
    "授权码轮换后旧 runtime 不得继续签发考勤二维码",
  );
});

test("Expo 考勤配置拒绝设备、门店或硬件身份漂移并安全回退门店名", async () => {
  let current: ExpoAttendanceDeviceCredentials | null = credentials;
  const configuration = createExpoAttendanceRuntimeConfiguration({
    attendanceSecurity: security(),
    authorizationMarker: "MARKER:AUTHORIZATION-SECRET",
    connectivity: {
      currentOnline: () => false,
      async isOnline() {
        return false;
      },
    },
    credentials,
    localAudit: audit(),
    qrCache: cache(),
    qrCrypto: crypto(),
    readCurrentCredentials: async () => current,
    readStoreName: async () => "   ",
    remoteAudit: audit(),
    scheduler: scheduler(),
    sha256Hex: async (value) => `marker:${value}`,
  });

  assert.equal(
    (await configuration.deviceContext.getDeviceContext())?.storeName,
    "S001",
  );

  for (const changed of [
    { ...credentials, storeCode: "S002" },
    { ...credentials, deviceCode: "IPAD-2" },
    { ...credentials, hardwareId: "installation-2" },
  ]) {
    current = changed;
    assert.equal(
      await configuration.deviceContext.getDeviceContext(),
      null,
    );
  }

  current = null;
  assert.equal(
    await configuration.deviceContext.getDeviceContext(),
    null,
  );
});

function cache(): AttendanceQrCachePort {
  return {
    async clear() {},
    async load() {
      return null;
    },
    async replace() {},
  };
}

function crypto(): AttendanceQrCryptoPort {
  return {
    async createA256Identity() {
      return { keyHandle: "handle", kid: "kid" };
    },
    async destroyKey() {},
    async hasA256Key() {
      return true;
    },
    async issueAttendanceQr() {
      return { imageUri: "data:image/png;base64,AA==" };
    },
    async withRegistrationKey(_keyHandle, runWithMaterial) {
      return runWithMaterial("material");
    },
  };
}

function scheduler(): AttendanceSchedulerPort {
  return {
    every() {
      return () => undefined;
    },
  };
}

function security(): AttendanceSecurityRemotePort {
  return {
    async acknowledgeEmergencyPublicKeys(version) {
      return {
        acknowledged: true,
        serverTimeEpochMs: 1,
        serverVersion: version,
      };
    },
    async fetchEmergencyPublicKeys() {
      return { kind: "not-modified" };
    },
    async registerAttendanceKey() {
      return {
        kid: "kid",
        registeredAtEpochMs: 1,
        serverTimeEpochMs: 1,
      };
    },
  };
}

function audit(): OperationAuditReadPort {
  return {
    async get() {
      return null;
    },
    async list() {
      return [];
    },
  };
}
