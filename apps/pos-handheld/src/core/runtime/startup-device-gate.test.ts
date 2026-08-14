import assert from "node:assert/strict";
import test from "node:test";

import { HbposApiError } from "../api/hbpos-api";

import { resolveStartupDeviceGate } from "./startup-device-gate";

test("在线已授权设备必须以 verify 结果进入 authorized-online", async () => {
  let verifies = 0;
  const result = await resolveStartupDeviceGate({
    internetReachable: true,
    async verifyCurrentDevice() {
      verifies += 1;
      return { status: "authorized", deviceCode: "IPAD1", storeCode: "S1" };
    },
    readLocalDevice: async () => "authorized-local",
    async lockDevice() {},
  });

  assert.equal(verifies, 1);
  assert.deepEqual(result, {
    backend: "reachable",
    device: "authorized-online",
  });
});

test("待审批和业务禁用使用在线明确状态，不回退旧本地授权", async () => {
  const pending = await resolveStartupDeviceGate({
    internetReachable: true,
    verifyCurrentDevice: async () => ({ status: "pending-approval" }),
    readLocalDevice: async () => "authorized-local",
    async lockDevice() {},
  });
  const disabled = await resolveStartupDeviceGate({
    internetReachable: true,
    verifyCurrentDevice: async () => ({ status: "disabled" }),
    readLocalDevice: async () => "authorized-local",
    async lockDevice() {},
  });

  assert.deepEqual(pending, {
    backend: "reachable",
    device: "pending-approval",
  });
  assert.deepEqual(disabled, { backend: "rejected", device: "locked" });
});

test("已知离线和传输/5xx 失败才允许使用本地授权现金收银", async () => {
  let offlineVerifyCalls = 0;
  const knownOffline = await resolveStartupDeviceGate({
    internetReachable: false,
    async verifyCurrentDevice() {
      offlineVerifyCalls += 1;
      return { status: "authorized" };
    },
    readLocalDevice: async () => "authorized-local",
    async lockDevice() {},
  });
  const transportFailure = await resolveStartupDeviceGate({
    internetReachable: true,
    async verifyCurrentDevice() {
      throw new HbposApiError("network", { kind: "transport" });
    },
    readLocalDevice: async () => "authorized-local",
    async lockDevice() {},
  });
  const serverFailure = await resolveStartupDeviceGate({
    internetReachable: true,
    async verifyCurrentDevice() {
      throw new HbposApiError("server", { kind: "http", status: 503 });
    },
    readLocalDevice: async () => "authorized-local",
    async lockDevice() {},
  });

  assert.equal(offlineVerifyCalls, 0);
  assert.deepEqual(knownOffline, {
    backend: "offline",
    device: "authorized-local",
  });
  assert.deepEqual(transportFailure, knownOffline);
  assert.deepEqual(serverFailure, knownOffline);
});

test("在线 403 明确拒绝锁机，旧本地凭据不得兜底", async () => {
  const locks: string[] = [];
  const result = await resolveStartupDeviceGate({
    internetReachable: true,
    async verifyCurrentDevice() {
      throw new HbposApiError("device rejected", {
        kind: "http",
        status: 403,
      });
    },
    readLocalDevice: async () => "authorized-local",
    async lockDevice(reason) {
      locks.push(reason);
    },
  });

  assert.deepEqual(result, { backend: "rejected", device: "locked" });
  assert.deepEqual(locks, ["device rejected"]);
});

test("envelope、401、429、其他 4xx 和编程错误保持不可交易，不得伪装成离线", async () => {
  const failures: unknown[] = [
    new HbposApiError("business rejected", {
      kind: "envelope",
      code: "DEVICE_VERIFY_REJECTED",
    }),
    new HbposApiError("cashier authorization expired", {
      kind: "http",
      status: 401,
    }),
    new HbposApiError("bad request", { kind: "http", status: 400 }),
    new HbposApiError("rate limited", { kind: "http", status: 429 }),
    new Error("unexpected parser failure"),
  ];

  for (const failure of failures) {
    let localReads = 0;
    let locks = 0;
    await assert.rejects(
      resolveStartupDeviceGate({
        internetReachable: true,
        async verifyCurrentDevice() {
          throw failure;
        },
        async readLocalDevice() {
          localReads += 1;
          return "authorized-local";
        },
        async lockDevice() {
          locks += 1;
        },
      }),
      (error) => error === failure,
    );
    assert.equal(localReads, 0);
    assert.equal(locks, 0);
  }
});
