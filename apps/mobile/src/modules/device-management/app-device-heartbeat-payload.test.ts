import assert from "node:assert/strict";
import { buildAppDeviceHeartbeatPayload } from "./app-device-heartbeat-payload";

const deviceSessionPayload = buildAppDeviceHeartbeatPayload({
  installationId: "install-1",
  platformOS: "android",
  session: {
    hardwareId: "hbmobile-device-1",
    authCode: "AUTH",
    storeCode: "1004",
    storeName: "Sunnybank",
    systemDeviceNumber: "SYS-001",
    status: 1,
    statusDescription: "启用",
  },
  updateInfo: {
    appVersion: "1.0.1",
    runtimeVersion: "1.0.1",
    channel: "preview",
    updateId: "11111111-1111-1111-8111-111111111111",
    isEmbeddedLaunch: false,
  },
  applicationInfo: {
    nativeApplicationVersion: "1.0.2",
    nativeBuildVersion: "12",
  },
});

assert.equal(deviceSessionPayload.hardwareId, "hbmobile-device-1");
assert.equal(deviceSessionPayload.systemDeviceNumber, "SYS-001");
assert.equal(deviceSessionPayload.storeCode, "1004");
assert.equal(deviceSessionPayload.deviceSystem, "Android");
assert.equal(deviceSessionPayload.platform, "android");
assert.equal(deviceSessionPayload.appVersion, "1.0.2");
assert.equal(deviceSessionPayload.appBuildVersion, "12");
assert.equal(deviceSessionPayload.runtimeVersion, "1.0.1");
assert.equal(deviceSessionPayload.channel, "preview");
assert.equal(deviceSessionPayload.updateSource, "ota");
assert.equal(
  Object.prototype.hasOwnProperty.call(deviceSessionPayload, "lastSeenUsername"),
  false,
  "心跳 payload 不应携带用户身份字段"
);

const accountSessionPayload = buildAppDeviceHeartbeatPayload({
  installationId: "install-2",
  platformOS: "ios",
  session: null,
  updateInfo: {
    appVersion: "1.0.0",
    isEmbeddedLaunch: true,
  },
  applicationInfo: null,
});

assert.equal(accountSessionPayload.hardwareId, "install-2");
assert.equal(accountSessionPayload.deviceSystem, "iOS");
assert.equal(accountSessionPayload.platform, "ios");
assert.equal(accountSessionPayload.appVersion, "1.0.0");
assert.equal(accountSessionPayload.updateSource, "embedded");
