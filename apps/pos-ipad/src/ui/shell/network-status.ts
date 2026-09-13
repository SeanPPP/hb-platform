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
 * - 后端实测可达（true）→ 在线，即使系统网络状态暂时误报离线；
 * - 后端实测不可达（false）→ 离线；
 * - 尚未探测（null）→ 不乐观宣称在线，沿用明确离线或显示检查中。
 */
export function resolveBackendAwareConnectivity(
  deviceStatus: ConnectivityStatus,
  backendReachable: boolean | null,
): ConnectivityStatus {
  if (backendReachable === true) return "online";
  if (backendReachable === false) return "offline";
  return deviceStatus === "offline" ? "offline" : "checking";
}
