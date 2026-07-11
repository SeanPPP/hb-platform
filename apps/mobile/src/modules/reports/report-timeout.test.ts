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
  const requests: Array<{ url: string; timeout?: number }> = [];
  const originalGet = apiClient.get;
  apiClient.get = (async (url: string, config?: { timeout?: number }) => {
    requests.push({ url, timeout: config?.timeout });
    return { data: [] };
  }) as typeof apiClient.get;

  try {
    await fetchExecutiveBranchPerformance(query);
    await fetchExecutiveHourlyTraffic(query);
    await fetchBranchDailyPerformance(query);
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
    requests.slice(8).forEach((request) => assert.equal(request.timeout, undefined, request.url));
  } finally {
    apiClient.get = originalGet;
  }
}

void run();
