import { useMutation, useQueryClient } from "@tanstack/react-query";
import { i18n } from "@/shared/i18n/i18n";
import { removeCartLine } from "@/modules/orders/store-order-api";

interface RemoveCartLineVariables {
  detailGUID: string;
}

export function useRemoveCartLine(storeCode?: string | null) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationKey: ["cartMutation", storeCode ?? null],
    scope: { id: `cart:${storeCode ?? "none"}` },
    mutationFn: async ({ detailGUID }: RemoveCartLineVariables) => {
      if (!storeCode) {
        throw new Error(i18n.t("common:errors.selectStoreFirst"));
      }

      await removeCartLine(storeCode, detailGUID);
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
