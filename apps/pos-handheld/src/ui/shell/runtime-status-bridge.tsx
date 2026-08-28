import { useCallback, useEffect, useRef } from "react";
import { AppState } from "react-native";

import { usePosShellStore, type DeviceGateStatus } from "./pos-shell-store";

import { usePosRuntime } from "@/core/runtime/pos-runtime-context";

export function RuntimeStatusBridge() {
  const { services, state } = usePosRuntime();
  const deviceSession = services?.deviceSession;
  const sync = services?.sync;
  const connectivity = usePosShellStore((current) => current.connectivity);
  const setDeviceGate = usePosShellStore((current) => current.setDeviceGate);
  const setPendingSync = usePosShellStore(
    (current) => current.setPendingSync,
  );
  const setTerminalPresentation = usePosShellStore(
    (current) => current.setTerminalPresentation,
  );
  const pendingSyncReadGeneration = useRef(0);
  const previousConnectivity = useRef(connectivity);
  const pendingSyncReadable =
    state.phase === "ready" || state.phase === "ready-offline";
  const refreshPendingSync = useCallback(
    (mode: "checking" | "background") => {
      const generation = ++pendingSyncReadGeneration.current;
      if (!pendingSyncReadable) {
        setPendingSync({ kind: "checking" });
        return;
      }
      if (
        !sync ||
        typeof sync.readPendingOrderSyncCount !== "function" ||
        typeof sync.subscribeDrainSettled !== "function"
      ) {
        setPendingSync({ kind: "unavailable" });
        return;
      }

      if (mode === "checking") setPendingSync({ kind: "checking" });
      void sync
        .readPendingOrderSyncCount()
        .then((count) => {
          if (generation !== pendingSyncReadGeneration.current) return;
          setPendingSync({ kind: "ready", count });
        })
        .catch(() => {
          if (generation === pendingSyncReadGeneration.current) {
            setPendingSync({ kind: "unavailable" });
          }
        });
    },
    [pendingSyncReadable, setPendingSync, sync],
  );

  useEffect(() => {
    const next = toDeviceGateStatus(state);
    if (next) {
      setDeviceGate(next);
    }
  }, [setDeviceGate, state]);

  useEffect(() => {
    let cancelled = false;
    const presentationAvailable =
      state.phase === "ready" || state.phase === "ready-offline";

    if (!presentationAvailable || !deviceSession) {
      setTerminalPresentation(null);
      return () => {
        cancelled = true;
      };
    }

    setTerminalPresentation(null);
    void deviceSession
      .getDevicePresentation()
      .then((presentation) => {
        if (cancelled) return;
        setTerminalPresentation(
          presentation
            ? {
                storeName: presentation.storeName,
                deviceCode: presentation.deviceCode,
              }
            : null,
        );
      })
      .catch(() => {
        if (!cancelled) {
          setTerminalPresentation(null);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [deviceSession, setTerminalPresentation, state.phase]);

  useEffect(() => {
    refreshPendingSync("checking");
    if (
      !pendingSyncReadable ||
      !sync ||
      typeof sync.subscribeDrainSettled !== "function"
    ) {
      return () => {
        pendingSyncReadGeneration.current += 1;
      };
    }

    let unsubscribeDrain: () => void = () => undefined;
    try {
      unsubscribeDrain = sync.subscribeDrainSettled(() => {
        refreshPendingSync("background");
      });
    } catch {
      setPendingSync({ kind: "unavailable" });
    }
    const appStateSubscription = AppState.addEventListener(
      "change",
      (nextState) => {
        if (nextState === "active") refreshPendingSync("background");
      },
    );
    return () => {
      pendingSyncReadGeneration.current += 1;
      unsubscribeDrain();
      appStateSubscription.remove();
    };
  }, [pendingSyncReadable, refreshPendingSync, setPendingSync, sync]);

  useEffect(() => {
    const previous = previousConnectivity.current;
    previousConnectivity.current = connectivity;
    if (previous === "offline" && connectivity === "online") {
      refreshPendingSync("background");
    }
  }, [connectivity, refreshPendingSync]);

  return null;
}

function toDeviceGateStatus(
  state: ReturnType<typeof usePosRuntime>["state"],
): DeviceGateStatus | null {
  switch (state.phase) {
    case "registration-required":
      return "unregistered";
    case "pending-approval":
      return "pending-approval";
    case "locked":
      return "locked";
    case "ready":
    case "ready-offline":
      return "authorized";
    case "idle":
    case "starting":
    case "failed":
      return null;
  }
}
