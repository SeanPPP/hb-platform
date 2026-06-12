import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { i18n } from "@/shared/i18n/i18n";
import { resolveLocalizedErrorMessage } from "@/shared/i18n/error-message";
import { useAddToCart, resolveMinimumOrderQuantity } from "@/modules/shop/use-add-to-cart";
import { lookupProductsByBarcode } from "@/modules/scanner/api";
import { playScanFeedbackSound, preloadScanFeedbackSounds } from "@/modules/scanner/scan-sound";
import {
  createScanTraceId,
  getScanPerformanceTimestamp,
  logScanPerformance,
} from "@/modules/scanner/scan-performance";
import {
  canCompleteScanJob,
  completeActiveScanJob,
  createInitialScanQueue,
  enqueueScanJob,
  startScanJob,
  type EnqueueScanDecision,
  type ScanQueueJob,
} from "@/modules/scanner/scan-queue";
import type { StoreOrderProductItem } from "@/modules/shop/types";
import type { ScanFeedbackState, ScanSelectionState, ScanSource } from "@/modules/scanner/types";

const scanQueueOptions = {
  duplicateWindowMs: 250,
  maxSize: 10,
};

const initialFeedback: ScanFeedbackState = {
  status: "ready",
  message: i18n.t("common:scanner.waiting"),
};

function normalizeStoreCode(storeCode?: string | null) {
  const normalized = storeCode?.trim();
  return normalized ? normalized : null;
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
  const [feedback, setFeedback] = useState<ScanFeedbackState>(initialFeedback);
  const [selectionState, setSelectionState] = useState<ScanSelectionState | null>(null);
  const [isLookupPending, setIsLookupPending] = useState(false);
  const isHandlingBarcodeRef = useRef(false);
  const scanQueueRef = useRef(createInitialScanQueue());
  const scanGenerationRef = useRef(0);
  const selectionStateRef = useRef<ScanSelectionState | null>(null);
  const storeCodeRef = useRef<string | null>(normalizeStoreCode(storeCode));
  const drainScanQueueRef = useRef<() => void>(() => undefined);
  const addToCart = useAddToCart(storeCode);

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

  const addMatchedProduct = useCallback(
    async (
      product: StoreOrderProductItem,
      barcode: string,
      source: ScanSource,
      scanTraceId: string | undefined,
      expectedStoreCode: string | null
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

      await onAddedToCart?.(product, barcode, source, scanTraceId);

      updateFeedback({
        status: "added",
        message: i18n.t(source === "camera" ? "common:scanner.addedByCamera" : "common:scanner.addedByScanner"),
        barcode,
        productName: product.productName,
        addedQuantity: quantity,
      });
    },
    [addToCart, onAddedToCart, updateFeedback]
  );

  const processScanJob = useCallback(
    async (job: ScanQueueJob) => {
      const { barcode, scanTraceId, source } = job;
      const activeStoreCode = normalizeStoreCode(job.storeCode);
      const scanStartedAt = job.receivedAt ?? getScanPerformanceTimestamp();
      const jobGeneration = scanGenerationRef.current;
      isHandlingBarcodeRef.current = true;
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

        const lookupStartedAt = getScanPerformanceTimestamp();
        logScanPerformance("scan.lookup.frontend.start", {
          scanTraceId,
          barcode,
          source,
          storeCode: activeStoreCode,
        });
        const result = await lookupProductsByBarcode(barcode, activeStoreCode, scanTraceId);
        logScanPerformance("scan.lookup.frontend.done", {
          scanTraceId,
          barcode: result.barcode,
          source,
          storeCode: activeStoreCode,
          itemCount: result.items?.length ?? 0,
          elapsedMs: getScanPerformanceTimestamp() - lookupStartedAt,
        });
        if (!isCurrentStoreJob(job)) {
          logStaleStoreJob("after-lookup", job);
          return;
        }

        const items = result.items ?? [];

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

          await addMatchedProduct(items[0], result.barcode, source, scanTraceId, activeStoreCode);
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
    [addMatchedProduct, isCurrentStoreJob, logStaleStoreJob, mode, onProductFound, updateFeedback, updateSelectionState]
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
    isHandlingBarcodeRef.current = false;
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

      if (isHandlingBarcodeRef.current || selectionStateRef.current) {
        const enqueued = enqueueScanJob(scanQueueRef.current, job, scanStartedAt, scanQueueOptions);
        scanQueueRef.current = enqueued.queue;
        logEnqueueDecision(enqueued.decision, job);
        return;
      }

      if (!storeCode) {
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
    [logEnqueueDecision, processScanJob, storeCode, updateFeedback]
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
          selectionStoreCode
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
