import { useMutation, useQueryClient } from "@tanstack/react-query";
import { i18n } from "@/shared/i18n/i18n";
import { updateCartQuantity } from "@/modules/shop/api";
import {
  applyCartMutationResultToCart,
  getOptimisticCartMutationCache,
  isCurrentCartStore,
  isCartMutationResult,
  resolveCartMutationCache,
  snapshotCartMutationCache,
  syncCartMutationCache,
} from "@/modules/shop/cart-cache";
import {
  getScanPerformanceTimestamp,
  logScanPerformance,
} from "@/modules/scanner/scan-performance";
import { useCartStore } from "@/store/cart-store";

interface CartQuantityProduct {
  importPrice?: number;
  productCode: string;
}

interface UpdateCartQuantityVariables {
  nextQuantity: number;
  product: CartQuantityProduct;
  scanTraceId?: string;
}

export function useUpdateCartQuantity(storeCode?: string | null) {
  const queryClient = useQueryClient();
  const setCartSummary = useCartStore((state) => state.setCartSummary);

  return useMutation({
    mutationKey: ["cartMutation", storeCode ?? null],
    scope: { id: `cart:${storeCode ?? "none"}` },
    mutationFn: async ({ nextQuantity, product, scanTraceId }: UpdateCartQuantityVariables) => {
      if (!storeCode) {
        throw new Error(i18n.t("common:errors.selectStoreFirst"));
      }

      const resolvedQuantity = Math.max(0, nextQuantity);
      const requestStartedAt = getScanPerformanceTimestamp();
      logScanPerformance("cart.update.api.frontend.start", {
        scanTraceId,
        storeCode,
        productCode: product.productCode,
        quantity: resolvedQuantity,
      });

      try {
        const cart = await updateCartQuantity(
          {
            storeCode,
            productCode: product.productCode,
            quantity: resolvedQuantity,
            importPrice: product.importPrice,
          },
          scanTraceId
        );
        logScanPerformance("cart.update.api.frontend.done", {
          scanTraceId,
          storeCode,
          productCode: product.productCode,
          quantity: resolvedQuantity,
          totalQuantity: isCartMutationResult(cart) ? cart.summary.totalQuantity : cart?.totalQuantity ?? 0,
          elapsedMs: getScanPerformanceTimestamp() - requestStartedAt,
        });
        return cart;
      } catch (error) {
        logScanPerformance("cart.update.api.frontend.error", {
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
    onMutate: async (variables) => {
      if (!storeCode) {
        return undefined;
      }

      const resolvedQuantity = Math.max(0, variables.nextQuantity);
      await Promise.all([
        queryClient.cancelQueries({ queryKey: ["cartSummary", storeCode] }),
        queryClient.cancelQueries({ queryKey: ["shopDynamicData", storeCode] }),
      ]);
      const snapshot = snapshotCartMutationCache(queryClient, storeCode, {
        product: variables.product,
        quantity: resolvedQuantity,
        type: "set",
      });
      const optimisticCart = getOptimisticCartMutationCache(queryClient, storeCode);
      const currentStoreCode = useCartStore.getState().selectedStore?.storeCode ?? null;

      // 数量调整立即同步列表和顶部数量；失败时按快照回滚。
      syncCartMutationCache(queryClient, storeCode, optimisticCart ?? null, variables.product.productCode);
      if (isCurrentCartStore(currentStoreCode, storeCode)) {
        setCartSummary(optimisticCart ?? null);
      }

      return snapshot;
    },
    onError: (_error, _variables, snapshot) => {
      if (!storeCode || !snapshot) {
        return;
      }

      const nextCart = resolveCartMutationCache(queryClient, storeCode, snapshot);
      const currentStoreCode = useCartStore.getState().selectedStore?.storeCode ?? null;
      if (isCurrentCartStore(currentStoreCode, storeCode)) {
        setCartSummary(nextCart ?? null);
      }
    },
    onSuccess: (cart, variables, snapshot) => {
      if (!storeCode) {
        return;
      }

      const cacheStartedAt = getScanPerformanceTimestamp();
      const nextCart = snapshot
        ? resolveCartMutationCache(queryClient, storeCode, snapshot, cart)
        : isCartMutationResult(cart)
          ? applyCartMutationResultToCart(
              queryClient.getQueryData(["cartSummary", storeCode]),
              cart
            )
          : cart;
      const currentStoreCode = useCartStore.getState().selectedStore?.storeCode ?? null;
      const shouldUpdateGlobalCart = isCurrentCartStore(currentStoreCode, storeCode);
      if (shouldUpdateGlobalCart) {
        setCartSummary(nextCart);
      } else {
        // 门店已切换时只更新 scoped query cache，避免旧门店购物车覆盖当前顶部数量。
        logScanPerformance("cart.cache.frontend.skipped-stale-store", {
          scanTraceId: variables.scanTraceId,
          storeCode,
          currentStoreCode,
          productCode: variables.product.productCode,
        });
      }
      if (!snapshot) {
        syncCartMutationCache(
          queryClient,
          storeCode,
          nextCart,
          isCartMutationResult(cart) ? cart.productCode : undefined
        );
      }
      logScanPerformance("cart.cache.frontend.done", {
        scanTraceId: variables.scanTraceId,
        storeCode,
        productCode: variables.product.productCode,
        totalQuantity: nextCart?.totalQuantity ?? 0,
        updatedGlobalCart: shouldUpdateGlobalCart,
        elapsedMs: getScanPerformanceTimestamp() - cacheStartedAt,
      });
    },
  });
}
