import type { AxiosRequestConfig } from "axios";
import { apiClient } from "@/shared/api/client";
import { useDeviceStore } from "@/store/device-store";
import type {
  MultiCodeEditableItem,
  ProductDetail,
  ProductLookupItem,
  ProductSetCodeItem,
  StoreClearancePriceItem,
  StorePriceEditable,
  StoreProductLookupRequest,
  UpdateMultiCodeRequest,
  UpdateStorePriceRequest,
} from "@/modules/product-maintenance/types";

const BASE_PATH = "/react/v1/store-product-maintenance";

function buildRequestConfig(): AxiosRequestConfig {
  const session = useDeviceStore.getState().session;
  if (!session?.hardwareId || !session.authCode) {
    return {};
  }

  return {
    headers: {
      "X-Device-Id": session.hardwareId,
      "X-Auth-Code": session.authCode,
    },
  };
}

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

function normalizeLookupItem(payload: unknown): ProductLookupItem {
  const data = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;
  return {
    productCode: String(data.productCode ?? data.ProductCode ?? ""),
    productName: String(data.productName ?? data.ProductName ?? ""),
    itemNumber: (data.itemNumber ?? data.ItemNumber ?? null) as string | null,
    barcode: (data.barcode ?? data.Barcode ?? null) as string | null,
    productImage: (data.productImage ?? data.ProductImage ?? null) as string | null,
    matchSource: (data.matchSource ?? data.MatchSource ?? null) as string | null,
    matchValue: (data.matchValue ?? data.MatchValue ?? null) as string | null,
    productTypeLabel: (data.productTypeLabel ?? data.ProductTypeLabel ?? null) as string | null,
    grade: (data.grade ?? data.Grade ?? null) as string | null,
  };
}

function normalizeStorePrice(payload: unknown): StorePriceEditable | null {
  if (!payload || typeof payload !== "object") {
    return null;
  }

  const data = payload as Record<string, unknown>;
  return {
    uuid: String(data.uuid ?? data.Uuid ?? ""),
    storeCode: (data.storeCode ?? data.StoreCode ?? null) as string | null,
    storeName: (data.storeName ?? data.StoreName ?? null) as string | null,
    productCode: (data.productCode ?? data.ProductCode ?? null) as string | null,
    storeProductCode: (data.storeProductCode ?? data.StoreProductCode ?? null) as string | null,
    supplierCode: (data.supplierCode ?? data.SupplierCode ?? null) as string | null,
    purchasePrice: toNumber(data.purchasePrice ?? data.PurchasePrice),
    retailPrice: toNumber(data.retailPrice ?? data.RetailPrice),
    discountRate: toNumber(data.discountRate ?? data.DiscountRate),
    isAutoPricing: Boolean(data.isAutoPricing ?? data.IsAutoPricing),
    isSpecialProduct: Boolean(data.isSpecialProduct ?? data.IsSpecialProduct),
    isActive: Boolean(data.isActive ?? data.IsActive),
    rate: toNumber(data.rate ?? data.Rate),
    strategySourceLabel: (data.strategySourceLabel ?? data.StrategySourceLabel ?? null) as string | null,
    strategyRuleLabel: (data.strategyRuleLabel ?? data.StrategyRuleLabel ?? null) as string | null,
  };
}

function normalizeMultiCodeItem(payload: unknown): MultiCodeEditableItem {
  const data = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;
  return {
    uuid: String(data.uuid ?? data.Uuid ?? ""),
    storeCode: (data.storeCode ?? data.StoreCode ?? null) as string | null,
    productCode: (data.productCode ?? data.ProductCode ?? null) as string | null,
    multiCodeProductCode: (data.multiCodeProductCode ?? data.MultiCodeProductCode ?? null) as string | null,
    storeMultiCodeProductCode:
      (data.storeMultiCodeProductCode ?? data.StoreMultiCodeProductCode ?? null) as string | null,
    barcode: (data.barcode ?? data.Barcode ?? null) as string | null,
    purchasePrice: toNumber(data.purchasePrice ?? data.PurchasePrice),
    retailPrice: toNumber(data.retailPrice ?? data.RetailPrice),
    discountRate: toNumber(data.discountRate ?? data.DiscountRate),
    isAutoPricing: Boolean(data.isAutoPricing ?? data.IsAutoPricing),
    isSpecialProduct: Boolean(data.isSpecialProduct ?? data.IsSpecialProduct),
    isActive: Boolean(data.isActive ?? data.IsActive),
    rate: toNumber(data.rate ?? data.Rate),
    strategySourceLabel: (data.strategySourceLabel ?? data.StrategySourceLabel ?? null) as string | null,
    strategyRuleLabel: (data.strategyRuleLabel ?? data.StrategyRuleLabel ?? null) as string | null,
  };
}

