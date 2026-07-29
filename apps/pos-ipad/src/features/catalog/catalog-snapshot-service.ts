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

export class CatalogSnapshotService {
  private readonly nowIso: () => string;
  private readonly pageSize: number;

  public constructor(
    private readonly storage: CatalogSnapshotStoragePort,
    private readonly remote: CatalogSyncRemotePort,
    private readonly options: CatalogSnapshotServiceOptions,
  ) {
    this.nowIso = options.nowIso ?? (() => new Date().toISOString());
    this.pageSize = options.pageSize ?? 500;
  }

  public async downloadAndActivate(input: Readonly<{ storeCode: string }>): Promise<Readonly<{ snapshotId: string; itemCount: number }>> {
    const snapshotId = this.options.createSnapshotId();
    let stagingStarted = false;
    try {
      const first = await this.remote.getPage({ storeCode: input.storeCode, cursor: null, pageSize: this.pageSize });
      assertPageContract(first, {
        requestedStoreCode: input.storeCode,
        requestedCursor: null,
      });
      const firstItems = first.items.map(mapCatalogLookupToStagedItem);
      await this.storage.beginStaging({
        snapshotId,
        catalogVersion: first.catalogVersion,
        checksum: first.pageChecksum,
        downloadedAtIso: this.nowIso(),
      });
      stagingStarted = true;

      const seenLookupKeys = new Set<string>();
      const seenCursors = new Set<string>();
      let count = 0;
      let page: VerifiedCatalogSyncPage | null = first;
      let stagedItems = firstItems;
      let requestedCursor: string | null = null;
      while (page) {
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
        await this.storage.appendPage(snapshotId, stagedItems);
        count += stagedItems.length;
        if (count > first.totalCount) {
          throw new Error("Catalog page count exceeds the server total.");
        }
        if (page.nextCursor === null) {
          page = null;
        } else {
          if (seenCursors.has(page.nextCursor)) {
            throw new Error("Catalog pagination cursor repeated.");
          }
          seenCursors.add(page.nextCursor);
          requestedCursor = page.nextCursor;
          page = await this.remote.getPage({
            storeCode: input.storeCode,
            cursor: requestedCursor,
            pageSize: this.pageSize,
          });
          stagedItems = page.items.map(mapCatalogLookupToStagedItem);
        }
      }
      if (count !== first.totalCount) {
        throw new Error("Catalog page count does not match the server total.");
      }

      const promotions = await this.remote.getPromotions?.({ storeCode: input.storeCode }) ?? [];
      await this.storage.replacePromotions(snapshotId, promotions);
      await this.storage.activate(snapshotId, count, this.nowIso());
      return { snapshotId, itemCount: count };
    } catch (error) {
      // 仅清理 staging；任何 active 快照都不在失败路径删除。
      if (stagingStarted) await this.storage.discardStaging(snapshotId);
      throw error;
    }
  }

  /** “重置目录”不会先清空 active：它只是强制重新下载并以同一安全切换流程替换。 */
  public resetAndRedownload(input: Readonly<{ storeCode: string }>): Promise<Readonly<{ snapshotId: string; itemCount: number }>> {
    return this.downloadAndActivate(input);
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
  if (!page.catalogVersion || !Number.isSafeInteger(page.totalCount) || page.totalCount < 0) {
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
