import type { AxiosRequestConfig } from "axios";
import { apiClient } from "@/shared/api/client";
import { useDeviceStore } from "@/store/device-store";
import {
  normalizePriceNotificationPreview,
  normalizePriceUpdateBatchResult,
  normalizePriceUpdatePendingCount,
  normalizePriceUpdateTaskPage,
  normalizeSuggestedDiscountLookup,
  normalizeSyncTargets,
} from "./normalize";
import { parsePriceNotificationHeader, type PriceNotificationSummary } from "./price-notification";
import type {
  PriceNotificationPreview,
  PriceUpdateApplyItem,
  PriceUpdateBatchResult,
  PriceUpdateLabelMode,
  PriceUpdateTaskQuery,
  StorePriceUpdateTaskPage,
  SuggestedDiscountSource,
  SyncTargetsResult,
  SyncToOtherStoresRequest,
} from "./types";

const BASE_PATH = "/react/v1/store-product-maintenance";
const MONITOR_BASE_PATH = "/react/v1/store-price-update-tasks";

// 与商品维护模块一致的设备/账号双轨：设备会话带设备头，账号会话由拦截器补 Bearer。
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

export async function getPriceUpdateTasks(query: PriceUpdateTaskQuery): Promise<StorePriceUpdateTaskPage> {
  const response = await apiClient.get(`${BASE_PATH}/price-update-tasks`, {
    ...buildRequestConfig(),
    params: {
      storeCode: query.storeCode,
      status: query.status,
      ...(query.kind ? { kind: query.kind } : {}),
      ...(query.keyword?.trim() ? { keyword: query.keyword.trim() } : {}),
      ...(query.hqSyncFailedOnly ? { hqSyncFailedOnly: true } : {}),
      page: query.page,
      pageSize: query.pageSize,
    },
  });
  return normalizePriceUpdateTaskPage(response.data);
}

export async function getPriceUpdatePendingCount(storeCode: string): Promise<number> {
  const response = await apiClient.get(`${BASE_PATH}/price-update-tasks/count`, {
    ...buildRequestConfig(),
    params: { storeCode },
  });
  return normalizePriceUpdatePendingCount(response.data);
}

export async function applyPriceUpdateTasks(
  storeCode: string,
  items: PriceUpdateApplyItem[]
): Promise<PriceUpdateBatchResult> {
  const response = await apiClient.post(
    `${BASE_PATH}/price-update-tasks/apply`,
    { storeCode, items },
    buildRequestConfig()
  );
  return normalizePriceUpdateBatchResult(response.data);
}

export async function keepStorePriceForTasks(
  storeCode: string,
  taskIds: number[]
): Promise<PriceUpdateBatchResult> {
  const response = await apiClient.post(
    `${BASE_PATH}/price-update-tasks/keep`,
    { storeCode, taskIds },
    buildRequestConfig()
  );
  return normalizePriceUpdateBatchResult(response.data);
}

export async function completePriceUpdateLabels(
  storeCode: string,
  taskIds: number[],
  mode: PriceUpdateLabelMode
): Promise<PriceUpdateBatchResult> {
  const response = await apiClient.post(
    `${BASE_PATH}/price-update-tasks/labels`,
    { storeCode, taskIds, mode },
    buildRequestConfig()
  );
  return normalizePriceUpdateBatchResult(response.data);
}

export async function getSyncTargets(productCode: string, sourceStoreCode: string): Promise<SyncTargetsResult> {
  const response = await apiClient.get(
    `${BASE_PATH}/products/${encodeURIComponent(productCode)}/sync-targets`,
    { ...buildRequestConfig(), params: { sourceStoreCode } }
  );
  return normalizeSyncTargets(response.data);
}

export async function syncToOtherStores(
  payload: SyncToOtherStoresRequest
): Promise<{ updatedStoreCount: number; notification: PriceNotificationSummary | null }> {
  const response = await apiClient.post(
    `${BASE_PATH}/products/sync-to-other-stores`,
    payload,
    buildRequestConfig()
  );
  const data = (response.data && typeof response.data === "object" ? response.data : {}) as Record<string, unknown>;
  const updatedStoreCount = Number(data.updatedStoreCount ?? data.UpdatedStoreCount ?? 0);
  return {
    updatedStoreCount: Number.isFinite(updatedStoreCount) ? updatedStoreCount : 0,
    notification: parsePriceNotificationHeader(response.headers),
  };
}

export async function getPriceNotificationPreview(input: {
  productCode: string;
  /** 省略 = 零售价不变。 */
  retailPrice?: number | null;
  /** undefined = 建议折扣不变；null = 清空为未设置。 */
  suggestedDiscountRate?: number | null;
}): Promise<PriceNotificationPreview> {
  const suggestedDiscountSpecified = input.suggestedDiscountRate !== undefined;
  const response = await apiClient.get(`${MONITOR_BASE_PATH}/preview`, {
    params: {
      productCode: input.productCode,
      ...(input.retailPrice != null ? { retailPrice: input.retailPrice } : {}),
      suggestedDiscountSpecified,
      ...(suggestedDiscountSpecified && input.suggestedDiscountRate != null
        ? { suggestedDiscountRate: input.suggestedDiscountRate }
        : {}),
    },
  });
  return normalizePriceNotificationPreview(response.data);
}

export async function lookupSuggestedDiscount(productCode: string): Promise<number | null> {
  const response = await apiClient.post(`${MONITOR_BASE_PATH}/suggested-discounts/lookup`, {
    productCodes: [productCode],
  });
  return normalizeSuggestedDiscountLookup(response.data).get(productCode) ?? null;
}

export async function updateSuggestedDiscount(
  productCodes: string[],
  suggestedDiscountRate: number | null,
  source: SuggestedDiscountSource
): Promise<{ changedCount: number; notification: PriceNotificationSummary | null }> {
  const response = await apiClient.put(`${MONITOR_BASE_PATH}/suggested-discounts`, {
    productCodes,
    suggestedDiscountRate,
    source,
  });
  const data = (response.data && typeof response.data === "object" ? response.data : {}) as Record<string, unknown>;
  const changedCount = Number(data.changedCount ?? data.ChangedCount ?? 0);
  return {
    changedCount: Number.isFinite(changedCount) ? changedCount : 0,
    notification: parsePriceNotificationHeader(response.headers),
  };
}