function normalizeSetCodeItem(payload: unknown): ProductSetCodeItem {
  const data = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;
  return {
    setCodeId: String(data.setCodeId ?? data.SetCodeId ?? ""),
    productCode: String(data.productCode ?? data.ProductCode ?? ""),
    setProductCode: String(data.setProductCode ?? data.SetProductCode ?? ""),
    setItemNumber: String(data.setItemNumber ?? data.SetItemNumber ?? ""),
    setBarcode: (data.setBarcode ?? data.SetBarcode ?? null) as string | null,
    setPurchasePrice: toNumber(data.setPurchasePrice ?? data.SetPurchasePrice),
    setRetailPrice: toNumber(data.setRetailPrice ?? data.SetRetailPrice),
    setQuantity: Number(data.setQuantity ?? data.SetQuantity ?? 0),
    setType: Number(data.setType ?? data.SetType ?? 0),
    setTypeDescription: (data.setTypeDescription ?? data.SetTypeDescription ?? null) as string | null,
    isActive: Boolean(data.isActive ?? data.IsActive),
  };
}

function normalizeClearancePrice(payload: unknown): StoreClearancePriceItem | null {
  if (!payload || typeof payload !== "object") {
    return null;
  }

  const data = payload as Record<string, unknown>;
  return {
    uuid: String(data.uuid ?? data.Uuid ?? ""),
    storeCode: (data.storeCode ?? data.StoreCode ?? null) as string | null,
    storeName: (data.storeName ?? data.StoreName ?? null) as string | null,
    productCode: (data.productCode ?? data.ProductCode ?? null) as string | null,
    clearanceBarcode: (data.clearanceBarcode ?? data.ClearanceBarcode ?? null) as string | null,
    clearancePrice: toNumber(data.clearancePrice ?? data.ClearancePrice),
  };
}

function normalizeDetail(payload: unknown): ProductDetail {
  const data = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;
  const setCodesRaw = data.setCodes ?? data.SetCodes;
  const multiCodesRaw = data.multiCodes ?? data.MultiCodes;
  return {
    productCode: String(data.productCode ?? data.ProductCode ?? ""),
    productName: String(data.productName ?? data.ProductName ?? ""),
    itemNumber: (data.itemNumber ?? data.ItemNumber ?? null) as string | null,
    barcode: (data.barcode ?? data.Barcode ?? null) as string | null,
    productImage: (data.productImage ?? data.ProductImage ?? null) as string | null,
    productType: toNumber(data.productType ?? data.ProductType),
    productTypeLabel: (data.productTypeLabel ?? data.ProductTypeLabel ?? null) as string | null,
    grade: (data.grade ?? data.Grade ?? null) as string | null,
    localSupplierCode: (data.localSupplierCode ?? data.LocalSupplierCode ?? null) as string | null,
    localSupplierName: (data.localSupplierName ?? data.LocalSupplierName ?? null) as string | null,
    storePrice: normalizeStorePrice(data.storePrice ?? data.StorePrice),
    clearancePrice: normalizeClearancePrice(data.clearancePrice ?? data.ClearancePrice),
    setCodes: Array.isArray(setCodesRaw) ? setCodesRaw.map(normalizeSetCodeItem) : [],
    multiCodes: Array.isArray(multiCodesRaw) ? multiCodesRaw.map(normalizeMultiCodeItem) : [],
  };
}

export async function lookupProducts(
  payload: StoreProductLookupRequest
): Promise<ProductLookupItem[]> {
  console.log("[product-maintenance-api] lookup request", payload);
  const response = await apiClient.post(BASE_PATH + "/lookup", payload, buildRequestConfig());
  const items = Array.isArray(response.data) ? response.data.map(normalizeLookupItem) : [];
  console.log("[product-maintenance-api] lookup response", {
    keyword: payload.keyword,
    count: items.length,
  });
  return items;
}

export async function getProductDetail(
  productCode: string,
  storeCode?: string | null
): Promise<ProductDetail> {
  console.log("[product-maintenance-api] detail request", { productCode, storeCode });
  const response = await apiClient.get(
    `${BASE_PATH}/${encodeURIComponent(productCode)}`,
    {
      ...buildRequestConfig(),
      params: storeCode ? { storeCode } : undefined,
    }
  );
  const detail = normalizeDetail(response.data);
  console.log("[product-maintenance-api] detail response", {
    productCode: detail.productCode,
    clearancePriceFound: Boolean(detail.clearancePrice),
    multiCodeCount: detail.multiCodes.length,
    setCodeCount: detail.setCodes.length,
  });
  return detail;
}

export async function updateStorePrice(
  uuid: string,
  payload: UpdateStorePriceRequest
): Promise<StorePriceEditable> {
  const response = await apiClient.put(
    `${BASE_PATH}/store-prices/${encodeURIComponent(uuid)}`,
    payload,
    buildRequestConfig()
  );
  return normalizeStorePrice(response.data)!;
}

export async function updateMultiCode(
  uuid: string,
  payload: UpdateMultiCodeRequest
): Promise<MultiCodeEditableItem> {
  const response = await apiClient.put(
    `${BASE_PATH}/multi-codes/${encodeURIComponent(uuid)}`,
    payload,
    buildRequestConfig()
  );
  return normalizeMultiCodeItem(response.data);
}
