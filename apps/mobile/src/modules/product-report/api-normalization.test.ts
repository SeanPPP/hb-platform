import assert from "node:assert/strict";
import {
  buildProductReportProductParams,
  getProductReportCacheVersionState,
  getProductReportCacheVersionSyncDecision,
  normalizeProductBranchReportSnapshot,
  normalizeProductReportProductPageSnapshot,
  normalizeProductReportTotalRevenue,
  normalizeProductBranchRows,
  normalizeProductPage,
  normalizeSupplierBranchReportSnapshot,
  normalizeSupplierBranchRows,
  normalizeSupplierReportSnapshot,
  normalizeSupplierRows,
  pollProductReportSnapshot,
  type ProductReportSnapshot,
} from "./api";

const alignedCacheVersionState = getProductReportCacheVersionState([
  { isComplete: true, cacheVersion: "batch-42" },
  { isComplete: true, cacheVersion: "batch-42" },
  { isComplete: true, cacheVersion: "batch-42" },
]);
assert.equal(alignedCacheVersionState, "aligned", "三个 Fresh 结果必须来自同一 cacheVersion 才能展示");

const mixedCacheVersionState = getProductReportCacheVersionState([
  { isComplete: true, cacheVersion: "batch-42" },
  { isComplete: true, cacheVersion: "batch-43" },
  { isComplete: true, cacheVersion: "batch-42" },
]);
assert.equal(mixedCacheVersionState, "mismatch", "任一结果版本不同都必须阻止混合批次渲染");
assert.equal(
  getProductReportCacheVersionSyncDecision(mixedCacheVersionState, 0, false, 2),
  "refetch",
  "首次版本不一致必须协调重取三块数据",
);
assert.equal(
  getProductReportCacheVersionSyncDecision(mixedCacheVersionState, 2, false, 2),
  "exhausted",
  "协调重取达到上限后必须进入可重试错误态，不能无限请求",
);
assert.equal(
  getProductReportCacheVersionSyncDecision(mixedCacheVersionState, 0, true, 2),
  "wait",
  "已有协调请求在途时不得重复发起 refetch",
);
assert.equal(
  getProductReportCacheVersionState([
    { isComplete: true, cacheVersion: "batch-42" },
    undefined,
    { isComplete: false, cacheVersion: "batch-42" },
  ]),
  "pending",
  "任一并发结果尚未 Fresh 时版本协调必须继续等待",
);

const productQuery = {
  startDate: "2026-09-01",
  endDate: "2026-09-01",
  compareStartDate: "2026-08-31",
  compareEndDate: "2026-08-31",
  compareMode: "ByDate" as const,
};

const chinaProductParams = buildProductReportProductParams(
  "china",
  productQuery,
  undefined,
  1,
  20,
);
assert.equal(
  chinaProductParams.get("supplierScope"),
  "china",
  "中国供应商页未选择具体供应商时，也必须显式约束商品明细为中国供应商范围",
);
assert.equal(chinaProductParams.has("localSupplierCodes"), false);
assert.equal(chinaProductParams.has("chinaSupplierCodes"), false);

const australiaProductParams = buildProductReportProductParams(
  "australia",
  productQuery,
  undefined,
  1,
  20,
);
assert.equal(
  australiaProductParams.has("supplierScope"),
  false,
  "澳洲/全部商品请求必须保持现有默认范围",
);

for (const statisticStatus of ["Pending", "Stale", "Failed"] as const) {
  const snapshot = normalizeSupplierReportSnapshot({
    statisticStatus,
    statisticMessage: "统计尚未就绪",
    statisticUpdatedAt: "2026-09-01T00:00:00.000Z",
    cacheVersion: "v42",
    data: [],
  });
  assert.equal(snapshot.isComplete, false, `${statisticStatus} 不能被归一化为供应商业务空结果`);
  assert.equal(snapshot.data.length, 0);
  assert.equal(snapshot.statisticStatus, statisticStatus);
  assert.equal(snapshot.cacheVersion, "v42");
}

const freshSupplierSnapshot = normalizeSupplierReportSnapshot({
  statisticStatus: "Fresh",
  statisticMessage: null,
  statisticUpdatedAt: "2026-09-01T00:00:00.000Z",
  cacheVersion: "v43",
  data: [{ SupplierCode: "S1", TotalAmount: 100 }],
});
assert.equal(freshSupplierSnapshot.isComplete, true);
assert.equal(freshSupplierSnapshot.data.length, 1);

