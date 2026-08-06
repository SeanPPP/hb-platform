import {
  rememberScanLookupProduct,
  type ScanLookupCache,
} from "./scan-lookup-cache";
import type { StoreOrderProductItem } from "@/modules/shop/types";

export function rememberScanLookupProductForMatch(
  cache: ScanLookupCache,
  storeCode: string | null | undefined,
  barcode: string,
  product: StoreOrderProductItem,
  now: number,
  matchType?: string | null,
) {
  const normalizedMatchType = matchType?.trim().toLowerCase();
  if (normalizedMatchType === "locationbarcode" || normalizedMatchType === "locationcode") {
    // 货位映射可能随绑定变化，不能把远端货位结果写入 60 秒商品扫码缓存。
    return;
  }

  // 普通商品命中（含旧版 barcode/fallback）保持原有缓存行为。
  rememberScanLookupProduct(cache, storeCode, barcode, product, now);
}
