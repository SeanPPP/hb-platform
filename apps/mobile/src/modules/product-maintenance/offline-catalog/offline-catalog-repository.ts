/**
 * 离线商品目录 SQLite 仓储（参考 apps/pos-ipad/src/core/db/catalog-repository.ts）。
 *
 * 不变量：
 * - active 快照只在 `activate` / `activateDelta` 的独占事务内切换，任何失败路径都不删除 active；
 * - full staging 先写完整子树再切换；delta staging 只保存 upsert 与墓碑，激活时回放到物理 active；
 * - 每批多值 INSERT 的绑定参数数量必须低于运行时 SQLite 的参数上限（见 MAX_BULK_INSERT_ROWS）。
 */
import type { SqliteConnectionPort, SqlValue } from "@/shared/db/types";
import {
  OFFLINE_CATALOG_MATCH_SOURCES,
  type ActiveOfflineCatalogMetadata,
  type OfflineCatalogDeletedItem,
  type OfflineCatalogItem,
  type OfflineCatalogMatchSource,
} from "./types";

/**
 * 每行的绑定参数数 = snapshot_id + 41 个值列 = 42。
 *
 * 上限不是系统 SQLite 的 999：expo-sqlite 自带 vendored sqlite3（3.50.x），
 * SQLITE_MAX_VARIABLE_NUMBER 为 32766，且构建配置未覆盖它。取 300 行/批 =
 * 12,600 参数，仍远低于上限，同时把 40 万行的 INSERT 条数从 2 万降到约 1,400
 * （expo-sqlite 的 runAsync 每次都 prepare→execute→finalize，没有语句缓存，
 * 条数直接等于 SQL 解析次数）。调大前请按 42 参数/行重新核对上限。
 */
const ITEM_VALUE_COLUMN_COUNT = 41;
const ITEM_BIND_PARAMETERS_PER_ROW = ITEM_VALUE_COLUMN_COUNT + 1;
const SQLITE_MAX_BIND_PARAMETERS = 32_766;
const MAX_BULK_INSERT_ROWS = 300;
const MAX_DELTA_BATCH_OPERATIONS = 500;

/**
 * 除 snapshot_id 外的全部列，顺序即 INSERT 列顺序与 `itemParameters` 的参数顺序，
 * 三者必须始终一致（delta 激活的 INSERT ... SELECT 也复用这份定义）。
 */
const ITEM_VALUE_COLUMNS = [
  "store_code", "lookup_key", "lookup_code", "lookup_code_normalized", "match_source",
  "product_code", "product_name", "item_number", "barcode", "product_image", "product_type", "grade",
  "local_supplier_code", "local_supplier_name", "store_name", "store_price_uuid", "purchase_price", "retail_price",
  "discount_rate", "is_auto_pricing", "is_special_product", "rate", "strategy_source_label", "strategy_rule_label",
  "clearance_uuid", "clearance_barcode", "clearance_price", "code_id", "code_uuid", "code_product_code",
  "code_item_number", "code_retail_price", "code_purchase_price", "code_quantity", "code_type",
  "code_discount_rate", "code_is_auto_pricing", "code_is_special_product", "code_is_active",
  "updated_at_iso", "row_version",
] as const;

const ITEM_COLUMNS = ["snapshot_id", ...ITEM_VALUE_COLUMNS].join(", ");

/** 冲突更新不能改主键组成部分（snapshot_id / lookup_key）。 */
const ITEM_UPDATE_COLUMNS = ITEM_VALUE_COLUMNS.filter((column) => column !== "lookup_key");

const ITEM_ROW_PLACEHOLDER = `(${Array.from({ length: ITEM_BIND_PARAMETERS_PER_ROW }, () => "?").join(", ")})`;

// 列定义、占位符、参数数三者必须同步；批大小也必须留在运行时上限内。
if (ITEM_VALUE_COLUMNS.length !== ITEM_VALUE_COLUMN_COUNT) {
  throw new Error("离线目录列定义与参数计数不一致");
}
if (MAX_BULK_INSERT_ROWS * ITEM_BIND_PARAMETERS_PER_ROW > SQLITE_MAX_BIND_PARAMETERS) {
  throw new Error("离线目录批量写入超出 SQLite 绑定参数上限");
}
const ITEM_UPSERT_SET = ITEM_UPDATE_COLUMNS.map((column) => `${column} = excluded.${column}`).join(", ");

