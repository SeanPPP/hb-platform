import type {
  CatalogRefreshProgressEvent,
  CatalogRefreshProgressObserver,
} from "./catalog-refresh-contract";
import type {
  CatalogLookupItem,
  VerifiedCatalogSyncPage,
} from "./hbpos-catalog-remote";

import { HbposApiError } from "@/core/api/hbpos-api";

/**
 * 已验证的 Hbpos lookup 行落库前必须显式转换为整数分。
 * 字段保持与 SQLCipher 仓储的输入结构一致，但 feature 不依赖具体 SQLite 实现。
 */
export type CatalogStagedItem = Readonly<{
  storeCode: string;
  productCode: string;
  referenceCode: string | null;
  itemNumber: string | null;
  displayName: string;
  barcode: string | null;
  lookupCode: string;
  lookupCodeNormalized: string;
  retailPriceCents: number;
  priceSource: 0 | 1 | 2 | 3 | 4;
  priceSourceLabel: string;
  quantityFactor: number;
  taxRateBasisPoints: number | null;
  updatedAtIso: string | null;
  rowVersion: string | null;
  productImage: string | null;
  discountRate: number | null;
  isSpecialProduct: boolean;
}>;

export type CatalogPromotion = Readonly<{
  promotionId: string;
  definitionJson: string;
  validFromIso: string | null;
  validUntilIso: string | null;
  priority: number;
}>;

/** 远端调用端必须只返回一个服务器固定快照；绝不由离线本地搜索伪造在线结果。 */
export interface CatalogSyncRemotePort {
  getPage(input: Readonly<{
    storeCode: string;
    cursor: string | null;
    pageSize: number;
    catalogVersion?: string;
    signal?: AbortSignal;
  }>): Promise<VerifiedCatalogSyncPage>;
  /** 当前 Hbpos lookup API 未返回促销定义；后端提供该合同时再由 adapter 实现。 */
  getPromotions?(input: Readonly<{
    storeCode: string;
    signal?: AbortSignal;
  }>): Promise<readonly CatalogPromotion[]>;
}

/** 由 SQLCipher 仓储实现；业务服务不接触裸 SQLite 或 SQL。 */
export interface CatalogSnapshotStoragePort {
  beginStaging(snapshot: Readonly<{ snapshotId: string; catalogVersion: string; checksum: string; downloadedAtIso: string }>): Promise<void>;
  appendPage(snapshotId: string, items: readonly CatalogStagedItem[]): Promise<void>;
  replacePromotions(snapshotId: string, promotions: readonly CatalogPromotion[]): Promise<void>;
  /** 实现必须验证完整数量后，在同一独占事务中 retire 旧 active 并激活 snapshot。 */
  activate(snapshotId: string, expectedItemCount: number, activatedAtIso: string): Promise<void>;
  discardStaging(snapshotId: string): Promise<void>;
}

export type CatalogSnapshotServiceOptions = Readonly<{
  createSnapshotId: () => string;
  nowIso?: () => string;
  nowMilliseconds?: () => number;
  pageSize?: number;
}>;

export type CatalogRefreshRequest = Readonly<{
  storeCode: string;
  onProgress?: CatalogRefreshProgressObserver | undefined;
  signal?: AbortSignal | undefined;
  /** 在写入 active 前由组合根重新核验可信会话，避免下载期间权限漂移后仍切换目录。 */
  beforeActivate?: (() => void | Promise<void>) | undefined;
  /**
   * active 已提交后，在本服务的串行临界区内重载同一快照的运行时依赖。
   * 调用方必须自行把可恢复告警收敛为结果；这里不会把它当作“切换前失败”。
   */
  afterActivate?: ((result: CatalogActivationResult) => void | Promise<void>) | undefined;
}>;

export type CatalogActivationResult = Readonly<{
  snapshotId: string;
  catalogVersion: string;
  itemCount: number;
  activatedAt: string;
}>;

export type CatalogSnapshotFailureContext = Readonly<{
  code: string;
  pageNumber: number;
  completedItemCount: number;
  totalItemCount?: number;
  httpStatus?: number;
}>;

/** 只携带可写入设备日志的分页坐标，不保留远端正文或商品身份。 */
export class CatalogSnapshotFailure extends HbposApiError {
  public constructor(public readonly context: CatalogSnapshotFailureContext) {
    super(context.code, {
      kind: "envelope",
      code: context.code,
      ...(context.httpStatus === undefined
        ? {}
        : { status: context.httpStatus }),
    });
    this.name = "CatalogSnapshotFailure";
  }
}

export class CatalogSnapshotService {
  private readonly nowIso: () => string;
  private readonly nowMilliseconds: () => number;
  private readonly pageSize: number;
  private serial = Promise.resolve();

