import { useCallback, useRef } from "react";
import type { BarcodeScanningResult, CameraType } from "expo-camera";
import { useCameraPermissions } from "expo-camera";
import {
  shouldForwardCameraScan,
  type CameraScanGateState,
} from "@/modules/scanner/camera-scan-gate";

interface UseCameraScanOptions {
  cooldownMs?: number;
  ignoreWhileProcessing?: boolean;
  suppressRepeatsUntilChange?: boolean;
  onBarcode: (barcode: string) => void | Promise<void>;
}

export type CameraScanMode = "single" | "continuous";

function normalizeBarcode(rawValue: string) {
  return rawValue.trim();
}

export function useCameraScan({
  cooldownMs = 1200,
  ignoreWhileProcessing = false,
  suppressRepeatsUntilChange = false,
  onBarcode,
}: UseCameraScanOptions) {
  const [permission, requestPermission] = useCameraPermissions();
  const scanGateRef = useRef<CameraScanGateState>({
    value: "",
    timestamp: 0,
    processing: false,
  });

  const handleBarcodeScanned = useCallback(
    async ({ data }: BarcodeScanningResult) => {
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

      scanGateRef.current = {
        value: barcode,
        timestamp: now,
        processing: true,
      };

      console.log("[camera-scan] forwarding barcode", { barcode });
      try {
        await onBarcode(barcode);
      } finally {
        scanGateRef.current.processing = false;
      }
    },
    [cooldownMs, ignoreWhileProcessing, onBarcode, suppressRepeatsUntilChange]
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