export class OfflineCatalogDeltaBaseChangedError extends Error {
  public readonly code = "OFFLINE_CATALOG_DELTA_BASE_CHANGED" as const;

  public constructor() {
    super("Offline catalog delta base is no longer active.");
    this.name = "OfflineCatalogDeltaBaseChangedError";
  }
}

export interface OfflineCatalogDeltaStagingBatch {
  items: readonly OfflineCatalogItem[];
  deletedItems: readonly OfflineCatalogDeletedItem[];
}

export class OfflineCatalogRepository {
  public constructor(private readonly db: SqliteConnectionPort) {}

  public async getActiveMetadata(storeCode: string): Promise<ActiveOfflineCatalogMetadata | null> {
    const normalizedStoreCode = requiredStoreCode(storeCode);
    const row = await this.db.getFirst<ActiveSnapshotRow>(
      `SELECT
         s.snapshot_id, s.store_code, s.catalog_version, s.generated_at_iso, s.activated_at_iso,
         (SELECT COUNT(*) FROM offline_catalog_items i WHERE i.snapshot_id = s.snapshot_id) AS item_count
       FROM offline_catalog_snapshots s
       WHERE s.state = 'active' AND s.store_code = ?
       LIMIT 1`,
      [normalizedStoreCode],
    );
    if (!row) {
      return null;
    }
    return {
      snapshotId: requiredText(row.snapshot_id, "snapshot_id"),
      storeCode: requiredText(row.store_code, "store_code"),
      catalogVersion: requiredText(row.catalog_version, "catalog_version"),
      itemCount: requiredNonNegativeInteger(row.item_count, "item_count"),
      generatedAt: requiredText(row.generated_at_iso, "generated_at_iso"),
      activatedAt: requiredText(row.activated_at_iso, "activated_at_iso"),
    };
  }

  public async beginStaging(snapshot: {
    snapshotId: string;
    storeCode: string;
    catalogVersion: string;
    checksum: string;
    generatedAtIso: string;
    downloadedAtIso: string;
  }): Promise<void> {
    const snapshotId = requiredText(snapshot.snapshotId, "snapshotId");
    await this.db.withExclusiveTransaction(async (tx) => {
      const existing = await tx.getFirst<{ state: string }>(
        "SELECT state FROM offline_catalog_snapshots WHERE snapshot_id = ?",
        [snapshotId],
      );
      if (existing && existing.state !== "staging") {
        throw new Error("Offline catalog snapshot id collision with a retained snapshot.");
      }
      await deleteSnapshot(tx, snapshotId, true);
      await tx.run(
        `INSERT INTO offline_catalog_snapshots (
           snapshot_id, store_code, catalog_version, checksum, state, sync_mode, generation_id,
           base_snapshot_id, base_catalog_version, generated_at_iso, downloaded_at_iso, activated_at_iso
         ) VALUES (?, ?, ?, ?, 'staging', 'full', ?, NULL, NULL, ?, ?, NULL)`,
        [
          snapshotId,
          requiredStoreCode(snapshot.storeCode),
          requiredText(snapshot.catalogVersion, "catalogVersion"),
          snapshot.checksum,
          snapshotId,
          snapshot.generatedAtIso,
          snapshot.downloadedAtIso,
        ],
      );
    });
  }

  /** 多行 VALUES 批量写入；单事务保证任一批失败整页回滚。 */
  public async appendPage(snapshotId: string, items: readonly OfflineCatalogItem[]): Promise<void> {
    const scopedSnapshotId = requiredText(snapshotId, "snapshotId");
    if (items.length === 0) {
      return;
    }
    await this.db.withExclusiveTransaction(async (tx) => {
      for (const chunk of chunkItems(items, MAX_BULK_INSERT_ROWS)) {
        const parameters: SqlValue[] = [];
        for (const item of chunk) {
          parameters.push(...itemParameters(scopedSnapshotId, item));
        }
        await tx.run(
          `INSERT INTO offline_catalog_items (${ITEM_COLUMNS}) VALUES ${multiRowValues(chunk.length)}`,
          parameters,
        );
      }
    });
  }

