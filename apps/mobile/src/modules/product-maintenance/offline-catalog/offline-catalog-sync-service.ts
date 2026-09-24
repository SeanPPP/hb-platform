/**
 * 离线目录同步服务（参考 apps/pos-ipad/src/features/catalog/catalog-snapshot-service.ts）。
 *
 * 流程：sync-plan → noChange（只更新检查时间）/ delta（≤5000 操作，回放到 active）/ full（分页下载到 staging 后原子切换）。
 * 全部页面在落库前校验：版本一致、总数一致、游标不重复、key 唯一、店码一致；任何失败都丢弃 staging，
 * 旧 active 永不受影响。delta 基线过期（409）或基线漂移时自动回退全量。
 */
import type { OfflineCatalogRemote } from "./offline-catalog-remote";
import {
  OfflineCatalogDeltaBaseChangedError,
  type OfflineCatalogDeltaStagingBatch,
  type OfflineCatalogRepository,
} from "./offline-catalog-repository";
import {
  OfflineCatalogError,
  type ActiveOfflineCatalogMetadata,
  type OfflineCatalogDeltaPage,
  type OfflineCatalogItem,
  type OfflineCatalogPage,
  type OfflineCatalogSyncPlan,
} from "./types";

export type OfflineCatalogRefreshStep = "prepare" | "products" | "activate";

export interface OfflineCatalogRefreshProgressEvent {
  step: OfflineCatalogRefreshStep;
  percent: number;
  completedItemCount?: number;
  totalItemCount?: number;
  completedPageCount?: number;
  totalPageCount?: number;
}

export interface OfflineCatalogRefreshRequest {
  storeCode: string;
  onProgress?: (event: OfflineCatalogRefreshProgressEvent) => void;
  signal?: AbortSignal;
  /** 仅用户主动更新时允许重试冷目录构建造成的网关超时。 */
  retrySyncPlanGatewayTimeout?: boolean;
}

export interface OfflineCatalogRefreshResult {
  mode: "full" | "delta" | "noChange";
  metadata: ActiveOfflineCatalogMetadata;
}

export type OfflineCatalogSyncStorage = Pick<
  OfflineCatalogRepository,
  | "getActiveMetadata"
  | "beginStaging"
  | "appendPage"
  | "activate"
  | "beginDeltaStaging"
  | "appendDeltaBatch"
  | "activateDelta"
  | "discardStagingBatch"
  | "cleanupStagingBatch"
  | "cleanupRetiredBatch"
>;

export interface OfflineCatalogSyncServiceOptions {
  createSnapshotId: () => string;
  nowIso?: () => string;
  pageSize?: number;
  /** 本地落库批大小；每批后让出事件循环，避免长时间阻塞 UI。 */
  localBatchSize?: number;
  yieldControl?: () => Promise<void>;
  /** 首次冷构建可能超过网关 60 秒；重试仍复用后端同店构建任务。 */
  syncPlanRetryDelaysMs?: readonly number[];
}

export const OFFLINE_CATALOG_PAGE_SIZE = 5_000;
export const OFFLINE_CATALOG_DELTA_MAX_OPERATIONS = 5_000;
const OFFLINE_CATALOG_DELTA_BATCH_SIZE = 500;
const DEFAULT_SYNC_PLAN_RETRY_DELAYS_MS = [1_000, 2_000] as const;

export class OfflineCatalogSyncService {
  private readonly nowIso: () => string;
  private readonly pageSize: number;
  private readonly localBatchSize: number;
  private readonly yieldControl: () => Promise<void>;
  private readonly syncPlanRetryDelaysMs: readonly number[];
  private serial: Promise<unknown> = Promise.resolve();

  public constructor(
    private readonly storage: OfflineCatalogSyncStorage,
    private readonly remote: OfflineCatalogRemote,
    private readonly options: OfflineCatalogSyncServiceOptions,
  ) {
    this.nowIso = options.nowIso ?? (() => new Date().toISOString());
    this.pageSize = options.pageSize ?? OFFLINE_CATALOG_PAGE_SIZE;
    this.localBatchSize = options.localBatchSize ?? 500;
    this.yieldControl = options.yieldControl ?? yieldToEventLoop;
    this.syncPlanRetryDelaysMs = options.syncPlanRetryDelaysMs ?? DEFAULT_SYNC_PLAN_RETRY_DELAYS_MS;
  }