const legacySupplierSnapshot = normalizeSupplierReportSnapshot([{ SupplierCode: "LEGACY", TotalAmount: 20 }]);
assert.equal(legacySupplierSnapshot.isComplete, false, "旧裸 data 响应缺完整性元数据时必须 fail-closed");
assert.equal(legacySupplierSnapshot.statisticStatus, "Pending");
assert.equal(legacySupplierSnapshot.data[0]?.supplierCode, "LEGACY");

for (const missingMetadataPayload of [
  {
    statisticStatus: "Fresh",
    cacheVersion: "v-missing-time",
    data: [{ SupplierCode: "MISSING-TIME", TotalAmount: 10 }],
  },
  {
    statisticStatus: "Fresh",
    statisticUpdatedAt: "2026-09-01T00:00:00.000Z",
    data: [{ SupplierCode: "MISSING-VERSION", TotalAmount: 10 }],
  },
  {
    statisticStatus: "Fresh",
    statisticUpdatedAt: "not-a-time",
    cacheVersion: "v-invalid-time",
    data: [{ SupplierCode: "INVALID-TIME", TotalAmount: 10 }],
  },
]) {
  const snapshot = normalizeSupplierReportSnapshot(missingMetadataPayload);
  assert.equal(snapshot.isComplete, false, "Fresh 但缺少完整性时间或版本元数据不得展示为完整报告");
  assert.equal(snapshot.statisticStatus, "Pending");
}

for (const normalizeSnapshot of [
  normalizeProductReportProductPageSnapshot,
  normalizeSupplierBranchReportSnapshot,
  normalizeProductBranchReportSnapshot,
]) {
  for (const statisticStatus of ["Pending", "Stale", "Failed", "Fresh"] as const) {
    const snapshot = normalizeSnapshot({
      statisticStatus,
      statisticMessage: "统计补算中",
      statisticUpdatedAt: "2026-09-01T00:00:00.000Z",
      cacheVersion: "v44",
      data: [],
    });
    assert.equal(
      snapshot.isComplete,
      statisticStatus === "Fresh",
      "商品及下钻必须按根级统计状态决定是否可渲染行级数据",
    );
    assert.equal(snapshot.statisticStatus, statisticStatus);
  }
}

const legacyProductSnapshot = normalizeProductReportProductPageSnapshot({
  data: [{ ProductCode: "P1", SalesAmount: 12 }],
  total: 1,
});
assert.equal(legacyProductSnapshot.isComplete, false, "旧商品页响应不得绕过完整性元数据");
assert.equal(legacyProductSnapshot.statisticStatus, "Pending");
assert.equal(legacyProductSnapshot.data.rows[0]?.productCode, "P1");

const freshPagedProductSnapshot = normalizeProductReportProductPageSnapshot({
  success: true,
  statisticStatus: "Fresh",
  statisticUpdatedAt: "2026-09-01T00:00:00.000Z",
  cacheVersion: "v46",
  data: {
    data: [{ ProductCode: "P21", SalesAmount: 21 }],
    total: 41,
    pageIndex: 2,
    pageSize: 20,
  },
});
assert.equal(freshPagedProductSnapshot.data.rows[0]?.productCode, "P21");
assert.equal(freshPagedProductSnapshot.data.total, 41, "保留统计包络后仍必须读取内层分页总数");
assert.equal(freshPagedProductSnapshot.data.pageIndex, 2);
assert.equal(freshPagedProductSnapshot.data.pageSize, 20);
assert.equal(freshPagedProductSnapshot.isComplete, true);

const legacySupplierBranchSnapshot = normalizeSupplierBranchReportSnapshot([
  { BranchCode: "B1", SupplierCode: "S1", TotalAmount: 12 },
]);
assert.equal(legacySupplierBranchSnapshot.isComplete, false, "旧供应商分店下钻不得绕过完整性元数据");

const legacyProductBranchSnapshot = normalizeProductBranchReportSnapshot([
  { BranchCode: "B1", ProductCode: "P1", SalesAmount: 12 },
]);
assert.equal(legacyProductBranchSnapshot.isComplete, false, "旧商品分店下钻不得绕过完整性元数据");

function createPollingSnapshot(
  statisticStatus: "Pending" | "Fresh",
): ProductReportSnapshot<string[]> {
  return {
    data: statisticStatus === "Fresh" ? ["complete"] : [],
    statisticStatus,
    statisticMessage: null,
    statisticUpdatedAt: "2026-09-01T00:00:00.000Z",
    cacheVersion: "v45",
    isComplete: statisticStatus === "Fresh",
    pollingExhausted: false,
    pollingAttemptCount: 1,
  };
}

