import assert from "node:assert/strict";
import { QueryClient, QueryObserver } from "@tanstack/react-query";
import * as reportRefresh from "./report-refresh";
import {
  ReportLoadPerformanceTimer,
  discardReportNavigationStart,
  hasPendingReportNavigationStart,
  markReportHubNavigationStart,
} from "./report-load-performance";

const { createReportRefreshController, getReportRefreshQueryOptions } = reportRefresh;
const reportRefetchOptions = (
  reportRefresh as typeof reportRefresh & {
    REPORT_REFETCH_OPTIONS?: { readonly cancelRefetch: boolean };
  }
).REPORT_REFETCH_OPTIONS;

assert.deepEqual(reportRefetchOptions, { cancelRefetch: false });

const revenueOptions = getReportRefreshQueryOptions("revenue");
assert.deepEqual(revenueOptions.queryKey, ["reports"]);
assert.equal(revenueOptions.type, "active");
assert.equal(revenueOptions.predicate({ queryKey: ["reports", "statistics-freshness"] }), false);
assert.equal(revenueOptions.predicate({ queryKey: ["reports", "revenue-summary"] }), true);

const productOptions = getReportRefreshQueryOptions("product");
assert.deepEqual(productOptions.queryKey, ["product-report"]);
assert.equal(productOptions.type, "active");
assert.equal(productOptions.predicate({ queryKey: ["product-report", "products"] }), true);
assert.equal(productOptions.predicate({ queryKey: ["reports", "revenue-summary"] }), false);

async function run() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  const queryKey = ["reports", "revenue-summary"] as const;
  let requestCount = 0;
  let releaseRequest: (() => void) | undefined;
  const queryFn = async () => {
    requestCount += 1;
    await new Promise<void>((resolve) => {
      releaseRequest = resolve;
    });
    return requestCount;
  };

  const initialFetch = queryClient.fetchQuery({ queryKey, queryFn });
  assert.equal(requestCount, 1);
  releaseRequest?.();
  await initialFetch;

  const observer = new QueryObserver(queryClient, { queryKey, queryFn, staleTime: Infinity });
  const unsubscribe = observer.subscribe(() => undefined);
  const activeRefresh = observer.refetch();
  assert.equal(requestCount, 2);
  const overlappingRefresh = queryClient.refetchQueries(
    getReportRefreshQueryOptions("revenue"),
    reportRefetchOptions,
  );
  await Promise.resolve();
  assert.equal(requestCount, 2);
  releaseRequest?.();
  await Promise.all([activeRefresh, overlappingRefresh]);
  unsubscribe();
  queryClient.clear();

  discardReportNavigationStart("revenue");
  discardReportNavigationStart("product");
  let focusNow = 100;
  const focusTimer = new ReportLoadPerformanceTimer(() => focusNow);
  const focusQueryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  const focusQueryKey = ["reports", "revenue-summary", "focus-race"] as const;
  let focusRequestCount = 0;
  let releaseFocusRequest: (() => void) | undefined;
  const focusRequestPending = new Promise<void>((resolve) => {
    releaseFocusRequest = resolve;
  });
  const focusQueryFn = async () => {
    focusRequestCount += 1;
    focusTimer.start("cold", "revenue");
    await focusRequestPending;
    focusNow = 300;
    focusTimer.markDataNormalized();
    return { rows: [{ id: "S01" }] };
  };
  const focusObserver = new QueryObserver(focusQueryClient, {
    queryKey: focusQueryKey,
    queryFn: focusQueryFn,
    staleTime: Infinity,
  });
  const unsubscribeFocus = focusObserver.subscribe(() => undefined);
  await Promise.resolve();
  assert.equal(focusRequestCount, 1, "进入 Reports 前必须已有一个真实在途请求");

  focusNow = 200;
  markReportHubNavigationStart(focusNow);
  const focusRefresh = focusQueryClient.refetchQueries(
    getReportRefreshQueryOptions("revenue"),
    reportRefetchOptions,
  );
  await Promise.resolve();
  assert.equal(
    focusRequestCount,
    1,
    "cancelRefetch=false 必须复用旧请求；该场景不能假设会启动新 queryFn",
  );
  releaseFocusRequest?.();
  await focusRefresh;
  focusNow = 350;

  assert.deepEqual(focusTimer.markFirstRowVisible(), {
    cacheState: "cold",
    navigationMs: 0,
    requestMs: 100,
    normalizeRenderMs: 50,
    totalMs: 150,
    budgetMs: 2_000,
    meetsFirstDataBudget: true,
  }, "本次焦点会话必须从 grouped marker 起点计时，不能沿用旧请求起点");
  assert.equal(
    hasPendingReportNavigationStart("revenue", focusNow),
    false,
    "在途请求完成时必须认领本次 grouped marker",
  );
  assert.equal(
    hasPendingReportNavigationStart("product", focusNow),
    false,
    "活动页签认领后必须整组清除未激活候选 marker",
  );
  unsubscribeFocus();
  focusQueryClient.clear();

  const calls: string[] = [];
  let release: (() => void) | undefined;
  const pending = new Promise<void>((resolve) => {
    release = resolve;
  });
  const controller = createReportRefreshController(
    async (tab) => {
      calls.push(`report:${tab}`);
      await pending;
    },
    async () => {
      calls.push("freshness");
    },
    (refreshing) => calls.push(`loading:${refreshing}`),
  );

  const firstRefresh = controller.refresh("revenue");
  assert.equal(controller.isRefreshing(), true);
  await controller.refresh("revenue");
  assert.deepEqual(calls, ["loading:true", "report:revenue", "freshness"]);
  release?.();
  await firstRefresh;
  assert.equal(controller.isRefreshing(), false);
  assert.deepEqual(calls, ["loading:true", "report:revenue", "freshness", "loading:false"]);

  const failureStates: boolean[] = [];
  const failingController = createReportRefreshController(
    async () => { throw new Error("report failed"); },
    async () => undefined,
    (refreshing) => failureStates.push(refreshing),
  );
  await assert.rejects(failingController.refresh("product"), /report failed/);
  assert.equal(failingController.isRefreshing(), false);
  assert.deepEqual(failureStates, [true, false]);

  const disposedCalls: string[] = [];
  let releaseDisposed: (() => void) | undefined;
  const disposedPending = new Promise<void>((resolve) => {
    releaseDisposed = resolve;
  });
  const disposedController = createReportRefreshController(
    async () => {
      disposedCalls.push("report");
      await disposedPending;
    },
    async () => { disposedCalls.push("freshness"); },
    (refreshing) => disposedCalls.push(`loading:${refreshing}`),
  );
  const disposedRefresh = disposedController.refresh("revenue");
  disposedController.dispose();
  releaseDisposed?.();
  await disposedRefresh;
  assert.deepEqual(disposedCalls, ["loading:true", "report", "freshness"]);
  await disposedController.refresh("product");
  assert.deepEqual(disposedCalls, ["loading:true", "report", "freshness"]);
}

void run();
