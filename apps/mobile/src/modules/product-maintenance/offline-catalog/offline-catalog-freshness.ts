/**
 * 离线快照自动刷新策略（纯函数）。
 */
import type { ActiveOfflineCatalogMetadata } from "./types";

export const OFFLINE_CATALOG_AUTO_REFRESH_INTERVAL_MS = 4 * 60 * 60 * 1000;
export const OFFLINE_CATALOG_FAILURE_BACKOFF_MS = 10 * 60 * 1000;
/** 用户主动取消后的抑制时长：否则焦点副作用会在取消的同一帧把下载重新拉起来。 */
export const OFFLINE_CATALOG_CANCEL_BACKOFF_MS = 30 * 60 * 1000;

export interface OfflineCatalogAutoRefreshInput {
  activeMeta: ActiveOfflineCatalogMetadata | null;
  isOnline: boolean;
  isRefreshing: boolean;
  /** 当前门店最近一次刷新失败的时刻（毫秒），无失败为 null。 */
  lastFailedAtMs: number | null;
  /** 当前门店最近一次被用户取消的时刻（毫秒），未取消为 null。 */
  lastCancelledAtMs?: number | null;
  /** 本次进程内最近一次成功刷新（含 noChange）的时刻，null 表示尚未检查过。 */
  lastRefreshedAtMs: number | null;
  nowMs: number;
  /** 员工在设置里关闭「自动更新」后为 false；未提供视为开启。 */
  autoRefreshEnabled?: boolean;
}

export function shouldAutoRefreshOfflineCatalog(input: OfflineCatalogAutoRefreshInput): boolean {
  if (input.autoRefreshEnabled === false) {
    // 关闭自动更新后只响应设置页/状态行的手动更新。
    return false;
  }
  if (!input.isOnline || input.isRefreshing) {
    return false;
  }
  if (
    input.lastFailedAtMs !== null &&
    input.nowMs - input.lastFailedAtMs < OFFLINE_CATALOG_FAILURE_BACKOFF_MS
  ) {
    // 失败后短时间内不重试，避免离线/服务端构建中反复打点。
    return false;
  }
  if (
    input.lastCancelledAtMs != null &&
    input.nowMs - input.lastCancelledAtMs < OFFLINE_CATALOG_CANCEL_BACKOFF_MS
  ) {
    // 用户刚刚主动取消，自动刷新必须让位，否则「取消」按钮形同虚设。
    return false;
  }
  if (input.activeMeta === null) {
    return true;
  }
  if (input.lastRefreshedAtMs !== null) {
    return input.nowMs - input.lastRefreshedAtMs >= OFFLINE_CATALOG_AUTO_REFRESH_INTERVAL_MS;
  }
  const activatedAtMs = Date.parse(input.activeMeta.activatedAt);
  if (!Number.isFinite(activatedAtMs)) {
    return true;
  }
  return input.nowMs - activatedAtMs >= OFFLINE_CATALOG_AUTO_REFRESH_INTERVAL_MS;
}
