import { useCallback, useRef } from "react";
import type { BarcodeScanningResult, CameraType } from "expo-camera";
import { useCameraPermissions } from "expo-camera";

interface UseCameraScanOptions {
  cooldownMs?: number;
  onBarcode: (barcode: string) => void | Promise<void>;
}

function normalizeBarcode(rawValue: string) {
  return rawValue.trim();
}

export function useCameraScan({ cooldownMs = 1200, onBarcode }: UseCameraScanOptions) {
  const [permission, requestPermission] = useCameraPermissions();
  const lastScannedRef = useRef<{ value: string; timestamp: number }>({
    value: "",
    timestamp: 0,
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
      if (
        lastScannedRef.current.value === barcode &&
        now - lastScannedRef.current.timestamp < cooldownMs
      ) {
        return;
      }

      lastScannedRef.current = {
        value: barcode,
        timestamp: now,
      };

      console.log("[camera-scan] forwarding barcode", { barcode });
      await onBarcode(barcode);
    },
    [cooldownMs, onBarcode]
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
