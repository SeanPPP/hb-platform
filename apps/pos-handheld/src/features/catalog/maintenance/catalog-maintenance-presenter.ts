import type {
  CatalogRefreshOutcome,
  CatalogRefreshProgressEvent,
  CatalogSummary,
} from "@hb/pos-domain/features/catalog/catalog-refresh-contract";
import {
  CatalogRefreshCoordinator,
  type CatalogRefreshErrorCode,
  type CatalogRefreshState,
} from "../catalog-refresh-coordinator";

export type {
  CatalogRefreshProgress,
  CatalogRefreshStepProgress,
  CatalogRefreshWarningCode,
} from "../catalog-refresh-coordinator";

export type CatalogMaintenanceErrorCode =
  | "catalog-metadata-unavailable"
  | CatalogRefreshErrorCode;

export type CatalogMaintenanceState = Readonly<{
  catalog:
    | Readonly<{ kind: "loading"; summary: CatalogSummary | null }>
    | Readonly<{ kind: "ready"; summary: CatalogSummary | null }>
    | Readonly<{
        kind: "failed";
        summary: CatalogSummary | null;
        errorCode: "catalog-metadata-unavailable";
      }>;
  refresh: CatalogRefreshState;
}>;

/**
 * 目录人工刷新的最小端口：presenter 不知道 HTTP、凭据、地址或任何持久化实现。
 * 门店始终来自已认证设备会话，屏幕也无法覆盖它。
 */
export interface CatalogMaintenancePort {
  getCurrentCatalog(
    input: Readonly<{
      storeCode: string;
      signal?: AbortSignal;
    }>,
  ): Promise<CatalogSummary | null>;
  downloadAndActivate(
    input: Readonly<{
      storeCode: string;
      onProgress?(event: CatalogRefreshProgressEvent): void;
      signal?: AbortSignal;
    }>,
  ): Promise<CatalogRefreshOutcome>;
}

export type CatalogMaintenancePresenterOptions = Readonly<{
  port: CatalogMaintenancePort;
  authenticatedStoreCode: string;
  /** 生产 runtime 传入共享实例；缺省值只用于独立 presenter 测试与预览。 */
  coordinator?: CatalogRefreshCoordinator;
}>;

/**
 * route 级薄代理：本地目录摘要读取跟随页面生命周期，刷新任务与进度则完全由
 * runtime coordinator 持有。销毁页面只退订并取消自身 metadata 读取。
 */
export class CatalogMaintenancePresenter {
  public state: CatalogMaintenanceState;

  private readonly listeners = new Set<() => void>();
  private readonly metadataAbortController = new AbortController();
  private readonly coordinator: CatalogRefreshCoordinator;
  private readonly unsubscribeCoordinator: () => void;
  private initializeInFlight: Promise<void> | null = null;
  private refreshInFlight: Promise<void> | null = null;
  private destroyed = false;

  public constructor(
    private readonly options: CatalogMaintenancePresenterOptions,
  ) {
    this.coordinator =
      options.coordinator ?? new CatalogRefreshCoordinator();
    const refresh = this.coordinator.getState();
    this.state = {
      catalog: catalogStateFromRefresh(refresh),
      refresh,
    };
    this.unsubscribeCoordinator = this.coordinator.subscribe(() => {
      this.applySharedRefreshState();
    });
  }

  public readonly getState = (): CatalogMaintenanceState => this.state;

  public readonly subscribe = (listener: () => void): (() => void) => {
    if (this.destroyed) return () => undefined;
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  public initialize(): Promise<void> {
    if (this.destroyed) return Promise.resolve();
    if (this.initializeInFlight) return this.initializeInFlight;

    this.publish({
      ...this.state,
      catalog: {
        kind: "loading",
        summary: this.state.catalog.summary,
      },
    });
    const initialize = this.loadCurrentCatalog().finally(() => {
      if (this.initializeInFlight === initialize) {
        this.initializeInFlight = null;
      }
    });
    this.initializeInFlight = initialize;
    return initialize;
  }

  public refresh(): Promise<void> {
    if (this.destroyed) return Promise.resolve();
    if (this.refreshInFlight) return this.refreshInFlight;

    const refresh = this.coordinator
      .start({
        storeCode: this.options.authenticatedStoreCode,
        execute: ({ signal, onProgress }) =>
          this.options.port.downloadAndActivate({
            storeCode: this.options.authenticatedStoreCode,
            signal,
            onProgress,
          }),
      })
      .then(
        () => undefined,
        async () => {
          if (
            !this.destroyed &&
            this.coordinator.getState().kind === "failed"
          ) {
            await this.loadCurrentCatalog();
          }
        },
      )
      .finally(() => {
        if (this.refreshInFlight === refresh) {
          this.refreshInFlight = null;
        }
      });
    this.refreshInFlight = refresh;
    return refresh;
  }

  public destroy(): void {
    if (this.destroyed) return;
    this.destroyed = true;
    this.metadataAbortController.abort();
    this.unsubscribeCoordinator();
    this.listeners.clear();
  }

  private applySharedRefreshState(): void {
    if (this.destroyed) return;
    const refresh = this.coordinator.getState();
    const catalog =
      refresh.kind === "success" || refresh.kind === "warning"
        ? { kind: "ready" as const, summary: refresh.summary }
        : this.state.catalog;
    this.publish({ catalog, refresh });
  }

  private async loadCurrentCatalog(): Promise<void> {
    try {
      const summary = await this.options.port.getCurrentCatalog({
        storeCode: this.options.authenticatedStoreCode,
        signal: this.metadataAbortController.signal,
      });
      if (this.destroyed) return;
      const refresh = this.coordinator.getState();
      const visibleSummary =
        refresh.kind === "success" || refresh.kind === "warning"
          ? refresh.summary
          : summary;
      this.publish({
        ...this.state,
        catalog: { kind: "ready", summary: visibleSummary },
      });
    } catch {
      if (this.destroyed || this.metadataAbortController.signal.aborted) {
        return;
      }
      // 底层异常可能含 URL、响应正文或凭据，页面只显示稳定安全码。
      this.publish({
        ...this.state,
        catalog: {
          kind: "failed",
          summary: this.state.catalog.summary,
          errorCode: "catalog-metadata-unavailable",
        },
      });
    }
  }

  private publish(state: CatalogMaintenanceState): void {
    this.state = state;
    for (const listener of this.listeners) listener();
  }
}

function catalogStateFromRefresh(
  refresh: CatalogRefreshState,
): CatalogMaintenanceState["catalog"] {
  if (refresh.kind === "success" || refresh.kind === "warning") {
    return { kind: "ready", summary: refresh.summary };
  }
  return { kind: "loading", summary: null };
}
