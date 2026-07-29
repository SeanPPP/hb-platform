import type { SqliteConnectionPort } from "./types";

/** 目录持久化模型只保存收银必需字段；顾客资料和支付资料不属于目录。 */
export type CatalogStoredItem = Readonly<{
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
  /** 以字符串存储十进制，避免 SQLite REAL 改写售卖数量系数。 */
  quantityFactor: number;
  taxRateBasisPoints: number | null;
  updatedAtIso: string | null;
  rowVersion: string | null;
  productImage: string | null;
  discountRate: number | null;
  isSpecialProduct: boolean;
}>;

export type CatalogStoredPromotion = Readonly<{
  promotionId: string;
  definitionJson: string;
  validFromIso: string | null;
  validUntilIso: string | null;
  priority: number;
}>;

export type LocalCatalogMatch = Readonly<{
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

export type ActiveCatalogMetadata = Readonly<{
  snapshotId: string;
  itemCount: number;
  activatedAt: string;
}>;

/** 仅供定价引擎加载的 active 目录促销投影，绝不暴露裸 SQLite 行。 */
export type ActiveCatalogPromotions = Readonly<{
  snapshotId: string;
  storeCode: string;
  promotions: readonly Readonly<{
    promotionId: string;
    definitionJson: string;
  }>[];
}>;

/**
 * SQLCipher 目录仓储。active 与 staging 共存，任何失败路径均不删除 active。
 * 该类不执行网络请求，在线 fallback 必须由 catalog feature 显式调用远端 Port。
 */
export class SqliteCatalogSnapshotRepository {
  public constructor(private readonly db: SqliteConnectionPort) {}

  /**
   * 单条查询读取全部快照状态并验证 active 唯一性；约束被破坏时也不能任选一行。
   */
  public async getActiveMetadata(): Promise<ActiveCatalogMetadata | null> {
    const rows = await this.db.getAll<ActiveCatalogMetadataRow>(
      `SELECT
         snapshots.snapshot_id,
         snapshots.state,
         snapshots.activated_at_iso,
         COUNT(items.lookup_code_normalized) AS item_count
       FROM catalog_snapshots snapshots
       LEFT JOIN catalog_items items
         ON items.snapshot_id = snapshots.snapshot_id
       GROUP BY
         snapshots.snapshot_id,
         snapshots.state,
         snapshots.activated_at_iso
       ORDER BY snapshots.snapshot_id`,
    );
    const active: ActiveCatalogMetadata[] = [];
    for (const row of rows) {
      const snapshotId = requiredCatalogSnapshotId(row.snapshot_id);
      const state = catalogSnapshotState(row.state);
      const itemCount = requiredNonNegativeInteger(
        row.item_count,
        "catalog item count",
      );
      const activatedAt =
        row.activated_at_iso === null
          ? null
          : requiredCanonicalIso(
              row.activated_at_iso,
              "catalog activation timestamp",
            );
      if (state === "staging" && activatedAt !== null) {
        throw new Error("Invalid catalog snapshot activation state.");
      }
      if (state !== "staging") {
        if (activatedAt === null) {
          throw new Error("Invalid catalog snapshot activation state.");
        }
        if (state === "active") {
          active.push({
            snapshotId,
            itemCount,
            activatedAt,
          });
        }
      }
    }
    if (active.length > 1) {
      throw new Error("Multiple active catalog snapshots detected.");
    }
    return active[0] ?? null;
  }

  /**
   * 促销没有独立门店列，故必须先由同一 active 快照中当前门店的 active 商品证明归属。
   * 即使唯一 active 索引被损坏，也绝不任选一个快照或跨店规则参与本地定价。
   */
  public async loadActivePromotions(
    storeCode: string,
  ): Promise<ActiveCatalogPromotions | null> {
    const requestedStoreCode = requiredCatalogStoreCode(storeCode);
    const scopeRows = await this.db.getAll<ActivePromotionScopeRow>(
      `SELECT
         snapshots.snapshot_id,
         CASE WHEN EXISTS (
           SELECT 1
           FROM catalog_items items
           WHERE items.snapshot_id = snapshots.snapshot_id
             AND items.store_code = ?
             AND items.is_active = 1
         ) THEN ? ELSE NULL END AS store_code
       FROM catalog_snapshots snapshots
       WHERE snapshots.state = 'active'
       ORDER BY snapshots.snapshot_id ASC`,
      [requestedStoreCode, requestedStoreCode],
    );
    if (scopeRows.length === 0) return null;
    if (scopeRows.length > 1) {
      throw new Error("Multiple active catalog snapshots detected.");
    }

    const scope = scopeRows[0];
    if (!scope) throw new Error("Invalid active catalog snapshot scope.");
    const snapshotId = requiredCatalogSnapshotId(scope.snapshot_id);
    if (scope.store_code === null || scope.store_code === undefined) return null;
    if (requiredCatalogStoreCode(scope.store_code) !== requestedStoreCode) {
      throw new Error("Active catalog snapshot store does not match the requested store.");
    }

    const rows = await this.db.getAll<ActivePromotionRow>(
      `SELECT
         snapshot_id,
         promotion_id,
         definition_json,
         priority
       FROM catalog_promotions
       WHERE snapshot_id = ?
       ORDER BY priority ASC, promotion_id ASC`,
      [snapshotId],
    );
    const promotionIds = new Set<string>();
    const promotions = rows.map((row) => {
      if (requiredCatalogSnapshotId(row.snapshot_id) !== snapshotId) {
        throw new Error("Promotion row belongs to another catalog snapshot.");
      }
      const promotionId = requiredPromotionId(row.promotion_id);
      if (promotionIds.has(promotionId)) {
        throw new Error("Duplicate promotion id in active catalog snapshot.");
      }
      promotionIds.add(promotionId);
      // 中文注释：priority 虽不下传 UI，但先验证以避免损坏排序掩盖部分促销行。
      requiredSafeInteger(row.priority, "catalog promotion priority");
      return {
        promotionId,
        definitionJson: requiredPromotionDefinitionJson(row.definition_json),
      };
    });
    return {
      snapshotId,
      storeCode: requestedStoreCode,
      promotions,
    };
  }

  public async beginStaging(snapshot: Readonly<{ snapshotId: string; catalogVersion: string; checksum: string; downloadedAtIso: string }>): Promise<void> {
    await this.db.withExclusiveTransaction(async (tx) => {
      // 相同 snapshotId 只能是可恢复的 staging；绝不覆盖任何 active/retired 目录。
      const existing = await tx.getFirst<{ state: string }>("SELECT state FROM catalog_snapshots WHERE snapshot_id = ?", [snapshot.snapshotId]);
      if (existing && existing.state !== "staging") throw new Error("Catalog snapshot id collision with a retained snapshot.");
      await deleteSnapshot(tx, snapshot.snapshotId, true);
      await tx.run(
        `INSERT INTO catalog_snapshots (snapshot_id, catalog_version, checksum, state, downloaded_at_iso, activated_at_iso)
         VALUES (?, ?, ?, 'staging', ?, NULL)`,
        [snapshot.snapshotId, snapshot.catalogVersion, snapshot.checksum, snapshot.downloadedAtIso],
      );
    });
  }

  public async appendPage(snapshotId: string, items: readonly CatalogStoredItem[]): Promise<void> {
    await this.db.withExclusiveTransaction(async (tx) => {
      for (const item of items) {
        assertStoredItem(item);
        await tx.run(
          `INSERT INTO catalog_items (
             snapshot_id, store_code, lookup_code_normalized, product_code, reference_code,
             item_number, barcode, lookup_code, display_name, retail_price_cents,
             price_source, price_source_label, quantity_factor, tax_rate_basis_points,
             row_version, product_image, discount_rate, is_special_product,
             is_active, updated_at_iso
           ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 1, ?)`,
          [
            snapshotId,
            item.storeCode,
            item.lookupCodeNormalized,
            item.productCode,
            item.referenceCode,
            item.itemNumber,
            item.barcode,
            item.lookupCode,
            item.displayName,
            item.retailPriceCents,
            item.priceSource,
            item.priceSourceLabel,
            formatStoredDecimal(item.quantityFactor),
            item.taxRateBasisPoints,
            item.rowVersion,
            item.productImage,
            item.discountRate === null ? null : formatStoredDecimal(item.discountRate),
            item.isSpecialProduct ? 1 : 0,
            item.updatedAtIso,
          ],
        );
        if (item.isSpecialProduct) {
          await tx.run(
            `INSERT INTO special_products (
               snapshot_id, store_code, lookup_code_normalized, sort_order, is_marked, updated_at_iso
             ) VALUES (?, ?, ?, 0, 1, ?)`,
            [snapshotId, item.storeCode, item.lookupCodeNormalized, item.updatedAtIso],
          );
        }
      }
    });
  }

  public async replacePromotions(snapshotId: string, promotions: readonly CatalogStoredPromotion[]): Promise<void> {
    await this.db.withExclusiveTransaction(async (tx) => {
      await tx.run("DELETE FROM catalog_promotions WHERE snapshot_id = ?", [snapshotId]);
      for (const promotion of promotions) {
        await tx.run(
          `INSERT INTO catalog_promotions (snapshot_id, promotion_id, definition_json, valid_from_iso, valid_until_iso, priority)
           VALUES (?, ?, ?, ?, ?, ?)`,
          [snapshotId, promotion.promotionId, promotion.definitionJson, promotion.validFromIso, promotion.validUntilIso, promotion.priority],
        );
      }
    });
  }

  public async activate(snapshotId: string, expectedItemCount: number, activatedAtIso: string): Promise<void> {
    await this.db.withExclusiveTransaction(async (tx) => {
      const row = await tx.getFirst<{ item_count: number | string }>(
        "SELECT COUNT(*) AS item_count FROM catalog_items WHERE snapshot_id = ?",
        [snapshotId],
      );
      if (Number(row?.item_count) !== expectedItemCount) throw new Error("Catalog staging count verification failed.");
      const staging = await tx.getFirst<{ state: string }>("SELECT state FROM catalog_snapshots WHERE snapshot_id = ?", [snapshotId]);
      if (staging?.state !== "staging") throw new Error("Catalog snapshot is not eligible for activation.");
      // 该事务中任一步失败会回滚，故旧 active 不会出现被退役却没有新 active 的窗口。
      await tx.run("UPDATE catalog_snapshots SET state = 'retired' WHERE state = 'active'");
      const result = await tx.run(
        "UPDATE catalog_snapshots SET state = 'active', activated_at_iso = ? WHERE snapshot_id = ? AND state = 'staging'",
        [activatedAtIso, snapshotId],
      );
      if (result.changes !== 1) throw new Error("Catalog snapshot activation was lost.");
    });
  }

  public async discardStaging(snapshotId: string): Promise<void> {
    await this.db.withExclusiveTransaction((tx) => deleteSnapshot(tx, snapshotId, true));
  }

  /**
   * 服务器已把条码、货号、套装和清仓码统一为规范化售卖码；因此不会因同一商品多条售价记录而 join 出错误价格。
   * 离线查询只读 active 快照，绝不会伪装成远端查询。
   */
  public async findExact(lookupCode: string): Promise<LocalCatalogMatch | null> {
    const normalized = normalizeLookupCode(lookupCode);
    if (!normalized) return null;
    const row = await this.db.getFirst<CatalogRow>(activeItemSql("i.lookup_code_normalized = ?"), [normalized]);
    return row ? mapMatch(row) : null;
  }

  /** 名称、商品号、货号和实际售卖码共用稳定分页，避免同名多行在翻页时漂移。 */
  public async searchByName(query: string, limit: number, offset = 0): Promise<readonly LocalCatalogMatch[]> {
    if (!Number.isSafeInteger(limit) || limit <= 0 || !Number.isSafeInteger(offset) || offset < 0) throw new Error("Invalid catalog search page.");
    const pattern = `%${escapeLike(query.trim())}%`;
    const rows = await this.db.getAll<CatalogRow>(
      `${activeItemSql("(i.display_name LIKE ? ESCAPE '\\' OR i.product_code LIKE ? ESCAPE '\\' OR COALESCE(i.item_number, '') LIKE ? ESCAPE '\\' OR i.lookup_code LIKE ? ESCAPE '\\')")}
       ORDER BY i.display_name COLLATE NOCASE ASC, COALESCE(i.item_number, '') COLLATE NOCASE ASC, i.lookup_code_normalized ASC LIMIT ? OFFSET ?`,
      [pattern, pattern, pattern, pattern, limit, offset],
    );
    return rows.map(mapMatch);
  }
}

type CatalogRow = Record<string, unknown>;
type ActiveCatalogMetadataRow = Readonly<{
  snapshot_id: unknown;
  state: unknown;
  activated_at_iso: unknown;
  item_count: unknown;
}>;
type ActivePromotionScopeRow = Readonly<{
  snapshot_id: unknown;
  store_code: unknown;
}>;
type ActivePromotionRow = Readonly<{
  snapshot_id: unknown;
  promotion_id: unknown;
  definition_json: unknown;
  priority: unknown;
}>;

function activeItemSql(where: string): string {
  return `SELECT
      i.store_code, i.product_code, i.reference_code, i.item_number, i.display_name,
      i.barcode, i.lookup_code, i.lookup_code_normalized, i.retail_price_cents AS price_cents,
      i.price_source, i.price_source_label, i.quantity_factor, i.tax_rate_basis_points,
      i.updated_at_iso, i.row_version, i.product_image, i.discount_rate, i.is_special_product
    FROM catalog_snapshots s
    JOIN catalog_items i ON i.snapshot_id = s.snapshot_id
    WHERE s.state = 'active' AND i.is_active = 1 AND (${where})`;
}

async function deleteSnapshot(tx: SqliteConnectionPort, snapshotId: string, onlyStaging = false): Promise<void> {
  const guard = onlyStaging ? " AND state = 'staging'" : "";
  const snapshot = await tx.getFirst<{ snapshot_id: string }>(`SELECT snapshot_id FROM catalog_snapshots WHERE snapshot_id = ?${guard}`, [snapshotId]);
  if (!snapshot) return;
  await tx.run("DELETE FROM special_products WHERE snapshot_id = ?", [snapshotId]);
  await tx.run("DELETE FROM catalog_promotions WHERE snapshot_id = ?", [snapshotId]);
  await tx.run("DELETE FROM catalog_items WHERE snapshot_id = ?", [snapshotId]);
  await tx.run(`DELETE FROM catalog_snapshots WHERE snapshot_id = ?${guard}`, [snapshotId]);
}

function mapMatch(row: CatalogRow): LocalCatalogMatch {
  const price = Number(row.price_cents);
  if (!Number.isSafeInteger(price)) throw new Error("Invalid catalog price.");
  return {
    storeCode: requiredText(row.store_code),
    productCode: requiredText(row.product_code),
    referenceCode: optionalText(row.reference_code),
    itemNumber: optionalText(row.item_number),
    displayName: requiredText(row.display_name),
    barcode: optionalText(row.barcode),
    lookupCode: requiredText(row.lookup_code),
    lookupCodeNormalized: requiredText(row.lookup_code_normalized),
    retailPriceCents: price,
    priceSource: requiredPriceSource(row.price_source),
    priceSourceLabel: requiredText(row.price_source_label),
    quantityFactor: requiredFiniteNumber(row.quantity_factor, "quantity factor"),
    taxRateBasisPoints: optionalInteger(row.tax_rate_basis_points, "tax rate"),
    updatedAtIso: optionalText(row.updated_at_iso),
    rowVersion: optionalText(row.row_version),
    productImage: optionalText(row.product_image),
    discountRate: optionalFiniteNumber(row.discount_rate, "discount rate"),
    isSpecialProduct: requiredBooleanInteger(row.is_special_product, "special product"),
  };
}

function requiredText(value: unknown): string { if (typeof value !== "string" || !value) throw new Error("Invalid catalog text."); return value; }
function optionalText(value: unknown): string | null { return value === null || value === undefined ? null : requiredText(value); }
function escapeLike(value: string): string { return value.replace(/[\\%_]/g, "\\$&"); }

function catalogSnapshotState(
  value: unknown,
): "staging" | "active" | "retired" {
  if (value === "staging" || value === "active" || value === "retired") {
    return value;
  }
  throw new Error("Invalid catalog snapshot state.");
}

function requiredCatalogSnapshotId(value: unknown): string {
  if (
    typeof value !== "string" ||
    !value ||
    value.trim() !== value ||
    value.length > 512 ||
    /[\u0000-\u001f\u007f]/u.test(value)
  ) {
    throw new Error("Invalid catalog snapshot id.");
  }
  return value;
}

function requiredCatalogStoreCode(value: unknown): string {
  if (
    typeof value !== "string" ||
    !value ||
    value.trim() !== value ||
    value.length > 512 ||
    /[\u0000-\u001f\u007f]/u.test(value)
  ) {
    throw new Error("Invalid catalog store code.");
  }
  return value;
}

function requiredPromotionId(value: unknown): string {
  if (
    typeof value !== "string" ||
    !value ||
    value.trim() !== value ||
    value.length > 512 ||
    /[\u0000-\u001f\u007f]/u.test(value)
  ) {
    throw new Error("Invalid catalog promotion id.");
  }
  return value;
}

function requiredPromotionDefinitionJson(value: unknown): string {
  if (typeof value !== "string" || value.trim().length === 0) {
    throw new Error("Invalid catalog promotion definition.");
  }
  return value;
}

function requiredNonNegativeInteger(value: unknown, label: string): number {
  if (typeof value === "string" && !/^(0|[1-9]\d*)$/u.test(value)) {
    throw new Error(`Invalid ${label}.`);
  }
  if (
    typeof value !== "number" &&
    typeof value !== "string" &&
    typeof value !== "bigint"
  ) {
    throw new Error(`Invalid ${label}.`);
  }
  const parsed = Number(value);
  if (!Number.isSafeInteger(parsed) || parsed < 0) {
    throw new Error(`Invalid ${label}.`);
  }
  return parsed;
}

function requiredSafeInteger(value: unknown, label: string): number {
  if (typeof value === "string" && !/^-?(0|[1-9]\d*)$/u.test(value)) {
    throw new Error(`Invalid ${label}.`);
  }
  if (
    typeof value !== "number" &&
    typeof value !== "string" &&
    typeof value !== "bigint"
  ) {
    throw new Error(`Invalid ${label}.`);
  }
  const parsed = Number(value);
  if (!Number.isSafeInteger(parsed)) {
    throw new Error(`Invalid ${label}.`);
  }
  return parsed;
}

function requiredCanonicalIso(value: unknown, label: string): string {
  if (typeof value !== "string") throw new Error(`Invalid ${label}.`);
  const timestamp = Date.parse(value);
  if (
    !Number.isFinite(timestamp) ||
    new Date(timestamp).toISOString() !== value
  ) {
    throw new Error(`Invalid ${label}.`);
  }
  return value;
}

function normalizeLookupCode(value: string): string { return value.trim().toUpperCase(); }

function formatStoredDecimal(value: number): string {
  if (!Number.isFinite(value)) throw new Error("Invalid catalog decimal.");
  return Object.is(value, -0) ? "0" : String(value);
}

function requiredFiniteNumber(value: unknown, field: string): number {
  const number = typeof value === "number" ? value : typeof value === "string" ? Number(value) : Number.NaN;
  if (!Number.isFinite(number)) throw new Error(`Invalid catalog ${field}.`);
  return number;
}

function optionalInteger(value: unknown, field: string): number | null {
  if (value === null || value === undefined) return null;
  const number = Number(value);
  if (!Number.isSafeInteger(number)) throw new Error(`Invalid catalog ${field}.`);
  return number;
}

function optionalFiniteNumber(value: unknown, field: string): number | null {
  if (value === null || value === undefined) return null;
  return requiredFiniteNumber(value, field);
}

function requiredPriceSource(value: unknown): 0 | 1 | 2 | 3 | 4 {
  const number = Number(value);
  if (number === 0 || number === 1 || number === 2 || number === 3 || number === 4) return number;
  throw new Error("Invalid catalog price source.");
}

function requiredBooleanInteger(value: unknown, field: string): boolean {
  if (value === 0 || value === false) return false;
  if (value === 1 || value === true) return true;
  throw new Error(`Invalid catalog ${field}.`);
}

function assertStoredItem(item: CatalogStoredItem): void {
  requiredText(item.storeCode);
  requiredText(item.productCode);
  requiredText(item.displayName);
  requiredText(item.lookupCode);
  if (normalizeLookupCode(item.lookupCodeNormalized) !== item.lookupCodeNormalized) {
    throw new Error("Catalog lookup code must be normalized by the verified remote adapter.");
  }
  if (!Number.isSafeInteger(item.retailPriceCents)) throw new Error("Catalog retail price must be integer cents.");
  requiredPriceSource(item.priceSource);
  requiredText(item.priceSourceLabel);
  formatStoredDecimal(item.quantityFactor);
  if (item.taxRateBasisPoints !== null && !Number.isSafeInteger(item.taxRateBasisPoints)) throw new Error("Invalid catalog tax rate.");
  if (item.discountRate !== null) formatStoredDecimal(item.discountRate);
}
