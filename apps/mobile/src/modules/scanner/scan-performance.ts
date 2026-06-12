import type { ScanSource } from "@/modules/scanner/types";

const LOG_PREFIX = "[shop-scan-perf]";

function getBarcodeLogFields(barcode: string) {
  const trimmed = barcode.trim();
  return {
    barcodeTail: trimmed.slice(-6) || "empty",
    barcodeLength: trimmed.length,
  };
}

function sanitizeScanPerformancePayload(payload: Record<string, unknown>) {
  const sanitized: Record<string, unknown> = {};

  for (const [key, value] of Object.entries(payload)) {
    if (key === "barcode" && typeof value === "string") {
      Object.assign(sanitized, getBarcodeLogFields(value));
      continue;
    }

    sanitized[key] = value;
  }

  return sanitized;
}

export function getScanPerformanceTimestamp() {
  return Date.now();
}

export function createScanTraceId(source: ScanSource, barcode: string) {
  // 日志 trace 只保留条码尾号，方便排查同一次扫码，也避免在前端日志里暴露完整条码。
  const normalizedBarcode = getBarcodeLogFields(barcode).barcodeTail;
  return `${source}-${normalizedBarcode}-${Date.now().toString(36)}-${Math.random()
    .toString(36)
    .slice(2, 7)}`;
}

export function logScanPerformance(
  stage: string,
  payload: Record<string, unknown>
) {
  console.info(LOG_PREFIX, {
    stage,
    ...sanitizeScanPerformancePayload(payload),
  });
}
