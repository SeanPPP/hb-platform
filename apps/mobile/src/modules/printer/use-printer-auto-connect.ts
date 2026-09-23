import { useEffect, useRef } from "react";
import { AppState, type AppStateStatus } from "react-native";
import {
  connectSavedPrinter,
  hydrateSavedPrinter,
  syncPrinterStatus,
} from "@/modules/printer/api";
import { subscribePrinterStatusChanged } from "@/modules/printer/native";
import { usePrinterStore } from "@/modules/printer/state";
import { i18n } from "@/shared/i18n/i18n";

const RECONNECT_INTERVAL_MS = 5000;

export function usePrinterAutoConnect(
  { enabled = true }: { enabled?: boolean } = {}
) {
  const savedPrinter = usePrinterStore((state) => state.savedPrinter);
  const autoReconnectPaused = usePrinterStore((state) => state.autoReconnectPaused);
  const hydrated = usePrinterStore((state) => state.hydrated);
  const status = usePrinterStore((state) => state.status);
  const setStatus = usePrinterStore((state) => state.setStatus);
  const setLastError = usePrinterStore((state) => state.setLastError);

  const savedPrinterAddress = savedPrinter?.address;
  const appStateRef = useRef<AppStateStatus>(AppState.currentState);
  const connectInFlightRef = useRef(false);
  const lastConnectAttemptRef = useRef(0);

  useEffect(() => {
    if (!enabled) {
      return;
    }
    void hydrateSavedPrinter().catch((error: unknown) => {
      setLastError(error instanceof Error ? error.message : i18n.t("common:errors.requestFailed"));
      setStatus("error");
    });
  }, [enabled, setLastError, setStatus]);

  useEffect(() => {
    if (!enabled || !hydrated) return;
    let cancelled = false;
    appStateRef.current = AppState.currentState;

    async function tick() {
      if (cancelled || appStateRef.current !== "active") return;
      try {
        // 原生事件到来时立即同步；连接等待期间也允许刷新状态，避免界面滞后。
        const nativeStatus = await syncPrinterStatus();
        if (cancelled) return;
        const current = usePrinterStore.getState();
        if (current.autoReconnectPaused || !current.savedPrinter) return;
        if (nativeStatus.connected && nativeStatus.address === current.savedPrinter.address) {
          lastConnectAttemptRef.current = 0;
          return;
        }
        if (!nativeStatus.supported || !nativeStatus.enabled || connectInFlightRef.current) return;
        if (Date.now() - lastConnectAttemptRef.current < RECONNECT_INTERVAL_MS) return;

        connectInFlightRef.current = true;
        lastConnectAttemptRef.current = Date.now();
        try {
          await connectSavedPrinter({ status: "reconnecting" });
        } finally {
          connectInFlightRef.current = false;
        }
      } catch (error) {
        if (!cancelled) {
          const current = usePrinterStore.getState();
          setLastError(error instanceof Error ? error.message : i18n.t("common:errors.requestFailed"));
          setStatus(current.autoReconnectPaused ? "paused" : "error");
        }
      }
    }

    const nativeUnsubscribe = subscribePrinterStatusChanged(() => { void tick(); });
    const appSubscription = AppState.addEventListener("change", (nextState) => {
      appStateRef.current = nextState;
      if (nextState === "active") void tick();
    });
    const storeUnsubscribe = usePrinterStore.subscribe((current, previous) => {
      // 旧包写失败由 JS 清理连接，也应立即触发恢复；连接失败留给定时重试，避免热循环。
      if (current.status === "disconnected" && previous.status !== "disconnected") void tick();
    });
    const intervalId = setInterval(() => { void tick(); }, RECONNECT_INTERVAL_MS);
    void tick();

    return () => {
      cancelled = true;
      nativeUnsubscribe();
      appSubscription.remove();
      storeUnsubscribe();
      clearInterval(intervalId);
    };
  }, [autoReconnectPaused, enabled, hydrated, savedPrinterAddress, setLastError, setStatus]);

  return {
    status,
    savedPrinter,
    autoReconnectPaused,
  };
}
