import assert from "node:assert/strict";
import test from "node:test";
import {
  INITIAL_PRODUCT_QUERY_CONNECTIVITY,
  reduceProductQueryConnectivity,
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
