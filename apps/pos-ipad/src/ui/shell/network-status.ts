import type { ConnectivityStatus } from "./pos-shell-store";

type Reachability = Readonly<{
  isConnected?: boolean | null;
  isInternetReachable?: boolean | null;
}>;

export function mapReachabilityToConnectivity(
  state: Reachability,
): ConnectivityStatus {
  if (
    state.isConnected === false ||
    state.isInternetReachable === false
  ) {
    return "offline";
  }

  if (state.isConnected === true) {
    return "online";
  }

  return "checking";
}
