import { useCallback, useEffect, useMemo, useState } from "react";
import { useAddToCart, resolveMinimumOrderQuantity } from "@/modules/shop/use-add-to-cart";
import { lookupProductsByBarcode } from "@/modules/scanner/api";
import { playScanFeedbackSound, preloadScanFeedbackSounds } from "@/modules/scanner/scan-sound";
import type { StoreOrderProductItem } from "@/modules/shop/types";
import type { ScanFeedbackState, ScanSelectionState, ScanSource } from "@/modules/scanner/types";

const initialFeedback: ScanFeedbackState = {
  status: "ready",
  message: "等待扫码",
};

interface UseScanResultOptions {
  autoAddWhenSingle?: boolean;
  mode?: "add-to-cart" | "lookup";
  onAddedToCart?: (product: StoreOrderProductItem, barcode: string, source: ScanSource) => void | Promise<void>;
  onProductFound?: (product: StoreOrderProductItem, barcode: string, source: ScanSource) => void | Promise<void>;
  storeCode?: string | null;
}

export function useScanResult({
  autoAddWhenSingle = false,
  mode = "add-to-cart",
  onAddedToCart,
  onProductFound,
  storeCode,
}: UseScanResultOptions) {
  const [feedback, setFeedback] = useState<ScanFeedbackState>(initialFeedback);
  const [selectionState, setSelectionState] = useState<ScanSelectionState | null>(null);
  const addToCart = useAddToCart(storeCode);

  useEffect(() => {
    void preloadScanFeedbackSounds();
  }, []);

  const updateFeedback = useCallback((nextFeedback: ScanFeedbackState) => {
    setFeedback(nextFeedback);
    void playScanFeedbackSound(nextFeedback.status);
  }, []);

  const addMatchedProduct = useCallback(
    async (product: StoreOrderProductItem, barcode: string, source: ScanSource) => {
      const quantity = resolveMinimumOrderQuantity(product);
      await addToCart.mutateAsync({
        product,
        quantity,
      });
      await onAddedToCart?.(product, barcode, source);

      updateFeedback({
        status: "added",
        message: `${source === "camera" ? "摄像头" : "扫码枪"}扫码已加入购物车`,
        barcode,
        productName: product.productName,
        addedQuantity: quantity,
      });
    },
    [addToCart, onAddedToCart, updateFeedback]
  );

  const handleBarcode = useCallback(
    async (barcode: string, source: ScanSource) => {
      if (!storeCode) {
        updateFeedback({
          status: "blocked",
          message: "请先选择门店后再扫码",
          barcode,
        });
        return;
      }

      setFeedback({
        status: "scanning",
        message: "正在查询条码对应商品",
        barcode,
      });

      try {
        const result = await lookupProductsByBarcode(barcode);
        const items = result.items ?? [];

        if (items.length === 0) {
          updateFeedback({
            status: "not_found",
            message: "未找到对应商品",
            barcode: result.barcode,
          });
          return;
        }

        if (items.length === 1) {
          if (mode === "lookup" && !autoAddWhenSingle) {
            await onProductFound?.(items[0], result.barcode, source);
            updateFeedback({
              status: "found",
              message: "已找到对应商品",
              barcode: result.barcode,
              productName: items[0].productName,
            });
            return;
          }

          await addMatchedProduct(items[0], result.barcode, source);
          return;
        }

        setSelectionState({
          barcode: result.barcode,
          source,
          items,
        });
        updateFeedback({
          status: "multiple",
          message: mode === "lookup" ? "条码匹配到多个商品，请选择查看" : "条码匹配到多个商品，请选择后加入购物车",
          barcode: result.barcode,
        });
      } catch (error) {
        updateFeedback({
          status: "error",
          message: error instanceof Error ? error.message : "扫码处理失败",
          barcode,
        });
      }
    },
    [addMatchedProduct, autoAddWhenSingle, mode, onProductFound, storeCode, updateFeedback]
  );

  const confirmSelection = useCallback(
    async (product: StoreOrderProductItem) => {
      if (!selectionState) {
        return;
      }

      setSelectionState(null);
      if (mode === "lookup") {
        await onProductFound?.(product, selectionState.barcode, selectionState.source);
        updateFeedback({
          status: "found",
          message: "已选择匹配商品",
          barcode: selectionState.barcode,
          productName: product.productName,
        });
        return;
      }

      await addMatchedProduct(product, selectionState.barcode, selectionState.source);
    },
    [addMatchedProduct, mode, onProductFound, selectionState, updateFeedback]
  );

  const actions = useMemo(
    () => ({
      clearSelection() {
        setSelectionState(null);
      },
      resetFeedback() {
        setFeedback(initialFeedback);
      },
    }),
    []
  );

  return {
    feedback,
    selectionState,
    isBusy: addToCart.isPending,
    handleBarcode,
    confirmSelection,
    ...actions,
  };
}
