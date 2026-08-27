import assert from "node:assert/strict";
import test from "node:test";

import { HbposApiError, type DeviceVerifyResponse } from "../api/hbpos-api";
import {
  DeviceRegistrationResetCoordinator,
  type DeviceRegistrationResetApi,
} from "./device-registration-reset";
import {
  CashierAuthorizationStore,
  DeviceCredentialStore,
  DeviceLockStore,
  DevicePresentationStore,
  DeviceRegistrationResetMarkerStore,
  InMemorySecureStore,
  InstallationIdentityStore,
  PendingDeviceRegistrationStore,
} from "./secure-storage";

const operationId = "10000000-0000-4000-8000-000000000001";
const hardwareId = "20000000-0000-4000-8000-000000000002";
const credentials = {
  deviceCode: "HANDHELD-1042-01",
  storeCode: "1042",
  hardwareId,
  authorizationCode: "device-secret",
};
const resetResponse = {
  operationId,
  deviceCode: credentials.deviceCode,
  storeCode: credentials.storeCode,
  disabledAtUtc: "2026-08-18T02:00:01.000Z",
};

function createHarness(api: DeviceRegistrationResetApi) {
  const secureStore = new InMemorySecureStore();
  const credentialStore = new DeviceCredentialStore(secureStore);
  const presentationStore = new DevicePresentationStore(secureStore);
  const pendingStore = new PendingDeviceRegistrationStore(secureStore);
  const lockStore = new DeviceLockStore(secureStore);
  const markerStore = new DeviceRegistrationResetMarkerStore(secureStore);
  const cashierAuthorization = new CashierAuthorizationStore(secureStore);
  const installation = new InstallationIdentityStore(secureStore, () => hardwareId);
  let invalidations = 0;
  const subject = new DeviceRegistrationResetCoordinator({
    api,
    authenticateOnline: async () => ({
      source: "online" as const,
      session: {
        cashierId: "CASHIER-1",
        userGuid: "USER-1",
        cashierName: "Reviewer",
        storeCode: credentials.storeCode,
        deviceCode: credentials.deviceCode,
        permissionCodes: [
          "Permissions.PosTerminal.Settings.DeviceRegistration",
        ],
        authorizationToken: "fresh-online-ticket",
      },
    }),
    credentials: credentialStore,
    presentation: presentationStore,
    pendingRegistration: pendingStore,
    lock: lockStore,
    marker: markerStore,
    cashierAuthorization,
    installation,
    createOperationId: () => operationId,
    nowIso: () => "2026-08-18T02:00:00.000Z",
    invalidateCurrentCashier: () => {
      invalidations += 1;
    },
  });
  return {
    secureStore,
    credentialStore,
    presentationStore,
    pendingStore,
    lockStore,
    markerStore,
    cashierAuthorization,
    installation,
    subject,
    invalidations: () => invalidations,
  };
}

async function seedRegistered(harness: ReturnType<typeof createHarness>) {
  await harness.installation.getOrCreate();
  await harness.credentialStore.save(credentials);
  await harness.presentationStore.save({
    deviceCode: credentials.deviceCode,
    storeCode: credentials.storeCode,
    storeName: "Sunnybank",
  });
  await harness.pendingStore.save({
    deviceCode: credentials.deviceCode,
    storeCode: credentials.storeCode,
  });
}

function marker(phase: "prepared" | "server-disabled" = "prepared") {
  return {
    version: 1 as const,
    operationId,
    phase,
    deviceCode: credentials.deviceCode,
    storeCode: credentials.storeCode,
    hardwareId,
    createdAtUtc: "2026-08-18T02:00:00.000Z",
  };
}

