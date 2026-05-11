import { useMutation, useQueryClient } from "@tanstack/react-query";
import { removeCartLine } from "@/modules/orders/store-order-api";

interface RemoveCartLineVariables {
  detailGUID: string;
}

export function useRemoveCartLine(storeCode?: string | null) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({ detailGUID }: RemoveCartLineVariables) => {
      if (!storeCode) {
        throw new Error("请先选择门店");
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
