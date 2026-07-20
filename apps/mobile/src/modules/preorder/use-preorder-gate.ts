import { useCallback, useMemo } from "react";
import { AppState } from "react-native";
import { useFocusEffect } from "@react-navigation/native";
import { useQuery } from "@tanstack/react-query";
import { fetchActivePreorders } from "./api";
import {
  preorderActiveQueryKey,
  resolveConfirmedGateValue,
  shouldBlockNormalOrder,
} from "./gate";

export { preorderActiveQueryKey } from "./gate";

export function usePreorderGate(storeCode?: string | null, canBypass = false) {
  const normalizedStoreCode = storeCode?.trim() || null;
  const query = useQuery({
    queryKey: preorderActiveQueryKey(normalizedStoreCode),
    enabled: Boolean(normalizedStoreCode),
    // 消费 React Query signal，提交成功后可真正取消 POST 前的旧 active GET。
    queryFn: ({ signal }) => fetchActivePreorders(normalizedStoreCode!, signal),
    staleTime: 10_000,
    retry: 1,
  });
  const refetch = query.refetch;

  useFocusEffect(
    useCallback(() => {
      if (normalizedStoreCode) {
        void refetch();
      }
    }, [normalizedStoreCode, refetch])
  );

  useFocusEffect(
    useCallback(() => {
      const subscription = AppState.addEventListener("change", (state) => {
        if (state === "active" && normalizedStoreCode) {
          void refetch();
        }
      });
      return () => subscription.remove();
    }, [normalizedStoreCode, refetch])
  );

  return useMemo(() => {
    // 缓存中的旧 false 不能在刷新中或刷新失败时放行普通订货。
    const confirmedServerValue = resolveConfirmedGateValue(
      query.data?.normalOrderBlocked,
      query.isFetching,
      query.isError
    );
    const normalOrderBlocked = shouldBlockNormalOrder(
      normalizedStoreCode,
      confirmedServerValue,
      canBypass
    );
    return {
      activations: query.data?.activations ?? [],
      isChecking: Boolean(normalizedStoreCode && query.isFetching),
      isError: query.isError,
      normalOrderBlocked,
      refresh: refetch,
      storeCode: normalizedStoreCode,
    };
  }, [canBypass, normalizedStoreCode, query.data, query.isError, query.isFetching, query.isPending, refetch]);
}

export type PreorderGateState = ReturnType<typeof usePreorderGate>;