  /** 校验数量后在同一事务内 retire 本店旧 active 并激活 staging。 */
  public async activate(snapshotId: string, expectedItemCount: number, activatedAtIso: string): Promise<void> {
    const scopedSnapshotId = requiredText(snapshotId, "snapshotId");
    await this.db.withExclusiveTransaction(async (tx) => {
      const staging = await tx.getFirst<{ state: string; store_code: string }>(
        "SELECT state, store_code FROM offline_catalog_snapshots WHERE snapshot_id = ?",
        [scopedSnapshotId],
      );
      if (staging?.state !== "staging") {
        throw new Error("Offline catalog snapshot is not eligible for activation.");
      }
      const count = await tx.getFirst<{ item_count: unknown }>(
        "SELECT COUNT(*) AS item_count FROM offline_catalog_items WHERE snapshot_id = ?",
        [scopedSnapshotId],
      );
      if (requiredNonNegativeInteger(count?.item_count, "item_count") !== expectedItemCount) {
        throw new Error("Offline catalog staging count verification failed.");
      }
      await tx.run(
        "UPDATE offline_catalog_snapshots SET state = 'retired' WHERE state = 'active' AND store_code = ?",
        [staging.store_code],
      );
      const result = await tx.run(
        "UPDATE offline_catalog_snapshots SET state = 'active', activated_at_iso = ? WHERE snapshot_id = ? AND state = 'staging'",
        [activatedAtIso, scopedSnapshotId],
      );
      if (result.changes !== 1) {
        throw new Error("Offline catalog snapshot activation was lost.");
      }
    });
  }

  /** delta staging 只登记基线与目标，不复制 active 商品。 */
  public async beginDeltaStaging(input: {
    sourceSnapshotId: string;
    baseCatalogVersion: string;
    snapshotId: string;
    storeCode: string;
    catalogVersion: string;
    checksum: string;
    generatedAtIso: string;
    downloadedAtIso: string;
  }): Promise<void> {
    const sourceSnapshotId = requiredText(input.sourceSnapshotId, "sourceSnapshotId");
    const snapshotId = requiredText(input.snapshotId, "snapshotId");
    if (sourceSnapshotId === snapshotId) {
      throw new Error("Offline catalog delta generation must differ from its physical base.");
    }
    await this.db.withExclusiveTransaction(async (tx) => {
      const source = await tx.getFirst<{ state: string; catalog_version: string; store_code: string }>(
        "SELECT state, catalog_version, store_code FROM offline_catalog_snapshots WHERE snapshot_id = ?",
        [sourceSnapshotId],
      );
      if (
        source?.state !== "active" ||
        source.catalog_version !== input.baseCatalogVersion ||
        source.store_code !== requiredStoreCode(input.storeCode)
      ) {
        throw new OfflineCatalogDeltaBaseChangedError();
      }
      const existing = await tx.getFirst<{ state: string }>(
        "SELECT state FROM offline_catalog_snapshots WHERE snapshot_id = ?",
        [snapshotId],
      );
      if (existing && existing.state !== "staging") {
        throw new Error("Offline catalog snapshot id collision with a retained snapshot.");
      }
      await deleteSnapshot(tx, snapshotId, true);
      await tx.run(
        `INSERT INTO offline_catalog_snapshots (
           snapshot_id, store_code, catalog_version, checksum, state, sync_mode, generation_id,
           base_snapshot_id, base_catalog_version, generated_at_iso, downloaded_at_iso, activated_at_iso
         ) VALUES (?, ?, ?, ?, 'staging', 'delta', ?, ?, ?, ?, ?, NULL)`,
        [
          snapshotId,
          source.store_code,
          requiredText(input.catalogVersion, "catalogVersion"),
          input.checksum,
          snapshotId,
          sourceSnapshotId,
          input.baseCatalogVersion,
          input.generatedAtIso,
          input.downloadedAtIso,
        ],
      );
    });
  }

