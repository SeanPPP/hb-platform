import type { StoreOrderProductQuery } from "./types";

export function buildShopProductsQueryKey(
  query: StoreOrderProductQuery,
  locationLookupEnabled: boolean,
) {
  return ["shopProducts", query, locationLookupEnabled] as const;
}

interface PreviousShopProductsQuery {
  queryKey: readonly unknown[];
}

export function resolveShopProductsPlaceholderData<T>(
  previousData: T | undefined,
  previousQuery: PreviousShopProductsQuery | undefined,
  locationLookupEnabled: boolean,
): T | undefined {
  // 权限范围变化时禁止沿用旧数据，避免短暂显示无权查看的货位结果。
  return previousQuery?.queryKey[2] === locationLookupEnabled
    ? previousData
    : undefined;
}