  public constructor(
    private readonly storage: CatalogSnapshotStoragePort,
    private readonly remote: CatalogSyncRemotePort,
    private readonly options: CatalogSnapshotServiceOptions,
  ) {
    this.nowIso = options.nowIso ?? (() => new Date().toISOString());
    this.nowMilliseconds = options.nowMilliseconds ?? (() => Date.now());
    this.pageSize = options.pageSize ?? 5_000;
  }

  public downloadAndActivate(input: CatalogRefreshRequest): Promise<CatalogActivationResult> {
    const operation = this.serial.then(
      () => this.runDownloadAndActivate(input),
      () => this.runDownloadAndActivate(input),
    );
    // 中文注释：失败只影响本次请求；后续刷新仍须排队而不能并发切换 active。
    this.serial = operation.then(() => undefined, () => undefined);
    return operation;
  }

  private async runDownloadAndActivate(input: CatalogRefreshRequest): Promise<CatalogActivationResult> {
    const startedAtMilliseconds = this.nowMilliseconds();
    const progress = (
      event: Omit<CatalogRefreshProgressEvent, "elapsedMilliseconds">,
    ): void => {
      reportProgress(input.onProgress, {
        ...event,
        elapsedMilliseconds: Math.max(
          0,
          this.nowMilliseconds() - startedAtMilliseconds,
        ),
      });
    };
    const snapshotId = this.options.createSnapshotId();
    let stagingStarted = false;
    let activated = false;
    let pageNumber = 1;
    let completedItemCount = 0;
    let totalItemCount: number | undefined;
    try {
      throwIfAborted(input.signal);
      progress({ step: "prepare", percent: 0 });
      throwIfAborted(input.signal);
      const first = await this.remote.getPage({
        storeCode: input.storeCode,
        cursor: null,
        pageSize: this.pageSize,
        ...(input.signal ? { signal: input.signal } : {}),
      });
      throwIfAborted(input.signal);
      assertPageContract(first, {
        requestedStoreCode: input.storeCode,
        requestedCursor: null,
      });
      totalItemCount = first.totalCount;
      const firstItems = first.items.map(mapCatalogLookupToStagedItem);
      throwIfAborted(input.signal);
      await this.storage.beginStaging({
        snapshotId,
        catalogVersion: first.catalogVersion,
        checksum: first.pageChecksum,
        downloadedAtIso: this.nowIso(),
      });
      stagingStarted = true;
      // 中文注释：begin 成功后必须先拥有清理权，即使此刻取消也只能丢弃 staging。
      throwIfAborted(input.signal);
      const totalPageCount = Math.max(
        1,
        Math.ceil(first.totalCount / this.pageSize),
      );
      progress({ step: "prepare", percent: 100 });
      progress({
        step: "products",
        percent: 0,
        completedItemCount: 0,
        totalItemCount: first.totalCount,
        completedPageCount: 0,
        totalPageCount,
      });

      const seenLookupKeys = new Set<string>();
      const seenCursors = new Set<string>();
      let count = 0;
      let completedPageCount = 0;
      let page: VerifiedCatalogSyncPage | null = first;
      let stagedItems = firstItems;
      let requestedCursor: string | null = null;
      while (page) {
        throwIfAborted(input.signal);
        assertPageContract(page, {
          requestedStoreCode: input.storeCode,
          requestedCursor,
        });
        if (page.catalogVersion !== first.catalogVersion) {
          throw catalogVerificationError(
            "Catalog snapshot version changed during paged download.",
            "CATALOG_SNAPSHOT_VERSION_CHANGED",
          );
        }
        if (page.totalCount !== first.totalCount) {
          throw catalogVerificationError(
            "Catalog total changed during paged download.",
            "CATALOG_SNAPSHOT_TOTAL_CHANGED",
          );
        }
        assertUniqueLookupKeys(stagedItems, seenLookupKeys);
        const finalPage = page.nextCursor === null;
        if (!finalPage && first.totalCount === 0) {
          throw catalogVerificationError(
            "Catalog pagination cannot continue after an empty total.",
            "CATALOG_PAGINATION_INVALID",
          );
        }

        const localBatches = chunkItems(stagedItems, 500);
        for (const [batchIndex, batch] of localBatches.entries()) {
          throwIfAborted(input.signal);
          await this.storage.appendPage(snapshotId, batch);
          throwIfAborted(input.signal);
          count += batch.length;
          completedItemCount = count;
          if (count > first.totalCount) {
            throw catalogVerificationError(
              "Catalog page count exceeds the server total.",
              "CATALOG_ITEM_COUNT_MISMATCH",
            );
          }
          const serverPageCompleted = batchIndex === localBatches.length - 1;
          if (finalPage && serverPageCompleted && count !== first.totalCount) {
            throw catalogVerificationError(
              "Catalog page count does not match the server total.",
              "CATALOG_ITEM_COUNT_MISMATCH",
            );
          }
          progress({
            step: "products",
            // 中文注释：只有最终服务端页已完整分批落库后才能报告 100%。
            percent: finalPage && serverPageCompleted
              ? 100
              : Math.min(99, Math.floor((count / first.totalCount) * 100)),
            completedItemCount: count,
            totalItemCount: first.totalCount,
            completedPageCount:
              completedPageCount + (serverPageCompleted ? 1 : 0),
            totalPageCount,
          });
        }
        completedPageCount += 1;
        if (localBatches.length === 0) {
          progress({
            step: "products",
            percent: finalPage ? 100 : 0,
            completedItemCount: count,
            totalItemCount: first.totalCount,
            completedPageCount,
            totalPageCount,
          });
        }
        if (finalPage && count !== first.totalCount) {
          throw catalogVerificationError(
            "Catalog page count does not match the server total.",
            "CATALOG_ITEM_COUNT_MISMATCH",
          );
        }
        if (finalPage) {
          page = null;
        } else {
          const nextCursor = page.nextCursor;
          if (nextCursor === null) {
            throw catalogVerificationError(
              "Catalog pagination cursor is missing.",
              "CATALOG_PAGINATION_INVALID",
            );
          }
          if (seenCursors.has(nextCursor)) {
            throw catalogVerificationError(
              "Catalog pagination cursor repeated.",
              "CATALOG_CURSOR_REPEATED",
            );
          }
          seenCursors.add(nextCursor);
          requestedCursor = nextCursor;
          pageNumber = completedPageCount + 1;
          page = await this.remote.getPage({
            storeCode: input.storeCode,
            cursor: requestedCursor,
            pageSize: this.pageSize,
            catalogVersion: first.catalogVersion,
            ...(input.signal ? { signal: input.signal } : {}),
          });
          throwIfAborted(input.signal);
          stagedItems = page.items.map(mapCatalogLookupToStagedItem);
        }
      }
      if (count !== first.totalCount) {
        throw catalogVerificationError(
          "Catalog page count does not match the server total.",
          "CATALOG_ITEM_COUNT_MISMATCH",
        );
      }

      progress({ step: "promotions", percent: 0 });
      throwIfAborted(input.signal);
      const promotions = await this.remote.getPromotions?.({
        storeCode: input.storeCode,
        ...(input.signal ? { signal: input.signal } : {}),
      }) ?? [];
      throwIfAborted(input.signal);
      await this.storage.replacePromotions(snapshotId, promotions);
      throwIfAborted(input.signal);
      progress({ step: "promotions", percent: 100 });
      progress({ step: "activate", percent: 0 });
      throwIfAborted(input.signal);
      await input.beforeActivate?.();
      throwIfAborted(input.signal);
      const activatedAt = this.nowIso();
      await this.storage.activate(snapshotId, count, activatedAt);
      activated = true;
      const result: CatalogActivationResult = {
        snapshotId,
        catalogVersion: first.catalogVersion,
        itemCount: count,
        activatedAt,
      };
      await input.afterActivate?.(result);
      // 中文注释：100% 表示 active 与同快照运行时依赖均已完成收口。
      progress({ step: "activate", percent: 100 });
      return result;
    } catch (error) {
      // 中文注释：active 已提交后，后置重载异常绝不能回到“切换前失败”的清理路径。
      if (stagingStarted && !activated) await this.storage.discardStaging(snapshotId);
      throw contextualizeCatalogFailure(error, {
        pageNumber,
        completedItemCount,
        ...(totalItemCount === undefined ? {} : { totalItemCount }),
      });
    }
  }