  /** 串行执行；失败只影响本次请求，后续刷新仍排队而不能并发切换 active。 */
  public refresh(input: OfflineCatalogRefreshRequest): Promise<OfflineCatalogRefreshResult> {
    const operation = this.serial.then(
      () => this.runAndSweep(input),
      () => this.runAndSweep(input),
    );
    this.serial = operation.then(() => undefined, () => undefined);
    return operation;
  }

  /**
   * 激活成功后立刻回收被退役的旧快照。
   *
   * 只靠下一次刷新开头的回收是不够的：两份完整目录会一直并存到下次刷新，
   * 40 万行的门店等于白白占用双倍磁盘。回收失败不影响本次刷新的结果。
   */
  private async runAndSweep(input: OfflineCatalogRefreshRequest): Promise<OfflineCatalogRefreshResult> {
    const result = await this.runWithSyncPlan(input);
    if (result.mode !== "noChange") {
      try {
        await this.cleanupLoop((batch) => this.storage.cleanupRetiredBatch(batch));
      } catch (error) {
        const message = error instanceof Error ? error.message : String(error);
        console.warn("[offline-catalog] retired snapshot cleanup failed", { message });
      }
    }
    return result;
  }

  private async runWithSyncPlan(input: OfflineCatalogRefreshRequest): Promise<OfflineCatalogRefreshResult> {
    const storeCode = input.storeCode.trim();
    if (!storeCode) {
      throw new OfflineCatalogError("Store code is required.", "OFFLINE_CATALOG_STORE_REQUIRED");
    }
    throwIfAborted(input.signal);
    // 上次异常遗留的 staging / retired 先有界回收，让文件 freelist 可复用。
    await this.cleanupLoop((batch) => this.storage.cleanupStagingBatch(batch));
    await this.cleanupLoop((batch) => this.storage.cleanupRetiredBatch(batch));
    throwIfAborted(input.signal);

    const active = await this.storage.getActiveMetadata(storeCode);
    const plan = await this.getSyncPlan(input, storeCode, active?.catalogVersion ?? null);
    throwIfAborted(input.signal);
    assertSyncPlan(plan, active?.catalogVersion ?? null);

    if (active === null) {
      if (plan.mode !== "full") {
        throw verification("Offline catalog sync plan without a local base must be full.", "OFFLINE_CATALOG_SYNC_PLAN_INVALID");
      }
      return this.runFull(input, storeCode, plan);
    }
    if (plan.mode === "noChange") {
      if (plan.targetTotal !== active.itemCount) {
        // 本地条目数与服务端不一致说明本地损坏，宁可全量。
        return this.runFullWithFreshPlan(input, storeCode);
      }
      report(input.onProgress, { step: "activate", percent: 100, completedItemCount: active.itemCount, totalItemCount: active.itemCount });
      return { mode: "noChange", metadata: active };
    }
    if (plan.mode === "delta") {
      if ((plan.deltaOperationCount ?? 0) > OFFLINE_CATALOG_DELTA_MAX_OPERATIONS) {
        return this.runFullWithFreshPlan(input, storeCode);
      }
      return this.runDelta(input, storeCode, active, plan);
    }
    return this.runFull(input, storeCode, plan);
  }

  private async runFullWithFreshPlan(input: OfflineCatalogRefreshRequest, storeCode: string): Promise<OfflineCatalogRefreshResult> {
    throwIfAborted(input.signal);
    const plan = await this.getSyncPlan(input, storeCode, null);
    throwIfAborted(input.signal);
    assertSyncPlan(plan, null);
    if (plan.mode !== "full") {
      throw verification("Offline catalog fallback plan without a base must be full.", "OFFLINE_CATALOG_SYNC_PLAN_INVALID");
    }
    return this.runFull(input, storeCode, plan);
  }

  private async getSyncPlan(
    input: OfflineCatalogRefreshRequest,
    storeCode: string,
    baseCatalogVersion: string | null,
  ): Promise<OfflineCatalogSyncPlan> {
    const retryDelays = input.retrySyncPlanGatewayTimeout ? this.syncPlanRetryDelaysMs : [];
    for (let attempt = 0; ; attempt += 1) {
      throwIfAborted(input.signal);
      try {
        return await this.remote.getSyncPlan({ storeCode, baseCatalogVersion, signal: input.signal });
      } catch (error) {
        throwIfAborted(input.signal);
        if (!isGatewayTimeout(error) || !input.retrySyncPlanGatewayTimeout) {
          throw error;
        }
        if (attempt >= retryDelays.length) {
          throw new OfflineCatalogError(
            "Offline catalog preparation timed out. Please retry manually later.",
            "OFFLINE_CATALOG_PREPARATION_TIMEOUT",
          );
        }
        // 服务端会继续构建并合并同店请求；等待后再次取 plan，不重启整次下载。
        await waitForRetry(retryDelays[attempt], input.signal);
      }
    }
  }

