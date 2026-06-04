import type { WarehouseProductPatchRequest } from "@/modules/warehouse/types";

export interface WarehouseProductFormSnapshot {
  purchasePrice: string;
  retailPrice: string;
  domesticPrice: string;
  stockQuantity: string;
  middlePackageQuantity: string;
  packingQuantity: string;
  volume: string;
  grade: string;
  warehouseIsActive: boolean;
}

export type WarehouseProductPatchField =
  | "purchasePrice"
  | "retailPrice"
  | "domesticPrice"
  | "stockQuantity"
  | "middlePackageQuantity"
  | "packingQuantity"
  | "volume"
  | "grade"
  | "warehouseIsActive";

interface WarehouseProductPatchOptions {
  field?: WarehouseProductPatchField;
  statusOnly?: boolean;
  syncStoreRetailPrices?: boolean;
}

export function buildWarehouseProductPatchRequest(
  form: WarehouseProductFormSnapshot,
  parseNullableNumber: (value: string) => number | null,
  options?: WarehouseProductPatchOptions
): WarehouseProductPatchRequest {
  if (options?.statusOnly || options?.field === "warehouseIsActive") {
    return { warehouseIsActive: form.warehouseIsActive };
  }

  if (options?.field) {
    switch (options.field) {
      case "purchasePrice": {
        const purchasePrice = parseNullableNumber(form.purchasePrice);
        return { purchasePrice, importPrice: purchasePrice };
      }
      case "retailPrice": {
        const retailPrice = parseNullableNumber(form.retailPrice);
        return {
          retailPrice,
          oemPrice: retailPrice,
          syncStoreRetailPrices: options.syncStoreRetailPrices ?? false,
        };
      }
      case "domesticPrice":
        return { domesticPrice: parseNullableNumber(form.domesticPrice) };
      case "stockQuantity":
        return { stockQuantity: parseNullableNumber(form.stockQuantity) };
      case "middlePackageQuantity":
        return { middlePackageQuantity: parseNullableNumber(form.middlePackageQuantity) };
      case "packingQuantity":
        return { packingQuantity: parseNullableNumber(form.packingQuantity) };
      case "volume":
        return { volume: parseNullableNumber(form.volume) };
      case "grade":
        return { grade: form.grade || null };
    }
  }

  return {
    purchasePrice: parseNullableNumber(form.purchasePrice),
    importPrice: parseNullableNumber(form.purchasePrice),
    retailPrice: parseNullableNumber(form.retailPrice),
    oemPrice: parseNullableNumber(form.retailPrice),
    domesticPrice: parseNullableNumber(form.domesticPrice),
    stockQuantity: parseNullableNumber(form.stockQuantity),
    middlePackageQuantity: parseNullableNumber(form.middlePackageQuantity),
    packingQuantity: parseNullableNumber(form.packingQuantity),
    volume: parseNullableNumber(form.volume),
    grade: form.grade || null,
    warehouseIsActive: form.warehouseIsActive,
  };
}

export function isWarehouseStatusOnlyPatch(patch: Partial<WarehouseProductFormSnapshot>) {
  const keys = Object.keys(patch);
  return keys.length === 1 && keys[0] === "warehouseIsActive";
}
