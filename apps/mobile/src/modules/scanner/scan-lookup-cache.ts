import type { StoreOrderProductItem } from "@/modules/shop/types";

interface ScanLookupCacheEntry {
  expiresAt: number;
  product: StoreOrderProductItem;
}

export interface ScanLookupCache {
  entries: Map<string, ScanLookupCacheEntry>;
  ttlMs: number;
}

export interface ScanLookupInFlight<T> {
  entries: Map<string, Promise<T>>;
}

export interface ScanLookupInFlightResult<T> {
  result: T;
  shared: boolean;
}

function normalizeStoreCode(storeCode?: string | null) {
  const normalized = storeCode?.trim();
  return normalized ? normalized : "";
}

function getCacheKey(storeCode: string | null | undefined, barcode: string) {
  return `${normalizeStoreCode(storeCode)}:${barcode.trim()}`;
}

export function createScanLookupCache(ttlMs: number): ScanLookupCache {
  return {
    entries: new Map(),
    ttlMs: Math.max(0, ttlMs),
  };
}

export function createScanLookupInFlight<T>(): ScanLookupInFlight<T> {
  return {
    entries: new Map(),
  };
}

export function clearScanLookupInFlight<T>(inFlight: ScanLookupInFlight<T>) {
  inFlight.entries.clear();
}

export async function runScanLookupInFlight<T>(
  inFlight: ScanLookupInFlight<T>,
  storeCode: string | null | undefined,
  barcode: string,
  factory: () => Promise<T>
): Promise<ScanLookupInFlightResult<T>> {
  const key = getCacheKey(storeCode, barcode);
  const existing = inFlight.entries.get(key);
  if (existing) {
    return {
      result: await existing,
      shared: true,
    };
  }

  // 同门店同条码冷扫码只共享商品查询结果；完成或失败后都释放，后续可重试。
  const promise = factory();
  inFlight.entries.set(key, promise);
  try {
    return {
      result: await promise,
      shared: false,
    };
  } finally {
    if (inFlight.entries.get(key) === promise) {
      inFlight.entries.delete(key);
    }
  }
}

export function rememberScanLookupProduct(
  cache: ScanLookupCache,
  storeCode: string | null | undefined,
  barcode: string,
  product: StoreOrderProductItem,
  now: number
) {
  if (!normalizeStoreCode(storeCode) || !barcode.trim() || !product.productCode) {
    return;
  }

  // 缓存只保存单命中商品，重复扫码可直接 scan-add，冷扫码再走合并接口。
  cache.entries.set(getCacheKey(storeCode, barcode), {
    expiresAt: now + cache.ttlMs,
    product: { ...product },
  });
}

export function getCachedScanLookupProduct(
  cache: ScanLookupCache,
  storeCode: string | null | undefined,
  barcode: string,
  now: number
) {
  const entry = cache.entries.get(getCacheKey(storeCode, barcode));
  if (!entry) {
    return null;
  }

  if (entry.expiresAt <= now) {
    cache.entries.delete(getCacheKey(storeCode, barcode));
    return null;
  }

  return { ...entry.product };
}
