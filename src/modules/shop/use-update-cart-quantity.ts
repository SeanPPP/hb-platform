import { useMutation, useQueryClient } from "@tanstack/react-query";
import { updateCartQuantity } from "@/modules/shop/api";

interface CartQuantityProduct {
  importPrice?: number;
  productCode: string;
}

interface UpdateCartQuantityVariables {
  nextQuantity: number;
  product: CartQuantityProduct;
}

export function useUpdateCartQuantity(storeCode?: string | null) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({ nextQuantity, product }: UpdateCartQuantityVariables) => {
      if (!storeCode) {
        throw new Error("请先选择门店");
      }

      await updateCartQuantity({
        storeCode,
        productCode: product.productCode,
        quantity: Math.max(0, nextQuantity),
        importPrice: product.importPrice,
      });
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
