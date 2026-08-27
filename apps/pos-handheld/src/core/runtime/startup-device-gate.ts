import { HbposApiError } from "../api/hbpos-api";
import type { DeviceSessionState } from "../security/device-session";

import type {
  RuntimeBackendState,
  RuntimeDeviceState,
} from "./pos-runtime";

export type StartupDeviceGateResult = Readonly<{
  backend: RuntimeBackendState;
  device: Exclude<RuntimeDeviceState, "unknown">;
}>;

export type StartupDeviceGateOptions = Readonly<{
  internetReachable: boolean;
  registrationResetPending?: boolean;
  readPendingDeviceActivation(): Promise<string | null>;
  verifyCurrentDevice(): Promise<DeviceSessionState>;
  readLocalDevice(): Promise<Exclude<RuntimeDeviceState, "unknown">>;
  lockDevice(reason: string): Promise<void>;
}>;

/**
 * 在线启动必须重新验证设备；只有传输失败或 5xx 才允许回退本地离线状态。
 * 明确的设备 403 会锁机；其他 HTTP、业务 envelope、解析或编程错误必须保持
 * 不可交易，不能伪装成断网后使用旧 Keychain 凭据。
 */
export async function resolveStartupDeviceGate(
  options: StartupDeviceGateOptions,
): Promise<StartupDeviceGateResult> {
  if (options.registrationResetPending === true) {
    return {
      backend: options.internetReachable ? "unverified" : "offline",
      device: "locked",
    };
  }
  const pendingActivation = await readPendingDeviceActivation(options);
  if (!options.internetReachable) {
    if (pendingActivation) {
      throw new DeviceActivationRecoveryRequiredError();
    }
    return {
      backend: "offline",
      device: await options.readLocalDevice(),
    };
  }

  try {
    return fromVerifiedSession(await options.verifyCurrentDevice());
  } catch (error: unknown) {
    if (isExplicitDeviceRejection(error)) {
      await options.lockDevice(error.message);
      return { backend: "rejected", device: "locked" };
    }
    if (isOfflineCompatibleFailure(error)) {
      if (await readPendingDeviceActivation(options)) {
        throw new DeviceActivationRecoveryRequiredError();
      }
      return {
        backend: "offline",
        device: await options.readLocalDevice(),
      };
    }
    throw error;
  }
}

class DeviceActivationRecoveryRequiredError extends Error {
  public constructor() {
    super("Device activation recovery is pending; reconnect to finish recovery.");
    this.name = "DeviceActivationRecoveryRequiredError";
  }
}

async function readPendingDeviceActivation(
  options: StartupDeviceGateOptions,
): Promise<string | null> {
  try {
    return await options.readPendingDeviceActivation();
  } catch {
    // Keychain/JSON 状态无法判定时不得读取旧 Enabled cache 继续交易。
    throw new DeviceActivationRecoveryRequiredError();
  }
}

function fromVerifiedSession(
  state: DeviceSessionState,
): StartupDeviceGateResult {
  switch (state.status) {
    case "authorized":
      return { backend: "reachable", device: "authorized-online" };
    case "pending-approval":
      return { backend: "reachable", device: "pending-approval" };
    case "disabled":
      return { backend: "rejected", device: "locked" };
    case "denied":
      return { backend: "rejected", device: "registration-required" };
    case "unregistered":
      // 没有设备号时 poll 不会发出 API 请求，不能声称后端已验证可达。
      return { backend: "unverified", device: "registration-required" };
    case "registering":
    case "verifying":
    case "reregistering":
      throw new Error(`Device verification returned transient state: ${state.status}`);
  }
}

function isExplicitDeviceRejection(
  error: unknown,
): error is HbposApiError & { status: number } {
  return (
    error instanceof HbposApiError &&
    error.kind === "http" &&
    error.status === 403
  );
}

function isOfflineCompatibleFailure(error: unknown): boolean {
  return (
    error instanceof HbposApiError &&
    (error.kind === "transport" ||
      (error.kind === "http" &&
        error.status !== undefined &&
        error.status >= 500 &&
        error.status < 600))
  );
}
