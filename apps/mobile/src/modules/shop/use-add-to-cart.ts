import { useMutation, useQueryClient } from "@tanstack/react-query";
import { i18n } from "@/shared/i18n/i18n";
import { addToCart } from "@/modules/shop/api";
import { isCurrentCartStore, syncCartMutationCache } from "@/modules/shop/cart-cache";
import {
  getScanPerformanceTimestamp,
  logScanPerformance,
} from "@/modules/scanner/scan-performance";
import type { StoreOrderProductItem } from "@/modules/shop/types";
import { useCartStore } from "@/store/cart-store";

function resolveMinimumOrderQuantity(product: StoreOrderProductItem) {
  return product.minOrderQuantity > 0 ? product.minOrderQuantity : 1;
}

interface AddToCartVariables {
  product: StoreOrderProductItem;
  quantity?: number;
  scanTraceId?: string;
}

export function useAddToCart(storeCode?: string | null) {
  const queryClient = useQueryClient();
  const setCartSummary = useCartStore((state) => state.setCartSummary);

  return useMutation({
    mutationFn: async ({ product, quantity, scanTraceId }: AddToCartVariables) => {
      if (!storeCode) {
        throw new Error(i18n.t("common:errors.selectStoreFirst"));
      }

      const resolvedQuantity = quantity ?? resolveMinimumOrderQuantity(product);
      const requestStartedAt = getScanPerformanceTimestamp();
      logScanPerformance("cart.add.api.frontend.start", {
        scanTraceId,
        storeCode,
        productCode: product.productCode,
        quantity: resolvedQuantity,
      });

      try {
        const cart = await addToCart(
          {
            storeCode,
            productCode: product.productCode,
            quantity: resolvedQuantity,
            importPrice: product.importPrice,
          },
          scanTraceId
        );
        logScanPerformance("cart.add.api.frontend.done", {
          scanTraceId,
          storeCode,
          productCode: product.productCode,
          quantity: resolvedQuantity,
          totalQuantity: cart?.totalQuantity ?? 0,
          elapsedMs: getScanPerformanceTimestamp() - requestStartedAt,
        });
        return cart;
      } catch (error) {
        logScanPerformance("cart.add.api.frontend.error", {
          scanTraceId,
          storeCode,
          productCode: product.productCode,
          quantity: resolvedQuantity,
          elapsedMs: getScanPerformanceTimestamp() - requestStartedAt,
          error: error instanceof Error ? error.message : String(error),
        });
        throw error;
      }
    },
    onSuccess: (cart, variables) => {
      if (!storeCode) {
        return;
      }

      const cacheStartedAt = getScanPerformanceTimestamp();
      const currentStoreCode = useCartStore.getState().selectedStore?.storeCode ?? null;
      const shouldUpdateGlobalCart = isCurrentCartStore(currentStoreCode, storeCode);
      if (shouldUpdateGlobalCart) {
        setCartSummary(cart);
      } else {
        // 门店已切换时只更新 scoped query cache，避免旧门店购物车覆盖当前顶部数量。
        logScanPerformance("cart.cache.frontend.skipped-stale-store", {
          scanTraceId: variables.scanTraceId,
          storeCode,
          currentStoreCode,
          productCode: variables.product.productCode,
        });
      }
      syncCartMutationCache(queryClient, storeCode, cart);
      logScanPerformance("cart.cache.frontend.done", {
        scanTraceId: variables.scanTraceId,
        storeCode,
        productCode: variables.product.productCode,
        totalQuantity: cart?.totalQuantity ?? 0,
        updatedGlobalCart: shouldUpdateGlobalCart,
        elapsedMs: getScanPerformanceTimestamp() - cacheStartedAt,
      });
    },
  });
}

export { resolveMinimumOrderQuantity };
