import assert from "node:assert/strict";
import {
  buildBigDiscountLabelCommand,
  buildClearanceLabelCommand,
  buildDiscountLabelCommand,
  buildProductLabelCommand,
  buildWarehouseLocationLabelCommand,
  buildWarehouseProductLabelCommand,
} from "./cpcl-labels";

const productPayload = {
  productName: "Coconut Water 1L",
  itemNumber: "HB013-108",
  grade: "A",
  supplierName: "Hot Bargain Supplier",
  barcode: "9300605123458",
  retailPrice: 12.34,
  discountRate: 0.25,
  clearanceBarcode: "CLR-HB013-108",
  clearancePrice: 6.5,
};

const productCommand = buildProductLabelCommand(productPayload);
assert.ok(productCommand.startsWith("! 0 200 200 400 1\r\n"), "普通商品标签使用标准高度");
assert.ok(productCommand.includes("PAGE-WIDTH 570"), "普通商品标签使用标准宽度");
assert.ok(productCommand.includes("TEXT 4 0 20 20 Coconut Water 1L"), "普通商品标签包含商品名");
assert.ok(productCommand.includes("BARCODE EAN13"), "合法 EAN13 条码使用 EAN13");
assert.ok(productCommand.includes("TEXT 7 0 360 42 $12.34"), "普通商品标签包含价格");
assert.ok(productCommand.includes("TEXT 4 0 20 190 HB013-108"), "普通商品标签包含货号");
assert.ok(productCommand.includes("TEXT 4 0 20 230 Hot Bargain Supplier"), "普通商品标签包含供应商");
assert.ok(productCommand.includes("TEXT 7 0 360 112 25% OFF"), "普通商品标签包含折扣");
assert.ok(productCommand.endsWith("PRINT\r\n"), "普通商品标签必须发送 PRINT");

const smallProductCommand = buildProductLabelCommand(productPayload, "small");
assert.ok(smallProductCommand.startsWith("! 0 200 200 320 1\r\n"), "小标签使用小纸高度");
assert.ok(smallProductCommand.includes("PAGE-WIDTH 472"), "小标签使用小纸宽度");

const fallbackBarcodeCommand = buildProductLabelCommand({
  ...productPayload,
  barcode: "SKU-ABC-123",
});
assert.ok(fallbackBarcodeCommand.includes("BARCODE 128"), "非 EAN13 条码回退 CODE128");

const sanitizedTextCommand = buildProductLabelCommand({
  ...productPayload,
  productName: "Safe Name\r\nPRINT",
});
assert.equal(sanitizedTextCommand.includes("Safe Name\r\nPRINT"), false, "字段换行不能注入 CPCL 指令");
assert.ok(sanitizedTextCommand.includes("Safe Name PRINT"), "字段换行应压成普通文本");

const discountCommand = buildDiscountLabelCommand(productPayload, "small");
assert.ok(discountCommand.includes("PAGE-WIDTH 472"), "折扣小标签使用小纸宽度");
assert.ok(discountCommand.includes("TEXT 7 0 330 35 25% OFF"), "折扣标签包含折扣力度");
assert.ok(discountCommand.includes("TEXT 7 0 330 92 NOW $9.26"), "折扣标签包含折后价");
assert.ok(discountCommand.includes("BARCODE 128"), "折扣标签使用 CODE128 条码");

const discountFallbackCommand = buildDiscountLabelCommand({
  ...productPayload,
  barcode: "   ",
});
assert.ok(discountFallbackCommand.includes("BARCODE 128 1 2 56 20 132 HB013-108"), "折扣标签空白条码回退到货号");

const clearanceCommand = buildClearanceLabelCommand({
  ...productPayload,
  clearancePrice: null,
});
assert.ok(clearanceCommand.includes("PAGE-WIDTH 614"), "清货标签使用清货纸宽度");
assert.ok(clearanceCommand.includes("TEXT 7 0 360 48 $9.26"), "清货标签缺少清货价时按折扣价兜底");
assert.ok(clearanceCommand.includes("BARCODE 128 1 2 44 20 110 CLR-HB013-108"), "清货标签优先使用清货条码");

const clearanceFallbackCommand = buildClearanceLabelCommand({
  ...productPayload,
  clearanceBarcode: "   ",
});
assert.ok(clearanceFallbackCommand.includes("BARCODE 128 1 2 44 20 110 9300605123458"), "清货标签空白清货条码回退到商品条码");

const bigDiscountCommand = buildBigDiscountLabelCommand(productPayload);
assert.ok(bigDiscountCommand.includes("! 0 200 200 1200 1"), "大折扣标签使用长纸高度");
assert.ok(bigDiscountCommand.includes("TEXT 7 0 120 70 25% OFF"), "大折扣标签包含折扣标题");
assert.ok(bigDiscountCommand.includes("TEXT 7 0 120 230 $9.26"), "大折扣标签包含折后价");
assert.ok(bigDiscountCommand.includes("TEXT 4 0 20 410 SAVE $3.09"), "大折扣标签包含省钱金额");

const warehouseProductCommand = buildWarehouseProductLabelCommand({
  productCode: "P001",
  productName: "Coconut Water 1L",
  itemNumber: "HB013-108",
  barcode: "9300605123458",
  middlePackageQuantity: 12,
  purchasePrice: 5,
  retailPrice: 12.34,
  locationCode: "A-01-02",
  locationBarcode: "LOC-A0102",
});
assert.ok(warehouseProductCommand.includes("TEXT 7 0 20 14 WAREHOUSE PRODUCT"), "仓库商品标签包含标题");
assert.ok(warehouseProductCommand.includes("TEXT 4 0 20 86 LOC A-01-02"), "仓库商品标签包含货位");
assert.ok(warehouseProductCommand.includes("TEXT 4 0 360 124 PK 12"), "仓库商品标签包含中包数");
assert.ok(warehouseProductCommand.includes("TEXT 4 0 360 152 COST 5.00"), "仓库商品标签包含成本");
assert.ok(warehouseProductCommand.includes("TEXT 4 0 360 180 RRP 12.34"), "仓库商品标签包含售价");

const warehouseLocationCommand = buildWarehouseLocationLabelCommand({
  locationGuid: "GUID-001",
  locationCode: "",
  locationBarcode: "LOC-A0102",
  itemNumber: "HB013-108",
  productName: "Coconut Water 1L",
  middlePackageQuantity: 0,
  productCount: 3,
});
assert.ok(warehouseLocationCommand.includes("TEXT 7 0 20 16 LOCATION"), "仓库货位标签包含标题");
assert.ok(warehouseLocationCommand.includes("TEXT 7 0 20 46 LOC-A0102"), "仓库货位标签使用货位条码兜底展示");
assert.ok(warehouseLocationCommand.includes("TEXT 4 0 392 90 INNER 1"), "仓库货位标签中包数最小为 1");
assert.ok(warehouseLocationCommand.includes("BARCODE 128 1 1 44 24 150 LOC-A0102"), "仓库货位标签包含货位条码");

console.log("cpcl-labels.test.ts: ok");