  /** 每批 ≤500 个操作、短事务；upsert 与墓碑只写 delta staging。 */
  public async appendDeltaBatch(snapshotId: string, batch: OfflineCatalogDeltaStagingBatch): Promise<void> {
    const scopedSnapshotId = requiredText(snapshotId, "snapshotId");
    if (batch.items.length + batch.deletedItems.length > MAX_DELTA_BATCH_OPERATIONS) {
      throw new Error("Offline catalog delta staging batch exceeds 500 operations.");
    }
    await this.db.withExclusiveTransaction(async (tx) => {
      const staging = await tx.getFirst<{ state: string; sync_mode: string }>(
        "SELECT state, sync_mode FROM offline_catalog_snapshots WHERE snapshot_id = ?",
        [scopedSnapshotId],
      );
      if (staging?.state !== "staging" || staging.sync_mode !== "delta") {
        throw new Error("Offline catalog snapshot is not eligible for delta staging.");
      }
      for (const deleted of batch.deletedItems) {
        await tx.run(
          "DELETE FROM offline_catalog_items WHERE snapshot_id = ? AND lookup_key = ?",
          [scopedSnapshotId, deleted.lookupKey],
        );
        await tx.run(
          `INSERT INTO offline_catalog_delta_deletions (snapshot_id, store_code, lookup_key)
           VALUES (?, ?, ?)
           ON CONFLICT (snapshot_id, lookup_key) DO NOTHING`,
          [scopedSnapshotId, deleted.storeCode, deleted.lookupKey],
        );
      }
      for (const chunk of chunkItems(batch.items, MAX_BULK_INSERT_ROWS)) {
        for (const item of chunk) {
          await tx.run(
            "DELETE FROM offline_catalog_delta_deletions WHERE snapshot_id = ? AND lookup_key = ?",
            [scopedSnapshotId, item.lookupKey],
          );
        }
        const parameters: SqlValue[] = [];
        for (const item of chunk) {
          parameters.push(...itemParameters(scopedSnapshotId, item));
        }
        await tx.run(
          `INSERT INTO offline_catalog_items (${ITEM_COLUMNS}) VALUES ${multiRowValues(chunk.length)}
           ON CONFLICT (snapshot_id, lookup_key) DO UPDATE SET ${ITEM_UPSERT_SET}`,
          parameters,
        );
      }
    });
  }

  /**
   * 在一个事务内把 delta 回放到物理 active：先按墓碑删除，再 upsert staging 行，
   * 校验总数后更新 active 的版本/代次元数据并删除 staging。任一步失败整体回滚。
   */
  public async activateDelta(input: {
    sourceSnapshotId: string;
    baseCatalogVersion: string;
    stagingSnapshotId: string;
    expectedItemCount: number;
    activatedAtIso: string;
  }): Promise<ActiveOfflineCatalogMetadata> {
    const sourceSnapshotId = requiredText(input.sourceSnapshotId, "sourceSnapshotId");
    const stagingSnapshotId = requiredText(input.stagingSnapshotId, "stagingSnapshotId");
    return this.db.withExclusiveTransaction(async (tx) => {
      const active = await tx.getFirst<{ state: string; catalog_version: string; store_code: string }>(
        "SELECT state, catalog_version, store_code FROM offline_catalog_snapshots WHERE snapshot_id = ?",
        [sourceSnapshotId],
      );
      if (active?.state !== "active" || active.catalog_version !== input.baseCatalogVersion) {
        throw new OfflineCatalogDeltaBaseChangedError();
      }
      const staging = await tx.getFirst<DeltaStagingRow>(
        `SELECT state, sync_mode, catalog_version, checksum, base_snapshot_id, base_catalog_version,
                generated_at_iso, downloaded_at_iso
         FROM offline_catalog_snapshots WHERE snapshot_id = ?`,
        [stagingSnapshotId],
      );
      if (
        staging?.state !== "staging" ||
        staging.sync_mode !== "delta" ||
        staging.base_snapshot_id !== sourceSnapshotId ||
        staging.base_catalog_version !== input.baseCatalogVersion
      ) {
        throw new Error("Offline catalog delta staging does not match its active base.");
      }

      await tx.run(
        `DELETE FROM offline_catalog_items
         WHERE snapshot_id = ?
           AND EXISTS (
             SELECT 1 FROM offline_catalog_delta_deletions d
             WHERE d.snapshot_id = ? AND d.lookup_key = offline_catalog_items.lookup_key
           )`,
        [sourceSnapshotId, stagingSnapshotId],
      );
      await tx.run(
        `INSERT INTO offline_catalog_items (${ITEM_COLUMNS})
         SELECT ?, ${ITEM_VALUE_COLUMNS.join(", ")}
         FROM offline_catalog_items
         WHERE snapshot_id = ?
         ON CONFLICT (snapshot_id, lookup_key) DO UPDATE SET ${ITEM_UPSERT_SET}`,
        [sourceSnapshotId, stagingSnapshotId],
      );
      const count = await tx.getFirst<{ item_count: unknown }>(
        "SELECT COUNT(*) AS item_count FROM offline_catalog_items WHERE snapshot_id = ?",
        [sourceSnapshotId],
      );
      const itemCount = requiredNonNegativeInteger(count?.item_count, "item_count");
      if (itemCount !== input.expectedItemCount) {
        throw new Error("Offline catalog delta target count verification failed.");
      }
      const updated = await tx.run(
        `UPDATE offline_catalog_snapshots
         SET catalog_version = ?, checksum = ?, generated_at_iso = ?, downloaded_at_iso = ?,
             activated_at_iso = ?, generation_id = ?, sync_mode = 'delta',
             base_snapshot_id = ?, base_catalog_version = ?
         WHERE snapshot_id = ? AND state = 'active' AND catalog_version = ?`,
        [
          requiredText(staging.catalog_version, "catalog_version"),
          staging.checksum ?? "",
          requiredText(staging.generated_at_iso, "generated_at_iso"),
          requiredText(staging.downloaded_at_iso, "downloaded_at_iso"),
          input.activatedAtIso,
          stagingSnapshotId,
          sourceSnapshotId,
          input.baseCatalogVersion,
          sourceSnapshotId,
          input.baseCatalogVersion,
        ],
      );
      if (updated.changes !== 1) {
        throw new OfflineCatalogDeltaBaseChangedError();
      }
      await deleteSnapshot(tx, stagingSnapshotId, true);
      return {
        snapshotId: sourceSnapshotId,
        storeCode: active.store_code,
        catalogVersion: requiredText(staging.catalog_version, "catalog_version"),
        itemCount,
        generatedAt: requiredText(staging.generated_at_iso, "generated_at_iso"),
        activatedAt: input.activatedAtIso,
      };
    });
  }

