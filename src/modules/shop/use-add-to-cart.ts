import { useMutation, useQueryClient } from "@tanstack/react-query";
import { i18n } from "@/shared/i18n/i18n";
import { addToCart } from "@/modules/shop/api";
import type { StoreOrderProductItem } from "@/modules/shop/types";

function resolveMinimumOrderQuantity(product: StoreOrderProductItem) {
  return product.minOrderQuantity > 0 ? product.minOrderQuantity : 1;
}

interface AddToCartVariables {
  product: StoreOrderProductItem;
  quantity?: number;
}

export function useAddToCart(storeCode?: string | null) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({ product, quantity }: AddToCartVariables) => {
      if (!storeCode) {
        throw new Error(i18n.t("common:errors.selectStoreFirst"));
      }

      return addToCart({
        storeCode,
        productCode: product.productCode,
        quantity: quantity ?? resolveMinimumOrderQuantity(product),
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

export { resolveMinimumOrderQuantity };
