import type { CreateProductWithPricesRequest } from "@/modules/product-maintenance/types";

export interface CreateProductFormValues {
  localSupplierCode: string;
  itemNumber: string;
  barcode: string;
  productName: string;
  purchasePrice: string;
  retailPrice: string;
  isSpecialProduct: boolean;
  isAutoPricing: boolean;
}

export type CreateProductValidationResult =
  | {
      ok: true;
      payload: CreateProductWithPricesRequest;
    }
  | {
      ok: false;
      reason: "required" | "priceInvalid" | "manualRetailPriceInvalid";
    };

function parsePrice(value: string): number | null {
  const trimmed = value.trim();
  if (!trimmed) {
    return null;
  }

  const parsed = Number(trimmed);
  return Number.isFinite(parsed) ? parsed : null;
}

export function validateCreateProductForm(
  values: CreateProductFormValues
): CreateProductValidationResult {
  const localSupplierCode = values.localSupplierCode.trim();
  const itemNumber = values.itemNumber.trim();
  const barcode = values.barcode.trim();
  const productName = values.productName.trim();
  const purchasePrice = parsePrice(values.purchasePrice);
  const retailPrice = parsePrice(values.retailPrice);

  if (!localSupplierCode || !itemNumber || !barcode || !productName) {
    return { ok: false, reason: "required" };
  }

  if (
    purchasePrice == null
    || retailPrice == null
    || purchasePrice < 0
    || retailPrice < 0
  ) {
    return { ok: false, reason: "priceInvalid" };
  }

  if (!values.isAutoPricing && retailPrice <= 0) {
    return { ok: false, reason: "manualRetailPriceInvalid" };
  }

  return {
    ok: true,
    payload: {
      localSupplierCode,
      itemNumber,
      barcode,
      productName,
      purchasePrice,
      retailPrice,
      isSpecialProduct: values.isSpecialProduct,
      isAutoPricing: values.isAutoPricing,
    },
  };
}
