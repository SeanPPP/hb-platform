import { useEffect } from "react";

import { usePosShellStore, type DeviceGateStatus } from "./pos-shell-store";

import { usePosRuntime } from "@/core/runtime/pos-runtime-context";

export function RuntimeStatusBridge() {
  const { state } = usePosRuntime();
  const setDeviceGate = usePosShellStore((current) => current.setDeviceGate);

  useEffect(() => {
    const next = toDeviceGateStatus(state);
    if (next) {
      setDeviceGate(next);
    }
  }, [setDeviceGate, state]);

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