  /** “重置目录”不会先清空 active：它只是强制重新下载并以同一安全切换流程替换。 */
  public resetAndRedownload(input: CatalogRefreshRequest): Promise<CatalogActivationResult> {
    return this.downloadAndActivate(input);
  }
}

function contextualizeCatalogFailure(
  error: unknown,
  context: Omit<CatalogSnapshotFailureContext, "code" | "httpStatus">,
): unknown {
  if (error instanceof CatalogSnapshotFailure) return error;
  const candidate = error as Readonly<{
    code?: unknown;
    status?: unknown;
  }> | null;
  if (
    typeof candidate?.code !== "string"
    || !candidate.code.startsWith("CATALOG_")
  ) {
    return error;
  }
  return new CatalogSnapshotFailure({
    code: candidate.code,
    ...context,
    ...(typeof candidate.status === "number"
      ? { httpStatus: candidate.status }
      : {}),
  });
}

function chunkItems<T>(items: readonly T[], batchSize: number): readonly (readonly T[])[] {
  const batches: T[][] = [];
  for (let start = 0; start < items.length; start += batchSize) {
    batches.push(items.slice(start, start + batchSize));
  }
  return batches;
}

function throwIfAborted(signal: AbortSignal | undefined): void {
  if (signal?.aborted) {
    throw new Error("Catalog refresh was cancelled.");
  }
}

