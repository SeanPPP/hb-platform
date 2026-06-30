import { useMutation, useQueryClient } from "@tanstack/react-query";
import { i18n } from "@/shared/i18n/i18n";
import { addToCart } from "@/modules/shop/api";
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

interface UseAddToCartOptions {
  concurrent?: boolean;
}

export function useAddToCart(storeCode?: string | null, options: UseAddToCartOptions = {}) {
  const queryClient = useQueryClient();
  const setCartSummary = useCartStore((state) => state.setCartSummary);

  return useMutation({
    mutationKey: ["cartMutation", storeCode ?? null],
    scope: options.concurrent ? undefined : { id: `cart:${storeCode ?? "none"}` },
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
          totalQuantity: isCartMutationResult(cart) ? cart.summary.totalQuantity : cart?.totalQuantity ?? 0,
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
    onMutate: async (variables) => {
      if (!storeCode) {
        return undefined;
      }

      const resolvedQuantity = variables.quantity ?? resolveMinimumOrderQuantity(variables.product);
      await Promise.all([
        queryClient.cancelQueries({ queryKey: ["cartSummary", storeCode] }),
        queryClient.cancelQueries({ queryKey: ["shopDynamicData", storeCode] }),
      ]);
      const snapshot = snapshotCartMutationCache(queryClient, storeCode, {
        product: variables.product,
        quantity: resolvedQuantity,
        type: "add",
      });
      const optimisticCart = getOptimisticCartMutationCache(queryClient, storeCode);
      const currentStoreCode = useCartStore.getState().selectedStore?.storeCode ?? null;

      // 扫码链路先写本地缓存，服务端成功后再用真实 cart 覆盖。
      syncCartMutationCache(queryClient, storeCode, optimisticCart, variables.product.productCode);
      if (isCurrentCartStore(currentStoreCode, storeCode)) {
        setCartSummary(optimisticCart);
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

export { resolveMinimumOrderQuantity };
