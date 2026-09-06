import assert from "node:assert/strict";
import { QueryClient, QueryObserver } from "@tanstack/react-query";
import * as reportRefresh from "./report-refresh";
import {
  ReportLoadPerformanceTimer,
  discardReportNavigationStart,
  hasPendingReportNavigationStart,
  markReportHubNavigationStart,
} from "./report-load-performance";

const {
  createReportRefreshController,
  getReportRefreshQueryOptions,
  getReportStoreScopeRefreshQueryOptions,
} = reportRefresh;
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
assert.equal(revenueOptions.predicate({ queryKey: ["reports", "cashier-enabled-stores"] }), false);
assert.equal(revenueOptions.predicate({ queryKey: ["reports", "revenue-summary"] }), true);

const revenueScopeOptions = getReportStoreScopeRefreshQueryOptions("revenue");
assert.deepEqual(revenueScopeOptions.queryKey, ["reports", "cashier-enabled-stores"]);
assert.equal(revenueScopeOptions.exact, true);
assert.equal(revenueScopeOptions.type, "active");

const productOptions = getReportRefreshQueryOptions("product");
assert.deepEqual(productOptions.queryKey, ["product-report"]);
assert.equal(productOptions.type, "active");
assert.equal(productOptions.predicate({ queryKey: ["product-report", "stores"] }), false);
assert.equal(productOptions.predicate({ queryKey: ["product-report", "products"] }), true);
assert.equal(productOptions.predicate({ queryKey: ["reports", "revenue-summary"] }), false);

const productScopeOptions = getReportStoreScopeRefreshQueryOptions("product");
assert.deepEqual(productScopeOptions.queryKey, ["product-report", "stores"]);
assert.equal(productScopeOptions.exact, true);
assert.equal(productScopeOptions.type, "active");

async function run() {
  const waitFor = async (predicate: () => boolean, message: string) => {
    for (let attempt = 0; attempt < 20; attempt += 1) {
      if (predicate()) return;
      await Promise.resolve();
    }
    assert.fail(message);
  };

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

  // 两阶段刷新必须先完成收银启用范围重验：范围请求在途时，旧业务 observer 会被禁用，
  // 即使调用 active refetch 也不能用旧白名单再发一次营业额请求。
  const scopeQueryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  const scopeQueryKey = ["reports", "cashier-enabled-stores"] as const;
  const scopeCodes = ["1001"] as const;
  let scopeRequestCount = 0;
  let releaseScopeRevalidation: (() => void) | undefined;
  const scopeQueryFn = async () => {
    scopeRequestCount += 1;
    if (scopeRequestCount === 1) return [...scopeCodes];
    await new Promise<void>((resolve) => {
      releaseScopeRevalidation = resolve;
    });
    return [...scopeCodes];
  };
  const scopeObserver = new QueryObserver(scopeQueryClient, {
    queryKey: scopeQueryKey,
    queryFn: scopeQueryFn,
    enabled: false,
    staleTime: Infinity,
  });
  const unsubscribeScope = scopeObserver.subscribe(() => undefined);
  await scopeObserver.refetch();
  scopeObserver.setOptions({
    queryKey: scopeQueryKey,
    queryFn: scopeQueryFn,
    enabled: true,
    staleTime: Infinity,
  });

  const scopedRevenueQueryKey = ["reports", "revenue-summary", 1, { branchCodes: scopeCodes }] as const;
  let scopedRevenueRequestCount = 0;
  const scopedRevenueObserver = new QueryObserver(scopeQueryClient, {
    queryKey: scopedRevenueQueryKey,
    queryFn: async () => {
      scopedRevenueRequestCount += 1;
      return { rows: [] };
    },
    enabled: false,
    staleTime: Infinity,
  });
  const unsubscribeScopedRevenue = scopedRevenueObserver.subscribe(() => undefined);
  await scopedRevenueObserver.refetch();
  scopedRevenueObserver.setOptions({
    queryKey: scopedRevenueQueryKey,
    queryFn: async () => {
      scopedRevenueRequestCount += 1;
      return { rows: [] };
    },
    enabled: true,
    staleTime: Infinity,
  });
  assert.equal(scopedRevenueRequestCount, 1, "初始活跃范围只应发起一次营业额请求");

  const scopeRevalidation = scopeObserver.refetch();
  await waitFor(
    () => scopeRequestCount === 2 && scopeObserver.getCurrentResult().isFetching,
    "收银启用范围重验必须真实处于 in-flight 状态",
  );
  scopedRevenueObserver.setOptions({
    queryKey: scopedRevenueQueryKey,
    queryFn: async () => {
      scopedRevenueRequestCount += 1;
      return { rows: [] };
    },
    enabled: false,
    staleTime: Infinity,
  });
  await scopeQueryClient.refetchQueries(
    getReportRefreshQueryOptions("revenue"),
    reportRefetchOptions,
  );
  assert.equal(
    scopedRevenueRequestCount,
    1,
    "范围重验期间业务 query disabled，active refetch 不得用旧收银启用范围重发请求",
  );
  releaseScopeRevalidation?.();
  await scopeRevalidation;
  unsubscribeScopedRevenue();
  unsubscribeScope();
  scopeQueryClient.clear();

  // dataUpdatedAt 即使对应相同代码列表，也代表一次已完成的范围重验；它必须进入业务 key，
  // 使新 key 重新执行 queryFn，不能误用旧范围快照的缓存。
  const revisionQueryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  const sameCodesBeforeRefresh = ["1001", "1002"] as const;
  const sameCodesAfterRefresh = ["1001", "1002"] as const;
  assert.deepEqual(sameCodesAfterRefresh, sameCodesBeforeRefresh, "本用例必须保持收银启用代码内容不变");
  const revenueParams = { branchCodes: sameCodesBeforeRefresh };
  const revisionOne: number = 1_000;
  const revisionTwo: number = 2_000;
  let revisionRequestCount = 0;
  let releaseRevisionRequest: (() => void) | undefined;
  const revisionQueryFn = async () => {
    revisionRequestCount += 1;
    await new Promise<void>((resolve) => {
      releaseRevisionRequest = resolve;
    });
    return { rows: [] };
  };
  const revisionObserver = new QueryObserver(revisionQueryClient, {
    queryKey: ["reports", "revenue-summary", revisionOne, revenueParams] as const,
    queryFn: revisionQueryFn,
    enabled: true,
    staleTime: Infinity,
  });
  const unsubscribeRevision = revisionObserver.subscribe(() => undefined);
  await waitFor(() => revisionRequestCount === 1, "首个范围 revision 必须执行业务 queryFn");
  releaseRevisionRequest?.();
  await waitFor(() => revisionObserver.getCurrentResult().isSuccess, "首个业务请求必须成功落入缓存");
  revisionObserver.setOptions({
    queryKey: [
      "reports",
      "revenue-summary",
      revisionTwo,
      { branchCodes: sameCodesAfterRefresh },
    ] as const,
    queryFn: revisionQueryFn,
    enabled: true,
    staleTime: Infinity,
  });
  await waitFor(
    () => revisionRequestCount === 2,
    "收银启用代码内容相同但 scope revision 更新时，必须以新业务 key 再次执行 queryFn",
  );
  releaseRevisionRequest?.();
  await waitFor(() => revisionObserver.getCurrentResult().isSuccess, "新范围 revision 的业务请求必须成功完成");
  unsubscribeRevision();
  revisionQueryClient.clear();

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
