import { useEffect, useRef } from "react";
import { AppState, type AppStateStatus } from "react-native";
import {
  connectSavedPrinter,
  hydrateSavedPrinter,
  syncPrinterStatus,
} from "@/modules/printer/api";
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

  const appStateRef = useRef<AppStateStatus>(AppState.currentState);
  const connectInFlightRef = useRef(false);

  useEffect(() => {
    if (!enabled) {
      return;
    }
    void hydrateSavedPrinter();
  }, [enabled]);

  useEffect(() => {
    if (
      !enabled ||
      !hydrated ||
      !savedPrinter ||
      autoReconnectPaused ||
      status === "connected" ||
      status === "connecting" ||
      status === "reconnecting"
    ) {
      return;
    }

    let cancelled = false;

    async function connectOnce(initial: boolean) {
      if (connectInFlightRef.current || cancelled) {
        return;
      }

      connectInFlightRef.current = true;
      setStatus(initial ? "connecting" : "reconnecting");
      setLastError(null);

      try {
        await connectSavedPrinter({ status: initial ? "connecting" : "reconnecting" });
      } catch (error) {
        if (!cancelled) {
          setLastError(
            error instanceof Error
              ? error.message
              : i18n.t("common:errors.requestFailed")
          );
          setStatus("error");
        }
      } finally {
        connectInFlightRef.current = false;
      }
    }

    void connectOnce(true);

    return () => {
      cancelled = true;
    };
  }, [
    autoReconnectPaused,
    enabled,
    hydrated,
    savedPrinter,
    setLastError,
    setStatus,
    status,
  ]);

  useEffect(() => {
    if (!enabled || !hydrated || !savedPrinter) {
      return;
    }

    let intervalId: ReturnType<typeof setInterval> | null = null;
    let cancelled = false;

    async function tick() {
      if (cancelled || appStateRef.current !== "active") {
        return;
      }

      let nativeStatus;
      try {
        nativeStatus = await syncPrinterStatus();
      } catch (error) {
        if (!cancelled) {
          setLastError(
            error instanceof Error
              ? error.message
              : i18n.t("common:errors.requestFailed")
          );
          setStatus("error");
        }
        return;
      }

      const current = usePrinterStore.getState();

      if (current.autoReconnectPaused || !current.savedPrinter) {
        return;
      }

      if (nativeStatus.connected && nativeStatus.address === current.savedPrinter.address) {
        return;
      }

      if (connectInFlightRef.current) {
        return;
      }

      connectInFlightRef.current = true;
      try {
        await connectSavedPrinter({ status: "reconnecting" });
      } catch (error) {
        if (!cancelled) {
          setLastError(
            error instanceof Error
              ? error.message
              : i18n.t("common:errors.requestFailed")
          );
          setStatus("error");
        }
      } finally {
        connectInFlightRef.current = false;
      }
    }

    const subscription = AppState.addEventListener("change", (nextState) => {
      appStateRef.current = nextState;
      if (nextState === "active") {
        void tick();
      }
    });

    intervalId = setInterval(() => {
      void tick();
    }, RECONNECT_INTERVAL_MS);

    return () => {
      cancelled = true;
      subscription.remove();
      if (intervalId) {
        clearInterval(intervalId);
      }
    };
  }, [enabled, hydrated, savedPrinter, setLastError, setStatus]);

  return {
    status,
    savedPrinter,
    autoReconnectPaused,
  };
}
