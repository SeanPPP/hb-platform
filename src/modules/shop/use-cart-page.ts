import { useMemo } from "react";
import { useQuery } from "@tanstack/react-query";
import { getCart } from "@/modules/shop/api";
import type { StoreOrderCartItem } from "@/modules/shop/types";

interface UseCartPageOptions {
  page: number;
  pageSize: number;
  priorityProductCode?: string | null;
  storeCode?: string | null;
}

function getUpdatedTime(item: StoreOrderCartItem) {
  const timestamp = item.updatedAt ? Date.parse(item.updatedAt) : Number.NaN;
  return Number.isFinite(timestamp) ? timestamp : 0;
}

export function useCartPage({ page, pageSize, priorityProductCode, storeCode }: UseCartPageOptions) {
  const cartQuery = useQuery({
    queryKey: ["cartSummary", storeCode],
    enabled: Boolean(storeCode),
    queryFn: () => getCart(storeCode!),
  });

  const sortedItems = useMemo(() => {
    const items = cartQuery.data?.items ?? [];

    return items
      .map((item, index) => ({ index, item }))
      .sort((left, right) => {
        const leftPriority = priorityProductCode && left.item.productCode === priorityProductCode ? 1 : 0;
        const rightPriority = priorityProductCode && right.item.productCode === priorityProductCode ? 1 : 0;
        const priorityDiff = rightPriority - leftPriority;

        if (priorityDiff) {
          return priorityDiff;
        }

        const updatedDiff = getUpdatedTime(right.item) - getUpdatedTime(left.item);
        return updatedDiff || left.index - right.index;
      })
      .map(({ item }) => item);
  }, [cartQuery.data?.items, priorityProductCode]);

  const pagedItems = useMemo(() => {
    const start = (page - 1) * pageSize;
    return sortedItems.slice(start, start + pageSize);
  }, [page, pageSize, sortedItems]);

  return {
    ...cartQuery,
    cart: cartQuery.data ?? null,
    items: pagedItems,
    total: sortedItems.length,
  };
}