  public async discardStaging(snapshotId: string): Promise<void> {
    await this.db.withExclusiveTransaction((tx) => deleteSnapshot(tx, snapshotId, true));
  }

  /** 失败/取消路径按批回收 staging 子行，避免 40 万级目录的级联删除长期占用 SQLite。 */
  public async discardStagingBatch(snapshotId: string, batchSize = 500): Promise<number> {
    return cleanupSnapshotsByState(this.db, "staging", batchSize, requiredText(snapshotId, "snapshotId"));
  }

  public async cleanupStagingBatch(batchSize = 500): Promise<number> {
    return cleanupSnapshotsByState(this.db, "staging", batchSize);
  }

  public async cleanupRetiredBatch(batchSize = 500): Promise<number> {
    return cleanupSnapshotsByState(this.db, "retired", batchSize);
  }

  /** 在本店 active 快照内按规范化售卖码精确匹配；同一码可能命中多个商品/多种来源。 */
  public async lookup(storeCode: string, lookupCodeNormalized: string): Promise<OfflineCatalogItem[]> {
    if (!lookupCodeNormalized) {
      return [];
    }
    const rows = await this.db.getAll<ItemRow>(
      `${activeItemSql()} AND i.lookup_code_normalized = ? ORDER BY i.lookup_key`,
      [requiredStoreCode(storeCode), lookupCodeNormalized],
    );
    return rows.map(mapItemRow);
  }

  /** 读取一个商品在本店的全部行（主码 + 套码 + 多码 + 清货码），用于组装详情。 */
  public async getProductRows(storeCode: string, productCode: string): Promise<OfflineCatalogItem[]> {
    if (!productCode) {
      return [];
    }
    const rows = await this.db.getAll<ItemRow>(
      `${activeItemSql()} AND i.product_code = ? ORDER BY i.lookup_key`,
      [requiredStoreCode(storeCode), productCode],
    );
    return rows.map(mapItemRow);
  }
}

type ActiveSnapshotRow = Readonly<{
  snapshot_id: unknown;
  store_code: unknown;
  catalog_version: unknown;
  generated_at_iso: unknown;
  activated_at_iso: unknown;
  item_count: unknown;
}>;

type DeltaStagingRow = Readonly<{
  state: string;
  sync_mode: string;
  catalog_version: unknown;
  checksum: string | null;
  base_snapshot_id: string | null;
  base_catalog_version: string | null;
  generated_at_iso: unknown;
  downloaded_at_iso: unknown;
}>;

type ItemRow = Record<string, unknown>;

