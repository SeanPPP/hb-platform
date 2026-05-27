import type {
  CreateProductWithPricesRequest,
  CreateProductWithPricesResult,
  LocalSupplierOption,
} from "@/modules/product-maintenance/types";

function getEnvelopeData(payload: unknown): unknown {
  if (payload && typeof payload === "object" && "data" in payload) {
    return (payload as { data?: unknown }).data;
  }
  return payload;
}

function normalizeLocalSupplierOption(raw: unknown): LocalSupplierOption {
  const item = (raw && typeof raw === "object" ? raw : {}) as Record<string, unknown>;
  const supplierCode = String(
    item.supplierCode ?? item.SupplierCode ?? item.localSupplierCode ?? item.LocalSupplierCode ?? ""
  ).trim();
  const supplierName = String(
    item.supplierName
      ?? item.SupplierName
      ?? item.localSupplierName
      ?? item.LocalSupplierName
      ?? item.name
      ?? item.Name
      ?? supplierCode
  ).trim();

  return {
    supplierCode,
    supplierName,
  };
}

export function normalizeActiveLocalSuppliersResponse(payload: unknown): LocalSupplierOption[] {
  const data = getEnvelopeData(payload);
  return Array.isArray(data)
    ? data.map(normalizeLocalSupplierOption).filter((item) => item.supplierCode)
    : [];
}

export function buildCreateProductWithPricesPayload(
  payload: CreateProductWithPricesRequest
): CreateProductWithPricesRequest {
  return {
    localSupplierCode: payload.localSupplierCode.trim(),
    itemNumber: payload.itemNumber.trim(),
    barcode: payload.barcode.trim(),
    productName: payload.productName.trim(),
    purchasePrice: payload.purchasePrice,
    retailPrice: payload.retailPrice,
    isSpecialProduct: payload.isSpecialProduct,
    isAutoPricing: payload.isAutoPricing,
  };
}

export function normalizeCreateProductWithPricesResult(
  payload: unknown
): CreateProductWithPricesResult {
  const data = (getEnvelopeData(payload) && typeof getEnvelopeData(payload) === "object"
    ? getEnvelopeData(payload)
    : {}) as Record<string, unknown>;
  const rawStoreProductCodes = data.storeProductCodes ?? data.StoreProductCodes;
  const storeProductCodes =
    rawStoreProductCodes && typeof rawStoreProductCodes === "object"
      ? Object.fromEntries(
          Object.entries(rawStoreProductCodes as Record<string, unknown>).map(([key, value]) => [
            key,
            String(value ?? ""),
          ])
        )
      : {};

  return {
    productCode: String(data.productCode ?? data.ProductCode ?? ""),
    storeProductCodes,
  };
}
