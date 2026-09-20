/**
 * 菜单缓存作用域解析的源码契约。
 *
 * 真机复现过的缺陷：设备绑定账号登录会在 sessionKind / user 落到内存之前就把菜单拉完，
 * 那一刻按内存态算不出 scopeKey，于是既读不到也写不进缓存；而纯设备模式冷启动还会先
 * 经过 performLocalSessionClear，把上一次的缓存清掉。结果离线冷启动读不到菜单，
 * 商品查询 tab 消失。这里锁住三条修复约束。
 */
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const moduleDir = dirname(fileURLToPath(import.meta.url));

async function readMobileSource(relativePath: string) {
  return readFile(resolve(moduleDir, "../../..", relativePath), "utf8");
}

async function run() {
  const [authStore, navigationStore] = await Promise.all([
    readMobileSource("src/store/auth-store.ts"),
    readMobileSource("src/modules/navigation/store.ts"),
  ]);

  // 1) scopeKey 必须能在内存态未落定时回退到落盘态，否则读写两侧都拿不到 key。
  assert.match(
    navigationStore,
    /async function resolvePersistedNavigationMenuScopeKey/,
    "必须提供基于落盘态的 scopeKey 解析",
  );
  const resolverStart = navigationStore.indexOf("async function resolveNavigationMenuScopeKey");
  assert.notEqual(resolverStart, -1, "必须存在 scopeKey 解析入口");
  const resolverSource = navigationStore.slice(
    resolverStart,
    navigationStore.indexOf("const SETTINGS_ONLY_MENU", resolverStart),
  );
  assert.match(
    resolverSource,
    /if \(fromMemory\) \{[\s\S]*?return fromMemory;[\s\S]*?\}[\s\S]*?resolvePersistedNavigationMenuScopeKey\(\)/,
    "内存态算不出 key 时必须回退到落盘态",
  );
  // 落盘解析必须读会话标记与设备会话，而不是再去读内存 store。
  assert.match(navigationStore, /getAuthSessionMarker/, "落盘解析必须读取会话标记");
  assert.match(navigationStore, /DeviceStorage\.getSession\(\)/, "落盘解析必须读取设备会话");
  assert.match(
    navigationStore,
    /if \(marker === "account"\) \{[\s\S]*?return null;/,
    "普通账号会话不得参与菜单缓存",
  );

  // 2) reset 支持保留缓存；账号侧清理必须保留，隔离交给 scopeKey。
  assert.match(navigationStore, /reset\(options = \{\}\)/, "reset 必须接受选项");
  assert.match(
    navigationStore,
    /if \(!options\.keepMenuCache\) \{[\s\S]*?asyncStorageNavigationMenuCache\.clear\(\)/,
    "只有未要求保留时才清缓存",
  );
  const clearStart = authStore.indexOf("async performLocalSessionClear()");
  assert.notEqual(clearStart, -1, "必须存在 performLocalSessionClear");
  const clearSource = authStore.slice(clearStart, authStore.indexOf("async clearAccountSessionForDeviceLogin", clearStart));
  assert.match(
    clearSource,
    /reset\(\{ keepMenuCache: true \}\)/,
    "账号会话清理必须保留菜单缓存，否则纯设备模式冷启动读不到",
  );

  // 3) 根因既已在 scopeKey 解析处修好，就不应再有分散的补写点。
  assert.doesNotMatch(
    authStore,
    /persistMenuCache\(\)/,
    "不得用分散的补写点绕过 scopeKey 解析顺序问题",
  );

  console.log("menu-cache-source.test.ts: ok");
}

run().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
