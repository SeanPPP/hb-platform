import type {
  RuntimeBackendState,
  RuntimeDeviceState,
} from "@/core/runtime/pos-runtime";
import type { DeviceSessionState } from "@/core/security/device-session";

export type DeviceRegistrationRuntimeState = Readonly<{
  backend: RuntimeBackendState;
  device: Exclude<RuntimeDeviceState, "unknown">;
}>;

export type DeviceRegistrationRuntimeController = Readonly<{
  updateOperationalState(input: DeviceRegistrationRuntimeState): void;
  retry(): Promise<void>;
}>;

/**
 * 审批成功会把新的设备凭据写入 Keychain。此时必须安全重建组合根，才能让
 * 审计和支持导出读取新门店/设备身份；其余状态只需原地更新运行门禁。
 */
export async function reconcileDeviceSessionRuntime(
  state: DeviceSessionState,
  runtime: DeviceRegistrationRuntimeController,
): Promise<void> {
  if (state.status === "authorized") {
    await runtime.retry();
    return;
  }
  runtime.updateOperationalState(mapDeviceSessionToRuntime(state));
}

export function mapDeviceSessionToRuntime(
  state: DeviceSessionState,
): DeviceRegistrationRuntimeState {
  switch (state.status) {
    case "authorized":
      return { backend: "reachable", device: "authorized-online" };
    case "pending-approval":
    case "registering":
    case "verifying":
    case "reregistering":
      return { backend: "reachable", device: "pending-approval" };
    case "disabled":
      return { backend: "rejected", device: "locked" };
    case "unregistered":
      return { backend: "unverified", device: "registration-required" };
    case "denied":
      return { backend: "rejected", device: "registration-required" };
  }
}
