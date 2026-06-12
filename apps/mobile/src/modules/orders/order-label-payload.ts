import type { ProductLabelPrintPayload } from "@/modules/printer/types";
import type { StoreOrderDetailLine } from "./types";

function cleanOptionalText(value?: string | null) {
  const trimmed = value?.trim();
  return trimmed ? trimmed : null;
}

function cleanRequiredText(value?: string | null, fallback = "") {
  return cleanOptionalText(value) ?? fallback;
}

function cleanOptionalNumber(value?: number | null) {
  return typeof value === "number" && Number.isFinite(value) ? value : null;
}

export function buildOrderLineLabelPayload(
  line: StoreOrderDetailLine
): ProductLabelPrintPayload {
  const productCode = cleanRequiredText(line.productCode);

  // 订单明细打印改为走普通商品标签格式，仅映射普通标签所需字段。
  return {
    productName: cleanRequiredText(line.productName, productCode),
    itemNumber: cleanOptionalText(line.itemNumber),
    grade: null,
    supplierName: null,
    barcode: cleanOptionalText(line.barcode),
    retailPrice: cleanOptionalNumber(line.price),
    discountRate: null,
    clearanceBarcode: null,
    clearancePrice: null,
  };
}
