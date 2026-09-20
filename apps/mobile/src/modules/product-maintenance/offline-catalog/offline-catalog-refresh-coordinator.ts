/**
 * 离线目录刷新协调器（参考 apps/pos-ipad/src/features/catalog/catalog-refresh-coordinator.ts）。
 *
 * 单飞：同店重复请求复用同一 Promise；异店请求抢占（用户切换门店即放弃旧门店的下载）。
 * 页面只订阅状态，进度百分比只来自底层已发生的持久化事实。
 *
 * controller / activeStoreCode / inFlight 三个字段必须在 start 的同步段一次认领完：
 * 只要有一段是在 await 之后才赋值，期间到达的调用就会误判当前意图，造成重复下载
 * 或产生取消不掉的孤儿任务。
 */
import { isNetworkUnavailableError } from "@/shared/network/network-error";
import {
  isOfflineCatalogCancellation,
  type OfflineCatalogRefreshProgressEvent,
  type OfflineCatalogRefreshResult,
  type OfflineCatalogRefreshStep,
} from "./offline-catalog-sync-service";
import { OfflineCatalogError, type ActiveOfflineCatalogMetadata } from "./types";

/** 与 offline-catalog-sync-service 的取消错误码保持一致，便于 isOfflineCatalogCancellation 识别。 */
const OFFLINE_CATALOG_CANCELLED_CODE = "OFFLINE_CATALOG_CANCELLED";

export type OfflineCatalogRefreshErrorCode =
  | "network"
  | "capacityBusy"
  | "expired"
  | "verification"
  | "api"
  | "failed";

export interface OfflineCatalogRefreshProgress {
  step: OfflineCatalogRefreshStep;
  percent: number;
  completedItemCount: number;
  totalItemCount: number | null;
  startedAtMs: number;
}

export type OfflineCatalogRefreshState =
  | { kind: "idle" }
  | { kind: "running"; storeCode: string; progress: OfflineCatalogRefreshProgress }
  | { kind: "success"; storeCode: string; result: OfflineCatalogRefreshResult; finishedAtMs: number }
  | { kind: "failed"; storeCode: string; errorCode: OfflineCatalogRefreshErrorCode; errorDetail: string; failedAtMs: number };

export interface OfflineCatalogRefreshExecutionInput {
  signal: AbortSignal;
  onProgress(event: OfflineCatalogRefreshProgressEvent): void;
}

export class OfflineCatalogRefreshCoordinator {
  private state: OfflineCatalogRefreshState = { kind: "idle" };
  private readonly listeners = new Set<(state: OfflineCatalogRefreshState) => void>();
  private inFlight: Promise<OfflineCatalogRefreshResult> | null = null;
  private activeStoreCode: string | null = null;
  private activeController: AbortController | null = null;

  public constructor(private readonly nowMs: () => number = () => Date.now()) {}

  public getState(): OfflineCatalogRefreshState {
    return this.state;
  }

  public subscribe(listener: (state: OfflineCatalogRefreshState) => void): () => void {
    this.listeners.add(listener);
    listener(this.state);
    return () => this.listeners.delete(listener);
  }

  public get isRunning(): boolean {
    return this.inFlight !== null;
  }

