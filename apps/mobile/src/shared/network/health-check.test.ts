/**
 * 通用后端可达性检查（health-check.ts）单元测试。
 * 通过注入 fetchImpl / isReviewActive / getApiBaseUrl 隔离外部依赖。
 */
import { test } from "node:test";
import assert from "node:assert/strict";

import {
  buildHealthUrl,
  checkBackendReachable,
  NETWORK_CHECK_TIMEOUT_MS,
} from "./health-check";

test("buildHealthUrl 去除 /api 尾部并拼接 /health", () => {
  assert.equal(buildHealthUrl("https://hotbargain.vip/api"), "https://hotbargain.vip/health");
  assert.equal(buildHealthUrl("http://192.168.31.247:5002/api"), "http://192.168.31.247:5002/health");
  assert.equal(buildHealthUrl("http://host:5002"), "http://host:5002/health");
});

test("后端返回 2xx 时 checkBackendReachable 返回 ok=true", async () => {
  const result = await checkBackendReachable({
    isReviewActive: () => false,
    getApiBaseUrl: async () => "https://host.test/api",
    fetchImpl: (async () => new Response("ok", { status: 200 })) as typeof fetch,
    nowIso: () => "2026-01-01T00:00:00.000Z",
  });
  assert.deepEqual(result, { ok: true, checkedAtIso: "2026-01-01T00:00:00.000Z" });
});

test("后端返回 5xx 时视为不可达 ok=false", async () => {
  const result = await checkBackendReachable({
    isReviewActive: () => false,
    getApiBaseUrl: async () => "https://host.test/api",
    fetchImpl: (async () => new Response("boom", { status: 503 })) as typeof fetch,
  });
  assert.equal(result.ok, false);
});

test("fetch 抛错（网络不可达）时 ok=false 且不向上抛", async () => {
  const result = await checkBackendReachable({
    isReviewActive: () => false,
    getApiBaseUrl: async () => "https://host.test/api",
    fetchImpl: (async () => {
      throw new TypeError("Network request failed");
    }) as typeof fetch,
  });
  assert.equal(result.ok, false);
});

test("探测超时后 ok=false（AbortController 生效）", async () => {
  // fetch 挂起直到 signal abort：确认超时中止路径生效。
  const result = await checkBackendReachable({
    isReviewActive: () => false,
    getApiBaseUrl: async () => "https://host.test/api",
    timeoutMs: 10,
    fetchImpl: (async (_input, init?: RequestInit) => {
      await new Promise<void>((resolve) => {
        init?.signal?.addEventListener("abort", () => resolve());
      });
      throw new DOMException("Aborted", "AbortError");
    }) as typeof fetch,
  });
  assert.equal(result.ok, false);
  assert.equal(NETWORK_CHECK_TIMEOUT_MS, 5000);
});

test("iOS 审核态不触网，直接 ok=true", async () => {
  let called = false;
  const result = await checkBackendReachable({
    isReviewActive: () => true,
    getApiBaseUrl: async () => "https://host.test/api",
    fetchImpl: (async () => {
      called = true;
      return new Response("ok", { status: 200 });
    }) as typeof fetch,
  });
  assert.equal(result.ok, true);
  assert.equal(called, false, "审核态不应发起真实网络请求");
});

test("地址来源抛错时按不可达处理", async () => {
  const result = await checkBackendReachable({
    isReviewActive: () => false,
    getApiBaseUrl: async () => {
      throw new Error("storage failure");
    },
    fetchImpl: (async () => new Response("ok", { status: 200 })) as typeof fetch,
  });
  assert.equal(result.ok, false);
});
