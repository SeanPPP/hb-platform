import type {
  CatalogRefreshOutcome,
  CatalogRefreshProgressEvent,
  CatalogRefreshStep,
  CatalogSummary,
} from "../catalog-refresh-contract";

const CATALOG_REFRESH_STEPS = [
  "prepare",
  "products",
  "promotions",
  "activate",
] as const satisfies readonly CatalogRefreshStep[];

export type CatalogMaintenanceErrorCode =
  | "catalog-metadata-unavailable"
  | "catalog-refresh-failed";

export type CatalogRefreshWarningCode = Extract<
  CatalogRefreshOutcome,
  Readonly<{ kind: "activated-with-warning" }>
>["warningCode"];

export type CatalogRefreshStepProgress = Readonly<{
  step: CatalogRefreshStep;
  percent: number;
  completedItemCount?: number;
  totalItemCount?: number;
}>;

/** 提供给页面的进度是已发生的事实，不以计时器估算或补齐百分比。 */
export type CatalogRefreshProgress = Readonly<{
  currentStep: CatalogRefreshStep;
  overallPercent: number;
  steps: readonly CatalogRefreshStepProgress[];
}>;

export type CatalogMaintenanceState = Readonly<{
  catalog:
    | Readonly<{ kind: "loading"; summary: CatalogSummary | null }>
    | Readonly<{ kind: "ready"; summary: CatalogSummary | null }>
    | Readonly<{
        kind: "failed";
        summary: CatalogSummary | null;
        errorCode: "catalog-metadata-unavailable";
      }>;
  refresh:
    | Readonly<{ kind: "idle" }>
    | Readonly<{ kind: "running"; progress: CatalogRefreshProgress }>
    | Readonly<{ kind: "success"; progress: CatalogRefreshProgress }>
    | Readonly<{
        kind: "warning";
        warningCode: CatalogRefreshWarningCode;
        progress: CatalogRefreshProgress;
      }>
    | Readonly<{
        kind: "failed";
        errorCode: "catalog-refresh-failed";
        progress: CatalogRefreshProgress;
      }>;
}>;

/**
 * 目录人工刷新的最小端口：presenter 不知道 HTTP、凭据、地址或任何持久化实现。
 * 门店始终来自已认证设备会话，屏幕也无法覆盖它。
 */
export interface CatalogMaintenancePort {
  getCurrentCatalog(
    input: Readonly<{ storeCode: string }>,
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
}>;

/**
 * 路由无关的目录人工刷新呈现器。
 * 目录摘要与刷新状态独立保存，下载期间旧 active 快照不会从画面消失。
 */
export class CatalogMaintenancePresenter {
  public state: CatalogMaintenanceState = {
    catalog: { kind: "loading", summary: null },
    refresh: { kind: "idle" },
  };

  private readonly listeners = new Set<() => void>();
  private readonly lifetimeAbortController = new AbortController();
  private initializeInFlight: Promise<void> | null = null;
  private refreshInFlight: Promise<void> | null = null;
  private destroyed = false;

  public constructor(private readonly options: CatalogMaintenancePresenterOptions) {}

  public readonly getState = (): CatalogMaintenanceState => this.state;

