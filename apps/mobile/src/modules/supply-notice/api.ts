import { apiClient } from "@/shared/api/client";
import type { StoreSupplyStatus, StoreSupplyWatchSummary } from "./types";

const STORE_BASE = "/react/v1/store-order/supply";

/** apiClient 已统一剥掉 { success, data } 信封，这里只做形状兜底。 */
function asArray<T>(value: unknown): T[] {
  return Array.isArray(value) ? (value as T[]) : [];
}

/** 搜索或扫码零结果时调用：按条码 → 货号 → 商品编码精确查询暂停供货的商品。 */
export async function lookupStoreSupplyStatus(storeCode: string, code: string): Promise<StoreSupplyStatus[]> {
  const response = await apiClient.post(`${STORE_BASE}/lookup`, { storeCode, code });
  const data = response.data as { items?: unknown } | undefined;
  return asArray<StoreSupplyStatus>(data?.items);
}

export async function getStoreSupplyWatches(storeCode: string): Promise<StoreSupplyStatus[]> {
  const response = await apiClient.get(`${STORE_BASE}/watches/${encodeURIComponent(storeCode)}`);
  return asArray<StoreSupplyStatus>(response.data);
}

export async function getStoreSupplyWatchSummary(storeCode: string): Promise<StoreSupplyWatchSummary> {
  const response = await apiClient.get(`${STORE_BASE}/watches/${encodeURIComponent(storeCode)}/summary`);
  const data = response.data as Partial<StoreSupplyWatchSummary> | undefined;
  return {
    watchingCount: Number(data?.watchingCount ?? 0),
    restockedCount: Number(data?.restockedCount ?? 0),
  };
}

export async function watchStoreSupply(storeCode: string, productCode: string): Promise<void> {
  await apiClient.post(`${STORE_BASE}/watches`, { storeCode, productCode });
}

export async function unwatchStoreSupply(storeCode: string, productCode: string): Promise<void> {
  await apiClient.post(`${STORE_BASE}/watches/remove`, { storeCode, productCode });
}

/** 确认“已恢复订货”提醒；不传 productCodes 表示确认该分店全部已恢复的关注。 */
export async function acknowledgeStoreSupplyRestocked(storeCode: string, productCodes?: string[]): Promise<void> {
  await apiClient.post(`${STORE_BASE}/watches/acknowledge`, { storeCode, productCodes });
}
