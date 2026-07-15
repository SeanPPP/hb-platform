import assert from "node:assert/strict";
import Module from "node:module";
import { clearSecureAccountSession } from "./secure-session-cleanup";

async function main() {
  const calls: string[] = [];
  await clearSecureAccountSession({
    clearCashierBarcodePrintSession: async () => { calls.push("pending"); },
    removeToken: async () => { calls.push("access"); },
    removeRefreshToken: async () => { calls.push("refresh"); },
    removeUser: async () => { calls.push("user"); },
  });
  assert.equal(calls[0], "pending", "清 token 前必须先删除安全打印记录");
  assert.deepEqual(new Set(calls.slice(1)), new Set(["access", "refresh", "user"]));

  const fallbackCalls: string[] = [];
  const originalWarn = console.warn;
  console.warn = () => undefined;
  const result = await clearSecureAccountSession({
      clearCashierBarcodePrintSession: async () => {
        fallbackCalls.push("pending");
        throw new Error("secure delete failed");
      },
      removeToken: async () => { fallbackCalls.push("access"); },
      removeRefreshToken: async () => { fallbackCalls.push("refresh"); },
      removeUser: async () => { fallbackCalls.push("user"); },
    })
    .finally(() => { console.warn = originalWarn; });
  assert.equal(result.pendingCleared, false);
  assert.deepEqual(new Set(fallbackCalls.slice(1)), new Set(["access", "refresh", "user"]),
    "安全记录删除失败也必须继续清 token；身份校验负责阻止跨账号恢复");

  const secureStoreCalls: string[] = [];
  const filename = require.resolve("expo-secure-store");
  const mockedModule = new Module(filename);
  mockedModule.filename = filename;
  mockedModule.loaded = true;
  mockedModule.exports = {
    getItemAsync: async () => null,
    setItemAsync: async () => undefined,
    deleteItemAsync: async (key: string) => { secureStoreCalls.push(key); },
  };
  require.cache[filename] = mockedModule;
  const { SecureStorage } = await import("./secure");
  await SecureStorage.clearAll();
  assert.equal(secureStoreCalls[0], "hbweb_cashier_barcode_print_pending",
    "真实 SecureStorage.clearAll 接线必须先删除打印凭证");
  assert.deepEqual(new Set(secureStoreCalls.slice(1)), new Set([
    "hbweb_access_token",
    "hbweb_refresh_token",
    "hbweb_user",
  ]));

  console.log("secure-session-cleanup.test.ts: ok");
}

void main();
