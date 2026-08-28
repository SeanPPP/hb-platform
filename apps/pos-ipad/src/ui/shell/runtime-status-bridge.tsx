import { useEffect } from "react";
import { AppState } from "react-native";

import { usePosShellStore, type DeviceGateStatus } from "./pos-shell-store";

import { usePosRuntime } from "@/core/runtime/pos-runtime-context";

export function RuntimeStatusBridge() {
  const { services, state } = usePosRuntime();
  const deviceSession = services?.deviceSession;
  const sync = services?.sync;
  const runtimeReady =
    state.phase === "ready" || state.phase === "ready-offline";
  const setDeviceGate = usePosShellStore((current) => current.setDeviceGate);
  const setPendingSync = usePosShellStore((current) => current.setPendingSync);
  const setTerminalPresentation = usePosShellStore(
    (current) => current.setTerminalPresentation,
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
    let cancelled = false;
    let readRevision = 0;

    if (!runtimeReady) {
      setPendingSync({ kind: "checking" });
      return () => {
        cancelled = true;
      };
    }
    if (!sync) {
      setPendingSync({ kind: "unavailable" });
      return () => {
        cancelled = true;
      };
    }

    const refreshPendingSync = (showChecking: boolean): void => {
      const revision = ++readRevision;
      if (showChecking) setPendingSync({ kind: "checking" });
      void sync
        .readPendingOrderSyncCount()
        .then((count) => {
          if (cancelled || revision !== readRevision) return;
          setPendingSync({ kind: "ready", count });
        })
        .catch(() => {
          if (cancelled || revision !== readRevision) return;
          setPendingSync({ kind: "unavailable" });
        });
    };

    refreshPendingSync(true);
    // 启动或服务身份变化才显示检查中；生命周期刷新保留上次真实值，
    // 避免前台恢复或网络抖动把状态短暂伪装成 0。
    const unsubscribeDrainSettled = sync.subscribeDrainSettled(() => {
      refreshPendingSync(false);
    });
    const appStateSubscription = AppState.addEventListener(
      "change",
      (nextState) => {
        if (nextState === "active") refreshPendingSync(false);
      },
    );
    const unsubscribeConnectivity = usePosShellStore.subscribe(
      (current, previous) => {
        if (
          previous.connectivity === "offline" &&
          current.connectivity === "online"
        ) {
          refreshPendingSync(false);
        }
      },
    );

    return () => {
      cancelled = true;
      readRevision += 1;
      unsubscribeDrainSettled();
      unsubscribeConnectivity();
      appStateSubscription.remove();
    };
  }, [runtimeReady, setPendingSync, sync]);

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
