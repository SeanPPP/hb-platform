import { useMemo } from "react";
import { useQuery } from "@tanstack/react-query";
import { getProductDynamicData, getProducts } from "@/modules/shop/api";
import type { ProductDynamicDataMap, StoreOrderProductQuery } from "@/modules/shop/types";

export function useProducts(query: StoreOrderProductQuery) {
  const productsQuery = useQuery({
    queryKey: ["shopProducts", query],
    enabled: Boolean(query.storeCode),
    staleTime: 5 * 60 * 1000,
    retry: false,
    queryFn: () => getProducts(query),
  });

  const productCodes = useMemo(
    () => (productsQuery.data?.items ?? []).map((item) => item.productCode).filter(Boolean),
    [productsQuery.data?.items]
  );

  const dynamicDataQuery = useQuery({
    queryKey: ["shopDynamicData", query.storeCode ?? null, productCodes],
    enabled: Boolean(query.storeCode) && productCodes.length > 0,
    queryFn: () =>
      getProductDynamicData({
        storeCode: query.storeCode!,
        productCodes,
      }),
  });

  const dynamicDataMap = useMemo<ProductDynamicDataMap>(() => {
    const data = dynamicDataQuery.data ?? [];

    return data.reduce<ProductDynamicDataMap>((accumulator, item) => {
      accumulator[item.productCode] = item;
      return accumulator;
    }, {});
  }, [dynamicDataQuery.data]);

  return {
    ...productsQuery,
    dynamicData: dynamicDataQuery.data ?? [],
    dynamicDataMap,
    dynamicDataQuery,
  };
}
