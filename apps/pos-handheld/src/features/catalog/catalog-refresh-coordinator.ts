import type {
  CatalogRefreshOutcome,
  CatalogRefreshProgressEvent,
  CatalogRefreshStep,
  CatalogSummary,
} from "@hb/pos-domain/features/catalog/catalog-refresh-contract";
import { CatalogSnapshotFailure } from "./catalog-snapshot-service";

import { HbposApiError } from "@/core/api";

const CATALOG_REFRESH_STEPS = [
  "prepare",
  "products",
  "promotions",
  "activate",
] as const satisfies readonly CatalogRefreshStep[];

export type CatalogRefreshErrorCode =
  | "catalog-refresh-network-failed"
  | "catalog-refresh-api-rejected"
  | "catalog-refresh-verification-failed"
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
  completedPageCount?: number;
  totalPageCount?: number;
}>;

/** 百分比只来自底层已发生的持久化事实；计时器仅更新真实耗时。 */
export type CatalogRefreshProgress = Readonly<{
  currentStep: CatalogRefreshStep;
  overallPercent: number;
  elapsedMilliseconds: number;
  steps: readonly CatalogRefreshStepProgress[];
}>;

export type CatalogRefreshState =
  | Readonly<{ kind: "idle" }>
  | Readonly<{
      kind: "running";
      storeCode: string;
      progress: CatalogRefreshProgress;
    }>
  | Readonly<{
      kind: "success";
      storeCode: string;
      summary: CatalogSummary;
      progress: CatalogRefreshProgress;
    }>
  | Readonly<{
      kind: "warning";
      storeCode: string;
      summary: CatalogSummary;
      warningCode: CatalogRefreshWarningCode;
      progress: CatalogRefreshProgress;
    }>
  | Readonly<{
      kind: "failed";
      storeCode: string;
      errorCode: CatalogRefreshErrorCode;
      progress: CatalogRefreshProgress;
    }>;

export type CatalogRefreshExecutionInput = Readonly<{
  signal: AbortSignal;
  onProgress(event: CatalogRefreshProgressEvent): void;
}>;

export type CatalogRefreshStartInput = Readonly<{
  storeCode: string;
  execute(input: CatalogRefreshExecutionInput): Promise<CatalogRefreshOutcome>;
}>;

export type CatalogRefreshCoordinatorErrorCode =
  | "CATALOG_REFRESH_STORE_REQUIRED"
  | "CATALOG_REFRESH_STORE_CONFLICT"
  | "CATALOG_REFRESH_OPERATION_CONFLICT"
  | "CATALOG_REFRESH_COORDINATOR_SHUTDOWN";

export class CatalogRefreshCoordinatorError extends Error {
  public constructor(public readonly code: CatalogRefreshCoordinatorErrorCode) {
    super(code);
    this.name = "CatalogRefreshCoordinatorError";
  }
}

export type CatalogRefreshCoordinatorOptions = Readonly<{
  elapsedIntervalMilliseconds?: number;
  nowMilliseconds?: () => number;
  onDiagnostic?: (event: CatalogRefreshDiagnosticEvent) => void;
}>;

export type CatalogRefreshDiagnosticEvent = Readonly<{
  storeCode: string;
  code: string;
  pageNumber: number;
  completedItemCount: number;
  totalItemCount?: number;
  httpStatus?: number;
}>;

/**
 * 单个 runtime 持有的目录刷新 single-flight。页面只订阅状态；换页不会拥有或
 * 取消任务，只有 runtime shutdown 能中止并等待底层安全清理结束。
 */
export class CatalogRefreshCoordinator {
  private readonly elapsedIntervalMilliseconds: number;
  private readonly nowMilliseconds: () => number;
  private readonly onDiagnostic: (event: CatalogRefreshDiagnosticEvent) => void;
  private readonly listeners = new Set<() => void>();
  private state: CatalogRefreshState = { kind: "idle" };
  private inFlight: Promise<CatalogRefreshOutcome> | null = null;
  private exclusiveInFlight: Promise<unknown> | null = null;
  private activeStoreCode: string | null = null;
  private activeController: AbortController | null = null;
  private activeToken: symbol | null = null;
  private startedAtMilliseconds: number | null = null;
  private elapsedTimer: ReturnType<typeof setInterval> | null = null;
  private shutdownStarted = false;
  private shutdownInFlight: Promise<void> | null = null;

  public constructor(options: CatalogRefreshCoordinatorOptions = {}) {
    this.elapsedIntervalMilliseconds =
      options.elapsedIntervalMilliseconds ?? 1_000;
    this.nowMilliseconds = options.nowMilliseconds ?? (() => Date.now());
    this.onDiagnostic = options.onDiagnostic ?? logCatalogRefreshDiagnostic;
  }

  public readonly getState = (): CatalogRefreshState => this.state;

