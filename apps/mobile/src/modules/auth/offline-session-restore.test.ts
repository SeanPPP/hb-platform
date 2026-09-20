import assert from "node:assert/strict";
import test from "node:test";
import { resolveOfflineSessionRestore } from "./offline-session-restore";

const networkError = new TypeError("Network request failed");
const cachedUser = { userGUID: "u1", username: "store" };

test("设备绑定账号 + 设备会话 + 缓存用户 + 网络错误 → 用缓存恢复", () => {
  assert.equal(
    resolveOfflineSessionRestore({
      error: networkError,
      sessionKind: "deviceAccount",
      hasStoredDeviceSession: true,
      cachedUser,
    }),
    "restore-from-cache",
  );
});

test("普通账号会话断网仍然清理会话", () => {
  assert.equal(
    resolveOfflineSessionRestore({
      error: networkError,
      sessionKind: "account",
      hasStoredDeviceSession: true,
      cachedUser,
    }),
    "clear",
  );
});

test("缺少设备会话、缓存用户或非网络错误一律清理", () => {
  assert.equal(
    resolveOfflineSessionRestore({
      error: networkError,
      sessionKind: "deviceAccount",
      hasStoredDeviceSession: false,
      cachedUser,
    }),
    "clear",
  );
  assert.equal(
    resolveOfflineSessionRestore({
      error: networkError,
      sessionKind: "deviceAccount",
      hasStoredDeviceSession: true,
      cachedUser: null,
    }),
    "clear",
  );
  assert.equal(
    resolveOfflineSessionRestore({
      error: new Error("DEVICE_ACCOUNT_BINDING_NOT_FOUND"),
      sessionKind: "deviceAccount",
      hasStoredDeviceSession: true,
      cachedUser,
    }),
    "clear",
  );
});