  public start(
    storeCode: string,
    execute: (input: OfflineCatalogRefreshExecutionInput) => Promise<OfflineCatalogRefreshResult>,
  ): Promise<OfflineCatalogRefreshResult> {
    const normalized = storeCode.trim();
    if (!normalized) {
      return Promise.reject(new OfflineCatalogError("Store code is required.", "OFFLINE_CATALOG_STORE_REQUIRED"));
    }
    // 同店复用：这里能看到仍在排队等待上一任务落定的请求，所以连点不会重复下载；
    // 已被取消、正在解绕的任务不复用，否则调用方拿到的是一个注定拒绝的 Promise。
    if (
      this.inFlight &&
      this.activeStoreCode === normalized &&
      !this.activeController?.signal.aborted
    ) {
      return this.inFlight;
    }

    // 抢占：立刻中止旧任务，并在**同步**代码里认领 controller / storeCode / inFlight。
    // 这三个字段同步落定是关键——旧写法要等 await 之后才认领，期间的第三次调用会
    // 以为自己空闲而直接启动，随后被覆盖成孤儿任务：取消按钮够不到它，它也不复位状态。
    const previous = this.inFlight;
    this.activeController?.abort();
    const controller = new AbortController();
    this.activeController = controller;
    this.activeStoreCode = normalized;
    const startedAtMs = this.nowMs();
    this.publish({
      kind: "running",
      storeCode: normalized,
      progress: { step: "prepare", percent: 0, completedItemCount: 0, totalItemCount: null, startedAtMs },
    });
    const operation = this.run(normalized, controller, startedAtMs, previous, execute)
      .then((result) => {
        if (this.activeController === controller) {
          this.publish({ kind: "success", storeCode: normalized, result, finishedAtMs: this.nowMs() });
        }
        return result;
      })
      .catch((error: unknown) => {
        if (this.activeController === controller) {
          if (controller.signal.aborted || isOfflineCatalogCancellation(error)) {
            this.publish({ kind: "idle" });
          } else {
            this.publish({
              kind: "failed",
              storeCode: normalized,
              errorCode: classifyOfflineCatalogRefreshError(error),
              errorDetail: error instanceof Error ? error.message : String(error),
              failedAtMs: this.nowMs(),
            });
          }
        }
        throw error;
      })
      .finally(() => {
        if (this.activeController === controller) {
          this.inFlight = null;
          this.activeController = null;
          this.activeStoreCode = null;
        }
      });
    this.inFlight = operation;
    // 调用方不一定 await（例如被抢占的那次），先消费一次拒绝避免未处理拒绝告警。
    operation.catch(() => undefined);
    return operation;
  }

  /** 等上一任务真正落定后再执行；两次同步不能并发写同一份 SQLite staging。 */
  private run(
    normalized: string,
    controller: AbortController,
    startedAtMs: number,
    previous: Promise<OfflineCatalogRefreshResult> | null,
    execute: (input: OfflineCatalogRefreshExecutionInput) => Promise<OfflineCatalogRefreshResult>,
  ): Promise<OfflineCatalogRefreshResult> {
    const invoke = (): Promise<OfflineCatalogRefreshResult> => {
      if (controller.signal.aborted) {
        // 排队期间就被后来者抢占：连下载都不必启动，直接以取消收尾。
        return Promise.reject(
          new OfflineCatalogError(
            "Offline catalog refresh cancelled.",
            OFFLINE_CATALOG_CANCELLED_CODE,
          ),
        );
      }
      return execute({
        signal: controller.signal,
        onProgress: (event) => {
          if (this.state.kind !== "running" || this.activeController !== controller) {
            return;
          }
          this.publish({
            ...this.state,
            progress: {
              step: event.step,
              percent: Math.max(0, Math.min(100, Math.floor(event.percent))),
              completedItemCount: event.completedItemCount ?? this.state.progress.completedItemCount,
              totalItemCount: event.totalItemCount ?? this.state.progress.totalItemCount,
              startedAtMs,
            },
          });
        },
      });
    };
    // 没有前一个任务时同步进入，保持「调用 start 即开始下载」的语义。
    return previous ? previous.catch(() => undefined).then(invoke) : invoke();
  }

  public cancel(): void {
    this.activeController?.abort();
  }

  private publish(state: OfflineCatalogRefreshState): void {
    this.state = state;
    for (const listener of this.listeners) {
      listener(state);
    }
  }
}

export function classifyOfflineCatalogRefreshError(error: unknown): OfflineCatalogRefreshErrorCode {
  if (isNetworkUnavailableError(error)) {
    return "network";
  }
  const code = (error as { code?: unknown } | null)?.code;
  const status = (error as { status?: unknown; response?: { status?: unknown } } | null);
  const httpStatus = typeof status?.status === "number" ? status.status : status?.response?.status;
  if (code === "OFFLINE_CATALOG_CAPACITY_BUSY" || httpStatus === 503) {
    return "capacityBusy";
  }
  if (code === "OFFLINE_CATALOG_SNAPSHOT_EXPIRED" || httpStatus === 409) {
    return "expired";
  }
  if (typeof code === "string" && code.startsWith("OFFLINE_CATALOG_")) {
    return "verification";
  }
  if (typeof httpStatus === "number") {
    return "api";
  }
  return "failed";
}

export function toActiveMetadata(result: OfflineCatalogRefreshResult): ActiveOfflineCatalogMetadata {
  return result.metadata;
}
