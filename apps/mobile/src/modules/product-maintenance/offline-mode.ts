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
