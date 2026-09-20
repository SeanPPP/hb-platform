import assert from "node:assert/strict";
import test from "node:test";
import {
  OFFLINE_CATALOG_AUTO_REFRESH_INTERVAL_MS,
  OFFLINE_CATALOG_CANCEL_BACKOFF_MS,
  OFFLINE_CATALOG_FAILURE_BACKOFF_MS,
  shouldAutoRefreshOfflineCatalog,
} from "./offline-catalog-freshness";
import type { ActiveOfflineCatalogMetadata } from "./types";

const NOW = Date.parse("2026-09-17T10:00:00.000Z");

function meta(activatedAt: string): ActiveOfflineCatalogMetadata {
  return {
    snapshotId: "snap-1",
    storeCode: "S001",
    catalogVersion: "catalog-v1:abc",
    itemCount: 10,
    generatedAt: activatedAt,
    activatedAt,
  };
}

test("无快照且在线时立即刷新", () => {
  assert.equal(
    shouldAutoRefreshOfflineCatalog({
      activeMeta: null,
      isOnline: true,
      isRefreshing: false,
      lastFailedAtMs: null,
      lastRefreshedAtMs: null,
      nowMs: NOW,
    }),
    true,
  );
});

test("离线或正在刷新时不刷新", () => {
  const base = { activeMeta: null, lastFailedAtMs: null, lastRefreshedAtMs: null, nowMs: NOW };
  assert.equal(shouldAutoRefreshOfflineCatalog({ ...base, isOnline: false, isRefreshing: false }), false);
  assert.equal(shouldAutoRefreshOfflineCatalog({ ...base, isOnline: true, isRefreshing: true }), false);
});

test("失败后十分钟内不重试，超过后可重试", () => {
  const base = { activeMeta: null, isOnline: true, isRefreshing: false, lastRefreshedAtMs: null, nowMs: NOW };
  assert.equal(
    shouldAutoRefreshOfflineCatalog({ ...base, lastFailedAtMs: NOW - OFFLINE_CATALOG_FAILURE_BACKOFF_MS + 1 }),
    false,
  );
  assert.equal(
    shouldAutoRefreshOfflineCatalog({ ...base, lastFailedAtMs: NOW - OFFLINE_CATALOG_FAILURE_BACKOFF_MS }),
    true,
  );
});

test("有快照时按激活时间或本进程最近检查时间判断是否过期", () => {
  const fresh = meta(new Date(NOW - 60_000).toISOString());
  const stale = meta(new Date(NOW - OFFLINE_CATALOG_AUTO_REFRESH_INTERVAL_MS).toISOString());
  const base = { isOnline: true, isRefreshing: false, lastFailedAtMs: null, nowMs: NOW };
  assert.equal(shouldAutoRefreshOfflineCatalog({ ...base, activeMeta: fresh, lastRefreshedAtMs: null }), false);
  assert.equal(shouldAutoRefreshOfflineCatalog({ ...base, activeMeta: stale, lastRefreshedAtMs: null }), true);
  // 本进程刚检查过 noChange，即使快照激活很久也不重复检查。
  assert.equal(
    shouldAutoRefreshOfflineCatalog({ ...base, activeMeta: stale, lastRefreshedAtMs: NOW - 1_000 }),
    false,
  );
});

test("用户取消后在抑制期内不得自动重启下载", () => {
  const base = {
    activeMeta: null,
    isOnline: true,
    isRefreshing: false,
    lastFailedAtMs: null,
    lastRefreshedAtMs: null,
    nowMs: NOW,
  };
  // 取消发生的同一帧：焦点副作用会因 refresh.kind 变化重跑，这里必须拦住。
  assert.equal(shouldAutoRefreshOfflineCatalog({ ...base, lastCancelledAtMs: NOW }), false);
  assert.equal(
    shouldAutoRefreshOfflineCatalog({
      ...base,
      lastCancelledAtMs: NOW - OFFLINE_CATALOG_CANCEL_BACKOFF_MS + 1,
    }),
    false,
  );
  assert.equal(
    shouldAutoRefreshOfflineCatalog({
      ...base,
      lastCancelledAtMs: NOW - OFFLINE_CATALOG_CANCEL_BACKOFF_MS,
    }),
    true,
  );
  assert.equal(shouldAutoRefreshOfflineCatalog({ ...base, lastCancelledAtMs: null }), true);
});

test("关闭自动更新后即使没有快照也不自动下载", () => {
  const base = { activeMeta: null, isOnline: true, isRefreshing: false, lastFailedAtMs: null, lastRefreshedAtMs: null, nowMs: NOW };
  assert.equal(shouldAutoRefreshOfflineCatalog({ ...base, autoRefreshEnabled: false }), false);
  assert.equal(shouldAutoRefreshOfflineCatalog({ ...base, autoRefreshEnabled: true }), true);
  assert.equal(shouldAutoRefreshOfflineCatalog(base), true, "未提供开关视为开启");
});
