import assert from "node:assert/strict";
import { buildPriceUpdateLabelJob } from "./label-print";
import {
  formatSuggestedDiscountInput,
  isSameSuggestedDiscount,
  parseSuggestedDiscountInput,
} from "./suggested-discount";
import {
  DEFAULT_SYNC_FIELDS,
  describeSyncTargetChange,
  getDefaultSelectedSyncTargets,
  getSelectableSyncTargetCodes,
} from "./sync-targets";
import { createTask } from "./test-helpers";
import type { SyncTargetStore } from "./types";

function target(patch: Partial<SyncTargetStore>): SyncTargetStore {
  return { storeCode: "1002", storeName: "Gold Coast", hasRecord: true, retailPrice: 10, discountRate: 0, isSpecialProduct: false, ...patch };
}

const targets = [
  target({ storeCode: "1002" }),
  target({ storeCode: "1003", isSpecialProduct: true }),
  target({ storeCode: "1004", hasRecord: false }),
  target({ storeCode: "1005", hasRecord: false, isSpecialProduct: true }),
];
assert.deepEqual(DEFAULT_SYNC_FIELDS, { syncRetailPrice: true, syncDiscountRate: true, syncPurchasePrice: false });
assert.deepEqual([...getDefaultSelectedSyncTargets(targets)], ["1002"], "默认全选，但特殊商品与无记录分店不选");
assert.deepEqual(getSelectableSyncTargetCodes(targets), ["1002", "1003"], "特殊商品仍可手动勾选，无记录分店不可选");

const source = { retailPrice: 12, discountRate: 0.2 };
const both = { syncRetailPrice: true, syncDiscountRate: true };
assert.deepEqual(describeSyncTargetChange(target({ retailPrice: 10 }), source, both), { kind: "price", from: 10, to: 12 });
assert.deepEqual(describeSyncTargetChange(target({ retailPrice: 12, discountRate: 0.2 }), source, both), { kind: "same" });
assert.deepEqual(describeSyncTargetChange(target({ retailPrice: 12, discountRate: 0 }), source, both), { kind: "discountOnly" });
assert.deepEqual(describeSyncTargetChange(target({ hasRecord: false }), source, both), { kind: "noRecord" });
assert.deepEqual(
  describeSyncTargetChange(target({ retailPrice: 10, discountRate: 0.2 }), source, { syncRetailPrice: false, syncDiscountRate: true }),
  { kind: "same" },
  "未勾选零售价时，价格差异不算变化"
);
assert.deepEqual(
  describeSyncTargetChange(target({ retailPrice: 12, discountRate: 0 }), source, { syncRetailPrice: true, syncDiscountRate: false }),
  { kind: "same" }
);
assert.deepEqual(
  describeSyncTargetChange(target({ retailPrice: null }), source, both),
  { kind: "price", from: null, to: 12 }
);

// 建议折扣输入：百分比 ↔ 0~1
assert.deepEqual(parseSuggestedDiscountInput(""), { ok: true, rate: null }, "留空 = 未设置");
assert.deepEqual(parseSuggestedDiscountInput("  "), { ok: true, rate: null });
assert.deepEqual(parseSuggestedDiscountInput("0"), { ok: true, rate: 0 }, "0 = 明确无折扣，不能变成 null");
assert.deepEqual(parseSuggestedDiscountInput("20"), { ok: true, rate: 0.2 });
assert.deepEqual(parseSuggestedDiscountInput("12.5"), { ok: true, rate: 0.125 });
assert.deepEqual(parseSuggestedDiscountInput("100"), { ok: true, rate: 1 });
assert.deepEqual(parseSuggestedDiscountInput("100.01"), { ok: false });
assert.deepEqual(parseSuggestedDiscountInput("-1"), { ok: false });
assert.deepEqual(parseSuggestedDiscountInput("abc"), { ok: false });
assert.equal(formatSuggestedDiscountInput(null), "");
assert.equal(formatSuggestedDiscountInput(0), "0");
assert.equal(formatSuggestedDiscountInput(0.07), "7", "不得出现 7.000000000000001");
assert.equal(formatSuggestedDiscountInput(0.125), "12.5");
assert.equal(isSameSuggestedDiscount(null, null), true);
assert.equal(isSameSuggestedDiscount(null, 0), false, "未设置与 0 是两种不同的值");
assert.equal(isSameSuggestedDiscount(0.2, 0.2), true);
assert.equal(isSameSuggestedDiscount(0.2, 0.25), false);

// 标签任务：价格以任务为准，详情只补字段
const detail = {
  productCode: "P001",
  productName: "Detail Mug",
  itemNumber: "D-1",
  barcode: "111",
  grade: "A",
  localSupplierName: "Supplier",
  storePrice: { retailPrice: 1, discountRate: 0.9 },
} as unknown as Parameters<typeof buildPriceUpdateLabelJob>[1];

const productJob = buildPriceUpdateLabelJob(createTask({ kind: "LabelOnly", storeRetailPrice: 12, storeDiscountRate: 0 }), detail);
assert.equal(productJob.kind, "product");
assert.deepEqual(productJob.payload, {
  productName: "Detail Mug",
  itemNumber: "D-1",
  grade: "A",
  supplierName: "Supplier",
  barcode: "9300000000017",
  retailPrice: 12,
  discountRate: null,
});

const discountJob = buildPriceUpdateLabelJob(createTask({ kind: "LabelOnly", storeRetailPrice: 12, storeDiscountRate: 0.2 }), null);
assert.equal(discountJob.kind, "discount", "有折扣时用折扣标签");
assert.deepEqual(discountJob.payload, {
  productName: "Mug",
  itemNumber: "A-1",
  grade: null,
  supplierName: null,
  barcode: "9300000000017",
  retailPrice: 12,
  discountRate: 0.2,
});

console.log("price-updates/sync-and-discount.test.ts: ok");