function activeItemSql(): string {
  return `SELECT i.*
    FROM offline_catalog_snapshots s
    JOIN offline_catalog_items i ON i.snapshot_id = s.snapshot_id
    WHERE s.state = 'active' AND s.store_code = ?`;
}

async function deleteSnapshot(tx: SqliteConnectionPort, snapshotId: string, onlyStaging: boolean): Promise<void> {
  const guard = onlyStaging ? " AND state = 'staging'" : "";
  const snapshot = await tx.getFirst<{ snapshot_id: string }>(
    `SELECT snapshot_id FROM offline_catalog_snapshots WHERE snapshot_id = ?${guard}`,
    [snapshotId],
  );
  if (!snapshot) {
    return;
  }
  await tx.run("DELETE FROM offline_catalog_delta_deletions WHERE snapshot_id = ?", [snapshotId]);
  await tx.run("DELETE FROM offline_catalog_items WHERE snapshot_id = ?", [snapshotId]);
  await tx.run(`DELETE FROM offline_catalog_snapshots WHERE snapshot_id = ?${guard}`, [snapshotId]);
}

/** 每次最多删除 batchSize 个子行；子行清空后才删除父行。返回本次删除的行数。 */
async function cleanupSnapshotsByState(
  db: SqliteConnectionPort,
  state: "staging" | "retired",
  batchSize: number,
  requestedSnapshotId?: string,
): Promise<number> {
  if (!Number.isSafeInteger(batchSize) || batchSize <= 0 || batchSize > 500) {
    throw new Error("Invalid offline catalog cleanup batch size.");
  }
  return db.withExclusiveTransaction(async (tx) => {
    const target = await tx.getFirst<{ snapshot_id: string }>(
      requestedSnapshotId
        ? "SELECT snapshot_id FROM offline_catalog_snapshots WHERE snapshot_id = ? AND state = ? LIMIT 1"
        : "SELECT snapshot_id FROM offline_catalog_snapshots WHERE state = ? ORDER BY snapshot_id LIMIT 1",
      requestedSnapshotId ? [requestedSnapshotId, state] : [state],
    );
    if (!target) {
      return 0;
    }
    const deletedItems = await tx.run(
      `DELETE FROM offline_catalog_items
       WHERE rowid IN (SELECT rowid FROM offline_catalog_items WHERE snapshot_id = ? LIMIT ?)`,
      [target.snapshot_id, batchSize],
    );
    if (deletedItems.changes > 0) {
      return deletedItems.changes;
    }
    await tx.run("DELETE FROM offline_catalog_delta_deletions WHERE snapshot_id = ?", [target.snapshot_id]);
    const deletedSnapshot = await tx.run(
      "DELETE FROM offline_catalog_snapshots WHERE snapshot_id = ? AND state = ?",
      [target.snapshot_id, state],
    );
    return deletedSnapshot.changes;
  });
}

function multiRowValues(rowCount: number): string {
  return Array.from({ length: rowCount }, () => ITEM_ROW_PLACEHOLDER).join(", ");
}

function chunkItems<T>(items: readonly T[], batchSize: number): readonly (readonly T[])[] {
  const chunks: T[][] = [];
  for (let start = 0; start < items.length; start += batchSize) {
    chunks.push(items.slice(start, start + batchSize));
  }
  return chunks;
}

function boolToInt(value: boolean): number {
  return value ? 1 : 0;
}

function nullableBoolToInt(value: boolean | null): number | null {
  return value === null ? null : boolToInt(value);
}

function itemParameters(snapshotId: string, item: OfflineCatalogItem): SqlValue[] {
  return [
    snapshotId, item.storeCode, item.lookupKey, item.lookupCode, item.lookupCodeNormalized, item.matchSource,
    item.productCode, item.productName, item.itemNumber, item.barcode, item.productImage, item.productType, item.grade,
    item.localSupplierCode, item.localSupplierName, item.storeName, item.storePriceUuid, item.purchasePrice, item.retailPrice,
    item.discountRate, boolToInt(item.isAutoPricing), boolToInt(item.isSpecialProduct), item.rate, item.strategySourceLabel,
    item.strategyRuleLabel, item.clearanceUuid, item.clearanceBarcode, item.clearancePrice, item.codeId, item.codeUuid,
    item.codeProductCode, item.codeItemNumber, item.codeRetailPrice, item.codePurchasePrice, item.codeQuantity, item.codeType,
    item.codeDiscountRate, nullableBoolToInt(item.codeIsAutoPricing), nullableBoolToInt(item.codeIsSpecialProduct),
    nullableBoolToInt(item.codeIsActive), item.updatedAt, item.rowVersion,
  ];
}

