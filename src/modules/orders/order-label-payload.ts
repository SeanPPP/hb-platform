import type { WarehouseProductLabelPrintPayload } from "@/modules/printer/types";
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
): WarehouseProductLabelPrintPayload {
  const productCode = cleanRequiredText(line.productCode);

  return {
    productCode,
    productName: cleanRequiredText(line.productName, productCode),
    itemNumber: cleanOptionalText(line.itemNumber),
    barcode: cleanOptionalText(line.barcode),
    supplierName: null,
    middlePackageQuantity: null,
    purchasePrice: null,
    retailPrice: cleanOptionalNumber(line.price),
    domesticPrice: null,
    oemPrice: null,
    importPrice: cleanOptionalNumber(line.importPrice),
    locationCode: cleanOptionalText(line.locationCode),
    locationBarcode: null,
  };
}
