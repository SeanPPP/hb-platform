import assert from "node:assert/strict";
import { test } from "node:test";
import { normalizeWarehouseProductInsight } from "./api-normalization";
import {
  WAREHOUSE_INSIGHT_MAX_RANGE_DAYS,
  buildInsightFlowSegments,
  buildInsightPresetRange,
  canViewWarehouseProductInsights,
  countInsightRangeDays,
  matchInsightRangePreset,
  validateWarehouseInsightRange,
} from "./logic";
import type { WarehouseProductInsight } from "./types";

const insight: WarehouseProductInsight = {
  range: { startDate: "2026-06-19", endDate: "2026-09-17", dayCount: 91 },
  inboundRange: { startDate: "2025-09-18", endDate: "2026-09-17", dayCount: 365 },
  generatedAt: "2026-09-17T06:12:00Z",
  salesStatisticLastUpdatedAt: "2026-09-17T06:12:00Z",
  scope: "all-stores",
  product: {
    productCode: "P1",
    productName: "保温杯",
    itemNumber: "A1207",
    barcode: "9312004417",
    productImage: null,
    supplierCode: "200",
    supplierName: "中国供应商",
    locationCode: "A-03-12",
    stockQuantity: 318,
  },
  totals: {
    inboundQuantity: 1200,
    containerCount: 3,
    inTransitQuantity: 120,
    inTransitContainerCount: 1,
    orderedQuantity: 1050,
    orderedStoreCount: 18,
    orderDocumentCount: 42,
    shippedQuantity: 960,
    shippedStoreCount: 17,
    shipmentDocumentCount: 40,
    pendingQuantity: 90,
    pendingStoreCount: 4,
    salesQuantity: 742,
    salesAmount: 5936,
    salesStoreCount: 17,
  },
  branches: [
    {
      storeCode: "B1",
      storeName: "分店一",
      orderedQuantity: 120,
      shippedQuantity: 120,
      pendingQuantity: 0,
      salesQuantity: 96,
      salesAmount: 768,
      sellThroughRate: 0.8,
    },
  ],
  containers: [
    {
      containerNumber: "HG2026-014",
      arrivalDate: "2026-08-12",
      isEstimatedArrival: false,
      quantity: 600,
      pieces: 25,
      status: "arrived",
    },
  ],
  orders: [
    {
      documentNo: "WO-001",
      storeCode: "B1",
      storeName: "分店一",
      date: "2026-06-10",
      quantity: 120,
    },
  ],
  shipments: [],
  dailySales: [{ date: "2026-06-20", quantity: 30, amount: 240 }],
};

test("区间上限四百天按含首尾计算，四百天通过四百零一天拒绝", () => {
  assert.equal(WAREHOUSE_INSIGHT_MAX_RANGE_DAYS, 400);
  assert.equal(
    countInsightRangeDays({ startDate: "2026-09-17", endDate: "2026-09-17" }),
    1,
  );
  const exact = validateWarehouseInsightRange({
    startDate: "2025-08-14",
    endDate: "2026-09-17",
  });
  assert.equal(exact.dayCount, 400);
  assert.equal(exact.ok, true);

  const overflow = validateWarehouseInsightRange({
    startDate: "2025-08-13",
    endDate: "2026-09-17",
  });
  assert.equal(overflow.ok, false);
  assert.equal(overflow.reason, "tooLong");
  assert.equal(overflow.dayCount, 401);
  assert.equal(
    overflow.clampedStartDate,
    "2025-08-14",
    "收敛起始日必须正好落在四百天边界上",
  );
});

test("非法日期与倒置区间给出不同原因，便于界面分别提示", () => {
  assert.equal(
    validateWarehouseInsightRange({ startDate: "2026-02-30", endDate: "2026-03-01" })
      .reason,
    "format",
  );
  assert.equal(
    validateWarehouseInsightRange({ startDate: "2026-09-18", endDate: "2026-09-17" })
      .reason,
    "order",
  );
});

test("预设区间含首尾，近九十天起点是结束日往前八十九天", () => {
  const range = buildInsightPresetRange("2026-09-17", 90);
  assert.deepEqual(range, { startDate: "2026-06-20", endDate: "2026-09-17" });
  assert.equal(countInsightRangeDays(range), 90);
  assert.equal(matchInsightRangePreset(range), 90);
  assert.equal(
    matchInsightRangePreset({ startDate: "2026-06-19", endDate: "2026-09-17" }),
    null,
    "九十一天不是预设区间，不能被高亮成近九十天",
  );
});

