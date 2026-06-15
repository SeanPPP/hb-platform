import type { WarehouseLocationDetail, WarehouseLocationPrintPayload } from "./types";

function firstBoundProduct(location: WarehouseLocationDetail) {
  return location.products.find((item) => item.productCode || item.itemNumber || item.productName) ?? null;
}

export function normalizeLocationMiddlePackageQuantity(value: number | null | undefined) {
  // 标签上的中包数必须可直接读，后端空值或旧数据 0 都按 1 处理。
  return typeof value === "number" && Number.isFinite(value) && value > 0 ? value : 1;
}

export function buildWarehouseLocationLabelPayload(
  location: WarehouseLocationDetail
): WarehouseLocationPrintPayload {
  const product = firstBoundProduct(location);
  return {
    locationGuid: location.locationGuid,
    locationCode: location.locationCode,
    locationBarcode: location.locationBarcode,
    itemNumber: product?.itemNumber ?? product?.productCode ?? null,
    productName: product?.productName ?? null,
    middlePackageQuantity: normalizeLocationMiddlePackageQuantity(product?.middlePackageQuantity),
    productCount: location.products.length,
  };
}
