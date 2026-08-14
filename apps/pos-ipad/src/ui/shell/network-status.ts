import type { ConnectivityStatus } from "./pos-shell-store";

type Reachability = Readonly<{
  isConnected?: boolean | null;
  isInternetReachable?: boolean | null;
}>;

export function mapReachabilityToConnectivity(
  state: Reachability,
): ConnectivityStatus {
  if (state.isConnected === false) {
    return "offline";
  }

  if (state.isConnected === true) {
    // POS 后端可能位于可达的局域网；无公网不等于后端离线，交给 health 探测判定。
    return "online";
  }

  if (state.isInternetReachable === false) {
    return "offline";
  }

  return "checking";
}

/**
 * 后端感知的连通性判定：在设备网络状态之上叠加后端 health 探测结果。
 *
 * 场景：设备 Wi-Fi 正常但后端服务已停止时，仅靠 expo-network 会误报“在线”；
 * 这里把“后端可达”纳入判定，保证收银页状态与真实可用性一致。
 * 规则：
 * - 设备层非在线（offline/checking）→ 沿用设备判定；
 * - 设备在线且后端探测失败（false）→ 离线（仅现金可用）；
 * - 设备在线且后端可达或尚未探测（null）→ 在线（未探测保持乐观，避免启动闪烁）。
 */
export function resolveBackendAwareConnectivity(
  deviceStatus: ConnectivityStatus,
  backendReachable: boolean | null,
): ConnectivityStatus {
  if (deviceStatus !== "online") {
    return deviceStatus;
  }
  return backendReachable === false ? "offline" : "online";
}
