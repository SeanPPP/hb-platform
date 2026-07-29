import assert from "node:assert/strict";
import test from "node:test";

import { resolveLocalDeviceState } from "./local-device-state";

const credentials = {
  deviceCode: "POS-1",
  storeCode: "S1",
  hardwareId: "INSTALL-1",
  authorizationCode: "device-secret",
};

test("离线授权只接受与当前安装 UUID 完全绑定的完整设备凭据", () => {
  assert.equal(
    resolveLocalDeviceState({
      locked: false,
      installationId: "INSTALL-1",
      credentials,
      pending: null,
    }),
    "authorized-local",
  );
  assert.equal(
    resolveLocalDeviceState({
      locked: false,
      installationId: "INSTALL-2",
      credentials,
      pending: null,
    }),
    "registration-required",
  );
  assert.equal(
    resolveLocalDeviceState({
      locked: false,
      installationId: "INSTALL-1",
      credentials: { ...credentials, authorizationCode: "" },
      pending: null,
    }),
    "registration-required",
  );
});

test("设备锁优先于凭据，合法待审批记录只进入 pending 而不能离线收银", () => {
  assert.equal(
    resolveLocalDeviceState({
      locked: true,
      installationId: "INSTALL-1",
      credentials,
      pending: null,
    }),
    "locked",
  );
  assert.equal(
    resolveLocalDeviceState({
      locked: false,
      installationId: "INSTALL-1",
      credentials: null,
      pending: { deviceCode: "POS-PENDING", storeCode: "S1" },
    }),
    "pending-approval",
  );
});
