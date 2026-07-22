import assert from "node:assert/strict";
import {
  buildBigDiscountLabelCommand,
  buildClearanceLabelCommand,
  buildEmployeeCashierBarcodeLabelCommand,
  buildDiscountLabelCommand,
  buildProductLabelCommand,
  buildWarehouseLocationLabelCommand,
  buildWarehouseProductLabelCommand,
} from "./cpcl-labels";

const employeeBarcodeCommand = buildEmployeeCashierBarcodeLabelCommand({
  employeeName: "管理员",
  barcode: "2912345678906",
});
assert.ok(employeeBarcodeCommand.includes("PAGE-WIDTH 570"), "员工条码标签使用当前标准标签宽度");
assert.ok(employeeBarcodeCommand.includes("TEXT 7 0 20 30 管理员"), "员工条码标签包含员工姓名");
assert.ok(
  employeeBarcodeCommand.includes("BARCODE QR 201 104 M 2 U 8"),
  "员工条码标签必须使用居中的 CPCL 二维码"
);
assert.ok(employeeBarcodeCommand.includes("MA,2912345678906\r\nENDQR"), "二维码必须编码原始员工收银码");
assert.ok(employeeBarcodeCommand.includes("TEXT 4 0 207 326 2912345678906"), "二维码下方保留可读编号");
assert.equal(employeeBarcodeCommand.includes("BARCODE EAN13"), false, "员工标签不再输出 EAN13 条码");
assert.equal(employeeBarcodeCommand.includes("BARCODE-TEXT"), false, "二维码不使用一维条码文本命令");
assert.ok(employeeBarcodeCommand.endsWith("PRINT\r\n"), "员工条码标签必须发送 PRINT");
assert.throws(
  () => buildEmployeeCashierBarcodeLabelCommand({
    employeeName: "管理员",
    barcode: "2912345678906\r\nPRINT",
  }),
  /valid EAN13/,
  "员工二维码继续拒绝无效值和 CPCL 指令注入"
);

const productPayload = {
  productName: "Coconut Water 1L",
  itemNumber: "HB013-108",
  grade: "a+",
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
assert.ok(productCommand.includes("TEXT 4 0 5 5 Coconut Water 1L"), "普通商品标签商品名对齐 Android 左上角");
assert.ok(productCommand.includes("TEXT 4 0 5 120 HB013-108"), "普通商品标签货号对齐 Android 条码上方");
assert.ok(productCommand.includes("TEXT 4 0 123 118 H.B.S"), "普通商品标签供应商缩写对齐货号右侧");
assert.ok(productCommand.includes("BARCODE-TEXT 7 0 5"), "普通商品标签启用条码文本");
assert.ok(productCommand.includes("BARCODE EAN13 1 2 30 5 145 9300605123458"), "合法 EAN13 条码使用 Android 坐标");
assert.ok(productCommand.includes("TEXT 4 0 466 30 $"), "普通商品标签价格货币符号右上对齐");
assert.ok(productCommand.includes("TEXT 7 0 478 30 12"), "普通商品标签价格整数右上对齐");
assert.ok(productCommand.includes("TEXT 4 0 534 68 ."), "普通商品标签价格小数点贴近整数底部");
assert.ok(productCommand.includes("TEXT 4 0 546 30 34"), "普通商品标签价格小数右上对齐");
assert.ok(productCommand.includes("TEXT 4 0 358 175 25%OFF"), "普通商品标签折扣对齐 Android 底部");
assert.ok(productCommand.includes("TEXT 4 0 300 175 A"), "普通商品标签等级取大写首字母");
assert.match(productCommand, /TEXT 4 0 450 175 \d{4}\/\d{2}\/\d{2}/, "普通商品标签日期右对齐");
assert.ok(productCommand.endsWith("PRINT\r\n"), "普通商品标签必须发送 PRINT");

const smallProductCommand = buildProductLabelCommand(productPayload, "small");
assert.ok(smallProductCommand.startsWith("! 0 200 200 320 1\r\n"), "小标签使用小纸高度");
assert.ok(smallProductCommand.includes("PAGE-WIDTH 472"), "小标签使用小纸宽度");
assert.ok(smallProductCommand.includes("TEXT 4 0 448 30 34"), "小标签价格右侧跟随小纸宽");
assert.ok(smallProductCommand.includes("TEXT 4 0 260 175 25%OFF"), "小标签折扣跟随小纸宽");

const fallbackBarcodeCommand = buildProductLabelCommand({
  ...productPayload,
  barcode: "SKU-ABC-123",
});
assert.ok(fallbackBarcodeCommand.includes("BARCODE 128 1 2 30 5 145 SKU-ABC-123"), "非 EAN13 条码回退 CODE128 并保持 Android 坐标");

const paddedDiscountCommand = buildProductLabelCommand({
  ...productPayload,
  discountRate: 0.05,
});
assert.ok(paddedDiscountCommand.includes("TEXT 4 0 358 175 05%OFF"), "个位数折扣按 Android 样式补零");

const longNameCommand = buildProductLabelCommand({
  ...productPayload,
  productName: "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 SHOULD_NOT_PRINT",
  retailPrice: 1234.56,
});
const longNameLines = longNameCommand
  .split("\r\n")
  .filter((line) => line.startsWith("TEXT 4 0 5 5 ") || line.startsWith("TEXT 4 0 5 37 "));
assert.equal(longNameLines.length, 2, "长商品名最多输出两行");
assert.ok(longNameLines.every((line) => line.replace(/^TEXT 4 0 5 (5|37) /, "").length <= 33), "长商品名按价格左侧宽度裁剪");
assert.equal(longNameCommand.includes("SHOULD_NOT_PRINT"), false, "长商品名尾部不能覆盖右侧价格区");
assert.ok(longNameCommand.includes("TEXT 4 0 410 30 $"), "大价格仍保留右上价格块");

const blankFieldCommand = buildProductLabelCommand({
  ...productPayload,
  itemNumber: " ",
  supplierName: null,
  barcode: " ",
  grade: null,
});
const blankFieldLines = blankFieldCommand.split("\r\n");
assert.ok(blankFieldLines.includes("TEXT 4 0 5 120  "), "空货号仍保留 Android 货号行位置");
assert.ok(blankFieldLines.includes("TEXT 4 0 27 118  "), "空供应商仍按空货号宽度保留相对位置");
assert.equal(blankFieldCommand.includes("BARCODE-TEXT"), false, "空条码不输出条码文字");
assert.equal(blankFieldCommand.includes("BARCODE "), false, "空条码不输出条码命令");

const blankItemWithSupplierCommand = buildProductLabelCommand({
  ...productPayload,
  itemNumber: " ",
  barcode: " ",
});
assert.ok(blankItemWithSupplierCommand.includes("TEXT 4 0 27 118 H.B.S"), "空货号但有供应商时仍按空格宽度定位供应商");

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
