import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { i18n } from "@/shared/i18n/i18n";
import { resolveLocalizedErrorMessage } from "@/shared/i18n/error-message";
import { useAddToCart, resolveMinimumOrderQuantity } from "@/modules/shop/use-add-to-cart";
import { lookupProductsByBarcode } from "@/modules/scanner/api";
import { scanLookupAndAddToCart } from "@/modules/shop/api";
import { playScanFeedbackSound, preloadScanFeedbackSounds } from "@/modules/scanner/scan-sound";
import {
  createScanTraceId,
  getScanPerformanceTimestamp,
  logScanPerformance,
} from "@/modules/scanner/scan-performance";
import {
  clearScanLookupInFlight,
  createScanLookupCache,
  createScanLookupInFlight,
  getCachedScanLookupProduct,
  rememberScanLookupProduct,
  runScanLookupInFlight,
} from "@/modules/scanner/scan-lookup-cache";
import {
  canCompleteScanJob,
  completeActiveScanJob,
  createInitialScanQueue,
  enqueueScanJob,
  startScanJob,
  type EnqueueScanDecision,
  type ScanQueueJob,
} from "@/modules/scanner/scan-queue";
import {
  isCurrentCartStore,
  reserveCartMutationOperationId,
  resolveCartMutationCache,
  snapshotCartMutationCache,
} from "@/modules/shop/cart-cache";
import { useCartStore } from "@/store/cart-store";
import type {
  StoreOrderProductItem,
  StoreOrderScanLookupAddResult,
  StoreOrderScanLookupResult,
} from "@/modules/shop/types";
import type { ScanFeedbackState, ScanSelectionState, ScanSource } from "@/modules/scanner/types";

const scanQueueOptions = {
  duplicateWindowMs: 250,
  maxSize: 10,
};

const scanLookupCacheTtlMs = 60_000;

const initialFeedback: ScanFeedbackState = {
  status: "ready",
  message: i18n.t("common:scanner.waiting"),
};

function normalizeStoreCode(storeCode?: string | null) {
  const normalized = storeCode?.trim();
  return normalized ? normalized : null;
}

function isScanLookupAddResult(
  result: StoreOrderScanLookupResult | StoreOrderScanLookupAddResult
): result is StoreOrderScanLookupAddResult {
  return "added" in result;
}

interface UseScanResultOptions {
  autoAddWhenSingle?: boolean;
  mode?: "add-to-cart" | "lookup";
  onAddedToCart?: (product: StoreOrderProductItem, barcode: string, source: ScanSource, scanTraceId?: string) => void | Promise<void>;
  onProductFound?: (
    product: StoreOrderProductItem,
    barcode: string,
    source: ScanSource,
    scanTraceId?: string,
    storeCode?: string | null
  ) => void | Promise<void>;
  storeCode?: string | null;
}

