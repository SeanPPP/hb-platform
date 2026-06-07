import { apiClient } from "@/shared/api/client";
import {
  buildOrderListRequest,
  type StoreOrderListRequestParams,
} from "./order-list-display";
import type { StoreOrderCart, StoreOrderDetail, StoreOrderListResult } from "./types";

function assertSuccess<T>(data: T | undefined, message?: string): T {
  if (data === undefined || data === null) {
    throw new Error(message ?? "Empty response");
  }
  return data;
}

export async function fetchActiveCart(storeCode: string): Promise<StoreOrderCart | null> {
  const res = await apiClient.get(`/react/v1/store-order/cart/${encodeURIComponent(storeCode)}`);
  return (res.data as StoreOrderCart | null) ?? null;
}

export async function addLineToCart(
  storeCode: string,
  productCode: string,
  quantity: number,
  importPrice?: number
): Promise<void> {
  await apiClient.post("/react/v1/store-order/cart/add", {
    storeCode,
    productCode,
    quantity,
    importPrice,
  });
}

export async function updateCartLineQuantity(
  storeCode: string,
  productCode: string,
  quantity: number,
  importPrice?: number
): Promise<void> {
  await apiClient.post("/react/v1/store-order/cart/update", {
    storeCode,
    productCode,
    quantity,
    importPrice,
  });
}

export async function removeCartLine(storeCode: string, detailGUID: string): Promise<void> {
  await apiClient.post("/react/v1/store-order/cart/remove", {
    storeCode,
    detailGUID,
  });
}

export async function clearServerCart(storeCode: string): Promise<void> {
  await apiClient.post("/react/v1/store-order/cart/clear", { storeCode });
}

export async function submitStoreOrder(storeCode: string, remarks?: string): Promise<unknown> {
  const res = await apiClient.post("/react/v1/store-order/submit", { storeCode, remarks });
  return res.data;
}

export async function fetchOrderList(params: StoreOrderListRequestParams): Promise<StoreOrderListResult> {
  const res = await apiClient.post("/react/v1/store-order/list", buildOrderListRequest(params));
  return assertSuccess(res.data as StoreOrderListResult, "Order list");
}

export async function fetchOrderDetail(orderGuid: string): Promise<StoreOrderDetail> {
  const res = await apiClient.get(
    `/react/v1/store-order/detail/${encodeURIComponent(orderGuid)}`
  );
  return assertSuccess(res.data as StoreOrderDetail, "Order detail");
}
