import { buildOrderLineLabelPayload } from "./order-label-payload";
import type { StoreOrderDetailLine } from "./types";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

function assertNotIn(key: string, value: object, label: string) {
  if (key in value) {
    throw new Error(`${label}: unexpected key ${key}`);
  }
}

function makeLine(overrides: Partial<StoreOrderDetailLine> = {}): StoreOrderDetailLine {
  return {
    detailGUID: "detail-1",
    productCode: "P001",
    itemNumber: " ITEM-001 ",
    barcode: " 9300012345678 ",
    productName: " Test Product ",
    quantity: 2,
    allocQuantity: 1,
    price: 3.5,
    amount: 7,
    importPrice: 2.25,
    importAmount: 2.25,
    minOrderQuantity: 1,
    isActive: true,
    locationCode: " A-01-01-01 ",
    ...overrides,
  };
}

const normalPayload = buildOrderLineLabelPayload(makeLine());
assertEqual(normalPayload.productName, "Test Product", "商品名称去除首尾空格");
assertEqual(normalPayload.itemNumber, "ITEM-001", "货号去除首尾空格");
assertEqual(normalPayload.barcode, "9300012345678", "条码去除首尾空格");
assertEqual(normalPayload.retailPrice, 3.5, "零售价使用订单行销售价");
assertEqual(normalPayload.grade, null, "等级固定为空");
assertEqual(normalPayload.supplierName, null, "供应商固定为空");
assertEqual(normalPayload.discountRate, null, "折扣率固定为空");
assertEqual(normalPayload.clearanceBarcode, null, "清仓条码固定为空");
assertEqual(normalPayload.clearancePrice, null, "清仓价固定为空");
assertNotIn("productCode", normalPayload, "普通商品标签不携带商品编码字段");
assertNotIn("locationCode", normalPayload, "普通商品标签不携带货位字段");
assertNotIn("importPrice", normalPayload, "普通商品标签不携带进口价字段");

const emptyOptionalPayload = buildOrderLineLabelPayload(
  makeLine({ itemNumber: " ", barcode: "" })
);
assertEqual(emptyOptionalPayload.itemNumber, null, "空货号转为 null");
assertEqual(emptyOptionalPayload.barcode, null, "空条码转为 null");

const nameFallbackPayload = buildOrderLineLabelPayload(
  makeLine({ productCode: "P-FALLBACK", productName: " " })
);
assertEqual(nameFallbackPayload.productName, "P-FALLBACK", "商品名为空时回退商品编码");

const invalidPricePayload = buildOrderLineLabelPayload(
  makeLine({ price: Number.NaN })
);
assertEqual(invalidPricePayload.retailPrice, null, "非法零售价转为 null");
