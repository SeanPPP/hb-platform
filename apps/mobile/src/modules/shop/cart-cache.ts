import type { QueryClient } from "@tanstack/react-query";
import type { StoreOrderCart, StoreOrderDynamicData } from "@/modules/shop/types";

function normalizeStoreCode(storeCode?: string | null) {
  const normalized = storeCode?.trim();
  return normalized ? normalized : null;
}

export function isCurrentCartStore(currentStoreCode?: string | null, mutationStoreCode?: string | null) {
  return normalizeStoreCode(currentStoreCode) === normalizeStoreCode(mutationStoreCode);
}

export function mergeCartQuantityIntoDynamicData(
  currentData: StoreOrderDynamicData[] | undefined,
  cart: StoreOrderCart | null
): StoreOrderDynamicData[] | undefined {
  if (!currentData) {
    return currentData;
  }

  const quantityByProductCode = new Map(
    (cart?.items ?? []).map((item) => [item.productCode, item.quantity])
  );

  return currentData.map((item) => ({
    ...item,
    cartQuantity: quantityByProductCode.get(item.productCode) ?? 0,
  }));
}

export function syncCartMutationCache(
  queryClient: QueryClient,
  storeCode: string,
  cart: StoreOrderCart | null
) {
  queryClient.setQueryData(["cartSummary", storeCode], cart);
  queryClient.setQueriesData<StoreOrderDynamicData[]>(
    { queryKey: ["shopDynamicData", storeCode] },
    (currentData) => mergeCartQuantityIntoDynamicData(currentData, cart)
  );
}
