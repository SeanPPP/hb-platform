/**
 * 商品查询页的连通性状态机（纯 reducer，便于单测）。
 *
 * - `network_failure`：在线请求遇到网络不可达 → 进入离线，记录进入时刻；
 * - `request_succeeded`：任一在线请求成功 → 立即退出离线；
 * - `backend_check`：探测结果；只有「可达」且探测时间晚于进入离线的时刻才退出，
 *   避免恢复控制器的旧探测结果把刚进入的离线态误判为在线。
 */
export interface ProductQueryConnectivityState {
  offline: boolean;
  /** 进入离线的时间戳（毫秒），在线时为 null。 */
  sinceMs: number | null;
  /** 离线期间发生的最近一次查询关键字，恢复在线后用于自动重跑。 */
  pendingKeyword: string | null;
}

export type ProductQueryConnectivityEvent =
  | { type: "network_failure"; atMs: number; keyword?: string | null }
  | { type: "request_succeeded" }
  | { type: "offline_lookup"; keyword: string }
  | { type: "backend_check"; reachable: boolean; checkedAtMs: number }
  | { type: "reset" };

export const INITIAL_PRODUCT_QUERY_CONNECTIVITY: ProductQueryConnectivityState = {
  offline: false,
  sinceMs: null,
  pendingKeyword: null,
};

/** 恢复在线后自动重跑查询的最小间隔（毫秒）。 */
export const OFFLINE_AUTO_RELOOKUP_MIN_INTERVAL_MS = 10_000;

/**
 * 判定恢复在线后是否允许自动重跑离线期间的查询。
 *
 * 可达性探测与业务请求一旦解析出不同的后端（例如设备绑定 host 与设置页偏好 host
 * 不一致），就会「探测成功→退出离线→自动重跑→请求失败→回到离线」地空转。
 * 根因由 resolveEffectiveApiBaseUrl 统一 host 解析来修，这里再加一道兜底：
 * 两次自动重跑之间必须间隔足够久，任何未来的误判都无法把它拖成紧密循环。
 * 用户手动查询不受此限制。
 */
export function shouldAutoRelookupAfterRecovery(input: {
  lastAutoRelookupAtMs: number | null;
  nowMs: number;
}): boolean {
  if (input.lastAutoRelookupAtMs === null) {
    return true;
  }
  return input.nowMs - input.lastAutoRelookupAtMs >= OFFLINE_AUTO_RELOOKUP_MIN_INTERVAL_MS;
}

export function reduceProductQueryConnectivity(
  state: ProductQueryConnectivityState,
  event: ProductQueryConnectivityEvent,
): ProductQueryConnectivityState {
  switch (event.type) {
    case "network_failure": {
      if (state.offline) {
        // 已离线时只更新待重跑关键字，不重置进入时刻，否则旧探测永远无法越过它。
        return event.keyword?.trim()
          ? { ...state, pendingKeyword: event.keyword.trim() }
          : state;
      }
      return {
        offline: true,
        sinceMs: event.atMs,
        pendingKeyword: event.keyword?.trim() || null,
      };
    }
    case "offline_lookup": {
      if (!state.offline) {
        return state;
      }
      const keyword = event.keyword.trim();
      return keyword ? { ...state, pendingKeyword: keyword } : state;
    }
    case "request_succeeded":
    case "reset":
      return state.offline || state.pendingKeyword
        ? INITIAL_PRODUCT_QUERY_CONNECTIVITY
        : state;
    case "backend_check": {
      if (!state.offline || !event.reachable) {
        return state;
      }
      if (state.sinceMs !== null && event.checkedAtMs <= state.sinceMs) {
        // 探测早于进入离线的时刻，说明它反映的是断网前的状态，不能据此退出。
        return state;
      }
      // 退出离线但保留待重跑关键字，由页面消费后再清空。
      return { offline: false, sinceMs: null, pendingKeyword: state.pendingKeyword };
    }
    default:
      return state;
  }
}
