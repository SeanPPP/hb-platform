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
      reason: "required" | "priceInvalid" | "itemNumberTooShort" | "barcodeTooShort" | "retailPriceTooLow";
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
  const purchasePriceText = values.purchasePrice.trim();
  const retailPriceText = values.retailPrice.trim();
  const purchasePrice = parsePrice(values.purchasePrice);
  const retailPrice = parsePrice(values.retailPrice);

  if (!localSupplierCode || !itemNumber || !barcode || !productName || !purchasePriceText || !retailPriceText) {
    return { ok: false, reason: "required" };
  }

  if (itemNumber.length < 5) {
    return { ok: false, reason: "itemNumberTooShort" };
  }

  if (barcode.length < 7) {
    return { ok: false, reason: "barcodeTooShort" };
  }

  if (
    purchasePrice == null
    || retailPrice == null
    || purchasePrice < 0
    || retailPrice < 0
  ) {
    return { ok: false, reason: "priceInvalid" };
  }

  if (!values.isAutoPricing && retailPrice < purchasePrice * 2) {
    return { ok: false, reason: "retailPriceTooLow" };
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
