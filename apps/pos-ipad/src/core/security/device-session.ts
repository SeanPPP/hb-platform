import type {
  DeviceRegisterResponse,
  DeviceReregisterResponse,
  DeviceVerifyResponse
} from "../api/hbpos-api";

import {
  DeviceCredentialStore,
  DeviceLockStore,
  DevicePresentationStore,
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

export type DevicePresentation = Readonly<{
  deviceCode: string;
  storeCode: string;
  storeName: string | null;
}>;

export interface DeviceSessionApi {
  register(input: Readonly<{ storeCode: string; hardwareId: string; terminalName?: string }>): Promise<DeviceRegisterResponse>;
  verify(input: Readonly<{ deviceCode: string; storeCode: string; hardwareId: string; terminalName?: string }>): Promise<DeviceVerifyResponse>;
  reregister(input: Readonly<{ targetStoreCode: string; hardwareId: string; terminalName?: string }>): Promise<DeviceReregisterResponse>;
}

export class DeviceSessionCoordinator {
  private readonly lockStore: DeviceLockStore;
  private readonly pendingRegistration: PendingDeviceRegistrationStore;
  private readonly presentationStore: DevicePresentationStore;
  // 每次发起会改变设备授权的操作均递增；迟到响应只能返回当前结果，不能回写持久化状态。
  private operationGeneration = 0;
  // 安全存储写入不可取消；同一队列确保已启动旧写入先结束，由新操作在队尾完成最终覆盖。
  private mutationQueue: Promise<void> = Promise.resolve();
  private state: DeviceSessionState = { status: "unregistered" };

  public constructor(
    private readonly api: DeviceSessionApi,
    private readonly installation: InstallationIdentityStore,
    private readonly credentials: DeviceCredentialStore,
    lockStore?: DeviceLockStore,
    pendingRegistration?: PendingDeviceRegistrationStore,
    presentationStore?: DevicePresentationStore,
  ) {
    this.lockStore = lockStore ?? new DeviceLockStore(credentials.secureStore);
    this.pendingRegistration = pendingRegistration ?? new PendingDeviceRegistrationStore(credentials.secureStore);
    this.presentationStore =
      presentationStore ??
      new DevicePresentationStore(credentials.secureStore);
  }

  public async register(input: Readonly<{ storeCode: string; terminalName?: string }>): Promise<DeviceSessionState> {
    const generation = this.beginOperation();
    this.state = { status: "registering", storeCode: input.storeCode };
    const hardwareId = await this.installation.getOrCreate();
    if (!this.isCurrentOperation(generation)) {
      return this.state;
    }
    return this.updateState(this.resolve(this.api.register({ ...input, hardwareId }), "register", generation), generation);
  }

  public async verify(input: Readonly<{ deviceCode: string; storeCode: string; terminalName?: string }>): Promise<DeviceSessionState> {
    const generation = this.beginOperation();
    this.state = { status: "verifying", deviceCode: input.deviceCode, storeCode: input.storeCode };
    const hardwareId = await this.installation.getOrCreate();
    if (!this.isCurrentOperation(generation)) {
      return this.state;
    }
    return this.updateState(this.resolve(this.api.verify({ ...input, hardwareId }), "verify", generation), generation);
  }

  public async poll(): Promise<DeviceSessionState> {
    const generation = this.beginOperation();
    const current = await this.credentials.load();
    if (!this.isCurrentOperation(generation)) {
      return this.state;
    }
    if (current) {
      return this.verify({ deviceCode: current.deviceCode, storeCode: current.storeCode });
    }
    const pending = await this.pendingRegistration.load();
    if (!this.isCurrentOperation(generation)) {
      return this.state;
    }
    if (pending) {
      return this.verify(pending);
    }
    return this.updateState(Promise.resolve({ status: "unregistered" }), generation);
  }

  public async reregister(input: Readonly<{ targetStoreCode: string; terminalName?: string }>): Promise<DeviceSessionState> {
    const generation = this.beginOperation();
    this.state = { status: "reregistering", storeCode: input.targetStoreCode };
    const hardwareId = await this.installation.getOrCreate();
    if (!this.isCurrentOperation(generation)) {
      return this.state;
    }
    return this.updateState(this.resolve(this.api.reregister({ ...input, hardwareId }), "reregister", generation), generation);
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

  public async getDevicePresentation(): Promise<DevicePresentation | null> {
    try {
      const credentials = await this.getTransportCredentials();
      if (!credentials) {
        return null;
      }
      const cached = await this.presentationStore.load();
      const matchesCredentials =
        cached?.deviceCode === credentials.deviceCode &&
        cached.storeCode === credentials.storeCode;
      return {
        deviceCode: credentials.deviceCode,
        storeCode: credentials.storeCode,
        storeName: matchesCredentials ? cached.storeName : null,
      };
    } catch {
      // 损坏凭据或安全存储读取失败时，公开展示身份同样失败关闭。
      return null;
    }
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
    const generation = this.beginOperation();
    await this.mutateCurrentOperation(generation, async () => {
      await this.lockStore.lock(reason);
      if (this.isCurrentOperation(generation)) {
        this.state = { status: "disabled", message: reason };
      }
    });
  }

  private async resolve(
    responsePromise: Promise<DeviceRegisterResponse | DeviceVerifyResponse | DeviceReregisterResponse>,
    operation: "register" | "verify" | "reregister",
    generation: number,
  ): Promise<DeviceSessionState> {
    const response = await responsePromise;
    if (!this.isCurrentOperation(generation)) {
      return this.state;
    }
    return (await this.mutateCurrentOperation(generation, async () => {
      const deviceCode = response.deviceCode?.trim() ?? "";
      const storeCode = response.storeCode?.trim() ?? "";
      if (response.isAllowed && response.authorizationCode && deviceCode && storeCode) {
        const hardwareId = await this.installation.getOrCreate();
        if (!this.isCurrentOperation(generation)) {
          return this.state;
        }
        await this.credentials.save({
          deviceCode,
          storeCode,
          hardwareId,
          authorizationCode: response.authorizationCode
        });
        if (!this.isCurrentOperation(generation)) {
          return this.state;
        }
        await this.updatePresentationBestEffort(
          deviceCode,
          storeCode,
          response.storeName,
          generation,
        );
        if (!this.isCurrentOperation(generation)) {
          return this.state;
        }
        await this.pendingRegistration.clear();
        if (!this.isCurrentOperation(generation)) {
          return this.state;
        }
        await this.lockStore.unlock();
        return { status: "authorized", deviceCode, storeCode };
      }

      const existingCredentials = await this.credentials.load();
      if (!this.isCurrentOperation(generation)) {
        return this.state;
      }
      const isPending = response.deviceStatus === -1;
      if (isPending && deviceCode && storeCode) {
        await this.pendingRegistration.save({ deviceCode, storeCode });
        if (!this.isCurrentOperation(generation)) {
          return this.state;
        }
      }

      // 已授权设备的 verify 收到任何明确的非允许响应（硬件不匹配也可能仍返回启用状态）都必须停用出站凭据。
      const mustLockExistingDevice = operation === "verify" && existingCredentials !== null;
      const disabled = response.deviceStatus === 0 || response.deviceStatus === 2 || mustLockExistingDevice;
      if (disabled) {
        await this.lockStore.lock(response.message ?? "Device is disabled or no longer authorized.");
        if (!this.isCurrentOperation(generation)) {
          return this.state;
        }
        return stateWithDetails("disabled", deviceCode, storeCode, response.message);
      }

      return stateWithDetails(
        isPending ? "pending-approval" : "denied",
        deviceCode,
        storeCode,
        response.message
      );
    })) ?? this.state;
  }

  private async updatePresentationBestEffort(
    deviceCode: string,
    storeCode: string,
    storeName: string | null | undefined,
    generation: number,
  ): Promise<void> {
    try {
      if (!this.isCurrentOperation(generation)) {
        return;
      }
      const normalizedStoreName = storeName?.trim() ?? "";
      if (normalizedStoreName) {
        await this.presentationStore.save({
          deviceCode,
          storeCode,
          storeName: normalizedStoreName,
        });
        return;
      }

      const current = await this.presentationStore.load();
      if (!this.isCurrentOperation(generation)) {
        return;
      }
      if (
        current &&
        (current.deviceCode !== deviceCode ||
          current.storeCode !== storeCode)
      ) {
        await this.presentationStore.clear();
      }
    } catch {
      // 展示缓存永远不能改变凭据已保存后的授权结果。
    }
  }

  private beginOperation(): number {
    this.operationGeneration += 1;
    return this.operationGeneration;
  }

  private isCurrentOperation(generation: number): boolean {
    return generation === this.operationGeneration;
  }

  private async mutateCurrentOperation<T>(
    generation: number,
    mutation: () => Promise<T>,
  ): Promise<T | undefined> {
    const previous = this.mutationQueue;
    let release!: () => void;
    this.mutationQueue = new Promise<void>((resolve) => {
      release = resolve;
    });

    await previous;
    try {
      return this.isCurrentOperation(generation)
        ? await mutation()
        : undefined;
    } finally {
      release();
    }
  }

  private async updateState(next: Promise<DeviceSessionState>, generation: number): Promise<DeviceSessionState> {
    const resolved = await next;
    if (this.isCurrentOperation(generation)) {
      this.state = resolved;
    }
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
