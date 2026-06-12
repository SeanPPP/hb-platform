import { useEffect } from "react";
import { useQuery } from "@tanstack/react-query";
import { getCart } from "@/modules/shop/api";
import { useCartStore } from "@/store/cart-store";

export function useCartSummary(storeCode?: string | null) {
  const setCartSummary = useCartStore((state) => state.setCartSummary);

  const query = useQuery({
    queryKey: ["cartSummary", storeCode],
    enabled: Boolean(storeCode),
    queryFn: () => getCart(storeCode!),
  });

  useEffect(() => {
    if (!storeCode) {
      setCartSummary(null);
      return;
    }

    if (query.isSuccess) {
      setCartSummary(query.data ?? null);
      return;
    }

    if (query.isError) {
      setCartSummary(null);
    }
  }, [query.data, query.isError, query.isSuccess, setCartSummary, storeCode]);

  return query;
}
