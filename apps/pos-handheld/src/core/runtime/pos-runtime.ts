export type RuntimeBackendState =
  | "unverified"
  | "reachable"
  | "offline"
  | "rejected";

export type RuntimeDeviceState =
  | "unknown"
  | "registration-required"
  | "pending-approval"
  | "authorized-local"
  | "authorized-online"
  | "locked";

export type PosRuntimeState = Readonly<{
  phase:
    | "idle"
    | "starting"
    | "ready"
    | "ready-offline"
    | "registration-required"
    | "pending-approval"
    | "locked"
    | "failed";
  database: "closed" | "opening" | "ready" | "failed";
  backend: RuntimeBackendState;
  device: RuntimeDeviceState;
  error?: string;
}>;

export type PosRuntimeServices = Readonly<{
  /**
   * 仅由 PosRuntimeController 在 stop 时调用。公开运行时服务不会泄露 SQLCipher
   * 连接；route 只能使用受限的业务 facade。
   */
  shutdown(): Promise<void>;
  backend: RuntimeBackendState;
  device: Exclude<RuntimeDeviceState, "unknown">;
}>;

export type PosRuntimeFactory<
  TServices extends PosRuntimeServices = PosRuntimeServices,
> = () => Promise<TServices>;
export type PosRuntimeListener = (state: PosRuntimeState) => void;

const INITIAL_STATE: PosRuntimeState = {
  phase: "idle",
  database: "closed",
  backend: "unverified",
  device: "unknown",
};

/**
 * 生产组合根的单飞启动闸门。
 *
 * UI 只能读取这里的真实初始化状态；数据库未成功打开前，任何页面都不得把
 * “离线现金可用”显示为已就绪。
 */
export class PosRuntimeController<
  TServices extends PosRuntimeServices = PosRuntimeServices,
> {
  private state: PosRuntimeState = INITIAL_STATE;
  private services: TServices | undefined;
  private startPromise: Promise<void> | undefined;
  private stopPromise: Promise<void> | undefined;
  private readonly listeners = new Set<PosRuntimeListener>();

  public constructor(private readonly factory: PosRuntimeFactory<TServices>) {}

  public getState(): PosRuntimeState {
    return this.state;
  }

  public getServices(): TServices | null {
    return this.services ?? null;
  }

  /**
   * 设备审批、在线验证或网络降级后的运行态更新不重开数据库。
   * services 与 UI state 同步替换，避免注册页成功后仍持有旧的本地状态。
   */
  public updateOperationalState(input: Readonly<{
    backend: RuntimeBackendState;
    device: Exclude<RuntimeDeviceState, "unknown">;
  }>): void {
    if (!this.services || this.state.database !== "ready") {
      throw new Error("POS runtime is not ready for an operational state update.");
    }
    this.services = {
      ...this.services,
      ...input,
    };
    this.setState(toReadyState(this.services));
  }

  public subscribe(listener: PosRuntimeListener): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  public start(): Promise<void> {
    if (this.services) {
      return Promise.resolve();
    }
    if (this.startPromise) {
      return this.startPromise;
    }

    this.setState({
      phase: "starting",
      database: "opening",
      backend: "unverified",
      device: "unknown",
    });

    const startPromise = this.factory()
      .then((services) => {
        this.services = services;
        this.setState(toReadyState(services));
      })
      .catch((error: unknown) => {
        this.setState({
          phase: "failed",
          database: "failed",
          backend: "unverified",
          device: "unknown",
          error: errorMessage(error),
        });
        throw error;
      })
      .finally(() => {
        if (this.startPromise === startPromise) {
          this.startPromise = undefined;
        }
      });

    this.startPromise = startPromise;
    return startPromise;
  }

  public stop(): Promise<void> {
    if (this.stopPromise) {
      return this.stopPromise;
    }

    const stopPromise = (async () => {
      if (this.startPromise) {
        try {
          await this.startPromise;
        } catch {
          // 初始化失败后仍需把控制器恢复为可安全退出的 closed 状态。
        }
      }

      const services = this.services;
      this.services = undefined;
      if (services) {
        await services.shutdown();
      }
      this.setState(INITIAL_STATE);
    })().finally(() => {
      if (this.stopPromise === stopPromise) {
        this.stopPromise = undefined;
      }
    });

    this.stopPromise = stopPromise;
    return stopPromise;
  }

  private setState(state: PosRuntimeState): void {
    this.state = state;
    for (const listener of this.listeners) {
      listener(state);
    }
  }
}

function toReadyState(services: PosRuntimeServices): PosRuntimeState {
  let phase: PosRuntimeState["phase"];
  switch (services.device) {
    case "locked":
      phase = "locked";
      break;
    case "pending-approval":
      phase = "pending-approval";
      break;
    case "registration-required":
      phase = "registration-required";
      break;
    case "authorized-local":
    case "authorized-online":
      phase = services.backend === "offline" ? "ready-offline" : "ready";
      break;
  }

  return {
    phase,
    database: "ready",
    backend: services.backend,
    device: services.device,
  };
}

function errorMessage(error: unknown): string {
  return error instanceof Error && error.message
    ? error.message
    : "POS runtime initialization failed.";
}
