/**
 * 目录人工刷新的最小端口：调用方负责把现有快照服务适配到这里，
 * presenter 不知道 HTTP、凭据、地址或任何持久化实现。
 */
export interface CatalogMaintenancePort {
  downloadAndActivate(input: Readonly<{ storeCode: string }>): Promise<
    Readonly<{
      snapshotId: string;
      itemCount: number;
    }>
  >;
}

export type CatalogMaintenanceState =
  | Readonly<{ kind: "idle" }>
  | Readonly<{ kind: "downloading" }>
  | Readonly<{ kind: "success"; snapshotId: string; itemCount: number }>
  | Readonly<{ kind: "failed"; errorCode: CatalogMaintenanceErrorCode }>;

/** 屏幕仅暴露稳定的业务错误码，绝不把异常、HTTP 响应或凭据回显给收银员。 */
export type CatalogMaintenanceErrorCode = "catalog-refresh-failed";

export type CatalogMaintenancePresenterOptions = Readonly<{
  port: CatalogMaintenancePort;
  /** 仅接受已认证设备会话解析出的门店；界面不提供可编辑的门店或 API 设置。 */
  authenticatedStoreCode: string;
}>;

/**
 * 路由无关的目录人工刷新呈现器。
 * 同一轮下载只允许一个在途 Promise，防止连点造成并发 staging/activate。
 */
export class CatalogMaintenancePresenter {
  public state: CatalogMaintenanceState = { kind: "idle" };

  private readonly listeners = new Set<() => void>();
  private refreshInFlight: Promise<void> | null = null;
  private destroyed = false;

  public constructor(private readonly options: CatalogMaintenancePresenterOptions) {}

  public readonly getState = (): CatalogMaintenanceState => this.state;

  public readonly subscribe = (listener: () => void): (() => void) => {
    if (this.destroyed) return () => undefined;
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  public refresh(): Promise<void> {
    if (this.destroyed) return Promise.resolve();
    if (this.refreshInFlight) return this.refreshInFlight;

    this.publish({ kind: "downloading" });
    const refresh = this.downloadAndActivate().finally(() => {
      if (this.refreshInFlight === refresh) this.refreshInFlight = null;
    });
    this.refreshInFlight = refresh;
    return refresh;
  }

  public destroy(): void {
    if (this.destroyed) return;
    this.destroyed = true;
    this.listeners.clear();
  }

  private async downloadAndActivate(): Promise<void> {
    try {
      const result = await this.options.port.downloadAndActivate({
        storeCode: this.options.authenticatedStoreCode,
      });
      if (this.destroyed) return;
      this.publish({
        kind: "success",
        snapshotId: result.snapshotId,
        itemCount: result.itemCount,
      });
    } catch {
      if (this.destroyed) return;
      // 中文注释：底层错误可能含 HTTP URL、响应正文或凭据；统一收敛为稳定安全码。
      this.publish({ kind: "failed", errorCode: "catalog-refresh-failed" });
    }
  }

  private publish(state: CatalogMaintenanceState): void {
    this.state = state;
    for (const listener of this.listeners) listener();
  }
}
