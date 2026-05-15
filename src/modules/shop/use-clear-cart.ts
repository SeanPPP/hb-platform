import { useMutation, useQueryClient } from "@tanstack/react-query";
import { i18n } from "@/shared/i18n/i18n";
import { clearServerCart } from "@/modules/orders/store-order-api";

export function useClearCart(storeCode?: string | null) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async () => {
      if (!storeCode) {
        throw new Error(i18n.t("common:errors.selectStoreFirst"));
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
