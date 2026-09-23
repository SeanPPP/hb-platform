import type { ScanLabelPrintTarget, ScanLabelResult } from "./types";

/** 快速打印只能使用本次在线扫码的唯一商品及同门店目标，不能猜测套码或沿用旧价格。 */
export function getVerifiedScanPrintTarget(
  result: ScanLabelResult,
  keyword: string,
  storeCode: string,
): ScanLabelPrintTarget | null {
  const candidate = result.candidates.length === 1 ? result.candidates[0] : null;
  const detail = result.detail;
  const target = result.printTarget;
  if (
    !candidate || !detail || !target ||
    candidate.productCode !== detail.productCode ||
    target.productCode !== detail.productCode ||
    target.storeCode !== storeCode ||
    (target.kind !== "clearance" &&
      (detail.storePrice?.storeCode !== storeCode || !detail.storePrice?.uuid.trim())) ||
    target.retailPrice == null ||
    !Number.isFinite(target.retailPrice) ||
    target.retailPrice <= 0 ||
    (target.discountRate != null &&
      (!Number.isFinite(target.discountRate) || target.discountRate < 0 || target.discountRate > 1))
  ) {
    return null;
  }

  const scannedCode = keyword.trim();
  const matchSource = candidate.matchSource;
  if (candidate.matchValue?.trim() !== scannedCode) return null;
  if (target.kind === "product") {
    return (matchSource === "ProductBarcode" || matchSource === "ItemNumber") &&
      [detail.barcode, detail.itemNumber, detail.productCode, detail.storePrice?.storeProductCode]
        .some((code) => code?.trim() === target.barcode)
      ? target : null;
  }
  if (target.barcode !== scannedCode || !target.codeId?.trim()) return null;
  if (target.kind === "set" && matchSource === "SetBarcode") return target;
  if (target.kind === "multi" && matchSource === "SetBarcode") return target;
  if (
    target.kind === "clearance" &&
    matchSource === "ClearanceBarcode" &&
    detail.clearancePrice?.storeCode === storeCode &&
    detail.clearancePrice.clearanceBarcode?.trim() === scannedCode &&
    detail.clearancePrice.clearancePrice === target.retailPrice
  ) return target;
  return null;
}
