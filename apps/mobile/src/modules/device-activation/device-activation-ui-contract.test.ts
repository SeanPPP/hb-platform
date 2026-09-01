import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import test from "node:test";

const mobileRoot = path.resolve(__dirname, "../../..");

function read(relativePath: string) {
  return readFileSync(path.join(mobileRoot, relativePath), "utf8");
}

test("共享开通弹窗限定 QR，扫码只预览并要求显式确认", () => {
  const source = read("src/modules/device-activation/DeviceActivationDialog.tsx");

  assert.match(source, /barcodeTypes:\s*\["qr"\]/);
  assert.match(source, /parseDeviceActivationCode/);
  assert.match(source, /previewMobileDeviceActivation/);
  assert.match(source, /handleConfirm/);
  assert.match(source, /recoverStoredMobileDeviceActivation/);
  assert.match(source, /recoveryRequired/);
  assert.match(source, /activation\.retryRecovery/);
  assert.match(source, /onDismissRef\.current\("completed"\)/);
  assert.doesNotMatch(source, /console\.(?:log|info|warn|error)\([^\n]*activationCode/);
});

test("登录页提供开通、旧设备升级与设备账号登录入口", () => {
  const source = read("app/(auth)/login.tsx");

  assert.match(source, /DeviceActivationDialog/);
  assert.match(source, /loginDeviceAccount/);
  assert.match(source, /activation\.open/);
  assert.match(source, /activation\.upgrade/);
  assert.match(source, /activation\.rebind/);
  assert.match(source, /openActivation\(accountBinding \? "rebind" : "redeem"\)/);
});

test("设置页移除旧选店注册和先解绑重绑，复用原子扫码流程", () => {
  const source = read("app/(shell)/settings.tsx");

  assert.match(source, /DeviceActivationDialog/);
  assert.doesNotMatch(source, /handleRegisterDevice/);
  assert.doesNotMatch(source, /handleDeviceUnbind\("rebind"\)/);
  assert.doesNotMatch(source, /storePickerWrap/);
  assert.match(source, /device\.rebindByScan/);
  assert.match(source, /device\.unbind/);
});

test("401 恢复按会话类型分流，设备账号不尝试 refresh token", () => {
  const source = read("src/shared/api/client.ts");

  assert.match(source, /getAuthSessionMarker/);
  assert.match(source, /sessionKind\s*===\s*"deviceAccount"/);
  assert.match(source, /exchangeStoredDeviceAccountToken/);
  assert.match(source, /getRefreshToken/);
});
