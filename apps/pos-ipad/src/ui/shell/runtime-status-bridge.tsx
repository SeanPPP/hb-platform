import { useEffect } from "react";

import { usePosShellStore, type DeviceGateStatus } from "./pos-shell-store";

import { usePosRuntime } from "@/core/runtime/pos-runtime-context";

export function RuntimeStatusBridge() {
  const { services, state } = usePosRuntime();
  const deviceSession = services?.deviceSession;
  const setDeviceGate = usePosShellStore((current) => current.setDeviceGate);
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
