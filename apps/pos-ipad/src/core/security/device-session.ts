import type {
  DeviceRegisterResponse,
  DeviceReregisterResponse,
  DeviceVerifyResponse
} from "../api/hbpos-api";

import {
  DeviceCredentialStore,
  DeviceLockStore,
  InstallationIdentityStore,
  PendingDeviceRegistrationStore,
} from "./secure-storage";

export type DeviceSessionStatus =
  | "unregistered"
  | "registering"
  | "pending-approval"
  | "verifying"
  | "reregistering"
  | "authorized"
  | "denied"
  | "disabled";

export type DeviceSessionState = Readonly<{
  status: DeviceSessionStatus;
  deviceCode?: string;
  storeCode?: string;
  message?: string;
}>;

export interface DeviceSessionApi {
  register(input: Readonly<{ storeCode: string; hardwareId: string; terminalName?: string }>): Promise<DeviceRegisterResponse>;
  verify(input: Readonly<{ deviceCode: string; storeCode: string; hardwareId: string; terminalName?: string }>): Promise<DeviceVerifyResponse>;
  reregister(input: Readonly<{ targetStoreCode: string; hardwareId: string; terminalName?: string }>): Promise<DeviceReregisterResponse>;
}

export class DeviceSessionCoordinator {
  private readonly lockStore: DeviceLockStore;
  private readonly pendingRegistration: PendingDeviceRegistrationStore;
  private state: DeviceSessionState = { status: "unregistered" };

  public constructor(
    private readonly api: DeviceSessionApi,
    private readonly installation: InstallationIdentityStore,
    private readonly credentials: DeviceCredentialStore,
    lockStore?: DeviceLockStore,
    pendingRegistration?: PendingDeviceRegistrationStore,
  ) {
    this.lockStore = lockStore ?? new DeviceLockStore(credentials.secureStore);
    this.pendingRegistration = pendingRegistration ?? new PendingDeviceRegistrationStore(credentials.secureStore);
  }

  public async register(input: Readonly<{ storeCode: string; terminalName?: string }>): Promise<DeviceSessionState> {
    const hardwareId = await this.installation.getOrCreate();
    this.state = { status: "registering", storeCode: input.storeCode };
    return this.updateState(this.resolve(this.api.register({ ...input, hardwareId }), "register"));
  }

  public async verify(input: Readonly<{ deviceCode: string; storeCode: string; terminalName?: string }>): Promise<DeviceSessionState> {
    const hardwareId = await this.installation.getOrCreate();
    this.state = { status: "verifying", deviceCode: input.deviceCode, storeCode: input.storeCode };
    return this.updateState(this.resolve(this.api.verify({ ...input, hardwareId }), "verify"));
  }

  public async poll(): Promise<DeviceSessionState> {
    const current = await this.credentials.load();
    if (current) {
      return this.verify({ deviceCode: current.deviceCode, storeCode: current.storeCode });
    }
    const pending = await this.pendingRegistration.load();
    if (pending) {
      return this.verify(pending);
    }
    return this.updateState(Promise.resolve({ status: "unregistered" }));
  }

  public async reregister(input: Readonly<{ targetStoreCode: string; terminalName?: string }>): Promise<DeviceSessionState> {
    const hardwareId = await this.installation.getOrCreate();
    this.state = { status: "reregistering", storeCode: input.targetStoreCode };
    return this.updateState(this.resolve(this.api.reregister({ ...input, hardwareId }), "reregister"));
  }

  public getState(): DeviceSessionState {
    return this.state;
  }

  public async getRequestHeaders(): Promise<Readonly<Record<string, string>> | null> {
    const credentials = await this.getTransportCredentials();
    if (!credentials) {
      return null;
    }
    return {
      Authorization: `Bearer ${credentials.authorizationCode}`,
      "X-HBPOS-Device-Code": credentials.deviceCode,
      "X-HBPOS-Store-Code": credentials.storeCode,
      "X-HBPOS-Hardware-Id": credentials.hardwareId
    };
  }

  /** UI/feature 可读取的安全设备身份；不会返回授权码或安装硬件标识。 */
  public async getDeviceIdentity(): Promise<Readonly<{
    deviceCode: string;
    storeCode: string;
  }> | null> {
    const credentials = await this.getTransportCredentials();
    return credentials
      ? {
          deviceCode: credentials.deviceCode,
          storeCode: credentials.storeCode,
        }
      : null;
  }

  public async getTransportCredentials(): Promise<Readonly<{
    authorizationCode: string;
    deviceCode: string;
    storeCode: string;
    hardwareId: string;
  }> | null> {
    if (await this.lockStore.isLocked()) {
      return null;
    }
    const credentials = await this.credentials.load();
    const installationId = await this.installation.getOrCreate();
    return credentials && credentials.hardwareId === installationId ? credentials : null;
  }

  /** 认证中间件收到明确的设备 403 时调用；保留设备号以支持后续在线重新验证。 */
  public async lockFromAuthorizationFailure(reason: string): Promise<void> {
    await this.lockStore.lock(reason);
    this.state = { status: "disabled", message: reason };
  }

  private async resolve(
    responsePromise: Promise<DeviceRegisterResponse | DeviceVerifyResponse | DeviceReregisterResponse>,
    operation: "register" | "verify" | "reregister",
  ): Promise<DeviceSessionState> {
    const response = await responsePromise;
    const deviceCode = response.deviceCode?.trim() ?? "";
    const storeCode = response.storeCode?.trim() ?? "";
    if (response.isAllowed && response.authorizationCode && deviceCode && storeCode) {
      const hardwareId = await this.installation.getOrCreate();
      await this.credentials.save({
        deviceCode,
        storeCode,
        hardwareId,
        authorizationCode: response.authorizationCode
      });
      await this.pendingRegistration.clear();
      await this.lockStore.unlock();
      return { status: "authorized", deviceCode, storeCode };
    }

    const existingCredentials = await this.credentials.load();
    const isPending = response.deviceStatus === -1;
    if (isPending && deviceCode && storeCode) {
      await this.pendingRegistration.save({ deviceCode, storeCode });
    }

    // 已授权设备的 verify 收到任何明确的非允许响应（硬件不匹配也可能仍返回启用状态）都必须停用出站凭据。
    const mustLockExistingDevice = operation === "verify" && existingCredentials !== null;
    const disabled = response.deviceStatus === 0 || response.deviceStatus === 2 || mustLockExistingDevice;
    if (disabled) {
      await this.lockFromAuthorizationFailure(response.message ?? "Device is disabled or no longer authorized.");
      return stateWithDetails("disabled", deviceCode, storeCode, response.message);
    }

    return stateWithDetails(
      isPending ? "pending-approval" : "denied",
      deviceCode,
      storeCode,
      response.message
    );
  }

  private async updateState(next: Promise<DeviceSessionState>): Promise<DeviceSessionState> {
    this.state = await next;
    return this.state;
  }
}

function stateWithDetails(
  status: DeviceSessionStatus,
  deviceCode: string,
  storeCode: string,
  message: string | null | undefined
): DeviceSessionState {
  return {
    status,
    ...(deviceCode ? { deviceCode } : {}),
    ...(storeCode ? { storeCode } : {}),
    ...(message ? { message } : {})
  };
}