function mapItemRow(row: ItemRow): OfflineCatalogItem {
  return {
    storeCode: requiredText(row.store_code, "store_code"),
    lookupKey: requiredText(row.lookup_key, "lookup_key"),
    lookupCode: requiredText(row.lookup_code, "lookup_code"),
    lookupCodeNormalized: requiredText(row.lookup_code_normalized, "lookup_code_normalized"),
    matchSource: requiredMatchSource(row.match_source),
    productCode: requiredText(row.product_code, "product_code"),
    productName: typeof row.product_name === "string" ? row.product_name : "",
    itemNumber: optionalText(row.item_number),
    barcode: optionalText(row.barcode),
    productImage: optionalText(row.product_image),
    productType: optionalNumber(row.product_type),
    grade: optionalText(row.grade),
    localSupplierCode: optionalText(row.local_supplier_code),
    localSupplierName: optionalText(row.local_supplier_name),
    storeName: optionalText(row.store_name),
    storePriceUuid: optionalText(row.store_price_uuid),
    purchasePrice: optionalNumber(row.purchase_price),
    retailPrice: optionalNumber(row.retail_price),
    discountRate: optionalNumber(row.discount_rate),
    isAutoPricing: Number(row.is_auto_pricing) === 1,
    isSpecialProduct: Number(row.is_special_product) === 1,
    rate: optionalNumber(row.rate),
    strategySourceLabel: optionalText(row.strategy_source_label),
    strategyRuleLabel: optionalText(row.strategy_rule_label),
    clearanceUuid: optionalText(row.clearance_uuid),
    clearanceBarcode: optionalText(row.clearance_barcode),
    clearancePrice: optionalNumber(row.clearance_price),
    codeId: optionalText(row.code_id),
    codeUuid: optionalText(row.code_uuid),
    codeProductCode: optionalText(row.code_product_code),
    codeItemNumber: optionalText(row.code_item_number),
    codeRetailPrice: optionalNumber(row.code_retail_price),
    codePurchasePrice: optionalNumber(row.code_purchase_price),
    codeQuantity: optionalNumber(row.code_quantity),
    codeType: optionalNumber(row.code_type),
    codeDiscountRate: optionalNumber(row.code_discount_rate),
    codeIsAutoPricing: optionalBool(row.code_is_auto_pricing),
    codeIsSpecialProduct: optionalBool(row.code_is_special_product),
    codeIsActive: optionalBool(row.code_is_active),
    updatedAt: optionalText(row.updated_at_iso),
    rowVersion: requiredText(row.row_version, "row_version"),
  };
}

function requiredText(value: unknown, field: string): string {
  if (typeof value !== "string" || value.length === 0) {
    throw new Error(`Invalid offline catalog text: ${field}.`);
  }
  return value;
}

function requiredStoreCode(value: string): string {
  const trimmed = value?.trim();
  if (!trimmed) {
    throw new Error("Offline catalog store code is required.");
  }
  return trimmed;
}

function optionalText(value: unknown): string | null {
  return typeof value === "string" && value.length > 0 ? value : null;
}

function optionalNumber(value: unknown): number | null {
  if (value === null || value === undefined) {
    return null;
  }
  const numeric = Number(value);
  return Number.isFinite(numeric) ? numeric : null;
}

function optionalBool(value: unknown): boolean | null {
  if (value === null || value === undefined) {
    return null;
  }
  return Number(value) === 1;
}

function requiredNonNegativeInteger(value: unknown, field: string): number {
  const numeric = Number(value);
  if (!Number.isSafeInteger(numeric) || numeric < 0) {
    throw new Error(`Invalid offline catalog integer: ${field}.`);
  }
  return numeric;
}

function requiredMatchSource(value: unknown): OfflineCatalogMatchSource {
  if (typeof value === "string" && (OFFLINE_CATALOG_MATCH_SOURCES as readonly string[]).includes(value)) {
    return value as OfflineCatalogMatchSource;
  }
  throw new Error("Invalid offline catalog match source.");
}