  public readonly subscribe = (listener: () => void): (() => void) => {
    if (this.shutdownStarted) return () => undefined;
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  public start(
    input: CatalogRefreshStartInput,
  ): Promise<CatalogRefreshOutcome> {
    const storeCode = input.storeCode.trim();
    if (!storeCode) {
      return Promise.reject(
        new CatalogRefreshCoordinatorError(
          "CATALOG_REFRESH_STORE_REQUIRED",
        ),
      );
    }
    if (this.shutdownStarted) {
      return Promise.reject(
        new CatalogRefreshCoordinatorError(
          "CATALOG_REFRESH_COORDINATOR_SHUTDOWN",
        ),
      );
    }
    if (this.exclusiveInFlight) {
      return Promise.reject(
        new CatalogRefreshCoordinatorError(
          "CATALOG_REFRESH_OPERATION_CONFLICT",
        ),
      );
    }
    if (this.inFlight) {
      if (this.activeStoreCode === storeCode) return this.inFlight;
      return Promise.reject(
        new CatalogRefreshCoordinatorError(
          "CATALOG_REFRESH_STORE_CONFLICT",
        ),
      );
    }

    const controller = new AbortController();
    const token = Symbol("catalog-refresh");
    this.activeController = controller;
    this.activeStoreCode = storeCode;
    this.activeToken = token;
    this.startedAtMilliseconds = this.nowMilliseconds();
    const operation = Promise.resolve()
      .then(() => this.run(input.execute, storeCode, controller, token))
      .finally(() => {
        if (this.activeToken !== token) return;
        this.stopElapsedClock();
        this.inFlight = null;
        this.activeController = null;
        this.activeStoreCode = null;
        this.activeToken = null;
      });
    this.inFlight = operation;
    this.startElapsedClock(token);
    this.publish({
      kind: "running",
      storeCode,
      progress: createInitialProgress(),
    });
    return operation;
  }

  /**
   * 配置保存、设备重注册和应用重启与目录刷新共享互斥门闩，避免旧 transport
   * 在运行时分区切换后继续写入或激活。
   */
  public runExclusive<T>(operation: () => Promise<T>): Promise<T> {
    if (this.shutdownStarted) {
      return Promise.reject(
        new CatalogRefreshCoordinatorError(
          "CATALOG_REFRESH_COORDINATOR_SHUTDOWN",
        ),
      );
    }
    if (this.inFlight || this.exclusiveInFlight) {
      return Promise.reject(
        new CatalogRefreshCoordinatorError(
          "CATALOG_REFRESH_OPERATION_CONFLICT",
        ),
      );
    }
    const guarded = Promise.resolve()
      .then(operation)
      .finally(() => {
        if (this.exclusiveInFlight === guarded) {
          this.exclusiveInFlight = null;
        }
      });
    this.exclusiveInFlight = guarded;
    return guarded;
  }

  public shutdown(): Promise<void> {
    if (this.shutdownInFlight) return this.shutdownInFlight;
    this.shutdownStarted = true;
    this.activeController?.abort();
    const running = this.inFlight;
    const exclusive = this.exclusiveInFlight;
    const shutdown = (async () => {
      if (running || exclusive) {
        try {
          await Promise.all([running, exclusive]);
        } catch {
          // 刷新失败或取消已由共享状态/底层 staging 清理收口，shutdown 仍须继续。
        }
      }
      this.stopElapsedClock();
      this.listeners.clear();
    })();
    this.shutdownInFlight = shutdown;
    return shutdown;
  }

  private async run(
    execute: CatalogRefreshStartInput["execute"],
    storeCode: string,
    controller: AbortController,
    token: symbol,
  ): Promise<CatalogRefreshOutcome> {
    try {
      const outcome = await execute({
        signal: controller.signal,
        onProgress: (event) => {
          if (
            this.activeToken !== token ||
            this.state.kind !== "running"
          ) {
            return;
          }
          this.publish({
            ...this.state,
            progress: applyProgress(this.state.progress, event),
          });
        },
      });
      if (this.activeToken === token) {
        const progress = this.progressWithCurrentElapsed();
        this.publish(
          outcome.kind === "complete"
            ? {
                kind: "success",
                storeCode,
                summary: outcome.summary,
                progress,
              }
            : {
                kind: "warning",
                storeCode,
                summary: outcome.summary,
                warningCode: outcome.warningCode,
                progress,
              },
        );
      }
      return outcome;
    } catch (error) {
      if (this.activeToken === token) {
        if (
          controller.signal.aborted ||
          isCatalogRefreshCancellation(error)
        ) {
          this.publish({ kind: "idle" });
        } else {
          const diagnostic = catalogRefreshDiagnostic(error, storeCode);
          if (diagnostic) {
            try {
              this.onDiagnostic(diagnostic);
            } catch {
              // 中文注释：诊断日志绝不能改变目录清理或失败状态。
            }
          }
          this.publish({
            kind: "failed",
            storeCode,
            errorCode: classifyCatalogRefreshError(error),
            progress: this.progressWithCurrentElapsed(),
          });
        }
      }
      throw error;
    }
  }

  private startElapsedClock(token: symbol): void {
    this.stopElapsedClock(false);
    this.elapsedTimer = setInterval(() => {
      if (
        this.activeToken !== token ||
        this.state.kind !== "running"
      ) {
        return;
      }
      this.publish({
        ...this.state,
        progress: this.progressWithCurrentElapsed(),
      });
    }, this.elapsedIntervalMilliseconds);
  }

  private stopElapsedClock(clearStart = true): void {
    if (this.elapsedTimer !== null) {
      clearInterval(this.elapsedTimer);
      this.elapsedTimer = null;
    }
    if (clearStart) this.startedAtMilliseconds = null;
  }

  private progressWithCurrentElapsed(): CatalogRefreshProgress {
    const current =
      this.state.kind === "idle"
        ? createInitialProgress()
        : this.state.progress;
    const startedAt = this.startedAtMilliseconds;
    if (startedAt === null) return current;
    return {
      ...current,
      elapsedMilliseconds: Math.max(
        current.elapsedMilliseconds,
        this.nowMilliseconds() - startedAt,
      ),
    };
  }

  private publish(state: CatalogRefreshState): void {
    this.state = state;
    for (const listener of this.listeners) {
      try {
        listener();
      } catch {
        // 观察者销毁或异常不能中断目录刷新与安全激活。
      }
    }
  }
}

function createInitialProgress(): CatalogRefreshProgress {
  return {
    currentStep: "prepare",
    overallPercent: 0,
    elapsedMilliseconds: 0,
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
  if (
    eventIndex > currentIndex &&
    previous.steps
      .slice(0, eventIndex)
      .some((step) => step.percent !== 100)
  ) {
    return previous;
  }

  const steps = previous.steps.map((step) => {
    if (
      step.step !== event.step ||
      event.percent < step.percent
    ) {
      return step;
    }
    return {
      ...step,
      percent: event.percent,
      ...(event.completedItemCount === undefined
        ? {}
        : { completedItemCount: event.completedItemCount }),
      ...(event.totalItemCount === undefined
        ? {}
        : { totalItemCount: event.totalItemCount }),
      ...(event.completedPageCount === undefined
        ? {}
        : { completedPageCount: event.completedPageCount }),
      ...(event.totalPageCount === undefined
        ? {}
        : { totalPageCount: event.totalPageCount }),
    };
  });
  return {
    currentStep: event.step,
    overallPercent:
      steps.reduce((sum, step) => sum + step.percent, 0) /
      steps.length,
    elapsedMilliseconds: Math.max(
      previous.elapsedMilliseconds,
      event.elapsedMilliseconds ?? 0,
    ),
    steps,
  };
}

function isValidPercent(value: number): boolean {
  return Number.isFinite(value) && value >= 0 && value <= 100;
}

function isCatalogRefreshCancellation(error: unknown): boolean {
  if (
    error instanceof HbposApiError &&
    error.kind === "transport" &&
    error.code === "REQUEST_ABORTED"
  ) {
    return true;
  }
  if (typeof error !== "object" || error === null) return false;
  const candidate = error as Readonly<{
    name?: unknown;
    code?: unknown;
  }>;
  return (
    candidate.name === "AbortError" ||
    candidate.name === "CanceledError" ||
    candidate.code === "ERR_CANCELED"
  );
}

function classifyCatalogRefreshError(
  error: unknown,
): CatalogRefreshErrorCode {
  if (error instanceof HbposApiError) {
    if (error.code?.startsWith("CATALOG_")) {
      return "catalog-refresh-verification-failed";
    }
    if (error.kind === "transport") {
      return "catalog-refresh-network-failed";
    }
    if (error.kind === "http") {
      return "catalog-refresh-api-rejected";
    }
    if (error.kind === "envelope") {
      return "catalog-refresh-api-rejected";
    }
  }
  if (isCatalogVerificationFailure(error)) {
    return "catalog-refresh-verification-failed";
  }
  return "catalog-refresh-failed";
}

function catalogRefreshDiagnostic(
  error: unknown,
  storeCode: string,
): CatalogRefreshDiagnosticEvent | null {
  if (!(error instanceof CatalogSnapshotFailure)) return null;
  return {
    storeCode,
    ...error.context,
  };
}

function logCatalogRefreshDiagnostic(
  event: CatalogRefreshDiagnosticEvent,
): void {
  console.error(
    "[HBPOS][Handheld][CatalogRefresh]",
    JSON.stringify(event),
  );
}

function isCatalogVerificationFailure(error: unknown): boolean {
  return (
    typeof error === "object" &&
    error !== null &&
    "code" in error &&
    typeof error.code === "string" &&
    error.code.startsWith("CATALOG_")
  );
}
