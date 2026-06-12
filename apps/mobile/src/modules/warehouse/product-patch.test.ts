import assert from "node:assert/strict";
import { buildWarehouseProductPatchRequest, type WarehouseProductFormSnapshot } from "./product-patch";

const baseForm: WarehouseProductFormSnapshot = {
  purchasePrice: "4.28",
  retailPrice: "11.99",
  domesticPrice: "",
  stockQuantity: "140",
  middlePackageQuantity: "0",
  packingQuantity: "450",
  volume: "0.11",
  grade: "D",
  warehouseIsActive: true,
};

function parseNullableNumber(value: string) {
  if (!value.trim()) {
    return null;
  }
  return Number(value);
}

function run() {
  const statusOnly = buildWarehouseProductPatchRequest(
    { ...baseForm, warehouseIsActive: false },
    parseNullableNumber,
    { statusOnly: true }
  );

  assert.deepEqual(
    statusOnly,
    { warehouseIsActive: false },
    "上下架弹窗保存必须只提交仓库上下架字段，不能夹带价格、库存、等级"
  );

  const fullPatch = buildWarehouseProductPatchRequest(baseForm, parseNullableNumber);
  assert.equal(fullPatch.warehouseIsActive, true, "普通业务保存仍应携带当前仓库上下架状态");
  assert.equal(fullPatch.purchasePrice, 4.28, "普通业务保存应保留原有价格字段");
  assert.equal(fullPatch.packingQuantity, 450, "普通业务保存应保留原有装箱数字段");

  const importPricePatch = buildWarehouseProductPatchRequest(baseForm, parseNullableNumber, {
    field: "purchasePrice",
  });

  assert.deepEqual(
    importPricePatch,
    { purchasePrice: 4.28, importPrice: 4.28 },
    "进口价保存必须只提交 Product.PurchasePrice 与 WarehouseProduct.ImportPrice"
  );

  const retailPricePatch = buildWarehouseProductPatchRequest(baseForm, parseNullableNumber, {
    field: "retailPrice",
    syncStoreRetailPrices: true,
  });

  assert.deepEqual(
    retailPricePatch,
    { retailPrice: 11.99, oemPrice: 11.99, syncStoreRetailPrices: true },
    "零售价保存必须带上是否同步分店价格表的明确意图"
  );

  console.log("product-patch.test.ts: ok");
}

run();