async function runProductSnapshotPollingAssertions() {
  const snapshots = [createPollingSnapshot("Pending"), createPollingSnapshot("Fresh")];
  const waits: number[] = [];
  const completed = await pollProductReportSnapshot(
    async () => snapshots.shift() ?? createPollingSnapshot("Fresh"),
    { delaysMs: [200, 350], wait: async (delayMs) => { waits.push(delayMs); } },
  );
  assert.equal(completed.isComplete, true, "Fresh 才能完成商品统计有界追数");
  assert.deepEqual(waits, [200]);

  let elapsedMs = 0;
  const recoveredAfterLegacyWindow = await pollProductReportSnapshot(
    async () => createPollingSnapshot(elapsedMs > 800 ? "Fresh" : "Pending"),
    {
      deadlineMs: 5_000,
      now: () => elapsedMs,
      wait: async (delayMs) => { elapsedMs += delayMs; },
    },
  );
  assert.equal(
    recoveredAfterLegacyWindow.isComplete,
    true,
    "持续 Pending 超过旧 550ms 窗口后，仍必须在真实截止时间内自动恢复 Fresh",
  );
  assert.ok(elapsedMs > 800 && elapsedMs <= 5_000);

  elapsedMs = 0;
  const exhaustedAtDeadline = await pollProductReportSnapshot(
    async () => createPollingSnapshot("Pending"),
    {
      deadlineMs: 1_000,
      now: () => elapsedMs,
      wait: async (delayMs) => { elapsedMs += delayMs; },
    },
  );
  assert.equal(exhaustedAtDeadline.isComplete, false);
  assert.equal(exhaustedAtDeadline.pollingExhausted, true, "只有到达真实截止时间后才能标记追数耗尽");
  assert.equal(elapsedMs, 1_000, "最后一次退避必须截断到截止时间，不能提前放弃或超时等待");

  const controller = new AbortController();
  let resolvePending: ((value: ProductReportSnapshot<string[]>) => void) | undefined;
  const cancelled = pollProductReportSnapshot(
    () => new Promise<ProductReportSnapshot<string[]>>((resolve) => { resolvePending = resolve; }),
    { signal: controller.signal, delaysMs: [] },
  );
  await Promise.resolve();
  controller.abort();
  resolvePending?.(createPollingSnapshot("Fresh"));
  await assert.rejects(cancelled, (error: unknown) => error instanceof Error && error.name === "AbortError");
}

void runProductSnapshotPollingAssertions();

const partialTotalRevenue = normalizeProductReportTotalRevenue({
  data: [
    { BranchCode: "S01", Revenue: 100, RevenueLY: 80 },
    { BranchCode: "S02", Revenue: 200, RevenueLY: 160 },
  ],
  statisticsPending: true,
  statisticsExpectedBranchCount: 28,
  statisticsSnapshotBranchCount: 2,
});

assert.equal(partialTotalRevenue.revenue, 300);
assert.equal(partialTotalRevenue.compareRevenue, 240);
assert.equal(
  partialTotalRevenue.isComplete,
  false,
  "商品页总营业额不得把部分分店统计快照当成完整首屏数据",
);

const countMismatchTotalRevenue = normalizeProductReportTotalRevenue({
  data: [{ BranchCode: "S01", Revenue: 100, RevenueLY: 80 }],
  statisticsPending: false,
  statisticsExpectedBranchCount: 28,
  statisticsSnapshotBranchCount: 1,
});

assert.equal(
  countMismatchTotalRevenue.isComplete,
  false,
  "统计标记完成但快照分店数不足时，商品页总营业额仍不得完成",
);

// 与 executive-branch-performance?includeProductStatisticMetadata=true 的根级包络保持一致。
const executiveBranchPerformanceWithProductStatisticMetadata = {
  success: true,
  data: [{ BranchCode: "S01", Revenue: 100, RevenueLY: 80 }],
  statisticsPending: false,
  statisticsExpectedBranchCount: 1,
  statisticsSnapshotBranchCount: 1,
  statisticStatus: "Fresh",
  statisticMessage: null,
  statisticUpdatedAt: "2026-07-08T09:10:11.000Z",
  cacheVersion: "product-fresh",
};

