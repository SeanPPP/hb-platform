/**
 * 离线态快速重连探测的 React 绑定。
 *
 * enabled 为真（离线 + 页面聚焦 + App 前台）时启动固定间隔探测；任一探测成功
 * 立即回调 onReachable。App 回到前台时额外即时探测一次。
 */
import { useCallback, useEffect, useRef } from "react";
import { AppState } from "react-native";
import { checkBackendReachable } from "@/shared/network/health-check";
import {
  createOfflineReconnectProbe,
  OFFLINE_RECONNECT_PROBE_TIMEOUT_MS,
  type OfflineReconnectProbe,
} from "./offline-reconnect-probe";

export interface UseOfflineReconnectProbeOptions {
  enabled: boolean;
  onReachable: (checkedAtMs: number) => void;
}

export function useOfflineReconnectProbe({ enabled, onReachable }: UseOfflineReconnectProbeOptions) {
  const onReachableRef = useRef(onReachable);
  onReachableRef.current = onReachable;
  const probeRef = useRef<OfflineReconnectProbe | null>(null);

  useEffect(() => {
    if (!enabled) {
      probeRef.current?.stop();
      probeRef.current = null;
      return;
    }
    const probe = createOfflineReconnectProbe({
      checkBackend: async () =>
        (await checkBackendReachable({ timeoutMs: OFFLINE_RECONNECT_PROBE_TIMEOUT_MS })).ok,
      onReachable: (checkedAtMs) => onReachableRef.current(checkedAtMs),
    });
    probeRef.current = probe;
    probe.start();
    const subscription = AppState.addEventListener("change", (next) => {
      if (next === "active") {
        void probe.probeNow();
      }
    });
    return () => {
      subscription.remove();
      probe.stop();
      if (probeRef.current === probe) {
        probeRef.current = null;
      }
    };
  }, [enabled]);

  const probeNow = useCallback(async () => {
    await probeRef.current?.probeNow();
  }, []);

  return { probeNow };
}