  private async runFull(
    input: OfflineCatalogRefreshRequest,
    storeCode: string,
    plan: OfflineCatalogSyncPlan,
  ): Promise<OfflineCatalogRefreshResult> {
    const snapshotId = this.options.createSnapshotId();
    let stagingStarted = false;
    let activated = false;
    try {
      report(input.onProgress, { step: "prepare", percent: 0 });
      const first = await this.remote.getPage({
        storeCode,
        cursor: null,
        pageSize: this.pageSize,
        catalogVersion: plan.targetCatalogVersion,
        downloadLeaseId: plan.downloadLeaseId,
        signal: input.signal,
      });
      throwIfAborted(input.signal);
      assertPageContract(first, { storeCode, requestedCursor: null });
      if (first.catalogVersion !== plan.targetCatalogVersion || first.totalCount !== plan.targetTotal) {
        throw verification("Offline catalog full page does not match the pinned sync plan.", "OFFLINE_CATALOG_SYNC_PLAN_TARGET_CHANGED");
      }
      await this.storage.beginStaging({
        snapshotId,
        storeCode,
        catalogVersion: first.catalogVersion,
        checksum: first.pageChecksum,
        generatedAtIso: plan.generatedAt,
        downloadedAtIso: this.nowIso(),
      });
      stagingStarted = true;
      throwIfAborted(input.signal);
      const totalPageCount = Math.max(1, Math.ceil(first.totalCount / this.pageSize));
      report(input.onProgress, { step: "prepare", percent: 100 });
      report(input.onProgress, {
        step: "products",
        percent: 0,
        completedItemCount: 0,
        totalItemCount: first.totalCount,
        completedPageCount: 0,
        totalPageCount,
      });

      const seenKeys = new Set<string>();
      const seenCursors = new Set<string>();
      let count = 0;
      let completedPageCount = 0;
      let page: OfflineCatalogPage | null = first;
      let requestedCursor: string | null = null;
      let pendingCursor: string | null = null;
      let inflight: Promise<OfflineCatalogPage> | null = null;

      // 预取管道：落库当前页期间提前请求下一页，让网络 RTT 与 SQLite 写入重叠。
      const prefetchNext = (current: OfflineCatalogPage) => {
        const nextCursor = current.nextCursor;
        if (nextCursor === null || inflight !== null) {
          return;
        }
        if (seenCursors.has(nextCursor)) {
          throw verification("Offline catalog pagination cursor repeated.", "OFFLINE_CATALOG_CURSOR_REPEATED");
        }
        seenCursors.add(nextCursor);
        pendingCursor = nextCursor;
        inflight = this.remote.getPage({
          storeCode,
          cursor: nextCursor,
          pageSize: this.pageSize,
          catalogVersion: first.catalogVersion,
          downloadLeaseId: plan.downloadLeaseId,
          signal: input.signal,
        });
        void inflight.catch(() => undefined);
      };

      while (page) {
        throwIfAborted(input.signal);
        assertPageContract(page, { storeCode, requestedCursor });
        if (page.catalogVersion !== first.catalogVersion) {
          throw verification("Offline catalog snapshot version changed during paged download.", "OFFLINE_CATALOG_SNAPSHOT_VERSION_CHANGED");
        }
        if (page.totalCount !== first.totalCount) {
          throw verification("Offline catalog total changed during paged download.", "OFFLINE_CATALOG_SNAPSHOT_TOTAL_CHANGED");
        }
        prefetchNext(page);
        assertUniqueKeys(page.items, seenKeys);
        const finalPage = page.nextCursor === null;
        if (!finalPage && first.totalCount === 0) {
          throw verification("Offline catalog pagination cannot continue after an empty total.", "OFFLINE_CATALOG_PAGINATION_INVALID");
        }
        for (const batch of chunkItems(page.items, this.localBatchSize)) {
          throwIfAborted(input.signal);
          await this.storage.appendPage(snapshotId, batch);
          count += batch.length;
          if (count > first.totalCount) {
            throw verification("Offline catalog page count exceeds the server total.", "OFFLINE_CATALOG_ITEM_COUNT_MISMATCH");
          }
          await this.yieldControl();
        }
        completedPageCount += 1;
        if (finalPage && count !== first.totalCount) {
          throw verification("Offline catalog page count does not match the server total.", "OFFLINE_CATALOG_ITEM_COUNT_MISMATCH");
        }
        report(input.onProgress, {
          step: "products",
          percent: finalPage ? 100 : Math.min(99, Math.floor((count / Math.max(1, first.totalCount)) * 100)),
          completedItemCount: count,
          totalItemCount: first.totalCount,
          completedPageCount,
          totalPageCount,
        });
        if (finalPage) {
          break;
        }
        if (!inflight) {
          throw verification("Offline catalog pagination lost its continuation.", "OFFLINE_CATALOG_PAGINATION_INVALID");
        }
        page = await inflight;
        inflight = null;
        requestedCursor = pendingCursor;
      }

      report(input.onProgress, { step: "activate", percent: 0 });
      throwIfAborted(input.signal);
      const activatedAt = this.nowIso();
      await this.storage.activate(snapshotId, count, activatedAt);
      activated = true;
      report(input.onProgress, { step: "activate", percent: 100 });
      return {
        mode: "full",
        metadata: {
          snapshotId,
          storeCode,
          catalogVersion: first.catalogVersion,
          itemCount: count,
          generatedAt: plan.generatedAt,
          activatedAt,
        },
      };
    } catch (error) {
      if (stagingStarted && !activated) {
        await this.discardStaging(snapshotId);
      }
      throw error;
    }
  }