const completeTotalRevenue = normalizeProductReportTotalRevenue(
  executiveBranchPerformanceWithProductStatisticMetadata,
);

assert.equal(
  completeTotalRevenue.isComplete,
  true,
  "商品页必须识别营业额接口显式返回的完整商品统计元数据",
);
assert.equal(completeTotalRevenue.statisticStatus, "Fresh");
assert.equal(completeTotalRevenue.cacheVersion, "product-fresh");

const productPage = normalizeProductPage({
  data: [
    {
      productCode: "P1",
      itemNumber: "HB001",
      productName: "商品一",
      quantity: 2,
      quantityLY: 3,
      salesAmount: 40,
      salesAmountLY: 60,
      orderCount: 1,
      orderCountLY: 2,
      grossProfit: 0,
      GrossProfitLY: 12,
      grossMarginRate: 0,
      GrossMarginRateLY: 0.205,
    },
  ],
  total: 1,
  pageIndex: 1,
  pageSize: 20,
});

assert.equal(productPage.rows[0]?.quantity, 2);
assert.equal(productPage.rows[0]?.compareQuantity, 3);
assert.equal(productPage.rows[0]?.salesAmount, 40);
assert.equal(productPage.rows[0]?.compareSalesAmount, 60);
assert.equal(productPage.rows[0]?.orderCount, 1);
assert.equal(productPage.rows[0]?.compareOrderCount, 2);
assert.equal(productPage.rows[0]?.grossProfit, 0, "毛利 0 不能被归一化为空");
assert.equal(productPage.rows[0]?.compareGrossProfit, 12, "必须接受 PascalCase 同期毛利");
assert.equal(productPage.rows[0]?.grossMarginRate, 0, "毛利率 0 不能被归一化为空");
assert.equal(productPage.rows[0]?.compareGrossMarginRate, 0.205, "毛利率接口统一使用 0 到 1 的比率");

const productBranchRows = normalizeProductBranchRows([
  {
    branchCode: "S1",
    branchName: "分店一",
    quantity: 2,
    compareQuantity: 3,
    salesAmount: 40,
    compareSalesAmount: 60,
    averageUnitPrice: 20,
    compareAverageUnitPrice: 20,
    GrossProfit: 0,
    CompareGrossProfit: null,
    GrossMarginRate: 0,
    CompareGrossMarginRate: null,
  },
]);

assert.equal(productBranchRows[0]?.quantity, 2);
assert.equal(productBranchRows[0]?.compareQuantity, 3);
assert.equal(productBranchRows[0]?.salesAmount, 40);
assert.equal(productBranchRows[0]?.compareSalesAmount, 60);
assert.equal(productBranchRows[0]?.averageUnitPrice, 20);
assert.equal(productBranchRows[0]?.compareAverageUnitPrice, 20);
assert.equal(productBranchRows[0]?.grossProfit, 0);
assert.equal(productBranchRows[0]?.compareGrossProfit, null, "缺失同期成本必须保留 null");
assert.equal(productBranchRows[0]?.grossMarginRate, 0);
assert.equal(productBranchRows[0]?.compareGrossMarginRate, null);

const supplierRows = normalizeSupplierRows([
  {
    supplierCode: "AU-1001",
    supplierName: "Supplier One",
    totalAmount: 100,
    compareTotalAmount: 80,
    grossProfit: 0,
    compareGrossProfit: null,
    GrossMarginRate: 0,
    CompareGrossMarginRate: null,
  },
]);

assert.equal(supplierRows[0]?.grossProfit, 0);
assert.equal(supplierRows[0]?.compareGrossProfit, null);
assert.equal(supplierRows[0]?.grossMarginRate, 0);
assert.equal(supplierRows[0]?.compareGrossMarginRate, null);

const supplierBranchRows = normalizeSupplierBranchRows([
  {
    BranchCode: "S1",
    SupplierCode: "AU-1001",
    TotalAmount: 100,
    CompareTotalAmount: 80,
    GrossProfit: 12,
    GrossProfitLY: 0,
    GrossMarginRate: 0.12,
    GrossMarginRateLY: 0,
  },
]);

assert.equal(supplierBranchRows[0]?.grossProfit, 12);
assert.equal(supplierBranchRows[0]?.compareGrossProfit, 0);
assert.equal(supplierBranchRows[0]?.grossMarginRate, 0.12);
assert.equal(supplierBranchRows[0]?.compareGrossMarginRate, 0);
