import type { AxiosRequestConfig } from "axios";
import { isAxiosError } from "axios";
import { apiClient } from "@/shared/api/client";
import { useDeviceStore } from "@/store/device-store";
import type {
  CreateProductWithPricesRequest,
  CreateProductWithPricesResult,
  CreateSetCodeRequest,
  LocalSupplierOption,
  MultiCodeEditableItem,
  ProductDetail,
  ProductCodePage,
  ProductLookupItem,
  ProductSetCodeItem,
  StoreClearancePriceItem,
  StorePriceEditable,
  StoreProductLookupRequest,
  EvaluateAutoPricingRequest,
  EvaluateAutoPricingResult,
  UpdateSetCodeRequest,
  UpdateProductTypeRequest,
  UpdateProductTypeResult,
  UpdateMultiCodeRequest,
  UpdateStorePriceRequest,
  UpsertClearancePriceRequest,
} from "@/modules/product-maintenance/types";
import {
  buildCreateProductWithPricesPayload,
  normalizeActiveLocalSuppliersResponse,
  normalizeCreateProductWithPricesResult,
} from "@/modules/product-maintenance/api-normalization";

const BASE_PATH = "/react/v1/store-product-maintenance";
const PRODUCTS_PATH = "/react/v1/products";
const ACTIVE_LOCAL_SUPPLIERS_PATH = "/react/v1/local-suppliers/active";

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

function normalizeDiscountRate(value: unknown): number | null {
  const numeric = toNumber(value);
  if (numeric == null || numeric < 0) {
    return null;
  }

  if (numeric <= 1) {
    return numeric;
  }

  if (numeric <= 100) {
    return numeric / 100;
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
    discountRate: normalizeDiscountRate(data.discountRate ?? data.DiscountRate),
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
    setCodeId: String(data.setCodeId ?? data.SetCodeId ?? ""),
    storeCode: (data.storeCode ?? data.StoreCode ?? null) as string | null,
    productCode: (data.productCode ?? data.ProductCode ?? null) as string | null,
    multiCodeProductCode: (data.multiCodeProductCode ?? data.MultiCodeProductCode ?? null) as string | null,
    storeMultiCodeProductCode:
      (data.storeMultiCodeProductCode ?? data.StoreMultiCodeProductCode ?? null) as string | null,
    barcode: (data.barcode ?? data.Barcode ?? null) as string | null,
    purchasePrice: toNumber(data.purchasePrice ?? data.PurchasePrice),
    retailPrice: toNumber(data.retailPrice ?? data.RetailPrice),
    discountRate: normalizeDiscountRate(data.discountRate ?? data.DiscountRate),
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
    setCodeCount: Number(data.setCodeCount ?? data.SetCodeCount ?? 0),
    multiCodeCount: Number(data.multiCodeCount ?? data.MultiCodeCount ?? 0),
    codesIncluded: Boolean(data.codesIncluded ?? data.CodesIncluded),
  };
}

function normalizeAutoPricingEvaluation(payload: unknown): EvaluateAutoPricingResult {
  const data = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;
  return {
    productCode: String(data.productCode ?? data.ProductCode ?? ""),
    storeCode: (data.storeCode ?? data.StoreCode ?? null) as string | null,
    storePriceUuid: (data.storePriceUuid ?? data.StorePriceUuid ?? null) as string | null,
    currentRetailPrice: toNumber(data.currentRetailPrice ?? data.CurrentRetailPrice),
    recalculatedRetailPrice: toNumber(data.recalculatedRetailPrice ?? data.RecalculatedRetailPrice),
    currentRetailPriceFormatted: String(
      data.currentRetailPriceFormatted ?? data.CurrentRetailPriceFormatted ?? ""
    ),
    recalculatedRetailPriceFormatted: String(
      data.recalculatedRetailPriceFormatted ?? data.RecalculatedRetailPriceFormatted ?? ""
    ),
    discountRate: normalizeDiscountRate(data.discountRate ?? data.DiscountRate),
    isAutoPricing: Boolean(data.isAutoPricing ?? data.IsAutoPricing),
    hasValidPurchasePrice: Boolean(data.hasValidPurchasePrice ?? data.HasValidPurchasePrice),
    shouldUpdate: Boolean(data.shouldUpdate ?? data.ShouldUpdate),
  };
}

