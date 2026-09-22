import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useCallback, useState } from "react";
import { getStoreSupplyWatchSummary, lookupStoreSupplyStatus, unwatchStoreSupply, watchStoreSupply } from "./api";
import type { StoreSupplyStatus } from "./types";

export const supplyWatchSummaryQueryKey = (storeCode: string | null) => ["supply-watch-summary", storeCode] as const;

/** 关注汇总：横幅与入口角标共用；切店自动换键。 */
export function useSupplyWatchSummary(storeCode: string | null) {
  return useQuery({
    queryKey: supplyWatchSummaryQueryKey(storeCode),
    enabled: Boolean(storeCode),
    staleTime: 60 * 1000,
    retry: false,
    queryFn: () => getStoreSupplyWatchSummary(storeCode!),
  });
}

/**
 * 搜索零结果时的补充查询：按关键字精确查暂停供货商品。只在 enabled 为真（列表为空且有关键字）时请求。
 */
export function useSupplyStatusLookup(storeCode: string | null, code: string, enabled: boolean) {
  const queryClient = useQueryClient();
  const trimmed = code.trim();
  const query = useQuery({
    queryKey: ["supply-lookup", storeCode, trimmed] as const,
    enabled: enabled && Boolean(storeCode) && trimmed.length > 0,
    staleTime: 30 * 1000,
    retry: false,
    queryFn: () => lookupStoreSupplyStatus(storeCode!, trimmed),
  });
  const [busyCode, setBusyCode] = useState<string | null>(null);

  const toggleWatch = useCallback(async (status: StoreSupplyStatus, watch: boolean) => {
    if (!storeCode) {
      return false;
    }
    setBusyCode(status.productCode);
    try {
      if (watch) {
        await watchStoreSupply(storeCode, status.productCode);
      } else {
        await unwatchStoreSupply(storeCode, status.productCode);
      }
      // 就地改关注态，不重新请求。
      queryClient.setQueryData<StoreSupplyStatus[]>(["supply-lookup", storeCode, trimmed], (current) =>
        (current ?? []).map((item) => (item.productCode === status.productCode ? { ...item, isWatching: watch } : item))
      );
      void queryClient.invalidateQueries({ queryKey: supplyWatchSummaryQueryKey(storeCode) });
      return true;
    } catch {
      return false;
    } finally {
      setBusyCode(null);
    }
  }, [queryClient, storeCode, trimmed]);

  return { items: enabled ? query.data ?? [] : [], busyCode, toggleWatch };
}
