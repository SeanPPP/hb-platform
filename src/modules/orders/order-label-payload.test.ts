import { buildOrderLineLabelPayload } from "./order-label-payload";
import type { StoreOrderDetailLine } from "./types";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
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
assertEqual(normalPayload.productCode, "P001", "商品编码保持原值");
assertEqual(normalPayload.productName, "Test Product", "商品名称去除首尾空格");
assertEqual(normalPayload.itemNumber, "ITEM-001", "货号去除首尾空格");
assertEqual(normalPayload.barcode, "9300012345678", "条码去除首尾空格");
assertEqual(normalPayload.locationCode, "A-01-01-01", "货位去除首尾空格");
assertEqual(normalPayload.importPrice, 2.25, "进口价使用订单行进口价");
assertEqual(normalPayload.retailPrice, 3.5, "零售价使用订单行销售价");

const emptyOptionalPayload = buildOrderLineLabelPayload(
  makeLine({ itemNumber: " ", barcode: "", locationCode: "" })
);
assertEqual(emptyOptionalPayload.itemNumber, null, "空货号转为 null");
assertEqual(emptyOptionalPayload.barcode, null, "空条码转为 null");
assertEqual(emptyOptionalPayload.locationCode, null, "空货位转为 null");

const nameFallbackPayload = buildOrderLineLabelPayload(
  makeLine({ productCode: "P-FALLBACK", productName: " " })
);
assertEqual(nameFallbackPayload.productName, "P-FALLBACK", "商品名为空时回退商品编码");