  public readonly subscribe = (listener: () => void): (() => void) => {
    if (this.destroyed) return () => undefined;
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  /** 初始读取只影响目录摘要；刷新失败后则用专用复读路径，不闪烁整个页面。 */
  public initialize(): Promise<void> {
    if (this.destroyed) return Promise.resolve();
    if (this.initializeInFlight) return this.initializeInFlight;

    this.publish({
      ...this.state,
      catalog: { kind: "loading", summary: this.state.catalog.summary },
    });
    const initialize = this.loadCurrentCatalog(true).finally(() => {
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

    let progress = createInitialProgress();
    this.publish({
      ...this.state,
      refresh: { kind: "running", progress },
    });
    const refresh = this.downloadAndActivate((event) => {
      progress = applyProgress(progress, event);
      if (this.destroyed || this.state.refresh.kind !== "running") return;
      this.publish({
        ...this.state,
        refresh: { kind: "running", progress },
      });
    }).finally(() => {
      if (this.refreshInFlight === refresh) this.refreshInFlight = null;
    });
    this.refreshInFlight = refresh;
    return refresh;
  }

  public destroy(): void {
    if (this.destroyed) return;
    this.destroyed = true;
    this.lifetimeAbortController.abort();
    this.listeners.clear();
  }

  private async downloadAndActivate(
    onProgress: (event: CatalogRefreshProgressEvent) => void,
  ): Promise<void> {
    try {
      const outcome = await this.options.port.downloadAndActivate({
        storeCode: this.options.authenticatedStoreCode,
        onProgress,
        signal: this.lifetimeAbortController.signal,
      });
      if (this.destroyed || this.state.refresh.kind !== "running") return;
      this.publish({
        catalog: { kind: "ready", summary: outcome.summary },
        refresh:
          outcome.kind === "complete"
            ? { kind: "success", progress: this.state.refresh.progress }
            : {
                kind: "warning",
                warningCode: outcome.warningCode,
                progress: this.state.refresh.progress,
              },
      });
    } catch {
      if (this.destroyed || this.state.refresh.kind !== "running") return;
      const progress = this.state.refresh.progress;
      await this.loadCurrentCatalog(false);
      if (this.destroyed) return;
      this.publish({
        ...this.state,
        refresh: {
          kind: "failed",
          errorCode: "catalog-refresh-failed",
          progress,
        },
      });
    }
  }

  private async loadCurrentCatalog(isInitialLoad: boolean): Promise<void> {
    try {
      const summary = await this.options.port.getCurrentCatalog({
        storeCode: this.options.authenticatedStoreCode,
      });
      if (this.destroyed) return;
      this.publish({ ...this.state, catalog: { kind: "ready", summary } });
    } catch {
      if (this.destroyed) return;
      // 中文注释：底层异常可能含 URL、响应正文或凭据，页面只显示稳定安全码。
      this.publish({
        ...this.state,
        catalog: {
          kind: "failed",
          summary: this.state.catalog.summary,
          errorCode: "catalog-metadata-unavailable",
        },
      });
      if (!isInitialLoad) return;
    }
  }

  private publish(state: CatalogMaintenanceState): void {
    this.state = state;
    for (const listener of this.listeners) listener();
  }
}

function createInitialProgress(): CatalogRefreshProgress {
  return {
    currentStep: "prepare",
    overallPercent: 0,
    steps: CATALOG_REFRESH_STEPS.map((step) => ({ step, percent: 0 })),
  };
}

function applyProgress(
  previous: CatalogRefreshProgress,
  event: CatalogRefreshProgressEvent,
): CatalogRefreshProgress {
  if (!isValidPercent(event.percent)) return previous;
  const eventIndex = CATALOG_REFRESH_STEPS.indexOf(event.step);
  const currentIndex = CATALOG_REFRESH_STEPS.indexOf(previous.currentStep);
  if (eventIndex < currentIndex) return previous;
  // 只有前一步已确实达到 100% 才接受下一步，避免 UI 伪造“已完成”。
  if (
    eventIndex > currentIndex &&
    previous.steps.slice(0, eventIndex).some((step) => step.percent !== 100)
  ) {
    return previous;
  }

  const steps = previous.steps.map((step) => {
    if (step.step !== event.step || event.percent < step.percent) return step;
    return {
      step: step.step,
      percent: event.percent,
      ...(event.completedItemCount === undefined
        ? {}
        : { completedItemCount: event.completedItemCount }),
      ...(event.totalItemCount === undefined
        ? {}
        : { totalItemCount: event.totalItemCount }),
    };
  });
  return {
    currentStep: event.step,
    overallPercent: steps.reduce((sum, step) => sum + step.percent, 0) / steps.length,
    steps,
  };
}

function isValidPercent(value: number): boolean {
  return Number.isFinite(value) && value >= 0 && value <= 100;
}