  private async runDelta(
    input: OfflineCatalogRefreshRequest,
    storeCode: string,
    active: ActiveOfflineCatalogMetadata,
    plan: OfflineCatalogSyncPlan,
  ): Promise<OfflineCatalogRefreshResult> {
    const snapshotId = this.options.createSnapshotId();
    let stagingStarted = false;
    let activated = false;
    try {
      report(input.onProgress, { step: "prepare", percent: 0 });
      await this.storage.beginDeltaStaging({
        sourceSnapshotId: active.snapshotId,
        baseCatalogVersion: active.catalogVersion,
        snapshotId,
        storeCode,
        catalogVersion: plan.targetCatalogVersion,
        checksum: `delta:${plan.targetCatalogVersion}`,
        generatedAtIso: plan.generatedAt,
        downloadedAtIso: this.nowIso(),
      });
      stagingStarted = true;
      report(input.onProgress, { step: "prepare", percent: 100 });
      report(input.onProgress, { step: "products", percent: 0, completedItemCount: 0, totalItemCount: plan.targetTotal });

      const seenCursors = new Set<string>();
      const seenUpserts = new Set<string>();
      const seenDeletes = new Set<string>();
      let cursor: string | null = null;
      let completedOperations = 0;
      let completedPageCount = 0;
      let pendingCursor: string | null = null;
      let inflight: Promise<OfflineCatalogDeltaPage> | null = null;
      const requestPage = (requestCursor: string | null) =>
        this.remote.getDeltaPage({
          storeCode,
          baseCatalogVersion: active.catalogVersion,
          targetCatalogVersion: plan.targetCatalogVersion,
          cursor: requestCursor,
          pageSize: this.pageSize,
          downloadLeaseId: plan.downloadLeaseId,
          signal: input.signal,
        });

      while (true) {
        throwIfAborted(input.signal);
        let page: OfflineCatalogDeltaPage;
        if (inflight) {
          page = await inflight;
          inflight = null;
          cursor = pendingCursor;
        } else {
          page = await requestPage(cursor);
        }
        throwIfAborted(input.signal);
        assertPageContract(
          { ...page, totalCount: page.targetTotal, catalogVersion: page.targetCatalogVersion },
          { storeCode, requestedCursor: cursor },
        );
        if (page.targetCatalogVersion !== plan.targetCatalogVersion || page.targetTotal !== plan.targetTotal) {
          throw verification("Offline catalog delta target changed during paged download.", "OFFLINE_CATALOG_DELTA_TARGET_CHANGED");
        }
        assertUniqueKeys(page.items, seenUpserts);
        if (page.items.some((item) => seenDeletes.has(item.lookupKey))) {
          throw verification("Offline catalog delta contains conflicting upsert identities.", "OFFLINE_CATALOG_DELTA_INVALID");
        }
        for (const deleted of page.deletedItems) {
          if (deleted.storeCode !== storeCode || seenDeletes.has(deleted.lookupKey) || seenUpserts.has(deleted.lookupKey)) {
            throw verification("Offline catalog delta contains conflicting delete identities.", "OFFLINE_CATALOG_DELTA_INVALID");
          }
          seenDeletes.add(deleted.lookupKey);
        }
        const operationCount = page.items.length + page.deletedItems.length;
        if (completedOperations + operationCount > OFFLINE_CATALOG_DELTA_MAX_OPERATIONS) {
          await this.discardStaging(snapshotId);
          stagingStarted = false;
          return this.runFullWithFreshPlan(input, storeCode);
        }
        const nextCursor = page.nextCursor;
        if (nextCursor !== null && inflight === null) {
          if (seenCursors.has(nextCursor)) {
            throw verification("Offline catalog delta pagination cursor repeated.", "OFFLINE_CATALOG_CURSOR_REPEATED");
          }
          seenCursors.add(nextCursor);
          pendingCursor = nextCursor;
          inflight = requestPage(nextCursor);
          void inflight.catch(() => undefined);
        }
        const operations = [
          ...page.items.map((item) => ({ kind: "upsert" as const, key: item.lookupKey, item })),
          ...page.deletedItems.map((deleted) => ({ kind: "delete" as const, key: deleted.lookupKey, deleted })),
        ].sort((left, right) => (left.key < right.key ? -1 : left.key > right.key ? 1 : 0));
        for (const batch of chunkItems(operations, OFFLINE_CATALOG_DELTA_BATCH_SIZE)) {
          throwIfAborted(input.signal);
          const staging: OfflineCatalogDeltaStagingBatch = {
            items: batch.filter((operation) => operation.kind === "upsert").map((operation) => operation.item),
            deletedItems: batch.filter((operation) => operation.kind === "delete").map((operation) => operation.deleted),
          };
          await this.storage.appendDeltaBatch(snapshotId, staging);
          await this.yieldControl();
        }
        completedPageCount += 1;
        completedOperations += operationCount;
        const finalPage = page.nextCursor === null;
        if (finalPage && plan.deltaOperationCount !== null && completedOperations !== plan.deltaOperationCount) {
          throw verification("Offline catalog delta operation count does not match the sync plan.", "OFFLINE_CATALOG_DELTA_OPERATION_COUNT_MISMATCH");
        }
        report(input.onProgress, {
          step: "products",
          percent: finalPage ? 100 : Math.min(99, Math.max(1, completedPageCount)),
          completedItemCount: completedOperations,
          totalItemCount: plan.targetTotal,
          completedPageCount,
        });
        if (finalPage) {
          break;
        }
      }

      report(input.onProgress, { step: "activate", percent: 0 });
      throwIfAborted(input.signal);
      const metadata = await this.storage.activateDelta({
        sourceSnapshotId: active.snapshotId,
        baseCatalogVersion: active.catalogVersion,
        stagingSnapshotId: snapshotId,
        expectedItemCount: plan.targetTotal,
        activatedAtIso: this.nowIso(),
      });
      activated = true;
      report(input.onProgress, { step: "activate", percent: 100 });
      return { mode: "delta", metadata };
    } catch (error) {
      if (stagingStarted && !activated) {
        await this.discardStaging(snapshotId);
      }
      if (isDeltaFallback(error)) {
        // base/target 可能在 sync-plan 与首个 delta 页之间过期；清理后同次安全回退全量。
        return this.runFullWithFreshPlan(input, storeCode);
      }
      throw error;
    }
  }

