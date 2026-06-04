import type { WarehouseProduct, WarehouseShelfStatus } from "@/modules/warehouse/types";

function toNumber(value: unknown): number | null {
  if (typeof value === "number" && Number.isFinite(value)) {
    return value;
  }
  if (typeof value === "string" && value.trim()) {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : null;
  }
  return null;
}

function toBoolean(value: unknown, fallback = false): boolean {
  if (typeof value === "boolean") {
    return value;
  }
  if (typeof value === "number") {
    return value !== 0;
  }
  if (typeof value === "string") {
    const normalized = value.trim().toLowerCase();
    if (!normalized) {
      return fallback;
    }
    if (["true", "1", "yes", "y"].includes(normalized)) {
      return true;
    }
    if (["false", "0", "no", "n"].includes(normalized)) {
      return false;
    }
  }
  return fallback;
}

export function resolveWarehouseShelfStatus(warehouseIsActive: boolean): WarehouseShelfStatus {
  return warehouseIsActive ? "onShelf" : "offShelf";
}

export function normalizeWarehouseProduct(payload: unknown): WarehouseProduct {
  const data = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;
  const warehouseIsActive = toBoolean(
    data.warehouseIsActive ?? data.WarehouseIsActive ?? data.isActive ?? data.IsActive,
    true
  );
  return {
    productCode: String(data.productCode ?? data.ProductCode ?? ""),
    productName: String(data.productName ?? data.ProductName ?? ""),
    itemNumber: (data.itemNumber ?? data.ItemNumber ?? null) as string | null,
    barcode: (data.barcode ?? data.Barcode ?? null) as string | null,
    productImage: (data.productImage ?? data.ProductImage ?? null) as string | null,
    productType: toNumber(data.productType ?? data.ProductType),
    productTypeLabel: (data.productTypeLabel ?? data.ProductTypeLabel ?? null) as string | null,
    localSupplierCode: (data.localSupplierCode ?? data.LocalSupplierCode ?? null) as string | null,
    supplierCode: (data.supplierCode ?? data.SupplierCode ?? null) as string | null,
    supplierName: (data.supplierName ?? data.SupplierName ?? null) as string | null,
    grade: (data.grade ?? data.Grade ?? null) as string | null,
    warehouseIsActive,
    warehouseStatus: resolveWarehouseShelfStatus(warehouseIsActive),
    isActive: warehouseIsActive,
    purchasePrice: toNumber(data.purchasePrice ?? data.PurchasePrice),
    retailPrice: toNumber(data.retailPrice ?? data.RetailPrice),
    domesticPrice: toNumber(data.domesticPrice ?? data.DomesticPrice),
    oemPrice: toNumber(data.oEMPrice ?? data.OEMPrice ?? data.oemPrice ?? data.OemPrice),
    importPrice: toNumber(data.importPrice ?? data.ImportPrice),
    stockQuantity: toNumber(data.stockQuantity ?? data.StockQuantity),
    middlePackageQuantity: toNumber(data.middlePackageQuantity ?? data.MiddlePackageQuantity),
    packingQuantity: toNumber(data.packingQuantity ?? data.PackingQuantity),
    volume: toNumber(data.volume ?? data.Volume),
    locationGuid: (data.locationGuid ?? data.LocationGuid ?? null) as string | null,
    locationCode: (data.locationCode ?? data.LocationCode ?? null) as string | null,
    locationBarcode: (data.locationBarcode ?? data.LocationBarcode ?? null) as string | null,
    updatedAt: (data.updatedAt ?? data.UpdatedAt ?? null) as string | null,
  };
}
