import assert from "node:assert/strict";
import Module from "node:module";
import * as reportConfig from "./report-config";

const { REPORT_QUERY_TIMEOUT_MS } = reportConfig;
const reportQueryOptions = (
  reportConfig as typeof reportConfig & {
    REPORT_QUERY_OPTIONS?: { readonly retry: boolean };
  }
).REPORT_QUERY_OPTIONS;

const query = {
  startDate: "2026-01-01",
  endDate: "2026-07-11",
  compareStartDate: "2025-01-01",
  compareEndDate: "2025-07-11",
  compareMode: "ByDate" as const,
};

async function run() {
  Object.assign(globalThis, { __DEV__: false });
  const mockModule = (name: string, exports: object) => {
    const filename = require.resolve(name);
    const module = new Module(filename);
    module.filename = filename;
    module.loaded = true;
    module.exports = exports;
    require.cache[filename] = module;
  };
  // Node 测试不渲染路由，先替换 Expo Router 入口，避免加载其原生 JSX 实现。
  mockModule("expo-router", { router: { replace: () => undefined } });
  mockModule("react-native", {
    AppState: { addEventListener: () => ({ remove: () => undefined }) },
    NativeModules: {},
    Platform: { OS: "ios", select: <T>(values: { ios?: T; default?: T }) => values.ios ?? values.default },
  });
  mockModule("expo-secure-store", {
    getItemAsync: async () => null,
    setItemAsync: async () => undefined,
    deleteItemAsync: async () => undefined,
  });
  mockModule("expo-location", {
    hasStartedLocationUpdatesAsync: async () => false,
    stopLocationUpdatesAsync: async () => undefined,
  });
  mockModule("@react-native-async-storage/async-storage", {
    default: {
      getItem: async () => null,
      setItem: async () => undefined,
      removeItem: async () => undefined,
    },
  });
  const { apiClient } = await import("../../shared/api/client");
  const {
    fetchProductBranchBreakdown,
    fetchProductReportProductRows,
    fetchProductReportStoreOptions,
    fetchProductReportTotalRevenue,
    fetchSupplierBranchBreakdown,
    fetchSupplierReportRows,
  } = await import("../product-report/api");
  const {
    fetchBranchDailyPerformance,
    fetchExecutiveBranchPerformance,
    fetchExecutiveHourlyTraffic,
  } = await import("./api");
  const { fetchStatisticsFreshness } = await import("./statistics-freshness");
  const requests: { url: string; timeout?: number; signal?: AbortSignal }[] = [];
  const freshStatisticMetadata = {
    statisticStatus: "Fresh",
    statisticUpdatedAt: "2026-09-01T00:00:00.000Z",
    cacheVersion: "report-timeout-test-v1",
  };
  const responseFor = (url: string) => {
    if (url === "/react/v1/dashboard/executive-branch-performance") {
      return {
        ...freshStatisticMetadata,
        statisticsPending: false,
        statisticsExpectedBranchCount: 0,
        statisticsSnapshotBranchCount: 0,
        items: [],
      };
    }
    if (
      url === "/react/v1/dashboard/executive-hourly-traffic"
      || url === "/react/v1/dashboard/branch-daily-performance"
    ) {
      return {
        ...freshStatisticMetadata,
        statisticsPending: false,
        statisticsExpectedItemCount: 0,
        statisticsSnapshotItemCount: 0,
        items: [],
      };
    }
    if (
      url === "/react/v1/dashboard/supplier-sales-rank"
      || url === "/react/v1/dashboard/enhanced-sales-product-details"
      || url === "/react/v1/dashboard/supplier-store-sales"
      || url === "/react/v1/dashboard/product-sales-by-branches"
    ) {
      return { ...freshStatisticMetadata, data: [], items: [], total: 0 };
    }
    return [];
  };
  const originalGet = apiClient.get;
  apiClient.get = (async (url: string, config?: { timeout?: number; signal?: AbortSignal }) => {
    requests.push({ url, timeout: config?.timeout, signal: config?.signal });
    return { data: responseFor(url) };
  }) as typeof apiClient.get;

  try {
    const detailAbortController = new AbortController();
    await fetchExecutiveBranchPerformance(query);
    const hourlySnapshot = await fetchExecutiveHourlyTraffic(query, { signal: detailAbortController.signal });
    const dailySnapshot = await fetchBranchDailyPerformance(query, { signal: detailAbortController.signal });
    await fetchProductReportTotalRevenue(query);
    await fetchSupplierReportRows("australia", query);
    await fetchProductReportProductRows("china", query, undefined, 1);
    await fetchSupplierBranchBreakdown("australia", query, "S1");
    await fetchProductBranchBreakdown(query, "P1");
    await fetchProductReportStoreOptions();
    await fetchStatisticsFreshness();

    assert.equal(REPORT_QUERY_TIMEOUT_MS, 60_000);
    assert.deepEqual(reportQueryOptions, { retry: false });
    assert.equal(apiClient.defaults.timeout, 30_000);
    assert.equal(requests.length, 10);
    requests.slice(0, 8).forEach((request) => assert.equal(request.timeout, REPORT_QUERY_TIMEOUT_MS, request.url));
    assert.equal(requests[1]?.signal, detailAbortController.signal);
    assert.equal(requests[2]?.signal, detailAbortController.signal);
    assert.equal(hourlySnapshot.isComplete, true, "分时请求必须只把完整包络返回给页面");
    assert.equal(dailySnapshot.isComplete, true, "逐日请求必须只把完整包络返回给页面");
    requests.slice(8).forEach((request) => assert.equal(request.timeout, undefined, request.url));
  } finally {
    apiClient.get = originalGet;
  }
}

void run();
