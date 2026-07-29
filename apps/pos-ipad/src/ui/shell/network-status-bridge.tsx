import * as Network from "expo-network";
import { useEffect } from "react";

import { mapReachabilityToConnectivity } from "./network-status";
import { usePosShellStore } from "./pos-shell-store";

export function NetworkStatusBridge() {
  const setConnectivity = usePosShellStore(
    (state) => state.setConnectivity,
  );

  useEffect(() => {
    let active = true;

    void Network.getNetworkStateAsync()
      .then((state) => {
        if (active) {
          setConnectivity(mapReachabilityToConnectivity(state));
        }
      })
      .catch(() => {
        if (active) {
          setConnectivity("checking");
        }
      });

    const subscription = Network.addNetworkStateListener((state) => {
      setConnectivity(mapReachabilityToConnectivity(state));
    });

    return () => {
      active = false;
      subscription.remove();
    };
  }, [setConnectivity]);

  return null;
}