  private async discardStaging(snapshotId: string): Promise<void> {
    try {
      await this.cleanupLoop((batch) => this.storage.discardStagingBatch(snapshotId, batch));
    } catch {
      // 清理失败不改变 active；下次刷新会再次尝试回收 staging。
    }
  }

  private async cleanupLoop(cleanup: (batchSize: number) => Promise<number>): Promise<void> {
    while (true) {
      const deleted = await cleanup(500);
      if (deleted <= 0) {
        return;
      }
      await this.yieldControl();
    }
  }
}

export function isOfflineCatalogCancellation(error: unknown): boolean {
  if (error instanceof OfflineCatalogError && error.code === "OFFLINE_CATALOG_CANCELLED") {
    return true;
  }
  // AbortSignal 触发的取消不一定由我们自己抛出：axios 抛 CanceledError（code
  // ERR_CANCELED），原生 fetch 抛 AbortError，两者都不是 OfflineCatalogError。
  // 只认自己的错误类型会让「用户取消」被当成失败，抑制自动重启的记账也就不会发生。
  const candidate = error as { code?: unknown; name?: unknown } | null;
  return (
    candidate?.code === "ERR_CANCELED" ||
    candidate?.name === "CanceledError" ||
    candidate?.name === "AbortError"
  );
}

