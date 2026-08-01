import type { ScanSource } from "@/modules/scanner/types";

const LOG_PREFIX = "[shop-scan-perf]";
const SENSITIVE_SCAN_FIELD_PATTERN = /barcode|keyword|product[\s_-]*code/i;

function sanitizeScanPerformanceValue(value: unknown): unknown {
  if (Array.isArray(value)) {
    return value.map(sanitizeScanPerformanceValue);
  }
  if (!value || typeof value !== "object") {
    return value;
  }

  const sanitized: Record<string, unknown> = {};
  for (const [key, nestedValue] of Object.entries(value)) {
    if (SENSITIVE_SCAN_FIELD_PATTERN.test(key)) {
      continue;
    }
    sanitized[key] = sanitizeScanPerformanceValue(nestedValue);
  }
  return sanitized;
}

function sanitizeScanPerformancePayload(payload: Record<string, unknown>) {
  return sanitizeScanPerformanceValue(payload) as Record<string, unknown>;
}

export function getScanPerformanceTimestamp() {
  return Date.now();
}

export function createScanTraceId(source: ScanSource, _scanValue: string) {
  // trace 只用于关联一次扫码链路，必须与扫码内容完全无关。
  return `${source}-${Date.now().toString(36)}-${Math.random()
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
