import { useCallback, useEffect, useRef } from "react";
import type { BarcodeScanningResult, CameraType } from "expo-camera";
import { useCameraPermissions } from "expo-camera";
import {
  shouldForwardCameraScan,
  type CameraScanGateState,
} from "@/modules/scanner/camera-scan-gate";

interface UseCameraScanOptions {
  cooldownMs?: number;
  disabled?: boolean;
  ignoreWhileProcessing?: boolean;
  resetKey?: string | number | boolean | null;
  suppressRepeatsUntilChange?: boolean;
  onBarcode: (barcode: string) => void | Promise<void>;
}

export type CameraScanMode = "single" | "continuous";

function normalizeBarcode(rawValue: string) {
  return rawValue.trim();
}

function createInitialScanGateState(): CameraScanGateState {
  return {
    value: "",
    timestamp: 0,
    processing: false,
  };
}

export function useCameraScan({
  cooldownMs = 1200,
  disabled = false,
  ignoreWhileProcessing = false,
  resetKey = null,
  suppressRepeatsUntilChange = false,
  onBarcode,
}: UseCameraScanOptions) {
  const [permission, requestPermission] = useCameraPermissions();
  const scanGateRef = useRef<CameraScanGateState>(createInitialScanGateState());

  useEffect(() => {
    // 业务上下文切换时丢弃上一目标的条码记忆，避免跨弹窗/跨模式误拦截。
    scanGateRef.current = createInitialScanGateState();
  }, [resetKey]);

  const handleBarcodeScanned = useCallback(
    async ({ data }: BarcodeScanningResult) => {
      if (disabled) {
        return;
      }

      const barcode = normalizeBarcode(data);
      console.log("[camera-scan] raw barcode event", {
        rawData: data,
        normalized: barcode,
      });
      if (!barcode) {
        return;
      }

      const now = Date.now();
      if (!shouldForwardCameraScan(scanGateRef.current, barcode, now, {
        cooldownMs,
        ignoreWhileProcessing,
        suppressRepeatsUntilChange,
      })) {
        return;
      }

      const activeGateState: CameraScanGateState = {
        value: barcode,
        timestamp: now,
        processing: true,
      };
      scanGateRef.current = activeGateState;

      console.log("[camera-scan] forwarding barcode", { barcode });
      try {
        await onBarcode(barcode);
      } finally {
        // resetKey 可能已切换到新业务上下文；只释放本次扫码占用的门禁。
        if (scanGateRef.current === activeGateState) {
          scanGateRef.current.processing = false;
        }
      }
    },
    [cooldownMs, disabled, ignoreWhileProcessing, onBarcode, suppressRepeatsUntilChange]
  );

  return {
    permission,
    requestPermission,
    cameraProps: {
      facing: "back" as CameraType,
      onBarcodeScanned: handleBarcodeScanned,
    },
  };
}
