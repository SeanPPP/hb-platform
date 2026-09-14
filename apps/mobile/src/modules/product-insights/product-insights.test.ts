import assert from "node:assert/strict";
import { test } from "node:test";
import {
  normalizeProductBranchSales,
  normalizeStoreProductInsight,
} from "./api-normalization";
import {
  canViewProductInsightBranches,
  findProductInsightStore,
  isValidProductInsightRange,
} from "./logic";
import { createProductInsightRequestGate } from "./request-gate";
import type { ProductBranchSales, StoreProductInsight } from "./types";

const store: StoreProductInsight = {
  range: { startDate: "2026-06-17", endDate: "2026-09-14" },
  generatedAt: "2026-09-14T02:00:00Z",
  salesStatisticLastUpdatedAt: null,
  store: { storeCode: "S1", storeName: "门店一" },
  product: {
    productCode: "P1",
    productName: "清洁布",
    itemNumber: "CL1",
    barcode: "9301",
    productImage: null,
    localSupplierCode: "300",
    localSupplierName: "供应商",
  },
  sourceType: "local",
  sales: { quantity: 0, amount: 0, records: [] },
  purchases: {
    quantity: 0,
    documentCount: 0,
    records: [],
    lastRecord: {
      id: "I1",
      date: "2024-02-29",
      documentNo: "INV1",
      quantity: 48,
      supplierName: "供应商",
    },
  },
  warehouse: {
    orderedQuantity: 0,
    deliveredQuantity: 0,
    orders: [],
    deliveries: [],
    lastDelivery: null,
  },
};
const branches: ProductBranchSales = {
  range: store.range,
  generatedAt: store.generatedAt,
  salesStatisticLastUpdatedAt: null,
  productCode: "P1",
  scope: "all-pos",
  totalPosStoreCount: 2,
  includedStoreCount: 2,
  quantity: 5,
  amount: 12.5,
  rows: [
    { storeCode: "S1", storeName: "一店", quantity: 5, amount: 12.5 },
    { storeCode: "S2", storeName: "二店", quantity: 0, amount: 0 },
  ],
};

test("历史进货作为独立记录返回，不能改变近90天的零合计", () => {
  const result = normalizeStoreProductInsight({ success: true, data: store });
  assert.equal(result.purchases.quantity, 0);
  assert.equal(result.purchases.documentCount, 0);
  assert.equal(result.purchases.lastRecord?.date, "2024-02-29");
  assert.equal(result.purchases.lastRecord?.quantity, 48);
  assert.equal(result.salesStatisticLastUpdatedAt, null);
});

test("空合法数据和失败响应必须区分，缺少数量或无效金额不能补零", () => {
  assert.throws(
    () =>
      normalizeStoreProductInsight({
        success: false,
        data: store,
        message: "无法读取",
      }),
    /无法读取/,
  );
  assert.throws(
    () => normalizeStoreProductInsight({ ...store, sales: {} }),
    /Invalid/,
  );
  assert.throws(
    () =>
      normalizeStoreProductInsight({
        ...store,
        sales: { ...store.sales, amount: NaN },
      }),
    /Invalid/,
  );
  assert.throws(() => normalizeStoreProductInsight(null), /Invalid/);
  assert.throws(
    () =>
      normalizeStoreProductInsight({
        ...store,
        range: { ...store.range, startDate: "2026-06-17T00:00:00" },
      }),
    /Invalid/,
  );
});

test("全部POS销售保留零行，部分授权不伪装为全部", () => {
  assert.deepEqual(normalizeProductBranchSales(branches).rows, branches.rows);
  const partial = normalizeProductBranchSales({
    ...branches,
    scope: "authorized-pos",
    includedStoreCount: 1,
    rows: [branches.rows[0]],
  });
  assert.equal(partial.scope, "authorized-pos");
  assert.equal(partial.totalPosStoreCount, 2);
  assert.equal(partial.rows.length, 1);
});

test("后端省略null字段时正常解析空历史，但仍保留真实的零销售", () => {
  const payload = JSON.parse(
    JSON.stringify(
      { ...store, purchases: { ...store.purchases, lastRecord: null } },
      (_key, value) => (value === null ? undefined : value),
    ),
  );
  const result = normalizeStoreProductInsight(payload);
  assert.equal(result.purchases.lastRecord, null);
  assert.equal(result.warehouse.lastDelivery, null);
  assert.equal(result.product.productImage, null);
  assert.equal(result.salesStatisticLastUpdatedAt, null);
  assert.equal(result.sales.quantity, 0);
});

test("缺行或重复分店不能当成完整POS数据", () => {
  assert.throws(
    () =>
      normalizeProductBranchSales({ ...branches, rows: [branches.rows[0]] }),
    /Incomplete/,
  );
  assert.throws(
    () =>
      normalizeProductBranchSales({
        ...branches,
        rows: [branches.rows[0], { ...branches.rows[0], storeCode: "s1" }],
      }),
    /Incomplete/,
  );
  assert.throws(
    () => normalizeProductBranchSales({ ...branches, totalPosStoreCount: 3 }),
    /Incomplete/,
  );
});

test("日期支持闰日、含首尾同日，拒绝非法或逆序日期", () => {
  assert.equal(
    isValidProductInsightRange({
      startDate: "2024-02-29",
      endDate: "2024-02-29",
    }),
    true,
  );
  for (const range of [
    { startDate: "2026-02-29", endDate: "2026-03-01" },
    { startDate: "2026-09-15", endDate: "2026-09-14" },
    { startDate: "2026-06-17T00:00:00", endDate: "2026-09-14" },
    { startDate: "", endDate: "2026-09-14" },
  ])
    assert.equal(isValidProductInsightRange(range), false);
});

test("匿名设备和审核会话不能使用跨店销售，普通账号必须有报表权限", () => {
  assert.equal(
    canViewProductInsightBranches(false, () => true, false),
    false,
  );
  assert.equal(
    canViewProductInsightBranches(true, () => true, true),
    false,
  );
  assert.equal(
    canViewProductInsightBranches(true, () => false, false),
    false,
  );
  assert.equal(
    canViewProductInsightBranches(
      true,
      (key) => key === "Reports.ProductMovement.View",
      false,
    ),
    true,
  );
});

test("外部传入门店只在实际授权列表中匹配", () => {
  assert.equal(findProductInsightStore([store.store], " s1 ")?.storeCode, "S1");
  assert.equal(findProductInsightStore([store.store], "S2"), null);
});

test("快速两次查询中较晚返回的旧响应不会覆盖新商品", async () => {
  const gate = createProductInsightRequestGate();
  let resolveOld!: (value: string) => void;
  const oldResponse = new Promise<string>((resolve) => {
    resolveOld = resolve;
  });
  let rendered = "";
  const oldLease = gate.begin();
  const oldTask = oldResponse.then((value) => {
    if (oldLease.isCurrent()) rendered = value;
  });
  const newLease = gate.begin();
  if (newLease.isCurrent()) rendered = "new-product";
  resolveOld("old-product");
  await oldTask;
  assert.equal(rendered, "new-product");
  assert.equal(oldLease.signal.aborted, true);
});

test("切店或销毁会话同步取消当前请求，新的会话仍可继续查询", () => {
  const gate = createProductInsightRequestGate();
  const previous = gate.begin();
  gate.cancel();
  assert.equal(previous.isCurrent(), false);
  assert.equal(previous.signal.aborted, true);
  assert.equal(gate.begin().isCurrent(), true);
});
