/**
 * 离线目录 store 的源码契约。
 *
 * store 依赖 zustand 运行时与 expo-sqlite，Node 下无法实例化，因此用源码断言
 * 锁住一条曾经出过真实缺陷的不变量：刷新失败的退避记账不得依赖协调器状态。
 *
 * 缺陷现场：失败记账写成 `if (get().refresh.kind === "failed")` 时，切店抢占等
 * 情况会把 refresh.kind 覆盖成别的值，于是 lastFailedAtMs 漏记、
 * shouldAutoRefreshOfflineCatalog 的 10 分钟退避失效，焦点副作用在同一帧把下载
 * 重新拉起来 —— 实测断网进入商品查询页时 9 秒内打了 71 次请求。
 */
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const currentDir = dirname(fileURLToPath(import.meta.url));
const source = readFileSync(resolve(currentDir, "./offline-catalog-store.ts"), "utf8");

test("刷新失败的退避记账不得以协调器状态为条件", () => {
  const guarded = /if\s*\(\s*get\(\)\.refresh\.kind\s*===\s*"failed"\s*\)\s*\{\s*set\(/;
  assert.doesNotMatch(
    source,
    guarded,
    "失败记账一旦以 refresh.kind 为前提，切店抢占就会让退避失效并触发紧密重试",
  );
});

test("非取消失败必须无条件写入 lastFailedAtMs", () => {
  // 记账必须紧邻失败日志，且不被任何条件包裹。
  assert.match(
    source,
    /set\(\(state\) => \(\{\s*lastFailedAtMs: \{ \.\.\.state\.lastFailedAtMs, \[normalized\]: Date\.now\(\) \},\s*\}\)\);\s*console\.warn\("\[offline-catalog\] refresh failed"/,
    "refresh failed 日志之前必须无条件记录失败时刻",
  );
});

test("用户主动取消仍走取消记账并提前返回", () => {
  // 取消不能被当作失败记账，否则「取消」会顺带压制 10 分钟的自动刷新。
  assert.match(
    source,
    /if \(isOfflineCatalogCancellation\(error\)\) \{[\s\S]*?lastCancelledAtMs: \{ \.\.\.state\.lastCancelledAtMs, \[normalized\]: Date\.now\(\) \},[\s\S]*?return null;\s*\}/,
    "取消分支必须记 lastCancelledAtMs 并提前返回",
  );
});

console.log("offline-catalog-store-source.test.ts: ok");