/** 进度仅供展示；观察器自身故障绝不能中断已校验目录的安全切换。 */
function reportProgress(
  observer: CatalogRefreshProgressObserver | undefined,
  event: CatalogRefreshProgressEvent,
): void {
  try {
    observer?.(Object.freeze({ ...event }));
  } catch {
    // 中文注释：UI 销毁或订阅方异常不得影响目录下载与激活。
  }
}

export function mapCatalogLookupToStagedItem(item: CatalogLookupItem): CatalogStagedItem {
  return {
    storeCode: item.storeCode,
    productCode: item.productCode,
    referenceCode: item.referenceCode,
    itemNumber: item.itemNumber,
    displayName: item.displayName,
    barcode: item.barcode,
    lookupCode: item.lookupCode,
    lookupCodeNormalized: item.lookupCodeNormalized,
    retailPriceCents: toIntegerCents(item.retailPrice),
    priceSource: item.priceSource,
    priceSourceLabel: item.priceSourceLabel,
    quantityFactor: item.quantityFactor,
    // 当前 Hbpos CatalogLookupItemDto 没有税率字段；定价层按既有 GST 规则计算。
    taxRateBasisPoints: null,
    updatedAtIso: item.updatedAt,
    rowVersion: item.rowVersion,
    productImage: item.productImage,
    discountRate: item.discountRate,
    isSpecialProduct: item.isSpecialProduct,
  };
}

function assertPageContract(
  page: VerifiedCatalogSyncPage,
  expected: Readonly<{ requestedStoreCode: string; requestedCursor: string | null }>,
): void {
  if (!isCatalogVersion(page.catalogVersion)) {
    throw catalogVerificationError(
      "Catalog page is missing a valid snapshot version.",
      "CATALOG_SNAPSHOT_VERSION_INVALID",
    );
  }
  if (!Number.isSafeInteger(page.totalCount) || page.totalCount < 0) {
    throw catalogVerificationError(
      "Catalog page is missing a valid total.",
      "CATALOG_SNAPSHOT_TOTAL_INVALID",
    );
  }
  if (page.storeCode !== expected.requestedStoreCode) {
    throw catalogVerificationError(
      "Catalog page store does not match the requested store.",
      "CATALOG_STORE_MISMATCH",
    );
  }
  if (page.cursor !== expected.requestedCursor) {
    throw catalogVerificationError(
      "Catalog page cursor does not match the requested cursor.",
      "CATALOG_CURSOR_MISMATCH",
    );
  }
  if (page.hasMore !== (page.nextCursor !== null)) {
    throw catalogVerificationError(
      "Catalog page continuation fields are inconsistent.",
      "CATALOG_PAGINATION_INVALID",
    );
  }
  for (const item of page.items) {
    if (item.storeCode !== expected.requestedStoreCode) {
      throw catalogVerificationError(
        "Catalog item store does not match the requested store.",
        "CATALOG_ITEM_STORE_MISMATCH",
      );
    }
  }
}

function isCatalogVersion(value: unknown): value is string {
  return typeof value === "string" &&
    value.length > 0 &&
    value.trim() === value &&
    value.length <= 512 &&
    !/[\u0000-\u001f\u007f]/u.test(value);
}

function assertUniqueLookupKeys(items: readonly CatalogStagedItem[], seen: Set<string>): void {
  for (const item of items) {
    const key = `${item.storeCode}\u0000${item.lookupCodeNormalized}`;
    if (seen.has(key)) {
      throw catalogVerificationError(
        "Catalog snapshot contains a duplicate lookup code.",
        "CATALOG_DUPLICATE_LOOKUP",
      );
    }
    seen.add(key);
  }
}

function toIntegerCents(value: number): number {
  if (!Number.isFinite(value) || value < 0) {
    throw catalogVerificationError(
      "Catalog retail price must be a non-negative finite number.",
      "CATALOG_PRICE_INVALID",
    );
  }
  const scaled = value * 100;
  const rounded = Math.round(scaled);
  const tolerance = Number.EPSILON * Math.max(1, Math.abs(scaled)) * 8;
  if (!Number.isSafeInteger(rounded) || Math.abs(scaled - rounded) > tolerance) {
    throw catalogVerificationError(
      "Catalog retail price cannot be represented as integer cents.",
      "CATALOG_PRICE_INVALID",
    );
  }
  return rounded;
}

function catalogVerificationError(message: string, code: string): HbposApiError {
  return new HbposApiError(message, {
    kind: "envelope",
    code,
  });
}