export function useScanResult({
  autoAddWhenSingle = false,
  mode = "add-to-cart",
  onAddedToCart,
  onProductFound,
  storeCode,
}: UseScanResultOptions) {
  const queryClient = useQueryClient();
  const [feedback, setFeedback] = useState<ScanFeedbackState>(initialFeedback);
  const [selectionState, setSelectionState] = useState<ScanSelectionState | null>(null);
  const [isLookupPending, setIsLookupPending] = useState(false);
  const isHandlingBarcodeRef = useRef(false);
  const activeAddScanCountRef = useRef(0);
  const lookupCacheRef = useRef(createScanLookupCache(scanLookupCacheTtlMs));
  const scanLookupAddInFlightRef = useRef(createScanLookupInFlight<StoreOrderScanLookupAddResult>());
  const scanQueueRef = useRef(createInitialScanQueue());
  const scanGenerationRef = useRef(0);
  const selectionStateRef = useRef<ScanSelectionState | null>(null);
  const storeCodeRef = useRef<string | null>(normalizeStoreCode(storeCode));
  const drainScanQueueRef = useRef<() => void>(() => undefined);
  const addToCart = useAddToCart(storeCode, { concurrent: true });
  const setCartSummary = useCartStore((state) => state.setCartSummary);

  storeCodeRef.current = normalizeStoreCode(storeCode);

  useEffect(() => {
    preloadScanFeedbackSounds();
  }, []);

  const updateFeedback = useCallback((nextFeedback: ScanFeedbackState) => {
    setFeedback(nextFeedback);
    void playScanFeedbackSound(nextFeedback.status);
  }, []);

  const updateSelectionState = useCallback((nextSelectionState: ScanSelectionState | null) => {
    selectionStateRef.current = nextSelectionState;
    setSelectionState(nextSelectionState);
  }, []);

  const isCurrentStoreJob = useCallback((job: ScanQueueJob) => {
    return normalizeStoreCode(job.storeCode) === storeCodeRef.current;
  }, []);

  const logStaleStoreJob = useCallback((stage: string, job: ScanQueueJob) => {
    logScanPerformance("scan.ignored.stale-store", {
      scanTraceId: job.scanTraceId,
      barcode: job.barcode,
      source: job.source,
      stage,
      storeCode: normalizeStoreCode(job.storeCode),
      currentStoreCode: storeCodeRef.current,
    });
  }, []);

  const logEnqueueDecision = useCallback(
    (decision: EnqueueScanDecision, job: ScanQueueJob) => {
      const basePayload = {
        scanTraceId: job.scanTraceId,
        barcode: job.barcode,
        source: job.source,
        storeCode: normalizeStoreCode(job.storeCode),
        queueSize: decision.queueSize,
      };

      if (decision.type === "duplicate") {
        logScanPerformance("scan.ignored.duplicate", {
          ...basePayload,
          reason: decision.reason,
        });
        return;
      }

      if (decision.type === "overflow") {
        logScanPerformance("scan.queue.overflow", {
          ...basePayload,
          droppedScanTraceId: decision.droppedJob?.scanTraceId,
        });
        return;
      }

      logScanPerformance("scan.queued.busy", basePayload);
    },
    []
  );

  const completeAddedProduct = useCallback(
    async (
      product: StoreOrderProductItem,
      barcode: string,
      source: ScanSource,
      scanTraceId: string | undefined,
      expectedStoreCode: string | null,
      quantity: number,
      expectedGeneration?: number
    ) => {
      if (expectedStoreCode !== storeCodeRef.current) {
        logScanPerformance("cart.add.frontend.ignored.stale-store", {
          scanTraceId,
          barcode,
          source,
          storeCode: expectedStoreCode,
          currentStoreCode: storeCodeRef.current,
          productCode: product.productCode,
        });
        return;
      }
      if (expectedGeneration !== undefined && expectedGeneration !== scanGenerationRef.current) {
        logScanPerformance("cart.add.frontend.ignored.stale-generation", {
          scanTraceId,
          barcode,
          source,
          storeCode: expectedStoreCode,
          productCode: product.productCode,
          expectedGeneration,
          currentGeneration: scanGenerationRef.current,
        });
        return;
      }

      await onAddedToCart?.(product, barcode, source, scanTraceId);
      if (expectedGeneration !== undefined && expectedGeneration !== scanGenerationRef.current) {
        logScanPerformance("cart.add.frontend.ignored.stale-generation", {
          scanTraceId,
          barcode,
          source,
          storeCode: expectedStoreCode,
          productCode: product.productCode,
          expectedGeneration,
          currentGeneration: scanGenerationRef.current,
        });
        return;
      }

      updateFeedback({
        status: "added",
        message: i18n.t(source === "camera" ? "common:scanner.addedByCamera" : "common:scanner.addedByScanner"),
        barcode,
        productName: product.productName,
        addedQuantity: quantity,
      });
    },
    [onAddedToCart, updateFeedback]
  );

  const addMatchedProduct = useCallback(
    async (
      product: StoreOrderProductItem,
      barcode: string,
      source: ScanSource,
      scanTraceId: string | undefined,
      expectedStoreCode: string | null,
      expectedGeneration?: number
    ) => {
      updateFeedback({
        status: "found",
        message: i18n.t("common:scanner.addingToCart"),
        barcode,
        productName: product.productName,
      });

      const quantity = resolveMinimumOrderQuantity(product);
      const addStartedAt = getScanPerformanceTimestamp();
      logScanPerformance("cart.add.frontend.start", {
        scanTraceId,
        barcode,
        source,
        storeCode: expectedStoreCode,
        productCode: product.productCode,
        quantity,
      });
      await addToCart.mutateAsync({
        product,
        quantity,
        scanTraceId,
      });
      logScanPerformance("cart.add.frontend.done", {
        scanTraceId,
        barcode,
        source,
        storeCode: expectedStoreCode,
        productCode: product.productCode,
        elapsedMs: getScanPerformanceTimestamp() - addStartedAt,
      });
      await completeAddedProduct(
        product,
        barcode,
        source,
        scanTraceId,
        expectedStoreCode,
        quantity,
        expectedGeneration
      );
    },
    [addToCart, completeAddedProduct, updateFeedback]
  );

  const applyScanLookupAddCart = useCallback(
    async (
      product: StoreOrderProductItem,
      barcode: string,
      source: ScanSource,
      scanTraceId: string | undefined,
      expectedStoreCode: string,
      cartResult: NonNullable<Awaited<ReturnType<typeof scanLookupAndAddToCart>>["cart"]>,
      cartMutationOperationId: number,
      expectedGeneration?: number
    ) => {
      const quantity = resolveMinimumOrderQuantity(product);
      if (expectedGeneration !== undefined && expectedGeneration !== scanGenerationRef.current) {
        logScanPerformance("cart.cache.frontend.ignored.stale-generation", {
          scanTraceId,
          storeCode: expectedStoreCode,
          productCode: product.productCode,
          expectedGeneration,
          currentGeneration: scanGenerationRef.current,
        });
        return;
      }

      const cacheStartedAt = getScanPerformanceTimestamp();
      const snapshot = snapshotCartMutationCache(
        queryClient,
        expectedStoreCode,
        {
          product,
          quantity,
          type: "add",
        },
        cartMutationOperationId
      );
      const nextCart = resolveCartMutationCache(queryClient, expectedStoreCode, snapshot, cartResult);
      const currentStoreCode = useCartStore.getState().selectedStore?.storeCode ?? null;
      const shouldUpdateGlobalCart = isCurrentCartStore(currentStoreCode, expectedStoreCode);
      if (shouldUpdateGlobalCart) {
        setCartSummary(nextCart);
      } else {
        logScanPerformance("cart.cache.frontend.skipped-stale-store", {
          scanTraceId,
          storeCode: expectedStoreCode,
          currentStoreCode,
          productCode: product.productCode,
        });
      }
      logScanPerformance("cart.cache.frontend.done", {
        scanTraceId,
        storeCode: expectedStoreCode,
        productCode: product.productCode,
        totalQuantity: nextCart?.totalQuantity ?? 0,
        updatedGlobalCart: shouldUpdateGlobalCart,
        elapsedMs: getScanPerformanceTimestamp() - cacheStartedAt,
      });

      await completeAddedProduct(
        product,
        barcode,
        source,
        scanTraceId,
        expectedStoreCode,
        quantity,
        expectedGeneration
      );
    },
    [completeAddedProduct, queryClient, setCartSummary]
  );

  const processScanJob = useCallback(
    async (job: ScanQueueJob) => {
      const { barcode, scanTraceId, source } = job;
      const activeStoreCode = normalizeStoreCode(job.storeCode);
      const scanStartedAt = job.receivedAt ?? getScanPerformanceTimestamp();
      const jobGeneration = scanGenerationRef.current;
      const isAddMode = mode === "add-to-cart";
      if (isAddMode) {
        activeAddScanCountRef.current += 1;
      } else {
        isHandlingBarcodeRef.current = true;
      }
      setIsLookupPending(true);

      try {
        if (!isCurrentStoreJob(job)) {
          logStaleStoreJob("before-lookup", job);
          return;
        }

        if (!activeStoreCode) {
          logScanPerformance("scan.blocked.no-store", {
            scanTraceId,
            barcode,
            source,
          });
          updateFeedback({
            status: "blocked",
            message: i18n.t("common:scanner.selectStoreFirst"),
            barcode,
          });
          return;
        }

        setFeedback({
          status: "scanning",
          message: i18n.t("common:scanner.lookupInProgress"),
          barcode,
        });

        if (isAddMode) {
          const cachedProduct = getCachedScanLookupProduct(
            lookupCacheRef.current,
            activeStoreCode,
            barcode,
            getScanPerformanceTimestamp()
          );
          if (cachedProduct) {
            logScanPerformance("scan.lookup.frontend.cache-hit", {
              scanTraceId,
              barcode,
              source,
              storeCode: activeStoreCode,
              productCode: cachedProduct.productCode,
            });
            await addMatchedProduct(cachedProduct, barcode, source, scanTraceId, activeStoreCode, jobGeneration);
            logScanPerformance("scan.add-flow.done", {
              scanTraceId,
              barcode,
              source,
              storeCode: activeStoreCode,
              productCode: cachedProduct.productCode,
              cacheHit: true,
              totalElapsedMs: getScanPerformanceTimestamp() - scanStartedAt,
            });
            return;
          }
        }

        const lookupStartedAt = getScanPerformanceTimestamp();
        const lookupStage = isAddMode ? "scan.lookup-add.frontend" : "scan.lookup.frontend";
        logScanPerformance(`${lookupStage}.start`, {
          scanTraceId,
          barcode,
          source,
          storeCode: activeStoreCode,
        });
        let cartMutationOperationId: number | undefined;
        let sharedScanLookupAdd = false;
        let result: StoreOrderScanLookupResult | StoreOrderScanLookupAddResult;
        if (isAddMode) {
          const lookupAddResult = await runScanLookupInFlight(
            scanLookupAddInFlightRef.current,
            activeStoreCode,
            barcode,
            () => {
              cartMutationOperationId = reserveCartMutationOperationId();
              // 合并接口按扫码当刻捕获的门店提交；若用户随后切店，只忽略回来的 UI 更新。
              return scanLookupAndAddToCart(barcode, activeStoreCode, scanTraceId);
            }
          );
          result = lookupAddResult.result;
          sharedScanLookupAdd = lookupAddResult.shared;
        } else {
          result = await lookupProductsByBarcode(barcode, activeStoreCode, scanTraceId);
        }
        logScanPerformance(`${lookupStage}.done`, {
          scanTraceId,
          barcode: result.barcode,
          source,
          storeCode: activeStoreCode,
          itemCount: result.items?.length ?? 0,
          added: isScanLookupAddResult(result) ? result.added : undefined,
          shared: sharedScanLookupAdd,
          elapsedMs: getScanPerformanceTimestamp() - lookupStartedAt,
        });
        if (jobGeneration !== scanGenerationRef.current) {
          logScanPerformance("scan.ignored.stale-generation", {
            scanTraceId,
            barcode: result.barcode,
            source,
            storeCode: activeStoreCode,
            jobGeneration,
            currentGeneration: scanGenerationRef.current,
          });
          return;
        }
        if (!isCurrentStoreJob(job)) {
          logStaleStoreJob("after-lookup", job);
          return;
        }

        const items = result.items ?? [];

        if (isAddMode && items.length === 1) {
          rememberScanLookupProduct(
            lookupCacheRef.current,
            activeStoreCode,
            result.barcode,
            items[0],
            getScanPerformanceTimestamp()
          );

          if (isScanLookupAddResult(result) && result.added && result.cart) {
            if (sharedScanLookupAdd) {
              // 共享请求只复用商品命中，重复扫码仍必须再加一次数量。
              await addMatchedProduct(items[0], result.barcode, source, scanTraceId, activeStoreCode, jobGeneration);
              logScanPerformance("scan.add-flow.done", {
                scanTraceId,
                barcode: result.barcode,
                source,
                storeCode: activeStoreCode,
                productCode: items[0].productCode,
                sharedLookup: true,
                totalElapsedMs: getScanPerformanceTimestamp() - scanStartedAt,
              });
              return;
            }

            await applyScanLookupAddCart(
              items[0],
              result.barcode,
              source,
              scanTraceId,
              activeStoreCode,
              result.cart,
              cartMutationOperationId ?? reserveCartMutationOperationId(),
              jobGeneration
            );
            logScanPerformance("scan.add-flow.done", {
              scanTraceId,
              barcode: result.barcode,
              source,
              storeCode: activeStoreCode,
              productCode: items[0].productCode,
              totalElapsedMs: getScanPerformanceTimestamp() - scanStartedAt,
            });
            return;
          }
        }

        if (items.length === 0) {
          logScanPerformance("scan.not-found", {
            scanTraceId,
            barcode: result.barcode,
            source,
            storeCode: activeStoreCode,
            totalElapsedMs: getScanPerformanceTimestamp() - scanStartedAt,
          });
          updateFeedback({
            status: "not_found",
            message: i18n.t("common:scanner.notFound"),
            barcode: result.barcode,
          });
          return;
        }

        if (items.length === 1) {
          if (mode === "lookup") {
            updateFeedback({
              status: "found",
              message: i18n.t("common:scanner.found"),
              barcode: result.barcode,
              productName: items[0].productName,
            });
            logScanPerformance("scan.single-found", {
              scanTraceId,
              barcode: result.barcode,
              source,
              storeCode: activeStoreCode,
              productCode: items[0].productCode,
              totalElapsedMs: getScanPerformanceTimestamp() - scanStartedAt,
            });
            await onProductFound?.(items[0], result.barcode, source, scanTraceId, activeStoreCode);
            logScanPerformance("scan.lookup-flow.done", {
              scanTraceId,
              barcode: result.barcode,
              source,
              storeCode: activeStoreCode,
              productCode: items[0].productCode,
              totalElapsedMs: getScanPerformanceTimestamp() - scanStartedAt,
            });
            return;
          }

          await addMatchedProduct(items[0], result.barcode, source, scanTraceId, activeStoreCode, jobGeneration);
          logScanPerformance("scan.add-flow.done", {
            scanTraceId,
            barcode: result.barcode,
            source,
            storeCode: activeStoreCode,
            productCode: items[0].productCode,
            totalElapsedMs: getScanPerformanceTimestamp() - scanStartedAt,
          });
          return;
        }

        updateSelectionState({
          barcode: result.barcode,
          scanTraceId,
          storeCode: activeStoreCode,
          source,
          items,
        });
        logScanPerformance("scan.multiple-found", {
          scanTraceId,
          barcode: result.barcode,
          source,
          storeCode: activeStoreCode,
          itemCount: items.length,
          totalElapsedMs: getScanPerformanceTimestamp() - scanStartedAt,
        });
        updateFeedback({
          status: "multiple",
          message: i18n.t(mode === "lookup" ? "common:scanner.multipleLookup" : "common:scanner.multipleCart"),
          barcode: result.barcode,
        });
      } catch (error) {
        if (jobGeneration !== scanGenerationRef.current) {
          logScanPerformance("scan.error.ignored.stale-generation", {
            scanTraceId,
            barcode,
            source,
            storeCode: activeStoreCode,
            jobGeneration,
            currentGeneration: scanGenerationRef.current,
            error: error instanceof Error ? error.message : String(error),
          });
          return;
        }
        if (!isCurrentStoreJob(job)) {
          logStaleStoreJob("error", job);
          return;
        }

        logScanPerformance("scan.error", {
          scanTraceId,
          barcode,
          source,
          storeCode: activeStoreCode,
          totalElapsedMs: getScanPerformanceTimestamp() - scanStartedAt,
          error: error instanceof Error ? error.message : String(error),
        });
        updateFeedback({
          status: "error",
          message: resolveLocalizedErrorMessage(error, {
            language: i18n.resolvedLanguage ?? "zh",
            t: i18n.t.bind(i18n),
            fallbackKey: "common:scanner.failed",
          }),
          barcode,
        });
      } finally {
        if (isAddMode) {
          if (jobGeneration === scanGenerationRef.current) {
            activeAddScanCountRef.current = Math.max(0, activeAddScanCountRef.current - 1);
            setIsLookupPending(activeAddScanCountRef.current > 0);
          } else {
            logScanPerformance("scan.finally.ignored.stale-generation", {
              scanTraceId,
              barcode,
              source,
              storeCode: activeStoreCode,
              jobGeneration,
              currentGeneration: scanGenerationRef.current,
            });
          }
          return;
        }

        if (!canCompleteScanJob(scanQueueRef.current, job, jobGeneration, scanGenerationRef.current)) {
          logScanPerformance("scan.finally.ignored.stale-job", {
            scanTraceId,
            barcode,
            source,
            storeCode: activeStoreCode,
            currentActiveScanTraceId: scanQueueRef.current.active?.scanTraceId,
            jobGeneration,
            currentGeneration: scanGenerationRef.current,
          });
          return;
        }

        isHandlingBarcodeRef.current = false;
        setIsLookupPending(false);
        const completed = completeActiveScanJob(scanQueueRef.current);
        scanQueueRef.current = completed.queue;
        drainScanQueueRef.current();
      }
    },
    [
      addMatchedProduct,
      applyScanLookupAddCart,
      isCurrentStoreJob,
      logStaleStoreJob,
      mode,
      onProductFound,
      updateFeedback,
      updateSelectionState,
    ]
  );

  const drainScanQueue = useCallback(() => {
    // 多候选弹窗期间不消费下一条，避免用户选择上下文被后续扫码覆盖。
    if (selectionStateRef.current || isHandlingBarcodeRef.current) {
      return;
    }

    let nextJob = scanQueueRef.current.active;
    if (!nextJob && scanQueueRef.current.pending.length > 0) {
      const completed = completeActiveScanJob(scanQueueRef.current);
      scanQueueRef.current = completed.queue;
      nextJob = completed.nextJob;
    }

    if (!nextJob) {
      return;
    }

    void processScanJob(nextJob);
  }, [processScanJob]);

  useEffect(() => {
    drainScanQueueRef.current = drainScanQueue;
  }, [drainScanQueue]);

  useEffect(() => {
    // 门店切换时丢弃旧门店上下文，避免旧扫码在新门店下继续显示或加购。
    scanGenerationRef.current += 1;
    scanQueueRef.current = createInitialScanQueue();
    lookupCacheRef.current = createScanLookupCache(scanLookupCacheTtlMs);
    clearScanLookupInFlight(scanLookupAddInFlightRef.current);
    isHandlingBarcodeRef.current = false;
    activeAddScanCountRef.current = 0;
    setIsLookupPending(false);
    updateSelectionState(null);
    setFeedback(initialFeedback);
  }, [storeCode, updateSelectionState]);

  const handleBarcode = useCallback(
    async (barcode: string, source: ScanSource) => {
      const scanTraceId = createScanTraceId(source, barcode);
      const scanStartedAt = getScanPerformanceTimestamp();
      const job: ScanQueueJob = {
        barcode,
        scanTraceId,
        receivedAt: scanStartedAt,
        source,
        storeCode: normalizeStoreCode(storeCode),
      };
      logScanPerformance("scan.received", {
        scanTraceId,
        barcode,
        source,
        storeCode: job.storeCode,
      });

      if (mode === "add-to-cart") {
        if (selectionStateRef.current) {
          logScanPerformance("scan.dropped.selection-open", {
            scanTraceId,
            barcode,
            source,
            storeCode: job.storeCode,
          });
          return;
        }

        if (!job.storeCode) {
          logScanPerformance("scan.blocked.no-store", {
            scanTraceId,
            barcode,
            source,
          });
          updateFeedback({
            status: "blocked",
            message: i18n.t("common:scanner.selectStoreFirst"),
            barcode,
          });
          return;
        }

        // 加购模式不进队列：连续扫码每条都并发启动，缓存命中直接 scan-add。
        void processScanJob(job);
        return;
      }

      if (isHandlingBarcodeRef.current || selectionStateRef.current) {
        const enqueued = enqueueScanJob(scanQueueRef.current, job, scanStartedAt, scanQueueOptions);
        scanQueueRef.current = enqueued.queue;
        logEnqueueDecision(enqueued.decision, job);
        return;
      }

      if (!job.storeCode) {
        logScanPerformance("scan.blocked.no-store", {
          scanTraceId,
          barcode,
          source,
        });
        updateFeedback({
          status: "blocked",
          message: i18n.t("common:scanner.selectStoreFirst"),
          barcode,
        });
        return;
      }

      const started = startScanJob(scanQueueRef.current, job, scanStartedAt, scanQueueOptions);
      scanQueueRef.current = started.queue;
      if (started.decision.type === "duplicate") {
        logScanPerformance("scan.ignored.duplicate", {
          scanTraceId,
          barcode,
          source,
          storeCode: job.storeCode,
          queueSize: started.decision.queueSize,
          reason: started.decision.reason,
        });
        return;
      }

      await processScanJob(job);
    },
    [logEnqueueDecision, mode, processScanJob, storeCode, updateFeedback]
  );

  const confirmSelection = useCallback(
    async (product: StoreOrderProductItem) => {
      if (!selectionState) {
        return;
      }

      updateSelectionState(null);
      const selectionStoreCode = normalizeStoreCode(selectionState.storeCode);
      if (selectionStoreCode !== storeCodeRef.current) {
        logScanPerformance("scan.selection.ignored.stale-store", {
          scanTraceId: selectionState.scanTraceId,
          barcode: selectionState.barcode,
          source: selectionState.source,
          storeCode: selectionStoreCode,
          currentStoreCode: storeCodeRef.current,
          productCode: product.productCode,
        });
        drainScanQueueRef.current();
        return;
      }

      if (mode === "lookup") {
        const selectionGeneration = scanGenerationRef.current;
        isHandlingBarcodeRef.current = true;
        setIsLookupPending(true);
        try {
          const selectStartedAt = getScanPerformanceTimestamp();
          logScanPerformance("scan.selection.frontend.start", {
            scanTraceId: selectionState.scanTraceId,
            barcode: selectionState.barcode,
            source: selectionState.source,
            storeCode: selectionStoreCode,
            productCode: product.productCode,
          });
          await onProductFound?.(
            product,
            selectionState.barcode,
            selectionState.source,
            selectionState.scanTraceId,
            selectionStoreCode
          );
          logScanPerformance("scan.selection.frontend.done", {
            scanTraceId: selectionState.scanTraceId,
            barcode: selectionState.barcode,
            source: selectionState.source,
            storeCode: selectionStoreCode,
            productCode: product.productCode,
            elapsedMs: getScanPerformanceTimestamp() - selectStartedAt,
          });
          updateFeedback({
            status: "found",
            message: i18n.t("common:scanner.selected"),
            barcode: selectionState.barcode,
            productName: product.productName,
          });
        } finally {
          if (selectionGeneration === scanGenerationRef.current) {
            isHandlingBarcodeRef.current = false;
            setIsLookupPending(false);
            drainScanQueueRef.current();
          } else {
            logScanPerformance("scan.selection.finally.ignored.stale-generation", {
              scanTraceId: selectionState.scanTraceId,
              barcode: selectionState.barcode,
              source: selectionState.source,
              storeCode: selectionStoreCode,
              selectionGeneration,
              currentGeneration: scanGenerationRef.current,
            });
          }
        }
        return;
      }

      const selectionGeneration = scanGenerationRef.current;
      isHandlingBarcodeRef.current = true;
      setIsLookupPending(true);
      try {
        await addMatchedProduct(
          product,
          selectionState.barcode,
          selectionState.source,
          selectionState.scanTraceId,
          selectionStoreCode,
          selectionGeneration
        );
      } finally {
        if (selectionGeneration === scanGenerationRef.current) {
          isHandlingBarcodeRef.current = false;
          setIsLookupPending(false);
          drainScanQueueRef.current();
        } else {
          logScanPerformance("scan.selection.finally.ignored.stale-generation", {
            scanTraceId: selectionState.scanTraceId,
            barcode: selectionState.barcode,
            source: selectionState.source,
            storeCode: selectionStoreCode,
            selectionGeneration,
            currentGeneration: scanGenerationRef.current,
          });
        }
      }
    },
    [addMatchedProduct, mode, onProductFound, selectionState, updateFeedback, updateSelectionState]
  );

  const actions = useMemo(
    () => ({
      clearSelection() {
        updateSelectionState(null);
        drainScanQueueRef.current();
      },
      resetFeedback() {
        setFeedback({
          status: "ready",
          message: i18n.t("common:scanner.waiting"),
        });
      },
    }),
    [updateSelectionState]
  );

  return {
    feedback,
    selectionState,
    isBusy: isLookupPending || addToCart.isPending,
    handleBarcode,
    confirmSelection,
    ...actions,
  };
}
