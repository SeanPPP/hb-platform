import assert from "node:assert/strict";
import { mkdtemp, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { DatabaseSync, type SQLInputValue } from "node:sqlite";
import test from "node:test";
import { applyOfflineCatalogMigrations } from "./offline-catalog-migrations";
import {
  OfflineCatalogDeltaBaseChangedError,
  OfflineCatalogRepository,
} from "./offline-catalog-repository";
import {
  buildOfflineLookupKey,
  normalizeOfflineLookupCode,
  type OfflineCatalogItem,
  type OfflineCatalogMatchSource,
} from "./types";
import type { SqliteConnectionPort, SqlRunResult, SqlValue } from "@/shared/db/types";

/** 用 Node 内建 SQLite 跑真实 SQL，验证表结构、多值 INSERT 与事务切换语义。 */
class NodeSqliteConnection implements SqliteConnectionPort {
  public constructor(private readonly db: DatabaseSync, private readonly inTransaction = false) {}
  public async exec(sql: string): Promise<void> {
    this.db.exec(sql);
  }
  public async run(sql: string, parameters: readonly SqlValue[] = []): Promise<SqlRunResult> {
    const result = this.db.prepare(sql).run(...(parameters as SQLInputValue[]));
    return { changes: Number(result.changes), lastInsertRowId: Number(result.lastInsertRowid) };
  }
  public async getFirst<T extends object>(sql: string, parameters: readonly SqlValue[] = []): Promise<T | null> {
    return (this.db.prepare(sql).get(...(parameters as SQLInputValue[])) as T | undefined) ?? null;
  }
  public async getAll<T extends object>(sql: string, parameters: readonly SqlValue[] = []): Promise<readonly T[]> {
    return this.db.prepare(sql).all(...(parameters as SQLInputValue[])) as T[];
  }
  public async withExclusiveTransaction<T>(operation: (tx: SqliteConnectionPort) => Promise<T>): Promise<T> {
    if (this.inTransaction) throw new Error("nested");
    this.db.exec("BEGIN IMMEDIATE;");
    try {
      const result = await operation(new NodeSqliteConnection(this.db, true));
      this.db.exec("COMMIT;");
      return result;
    } catch (error) {
      this.db.exec("ROLLBACK;");
      throw error;
    }
  }
  public async close(): Promise<void> {
    this.db.close();
  }
}

function item(lookupCode: string, matchSource: OfflineCatalogMatchSource, productCode: string, extra: Partial<OfflineCatalogItem> = {}): OfflineCatalogItem {
  const lookupCodeNormalized = normalizeOfflineLookupCode(lookupCode);
  const codeId = extra.codeId ?? null;
  return {
    storeCode: "S001",
    lookupKey: buildOfflineLookupKey({ lookupCodeNormalized, matchSource, productCode, codeId }),
    lookupCode,
    lookupCodeNormalized,
    matchSource,
    productCode,
    productName: `商品 ${productCode}`,
    itemNumber: null,
    barcode: matchSource === "ProductBarcode" ? lookupCode : null,
    productImage: null,
    productType: 0,
    grade: null,
    localSupplierCode: null,
    localSupplierName: null,
    storeName: null,
    storePriceUuid: `sp-${productCode}`,
    purchasePrice: 1,
    retailPrice: 2.5,
    discountRate: null,
    isAutoPricing: false,
    isSpecialProduct: false,
    rate: null,
    strategySourceLabel: null,
    strategyRuleLabel: null,
    clearanceUuid: null,
    clearanceBarcode: null,
    clearancePrice: null,
    codeId,
    codeUuid: null,
    codeProductCode: null,
    codeItemNumber: null,
    codeRetailPrice: null,
    codePurchasePrice: null,
    codeQuantity: null,
    codeType: null,
    codeDiscountRate: null,
    codeIsAutoPricing: null,
    codeIsSpecialProduct: null,
    codeIsActive: null,
    updatedAt: null,
    rowVersion: `rv-${lookupCode}`,
    ...extra,
  };
}

async function createRepository() {
  const dir = await mkdtemp(join(tmpdir(), "offline-catalog-"));
  const db = new DatabaseSync(join(dir, "catalog.db"));
  const connection = new NodeSqliteConnection(db);
  await applyOfflineCatalogMigrations(connection, () => "2026-09-17T00:00:00.000Z");
  const repository = new OfflineCatalogRepository(connection);
  return {
    repository,
    connection,
    dispose: async () => {
      db.close();
      await rm(dir, { recursive: true, force: true });
    },
  };
}

test("全量 staging → activate 后可按售卖码与商品查询，旧 active 被 retire", async () => {
  const { repository, dispose } = await createRepository();
  try {
    await repository.beginStaging({
      snapshotId: "snap-1",
      storeCode: "S001",
      catalogVersion: "catalog-v1:a",
      checksum: "sha",
      generatedAtIso: "2026-09-17T01:00:00.000Z",
      downloadedAtIso: "2026-09-17T01:01:00.000Z",
    });
    const items = Array.from({ length: 45 }, (_, index) =>
      item(`930000000${String(index).padStart(4, "0")}`, "ProductBarcode", `P${index}`),
    );
    items.push(item("SET-1", "SetBarcode", "P1", { codeId: "set-1", codeRetailPrice: 9 }));
    await repository.appendPage("snap-1", items);
    await assert.rejects(
      () => repository.activate("snap-1", 1, "2026-09-17T01:02:00.000Z"),
      /count verification failed/,
    );
    await repository.activate("snap-1", items.length, "2026-09-17T01:02:00.000Z");

    const meta = await repository.getActiveMetadata("S001");
    assert.deepEqual(meta, {
      snapshotId: "snap-1",
      storeCode: "S001",
      catalogVersion: "catalog-v1:a",
      itemCount: items.length,
      generatedAt: "2026-09-17T01:00:00.000Z",
      activatedAt: "2026-09-17T01:02:00.000Z",
    });
    const hits = await repository.lookup("S001", normalizeOfflineLookupCode("set-1"));
    assert.equal(hits.length, 1);
    assert.equal(hits[0]?.matchSource, "SetBarcode");
    assert.equal(hits[0]?.codeRetailPrice, 9);
    const productRows = await repository.getProductRows("S001", "P1");
    assert.deepEqual(productRows.map((row) => row.matchSource).sort(), ["ProductBarcode", "SetBarcode"]);
    assert.equal(await repository.getActiveMetadata("S999"), null);

    // 第二次全量激活：旧 active 退役，查询只读新快照。
    await repository.beginStaging({
      snapshotId: "snap-2",
      storeCode: "S001",
      catalogVersion: "catalog-v1:b",
      checksum: "sha2",
      generatedAtIso: "2026-09-17T02:00:00.000Z",
      downloadedAtIso: "2026-09-17T02:01:00.000Z",
    });
    await repository.appendPage("snap-2", [item("NEW-1", "ProductBarcode", "PN")]);
    await repository.activate("snap-2", 1, "2026-09-17T02:02:00.000Z");
    assert.equal((await repository.getActiveMetadata("S001"))?.snapshotId, "snap-2");
    assert.equal((await repository.lookup("S001", "SET-1")).length, 0);
    let cleaned = 0;
    while ((await repository.cleanupRetiredBatch(10)) > 0) cleaned += 1;
    assert.ok(cleaned > 0, "retired 快照应被分批回收");
  } finally {
    await dispose();
  }
});

test("delta staging 回放：删除墓碑行、upsert 变更行、更新 active 版本元数据", async () => {
  const { repository, dispose } = await createRepository();
  try {
    await repository.beginStaging({
      snapshotId: "snap-1",
      storeCode: "S001",
      catalogVersion: "catalog-v1:a",
      checksum: "sha",
      generatedAtIso: "2026-09-17T01:00:00.000Z",
      downloadedAtIso: "2026-09-17T01:01:00.000Z",
    });
    await repository.appendPage("snap-1", [
      item("A", "ProductBarcode", "PA"),
      item("B", "ProductBarcode", "PB"),
      item("C", "ProductBarcode", "PC"),
    ]);
    await repository.activate("snap-1", 3, "2026-09-17T01:02:00.000Z");

    await assert.rejects(
      () =>
        repository.beginDeltaStaging({
          sourceSnapshotId: "snap-1",
          baseCatalogVersion: "catalog-v1:wrong",
          snapshotId: "delta-1",
          storeCode: "S001",
          catalogVersion: "catalog-v1:b",
          checksum: "delta",
          generatedAtIso: "2026-09-17T03:00:00.000Z",
          downloadedAtIso: "2026-09-17T03:01:00.000Z",
        }),
      OfflineCatalogDeltaBaseChangedError,
    );

    await repository.beginDeltaStaging({
      sourceSnapshotId: "snap-1",
      baseCatalogVersion: "catalog-v1:a",
      snapshotId: "delta-1",
      storeCode: "S001",
      catalogVersion: "catalog-v1:b",
      checksum: "delta",
      generatedAtIso: "2026-09-17T03:00:00.000Z",
      downloadedAtIso: "2026-09-17T03:01:00.000Z",
    });
    const updatedB = item("B", "ProductBarcode", "PB", { retailPrice: 9.9, rowVersion: "rv-B2" });
    const deletedC = item("C", "ProductBarcode", "PC");
    await repository.appendDeltaBatch("delta-1", {
      items: [updatedB, item("D", "ProductBarcode", "PD")],
      deletedItems: [{ storeCode: "S001", lookupKey: deletedC.lookupKey, deletedAt: null }],
    });
    const activated = await repository.activateDelta({
      sourceSnapshotId: "snap-1",
      baseCatalogVersion: "catalog-v1:a",
      stagingSnapshotId: "delta-1",
      expectedItemCount: 3,
      activatedAtIso: "2026-09-17T03:02:00.000Z",
    });
    assert.equal(activated.snapshotId, "snap-1");
    assert.equal(activated.catalogVersion, "catalog-v1:b");
    assert.equal(activated.itemCount, 3);
    assert.equal(activated.generatedAt, "2026-09-17T03:00:00.000Z");
    assert.equal((await repository.lookup("S001", "C")).length, 0, "墓碑行必须被删除");
    assert.equal((await repository.lookup("S001", "B"))[0]?.retailPrice, 9.9, "变更行必须被覆盖");
    assert.equal((await repository.lookup("S001", "D")).length, 1, "新增行必须写入");
    const meta = await repository.getActiveMetadata("S001");
    assert.equal(meta?.catalogVersion, "catalog-v1:b");
    assert.equal(await repository.cleanupStagingBatch(), 0, "delta staging 激活后应已删除");
  } finally {
    await dispose();
  }
});

test("discardStaging 只删 staging，不影响 active", async () => {
  const { repository, dispose } = await createRepository();
  try {
    await repository.beginStaging({
      snapshotId: "snap-1",
      storeCode: "S001",
      catalogVersion: "catalog-v1:a",
      checksum: "sha",
      generatedAtIso: "2026-09-17T01:00:00.000Z",
      downloadedAtIso: "2026-09-17T01:01:00.000Z",
    });
    await repository.appendPage("snap-1", [item("A", "ProductBarcode", "PA")]);
    await repository.activate("snap-1", 1, "2026-09-17T01:02:00.000Z");
    await repository.beginStaging({
      snapshotId: "snap-2",
      storeCode: "S001",
      catalogVersion: "catalog-v1:b",
      checksum: "sha",
      generatedAtIso: "2026-09-17T02:00:00.000Z",
      downloadedAtIso: "2026-09-17T02:01:00.000Z",
    });
    await repository.appendPage("snap-2", [item("B", "ProductBarcode", "PB")]);
    await repository.discardStaging("snap-1");
    await repository.discardStaging("snap-2");
    assert.equal((await repository.getActiveMetadata("S001"))?.snapshotId, "snap-1");
    assert.equal((await repository.lookup("S001", "A")).length, 1);
    assert.equal(await repository.cleanupStagingBatch(), 0);
  } finally {
    await dispose();
  }
});
