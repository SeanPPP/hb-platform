import type {
  CatalogRefreshProgressEvent,
  CatalogRefreshProgressObserver,
} from "./catalog-refresh-contract";
import type {
  CatalogLookupItem,
  VerifiedCatalogSyncPage,
} from "./hbpos-catalog-remote";

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
  getPage(input: Readonly<{ storeCode: string; cursor: string | null; pageSize: number }>): Promise<VerifiedCatalogSyncPage>;
  /** 当前 Hbpos lookup API 未返回促销定义；后端提供该合同时再由 adapter 实现。 */
  getPromotions?(input: Readonly<{ storeCode: string }>): Promise<readonly CatalogPromotion[]>;
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

export class CatalogSnapshotService {
  private readonly nowIso: () => string;
  private readonly pageSize: number;
  private serial = Promise.resolve();

  public constructor(
    private readonly storage: CatalogSnapshotStoragePort,
    private readonly remote: CatalogSyncRemotePort,
    private readonly options: CatalogSnapshotServiceOptions,
  ) {
    this.nowIso = options.nowIso ?? (() => new Date().toISOString());
    this.pageSize = options.pageSize ?? 500;
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
    const snapshotId = this.options.createSnapshotId();
    let stagingStarted = false;
    let activated = false;
    try {
      throwIfAborted(input.signal);
      reportProgress(input.onProgress, { step: "prepare", percent: 0 });
      throwIfAborted(input.signal);
      const first = await this.remote.getPage({ storeCode: input.storeCode, cursor: null, pageSize: this.pageSize });
      throwIfAborted(input.signal);
      assertPageContract(first, {
        requestedStoreCode: input.storeCode,
        requestedCursor: null,
      });
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
      reportProgress(input.onProgress, { step: "prepare", percent: 100 });
      reportProgress(input.onProgress, {
        step: "products",
        percent: 0,
        completedItemCount: 0,
        totalItemCount: first.totalCount,
      });

      const seenLookupKeys = new Set<string>();
      const seenCursors = new Set<string>();
      let count = 0;
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
          throw new Error("Catalog snapshot version changed during paged download.");
        }
        if (page.totalCount !== first.totalCount) {
          throw new Error("Catalog total changed during paged download.");
        }
        assertUniqueLookupKeys(stagedItems, seenLookupKeys);
        throwIfAborted(input.signal);
        await this.storage.appendPage(snapshotId, stagedItems);
        throwIfAborted(input.signal);
        count += stagedItems.length;
        if (count > first.totalCount) {
          throw new Error("Catalog page count exceeds the server total.");
        }
        const finalPage = page.nextCursor === null;
        if (!finalPage && first.totalCount === 0) {
          throw new Error("Catalog pagination cannot continue after an empty total.");
        }
        if (finalPage && count !== first.totalCount) {
          throw new Error("Catalog page count does not match the server total.");
        }
        reportProgress(input.onProgress, {
          step: "products",
          // 中文注释：只要服务器声明还有下一页，进度最多 99%，即使已写入数暂时等于 total。
          percent: finalPage
            ? 100
            : Math.min(99, Math.floor((count / first.totalCount) * 100)),
          completedItemCount: count,
          totalItemCount: first.totalCount,
        });
        if (finalPage) {
          page = null;
        } else {
          const nextCursor = page.nextCursor;
          if (nextCursor === null) {
            throw new Error("Catalog pagination cursor is missing.");
          }
          if (seenCursors.has(nextCursor)) {
            throw new Error("Catalog pagination cursor repeated.");
          }
          seenCursors.add(nextCursor);
          requestedCursor = nextCursor;
          page = await this.remote.getPage({
            storeCode: input.storeCode,
            cursor: requestedCursor,
            pageSize: this.pageSize,
          });
          throwIfAborted(input.signal);
          stagedItems = page.items.map(mapCatalogLookupToStagedItem);
        }
      }
      if (count !== first.totalCount) {
        throw new Error("Catalog page count does not match the server total.");
      }

      reportProgress(input.onProgress, { step: "promotions", percent: 0 });
      throwIfAborted(input.signal);
      const promotions = await this.remote.getPromotions?.({ storeCode: input.storeCode }) ?? [];
      throwIfAborted(input.signal);
      await this.storage.replacePromotions(snapshotId, promotions);
      throwIfAborted(input.signal);
      reportProgress(input.onProgress, { step: "promotions", percent: 100 });
      reportProgress(input.onProgress, { step: "activate", percent: 0 });
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
      reportProgress(input.onProgress, { step: "activate", percent: 100 });
      return result;
    } catch (error) {
      // 中文注释：active 已提交后，后置重载异常绝不能回到“切换前失败”的清理路径。
      if (stagingStarted && !activated) await this.storage.discardStaging(snapshotId);
      throw error;
    }
  }

  /** “重置目录”不会先清空 active：它只是强制重新下载并以同一安全切换流程替换。 */
  public resetAndRedownload(input: CatalogRefreshRequest): Promise<CatalogActivationResult> {
    return this.downloadAndActivate(input);
  }
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
  if (!isCatalogVersion(page.catalogVersion) || !Number.isSafeInteger(page.totalCount) || page.totalCount < 0) {
    throw new Error("Catalog page is missing a valid snapshot version or total.");
  }
  if (page.storeCode !== expected.requestedStoreCode) {
    throw new Error("Catalog page store does not match the requested store.");
  }
  if (page.cursor !== expected.requestedCursor) {
    throw new Error("Catalog page cursor does not match the requested cursor.");
  }
  if (page.hasMore !== (page.nextCursor !== null)) {
    throw new Error("Catalog page continuation fields are inconsistent.");
  }
  for (const item of page.items) {
    if (item.storeCode !== expected.requestedStoreCode) {
      throw new Error("Catalog item store does not match the requested store.");
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
      throw new Error(`Duplicate lookup code in catalog snapshot: ${item.lookupCodeNormalized}`);
    }
    seen.add(key);
  }
}

function toIntegerCents(value: number): number {
  if (!Number.isFinite(value) || value < 0) {
    throw new Error("Catalog retail price must be a non-negative finite number.");
  }
  const scaled = value * 100;
  const rounded = Math.round(scaled);
  const tolerance = Number.EPSILON * Math.max(1, Math.abs(scaled)) * 8;
  if (!Number.isSafeInteger(rounded) || Math.abs(scaled - rounded) > tolerance) {
    throw new Error("Catalog retail price cannot be represented as integer cents.");
  }
  return rounded;
}
