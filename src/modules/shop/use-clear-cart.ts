import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clearServerCart } from "@/modules/orders/store-order-api";

export function useClearCart(storeCode?: string | null) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async () => {
      if (!storeCode) {
        throw new Error("请先选择门店");
      }

      await clearServerCart(storeCode);
    },
    onSuccess: () => {
      if (!storeCode) {
        return;
      }

      void Promise.all([
        queryClient.invalidateQueries({ queryKey: ["cartSummary", storeCode] }),
        queryClient.invalidateQueries({ queryKey: ["shopDynamicData", storeCode] }),
      ]);
    },
  });
}