test("重置响应丢失时保留 prepared marker、旧凭据并立即锁机", async () => {
  const harness = createHarness({
    resetRegistration: async () => {
      throw new HbposApiError("network lost", { kind: "transport" });
    },
    verify: async () => {
      throw new Error("not used");
    },
  });
  await seedRegistered(harness);

  await assert.rejects(() => harness.subject.reset("EMPLOYEE-BARCODE"));

  assert.equal((await harness.markerStore.load())?.phase, "prepared");
  assert.deepEqual(await harness.credentialStore.load(), credentials);
  assert.equal(await harness.lockStore.isLocked(), true);
  assert.equal(harness.invalidations(), 1);
});

test("服务端已停用但本地清理崩溃时保留 server-disabled marker 并锁机", async () => {
  const harness = createHarness({
    resetRegistration: async () => resetResponse,
    verify: async () => {
      throw new Error("not used");
    },
  });
  await seedRegistered(harness);
  harness.credentialStore.clear = async () => {
    throw new Error("credential clear crashed");
  };

  await assert.rejects(
    () => harness.subject.reset("EMPLOYEE-BARCODE"),
    /credential clear crashed/,
  );

  assert.equal((await harness.markerStore.load())?.phase, "server-disabled");
  assert.deepEqual(await harness.credentialStore.load(), credentials);
  assert.equal(await harness.lockStore.isLocked(), true);
});

test("离线启动恢复保持 marker 与旧凭据并进入只读锁定", async () => {
  const harness = createHarness({
    resetRegistration: async () => resetResponse,
    verify: async () => {
      throw new HbposApiError("offline", { kind: "transport" });
    },
  });
  await seedRegistered(harness);
  await harness.markerStore.save(marker("server-disabled"));

  assert.equal(await harness.subject.recover(), "pending");
  assert.notEqual(await harness.markerStore.load(), null);
  assert.deepEqual(await harness.credentialStore.load(), credentials);
  assert.equal(await harness.lockStore.isLocked(), true);
});

test("损坏 marker 读取失败时不触网并锁定当前进程", async () => {
  let verifyCalls = 0;
  const harness = createHarness({
    resetRegistration: async () => resetResponse,
    verify: async () => {
      verifyCalls += 1;
      throw new Error("must not start");
    },
  });
  await seedRegistered(harness);
  await harness.secureStore.set(
    "hbpos.handheld.device-registration-reset.v1",
    "not-json",
    { requireThisDeviceOnly: true },
  );

  assert.equal(await harness.subject.recover(), "pending");
  assert.equal(verifyCalls, 0);
  assert.equal(await harness.lockStore.isLocked(), true);
  assert.equal(harness.invalidations(), 1);
});

test("匿名 verify 精确确认设备已停用后完成本地清理并释放恢复锁", async () => {
  const harness = createHarness({
    resetRegistration: async () => resetResponse,
    verify: async (input) =>
      ({
        deviceCode: input.deviceCode,
        storeCode: input.storeCode,
        deviceStatus: 0,
        isAllowed: false,
        exactIdentityMatched: true,
      }) as DeviceVerifyResponse & { exactIdentityMatched: true },
  });
  await seedRegistered(harness);
  await harness.markerStore.save(marker());
  await harness.lockStore.lockForRecovery("recovery-required");

  assert.equal(await harness.subject.recover(), "completed");
  assert.equal(await harness.credentialStore.load(), null);
  assert.equal(await harness.markerStore.load(), null);
  assert.equal(await harness.lockStore.isLocked(), false);
});

test("设备仍启用时无法证明重置未执行，必须保留 marker 并保持锁定", async () => {
  const harness = createHarness({
    resetRegistration: async () => resetResponse,
    verify: async (input) => ({
      deviceCode: input.deviceCode,
      storeCode: input.storeCode,
      deviceStatus: 1,
      isAllowed: true,
      authorizationCode: "replacement-secret",
    }),
  });
  await seedRegistered(harness);
  await harness.markerStore.save(marker());

  assert.equal(await harness.subject.recover(), "pending");
  assert.notEqual(await harness.markerStore.load(), null);
  assert.deepEqual(await harness.credentialStore.load(), credentials);
  assert.equal(await harness.lockStore.isLocked(), true);
});
