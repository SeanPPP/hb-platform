import assert from "node:assert/strict";
import test from "node:test";
import {
  INITIAL_PRODUCT_QUERY_CONNECTIVITY,
  OFFLINE_AUTO_RELOOKUP_MIN_INTERVAL_MS,
  reduceProductQueryConnectivity,
  shouldAutoRelookupAfterRecovery,
} from "./offline-mode";

test("网络失败进入离线并记录时刻与关键字", () => {
  const next = reduceProductQueryConnectivity(INITIAL_PRODUCT_QUERY_CONNECTIVITY, {
    type: "network_failure",
    atMs: 1_000,
    keyword: " 123 ",
  });
  assert.deepEqual(next, { offline: true, sinceMs: 1_000, pendingKeyword: "123" });
});

test("已离线时再次失败不重置进入时刻", () => {
  const offline = reduceProductQueryConnectivity(INITIAL_PRODUCT_QUERY_CONNECTIVITY, {
    type: "network_failure",
    atMs: 1_000,
  });
  const again = reduceProductQueryConnectivity(offline, {
    type: "network_failure",
    atMs: 5_000,
    keyword: "456",
  });
  assert.equal(again.sinceMs, 1_000);
  assert.equal(again.pendingKeyword, "456");
});

test("早于离线时刻的探测结果不能退出离线", () => {
  const offline = reduceProductQueryConnectivity(INITIAL_PRODUCT_QUERY_CONNECTIVITY, {
    type: "network_failure",
    atMs: 1_000,
  });
  const stale = reduceProductQueryConnectivity(offline, {
    type: "backend_check",
    reachable: true,
    checkedAtMs: 900,
  });
  assert.equal(stale.offline, true);
  const unreachable = reduceProductQueryConnectivity(offline, {
    type: "backend_check",
    reachable: false,
    checkedAtMs: 2_000,
  });
  assert.equal(unreachable.offline, true);
});

test("晚于离线时刻的可达探测退出离线并保留待重跑关键字", () => {
  const offline = reduceProductQueryConnectivity(INITIAL_PRODUCT_QUERY_CONNECTIVITY, {
    type: "network_failure",
    atMs: 1_000,
    keyword: "789",
  });
  const withLookup = reduceProductQueryConnectivity(offline, {
    type: "offline_lookup",
    keyword: "999",
  });
  const online = reduceProductQueryConnectivity(withLookup, {
    type: "backend_check",
    reachable: true,
    checkedAtMs: 1_001,
  });
  assert.deepEqual(online, { offline: false, sinceMs: null, pendingKeyword: "999" });
  const consumed = reduceProductQueryConnectivity(online, { type: "reset" });
  assert.deepEqual(consumed, INITIAL_PRODUCT_QUERY_CONNECTIVITY);
});

test("在线请求成功立即回到初始状态", () => {
  const offline = reduceProductQueryConnectivity(INITIAL_PRODUCT_QUERY_CONNECTIVITY, {
    type: "network_failure",
    atMs: 1_000,
  });
  assert.deepEqual(
    reduceProductQueryConnectivity(offline, { type: "request_succeeded" }),
    INITIAL_PRODUCT_QUERY_CONNECTIVITY,
  );
  // 在线态下重复成功不产生新对象，避免无谓渲染。
  assert.equal(
    reduceProductQueryConnectivity(INITIAL_PRODUCT_QUERY_CONNECTIVITY, { type: "request_succeeded" }),
    INITIAL_PRODUCT_QUERY_CONNECTIVITY,
  );
});

test("首次恢复在线允许自动重跑查询", () => {
  assert.equal(
    shouldAutoRelookupAfterRecovery({ lastAutoRelookupAtMs: null, nowMs: 10_000 }),
    true,
  );
});

test("刚自动重跑过就再次恢复在线时不再重跑，掐断空转", () => {
  // 探测与业务请求若解析出不同后端，会「判定恢复→重跑失败→再判定恢复」地循环；
  // 实测曾达到每 400ms 一轮，这里保证第二轮就被挡下。
  const lastAutoRelookupAtMs = 10_000;
  assert.equal(
    shouldAutoRelookupAfterRecovery({ lastAutoRelookupAtMs, nowMs: lastAutoRelookupAtMs + 400 }),
    false,
  );
  assert.equal(
    shouldAutoRelookupAfterRecovery({
      lastAutoRelookupAtMs,
      nowMs: lastAutoRelookupAtMs + OFFLINE_AUTO_RELOOKUP_MIN_INTERVAL_MS - 1,
    }),
    false,
  );
});

test("间隔足够久后恢复自动重跑，真实断网恢复不受影响", () => {
  const lastAutoRelookupAtMs = 10_000;
  assert.equal(
    shouldAutoRelookupAfterRecovery({
      lastAutoRelookupAtMs,
      nowMs: lastAutoRelookupAtMs + OFFLINE_AUTO_RELOOKUP_MIN_INTERVAL_MS,
    }),
    true,
  );
});