function normalizeProductTypeUpdate(payload: unknown): UpdateProductTypeResult {
  const data = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;
  return {
    productCode: String(data.productCode ?? data.ProductCode ?? ""),
    productType: Number(data.productType ?? data.ProductType ?? 0),
    productTypeLabel: (data.productTypeLabel ?? data.ProductTypeLabel ?? null) as string | null,
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

export async function fetchActiveLocalSuppliers(): Promise<LocalSupplierOption[]> {
  const response = await apiClient.get(ACTIVE_LOCAL_SUPPLIERS_PATH, buildRequestConfig());
  return normalizeActiveLocalSuppliersResponse(response.data);
}

export async function createProductWithPrices(
  payload: CreateProductWithPricesRequest
): Promise<CreateProductWithPricesResult> {
  const response = await apiClient.post(
    `${PRODUCTS_PATH}/create-with-prices`,
    buildCreateProductWithPricesPayload(payload),
    buildRequestConfig()
  );
  return normalizeCreateProductWithPricesResult(response.data);
}

export async function getProductDetail(
  productCode: string,
  storeCode?: string | null,
  options?: { includeCodes?: boolean }
): Promise<ProductDetail> {
  console.log("[product-maintenance-api] detail request", { productCode, storeCode, options });
  const response = await apiClient.get(
    `${BASE_PATH}/${encodeURIComponent(productCode)}`,
    {
      ...buildRequestConfig(),
      params: {
        ...(storeCode ? { storeCode } : {}),
        ...(options?.includeCodes == null ? {} : { includeCodes: options.includeCodes }),
      },
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

export async function getProductFastDetail(
  productCode: string,
  storeCode?: string | null
): Promise<ProductDetail> {
  console.log("[product-maintenance-api] fast-detail request", { productCode, storeCode });
  const response = await apiClient.get(
    `${BASE_PATH}/${encodeURIComponent(productCode)}/fast-detail`,
    {
      ...buildRequestConfig(),
      params: {
        ...(storeCode ? { storeCode } : {}),
      },
    }
  );
  const detail = normalizeDetail(response.data);
  console.log("[product-maintenance-api] fast-detail response", {
    productCode: detail.productCode,
    productType: detail.productType,
    clearancePriceFound: Boolean(detail.clearancePrice),
    multiCodeCount: detail.multiCodeCount,
    setCodeCount: detail.setCodeCount,
    codesIncluded: detail.codesIncluded,
  });
  return detail;
}

function normalizeCodePage<T>(
  payload: unknown,
  normalizeItem: (item: unknown) => T
): ProductCodePage<T> {
  const data = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;
  const rawItems = data.items ?? data.Items;
  const pageSize = Number(data.pageSize ?? data.PageSize ?? 50);
  const page = Number(data.page ?? data.Page ?? 1);
  const totalCount = Number(data.totalCount ?? data.TotalCount ?? 0);
  return {
    items: Array.isArray(rawItems) ? rawItems.map(normalizeItem) : [],
    totalCount,
    page,
    pageSize,
    hasMore: Boolean(data.hasMore ?? data.HasMore ?? page * pageSize < totalCount),
  };
}

export async function getProductCodes(
  productCode: string,
  storeCode: string | null | undefined,
  type: 1,
  page: number,
  pageSize: number,
  keyword?: string | null
): Promise<ProductCodePage<ProductSetCodeItem>>;
export async function getProductCodes(
  productCode: string,
  storeCode: string | null | undefined,
  type: 2,
  page: number,
  pageSize: number,
  keyword?: string | null
): Promise<ProductCodePage<MultiCodeEditableItem>>;
export async function getProductCodes(
  productCode: string,
  storeCode: string | null | undefined,
  type: 1 | 2,
  page: number,
  pageSize: number,
  keyword?: string | null
): Promise<ProductCodePage<ProductSetCodeItem> | ProductCodePage<MultiCodeEditableItem>> {
  const response = await apiClient.get(
    `${BASE_PATH}/${encodeURIComponent(productCode)}/codes`,
    {
      ...buildRequestConfig(),
      params: {
        ...(storeCode ? { storeCode } : {}),
        type,
        page,
        pageSize,
        ...(keyword?.trim() ? { keyword: keyword.trim() } : {}),
      },
    }
  );

  return type === 1
    ? normalizeCodePage(response.data, normalizeSetCodeItem)
    : normalizeCodePage(response.data, normalizeMultiCodeItem);
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

export async function evaluateAutoPricing(
  payload: EvaluateAutoPricingRequest
): Promise<EvaluateAutoPricingResult> {
  const response = await apiClient.post(
    `${BASE_PATH}/evaluate-auto-pricing`,
    payload,
    buildRequestConfig()
  );
  return normalizeAutoPricingEvaluation(response.data);
}

export async function updateProductType(
  productCode: string,
  payload: UpdateProductTypeRequest
): Promise<UpdateProductTypeResult> {
  const response = await apiClient.put(
    `${BASE_PATH}/products/${encodeURIComponent(productCode)}/type`,
    payload,
    buildRequestConfig()
  );
  return normalizeProductTypeUpdate(response.data);
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

export async function createSetCode(payload: CreateSetCodeRequest): Promise<ProductSetCodeItem> {
  const response = await apiClient.post(
    `${BASE_PATH}/set-codes`,
    payload,
    buildRequestConfig()
  );
  return normalizeSetCodeItem(response.data);
}

export async function updateSetCode(
  setCodeId: string,
  payload: UpdateSetCodeRequest
): Promise<ProductSetCodeItem> {
  const response = await apiClient.put(
    `${BASE_PATH}/set-codes/${encodeURIComponent(setCodeId)}`,
    payload,
    buildRequestConfig()
  );
  return normalizeSetCodeItem(response.data);
}

export async function deleteSetCode(setCodeId: string): Promise<boolean> {
  const response = await apiClient.delete(
    `${BASE_PATH}/set-codes/${encodeURIComponent(setCodeId)}`,
    buildRequestConfig()
  );
  return Boolean(response.data);
}

export async function upsertClearancePrice(
  productCode: string,
  payload: UpsertClearancePriceRequest
): Promise<StoreClearancePriceItem> {
  try {
    const response = await apiClient.put(
      `${BASE_PATH}/products/${encodeURIComponent(productCode)}/clearance-price`,
      payload,
      buildRequestConfig()
    );
    return normalizeClearancePrice(response.data)!;
  } catch (error) {
    console.error("[product-maintenance-api] clearance-price request failed", {
      productCode,
      payload,
      isAxiosError: isAxiosError(error),
      message: error instanceof Error ? error.message : String(error),
      responseStatus: isAxiosError(error) ? error.response?.status ?? null : null,
      responseData: isAxiosError(error) ? error.response?.data ?? null : null,
      requestHeaders: buildRequestConfig().headers ?? null,
    });
    throw error;
  }
}
