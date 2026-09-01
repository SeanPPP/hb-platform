import type { MobileDeviceActivationMode } from "./types";

export function resolveActivationHardwareId(
  mode: MobileDeviceActivationMode,
  installationHardwareId: string,
  currentBinding: { hardwareId: string } | null,
) {
  if (mode === "redeem") {
    return installationHardwareId;
  }
  if (!currentBinding) {
    throw new Error("DEVICE_ACCOUNT_REBIND_REQUIRES_CURRENT_BINDING");
  }
  return currentBinding.hardwareId;
}
