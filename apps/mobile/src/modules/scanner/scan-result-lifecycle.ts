import type { AppStateStatus } from "react-native";

export function shouldFlushCartSyncOnAppStateChange(
  previousState: AppStateStatus,
  nextState: AppStateStatus
) {
  return previousState === "active" && (nextState === "inactive" || nextState === "background");
}

export function shouldFlushCartSyncImmediately(
  pendingBatchSize: number,
  maxBatchSize: number,
  appState: AppStateStatus,
  isMounted: boolean
) {
  // 后台或卸载时不能依赖 timer；批量满也应立刻推送到后端。
  return pendingBatchSize >= maxBatchSize || appState !== "active" || !isMounted;
}

export function canUpdateAddScanFeedback(
  latestScanTraceId: string | null,
  scanTraceId?: string
) {
  return !latestScanTraceId || !scanTraceId || latestScanTraceId === scanTraceId;
}