function isDeltaFallback(error: unknown): boolean {
  if (error instanceof OfflineCatalogDeltaBaseChangedError) {
    return true;
  }
  const code = (error as { code?: unknown } | null)?.code;
  return code === "OFFLINE_CATALOG_SNAPSHOT_EXPIRED" || code === "OFFLINE_CATALOG_DELTA_BASE_CHANGED";
}

function verification(message: string, code: string): OfflineCatalogError {
  return new OfflineCatalogError(message, code);
}

function assertSyncPlan(plan: OfflineCatalogSyncPlan, activeVersion: string | null): void {
  if (plan.baseCatalogVersion !== activeVersion || !plan.targetCatalogVersion) {
    throw verification("Offline catalog sync plan is invalid.", "OFFLINE_CATALOG_SYNC_PLAN_INVALID");
  }
  if (
    (plan.mode === "noChange" && plan.targetCatalogVersion !== activeVersion) ||
    (plan.mode === "delta" && activeVersion === null)
  ) {
    throw verification("Offline catalog sync plan target is inconsistent.", "OFFLINE_CATALOG_SYNC_PLAN_INVALID");
  }
}

function assertPageContract(
  page: { storeCode: string; cursor: string | null; items: readonly OfflineCatalogItem[]; nextCursor: string | null; hasMore: boolean; totalCount: number; catalogVersion: string },
  expected: { storeCode: string; requestedCursor: string | null },
): void {
  if (page.storeCode !== expected.storeCode) {
    throw verification("Offline catalog page store does not match the requested store.", "OFFLINE_CATALOG_STORE_MISMATCH");
  }
  if (page.cursor !== expected.requestedCursor) {
    throw verification("Offline catalog page cursor does not match the requested cursor.", "OFFLINE_CATALOG_CURSOR_MISMATCH");
  }
  if (page.hasMore !== (page.nextCursor !== null)) {
    throw verification("Offline catalog page continuation fields are inconsistent.", "OFFLINE_CATALOG_PAGINATION_INVALID");
  }
  for (const item of page.items) {
    if (item.storeCode !== expected.storeCode) {
      throw verification("Offline catalog item store does not match the requested store.", "OFFLINE_CATALOG_ITEM_STORE_MISMATCH");
    }
  }
}

function assertUniqueKeys(items: readonly OfflineCatalogItem[], seen: Set<string>): void {
  for (const item of items) {
    if (seen.has(item.lookupKey)) {
      throw verification("Offline catalog snapshot contains a duplicate lookup key.", "OFFLINE_CATALOG_DUPLICATE_LOOKUP");
    }
    seen.add(item.lookupKey);
  }
}

function chunkItems<T>(items: readonly T[], batchSize: number): readonly (readonly T[])[] {
  const chunks: T[][] = [];
  for (let start = 0; start < items.length; start += batchSize) {
    chunks.push(items.slice(start, start + batchSize));
  }
  return chunks;
}

function throwIfAborted(signal: AbortSignal | undefined): void {
  if (signal?.aborted) {
    throw new OfflineCatalogError("Offline catalog refresh was cancelled.", "OFFLINE_CATALOG_CANCELLED");
  }
}

function isGatewayTimeout(error: unknown): boolean {
  const candidate = error as { status?: unknown; response?: { status?: unknown } } | null;
  return candidate?.status === 504 || candidate?.response?.status === 504;
}

function waitForRetry(delayMs: number, signal: AbortSignal | undefined): Promise<void> {
  throwIfAborted(signal);
  return new Promise((resolve, reject) => {
    const onAbort = () => {
      clearTimeout(timer);
      signal?.removeEventListener("abort", onAbort);
      reject(new OfflineCatalogError("Offline catalog refresh was cancelled.", "OFFLINE_CATALOG_CANCELLED"));
    };
    const timer = setTimeout(() => {
      signal?.removeEventListener("abort", onAbort);
      resolve();
    }, delayMs);
    signal?.addEventListener("abort", onAbort, { once: true });
    if (signal?.aborted) onAbort();
  });
}

function report(
  observer: OfflineCatalogRefreshRequest["onProgress"],
  event: OfflineCatalogRefreshProgressEvent,
): void {
  try {
    observer?.(Object.freeze({ ...event }));
  } catch {
    // 进度订阅方异常不得影响目录下载与激活。
  }
}

function yieldToEventLoop(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}
