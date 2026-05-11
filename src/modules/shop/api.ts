import { getUserStoresApi } from "@/modules/auth/api";
import type {
  Store,
  StoreOrderCategoryNode,
  StoreOrderDynamicData,
  StoreOrderDynamicDataRequest,
  StoreOrderProductQuery,
  StoreOrderProductListResult,
  StoreOrderScanLookupResult,
  AddToCartPayload,
  UpdateCartQuantityPayload,
  StoreOrderCart,
} from "@/modules/shop/types";
import { apiClient } from "@/shared/api/client";

function normalizeProductPagedList(payload: Partial<StoreOrderProductListResult> | null | undefined): StoreOrderProductListResult {
  const normalizedPayload = payload as (Partial<StoreOrderProductListResult> & { pageNumber?: number }) | null | undefined;

  return {
    items: Array.isArray(normalizedPayload?.items) ? normalizedPayload.items : [],
    total: normalizedPayload?.total ?? 0,
    page: normalizedPayload?.page ?? normalizedPayload?.pageNumber ?? 1,
    pageSize: normalizedPayload?.pageSize ?? 24,
  };
}

function normalizeCart(payload: Partial<StoreOrderCart> | null | undefined): StoreOrderCart | null {
  if (!payload) {
    return null;
  }

  return {
    orderGUID: payload.orderGUID ?? "",
    orderNo: payload.orderNo,
    storeCode: payload.storeCode,
    storeName: payload.storeName,
    totalAmount: payload.totalAmount ?? 0,
    totalQuantity: payload.totalQuantity ?? 0,
    totalImportAmount: payload.totalImportAmount ?? 0,
    totalVolume: payload.totalVolume ?? 0,
    remarks: payload.remarks,
    shippingFee: payload.shippingFee,
    orderDate: payload.orderDate,
    storeAddress: payload.storeAddress,
    flowStatus: payload.flowStatus,
    items: Array.isArray(payload.items) ? payload.items : [],
  };
}

export async function getStoresByUserGuid(userGuid: string): Promise<Store[]> {
  const stores = await getUserStoresApi(userGuid);

  return stores
    .filter((item) => item.storeCode)
    .map((item) => ({
      storeCode: item.storeCode,
      storeName: item.storeName || item.storeCode,
    }));
}

export async function getAllStores(): Promise<Store[]> {
  const response = await apiClient.get("/stores/all-by-name");
  const stores = Array.isArray(response.data) ? response.data : [];

  return stores
    .filter((item): item is { storeCode?: string; storeName?: string } => Boolean(item))
    .filter((item) => Boolean(item.storeCode))
    .map((item) => ({
      storeCode: item.storeCode!,
      storeName: item.storeName || item.storeCode!,
    }));
}

export async function getCategoryTree(): Promise<StoreOrderCategoryNode[]> {
  const response = await apiClient.get("/react/v1/warehouse-categories/tree");
  return Array.isArray(response.data) ? (response.data as StoreOrderCategoryNode[]) : [];
}

export async function getProducts(query: StoreOrderProductQuery): Promise<StoreOrderProductListResult> {
  const response = await apiClient.post("/react/v1/store-order/products", query);
  return normalizeProductPagedList(response.data as Partial<StoreOrderProductListResult>);
}

export async function getProductDynamicData(
  payload: StoreOrderDynamicDataRequest
): Promise<StoreOrderDynamicData[]> {
  const response = await apiClient.post("/react/v1/store-order/dynamic-data", payload);
  return Array.isArray(response.data) ? (response.data as StoreOrderDynamicData[]) : [];
}

export async function getCart(storeCode: string): Promise<StoreOrderCart | null> {
  const response = await apiClient.get(`/react/v1/store-order/cart/${encodeURIComponent(storeCode)}`);
  return normalizeCart(response.data as Partial<StoreOrderCart> | null);
}

export async function addToCart(payload: AddToCartPayload): Promise<StoreOrderCart | null> {
  const response = await apiClient.post("/react/v1/store-order/cart/add", payload);
  return normalizeCart(response.data as Partial<StoreOrderCart> | null);
}

export async function updateCartQuantity(payload: UpdateCartQuantityPayload): Promise<void> {
  await apiClient.post("/react/v1/store-order/cart/update", payload);
}

export async function lookupProductsByBarcode(barcode: string): Promise<StoreOrderScanLookupResult> {
  const response = await apiClient.post("/react/v1/store-order/products/scan-lookup", {
    barcode,
  });

  const data = response.data as Partial<StoreOrderScanLookupResult> | null | undefined;

  return {
    barcode: data?.barcode ?? barcode,
    items: Array.isArray(data?.items) ? data.items : [],
  };
}