test("流向分段以进货为分母，已发未售与结存不会出现负值", () => {
  const segments = buildInsightFlowSegments({
    inboundQuantity: 1200,
    shippedQuantity: 960,
    salesQuantity: 742,
  });
  assert.ok(Math.abs(segments.sold - 742 / 1200) < 1e-9);
  assert.ok(Math.abs(segments.shippedUnsold - 218 / 1200) < 1e-9);
  assert.ok(segments.remaining > 0);

  const oversold = buildInsightFlowSegments({
    inboundQuantity: 100,
    shippedQuantity: 20,
    salesQuantity: 80,
  });
  assert.equal(oversold.shippedUnsold, 0, "销量超过发货时已发未售必须归零而不是负数");
  assert.ok(oversold.remaining >= 0);

  // 发货可能来自更早进货的库存，此时各分段之和仍必须封顶在 100%。
  const overShipped = buildInsightFlowSegments({
    inboundQuantity: 1500,
    shippedQuantity: 1710,
    salesQuantity: 67,
  });
  assert.ok(
    overShipped.sold + overShipped.shippedUnsold <= 1 + 1e-9,
    "发货超过进货时流向占比不能超过 100%",
  );
  assert.equal(overShipped.remaining, 0);

  assert.deepEqual(
    buildInsightFlowSegments({
      inboundQuantity: 0,
      shippedQuantity: 0,
      salesQuantity: 0,
    }),
    { sold: 0, shippedUnsold: 0, remaining: 0 },
  );
});

test("零进货但有发货时以发货为分母，进度条不会整条消失", () => {
  const segments = buildInsightFlowSegments({
    inboundQuantity: 0,
    shippedQuantity: 200,
    salesQuantity: 150,
  });
  assert.equal(segments.sold, 0.75);
  assert.equal(segments.shippedUnsold, 0.25);
});

test("仓库进销查询权限只认仓库流向查看权限", () => {
  const has = (permission: string) =>
    permission === "SalesDashboard.WarehouseFlow.View";
  assert.equal(canViewWarehouseProductInsights(true, has, false), true);
  assert.equal(canViewWarehouseProductInsights(false, has, false), false);
  assert.equal(canViewWarehouseProductInsights(true, has, true), false);
  assert.equal(
    canViewWarehouseProductInsights(true, () => false, false),
    false,
    "只有商品查询权限不能查看仓库进销",
  );
});

test("合法响应通过校验并保留全部分段数据", () => {
  const result = normalizeWarehouseProductInsight({ success: true, data: insight });
  assert.equal(result.product.productCode, "P1");
  assert.equal(result.totals.pendingQuantity, 90);
  assert.equal(result.containers[0].containerNumber, "HG2026-014");
});

test("缺字段的响应视为接口失败，不静默补零", () => {
  const broken = {
    ...insight,
    totals: { ...insight.totals, salesAmount: undefined },
  };
  assert.throws(
    () => normalizeWarehouseProductInsight({ success: true, data: broken }),
    (error: unknown) =>
      (error as { code?: string }).code === "WAREHOUSE_INSIGHT_INVALID_RESPONSE",
  );
});

test("分店行重复时拒绝整份响应，避免合计被重复计算", () => {
  const duplicated = {
    ...insight,
    branches: [insight.branches[0], { ...insight.branches[0], storeCode: "b1" }],
  };
  assert.throws(
    () => normalizeWarehouseProductInsight({ success: true, data: duplicated }),
    (error: unknown) =>
      (error as { code?: string }).code === "WAREHOUSE_INSIGHT_INVALID_RESPONSE",
  );
});

test("超过四百天的响应区间不被接受，后端口径倒退能被前端发现", () => {
  const stale = {
    ...insight,
    range: { startDate: "2025-08-13", endDate: "2026-09-17", dayCount: 401 },
  };
  assert.throws(
    () => normalizeWarehouseProductInsight({ success: true, data: stale }),
    (error: unknown) =>
      (error as { code?: string }).code === "WAREHOUSE_INSIGHT_INVALID_RESPONSE",
  );
});
