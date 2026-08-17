import assert from "node:assert/strict";
import { DatabaseSync, type SQLInputValue } from "node:sqlite";
import test from "node:test";

import { SqliteCatalogLookupOverlayRepository } from "./catalog-lookup-overlay-repository";
import type { LocalCatalogMatch } from "./catalog-repository";
import { applyMigrations, POS_DATABASE_MIGRATIONS } from "./migrations";
import { PosDatabase } from "./pos-database";
import type {
  SqliteConnectionPort,
  SqliteDriverPort,
  SqlRunResult,
  SqlValue,
} from "./types";

const T0 = "2026-07-29T00:00:00.000Z";
const T1 = "2026-07-29T01:00:00.000Z";

test("M23 从 M22 增量新增目录在线校准覆盖层", async () => {
  await withDatabase(async (connection) => {
    await applyMigrations(
      connection,
      () => T0,
      POS_DATABASE_MIGRATIONS.filter((migration) => migration.version <= 22),
    );
    assert.equal(await schemaVersion(connection), 22);

    await applyMigrations(
      connection,
      () => T1,
      POS_DATABASE_MIGRATIONS.filter((migration) => migration.version <= 23),
    );

    assert.equal(await schemaVersion(connection), 23);
    const columns = await connection.getAll<{ name: string }>(
      "PRAGMA table_info('catalog_lookup_overlays')",
    );
    assert.deepEqual(
      columns.map((column) => column.name),
      [
        "base_snapshot_id",
        "store_code",
        "lookup_code_normalized",
        "record_kind",
        "product_code",
        "reference_code",
        "item_number",
        "display_name",
        "barcode",
        "lookup_code",
        "retail_price_cents",
        "price_source",
        "price_source_label",
        "quantity_factor",
        "tax_rate_basis_points",
        "updated_at_iso",
        "row_version",
        "product_image",
        "discount_rate",
        "is_special_product",
        "verified_at_iso",
      ],
    );
  });
});

test("M25/M26 从 M24 增量补齐目录代次与日志投递 outbox，并可重复开库", async () => {
  await withDatabase(async (connection) => {
    await applyMigrations(
      connection,
      () => T0,
      POS_DATABASE_MIGRATIONS.filter((migration) => migration.version <= 24),
    );
    await insertSnapshot(connection, "snapshot-before-m25", "active");

    await applyMigrations(
      connection,
      () => T1,
      POS_DATABASE_MIGRATIONS.filter((migration) => migration.version <= 26),
    );
    assert.equal(await schemaVersion(connection), 26);

    const snapshotColumns = await connection.getAll<{ name: string }>(
      "PRAGMA table_info('catalog_snapshots')",
    );
    assert.deepEqual(
      snapshotColumns.map((column) => column.name),
      [
        "snapshot_id",
        "catalog_version",
        "checksum",
        "state",
        "downloaded_at_iso",
        "activated_at_iso",
        "generation_id",
        "sync_mode",
        "base_snapshot_id",
        "base_catalog_version",
      ],
    );
    const auditColumns = await connection.getAll<{ name: string }>(
      "PRAGMA table_info('audit_events')",
    );
    assert.ok(auditColumns.some((column) => column.name === "delivery_state"));
    assert.ok(auditColumns.some((column) => column.name === "next_attempt_at_iso"));
    assert.ok(
      await connection.getFirst(
        "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'application_log_outbox'",
      ),
    );
    const migratedSnapshot = await connection.getFirst<{
        generation_id: string;
        sync_mode: string;
        base_snapshot_id: string | null;
        base_catalog_version: string | null;
      }>(
        `SELECT generation_id, sync_mode, base_snapshot_id, base_catalog_version
         FROM catalog_snapshots
         WHERE snapshot_id = 'snapshot-before-m25'`,
      );
    assert.deepEqual(
      migratedSnapshot ? { ...migratedSnapshot } : null,
      {
        generation_id: "snapshot-before-m25",
        sync_mode: "full",
        base_snapshot_id: null,
        base_catalog_version: null,
      },
    );

    const deletionColumns = await connection.getAll<{ name: string }>(
      "PRAGMA table_info('catalog_delta_deletions')",
    );
    assert.deepEqual(
      deletionColumns.map((column) => column.name),
      ["snapshot_id", "store_code", "lookup_code_normalized"],
    );

    // 中文注释：已应用 M25/M26 的数据库再次开库时，M17 目录结构核验也必须接受加法列。
    await applyMigrations(
      connection,
      () => T1,
      POS_DATABASE_MIGRATIONS.filter((migration) => migration.version <= 26),
    );
    assert.equal(await schemaVersion(connection), 26);
  });
});

test("在线命中按当前目录代次持久覆盖本地商品", async () => {
  await withMigratedDatabase(async (connection) => {
    await insertSnapshot(connection, "snapshot-1", "active");
    await insertCatalogItem(
      connection,
      "snapshot-1",
      item({
        displayName: "Local tea",
        retailPriceCents: 100,
      }),
    );
    const repository = createRepository(connection);

    assert.equal(await repository.getActiveSnapshotId(), "snapshot-1");
    assert.equal(
      await repository.upsert({
        baseSnapshotId: "snapshot-1",
        item: item({
          displayName: "Remote tea",
          retailPriceCents: 125,
          rowVersion: "remote-v2",
        }),
      }),
      "applied",
    );

    const stored = await connection.getFirst<{
      record_kind: string;
      verified_at_iso: string;
    }>(
      `SELECT record_kind, verified_at_iso
       FROM catalog_lookup_overlays
       WHERE base_snapshot_id = 'snapshot-1'
         AND store_code = 'STORE-1'
         AND lookup_code_normalized = 'TEA-1'`,
    );
    assert.equal(stored?.record_kind, "item");
    assert.equal(stored?.verified_at_iso, T1);

    // 新仓储实例只依赖 SQLCipher 数据，不依赖进程内缓存。
    assert.deepEqual(
      await createRepository(connection).findExact("STORE-1", " tea-1 "),
      item({
        displayName: "Remote tea",
        retailPriceCents: 125,
        rowVersion: "remote-v2",
      }),
    );
  });
});

test("精确查码把等值谓词下推到目录候选分支并使用索引", async () => {
  const connection = new ExactLookupPlanConnection(
    new DatabaseSync(":memory:"),
  );
  try {
    await applyMigrations(connection, () => T0);
    await insertSnapshot(connection, "snapshot-1", "active");
    await insertCatalogItem(connection, "snapshot-1", item());

    const result = await createRepository(connection).findExact(
      "STORE-1",
      "TEA-1",
    );

    assert.equal(result?.lookupCodeNormalized, "TEA-1");
    assert.ok(connection.exactLookupPlan.length > 0);
    assert.equal(
      connection.exactLookupPlan.some((detail) => /\bSCAN items\b/.test(detail)),
      false,
      connection.exactLookupPlan.join("\n"),
    );
  } finally {
    await connection.close();
  }
});

test("同一物理 active 原地换代后旧覆盖立即隔离，在途旧代次写入被拒绝", async () => {
  await withMigratedDatabase(async (connection) => {
    await insertSnapshot(connection, "snapshot-1", "active");
    await insertCatalogItem(
      connection,
      "snapshot-1",
      item({ displayName: "Active catalog tea" }),
    );
    const repository = createRepository(connection);

    assert.equal(await repository.getActiveSnapshotId(), "snapshot-1");
    assert.equal(
      await repository.upsert({
        baseSnapshotId: "snapshot-1",
        item: item({ displayName: "Old generation remote tea" }),
      }),
      "applied",
    );

    await connection.run(
      `UPDATE catalog_snapshots
       SET generation_id = 'generation-2',
           catalog_version = 'version-2'
       WHERE snapshot_id = 'snapshot-1'`,
    );

    assert.equal(await repository.getActiveSnapshotId(), "generation-2");
    assert.equal(
      (await repository.findExact("STORE-1", "TEA-1"))?.displayName,
      "Active catalog tea",
    );
    assert.equal(
      await repository.upsert({
        baseSnapshotId: "snapshot-1",
        item: item({ displayName: "Late old result" }),
      }),
      "stale-generation",
    );
    assert.equal(
      await repository.upsert({
        baseSnapshotId: "generation-2",
        item: item({ displayName: "Current generation remote tea" }),
      }),
      "applied",
    );
    assert.equal(await repository.cleanupOldGenerations(), 1);
  });
});

test("远程不存在写入 tombstone 并屏蔽同代次本地快照商品", async () => {
  await withMigratedDatabase(async (connection) => {
    await insertSnapshot(connection, "snapshot-1", "active");
    await insertCatalogItem(connection, "snapshot-1", item());
    const repository = createRepository(connection);

    assert.equal(
      await repository.tombstone({
        baseSnapshotId: "snapshot-1",
        storeCode: "STORE-1",
        lookupCodeNormalized: "TEA-1",
      }),
      "applied",
    );
    assert.equal(await repository.findExact("STORE-1", "TEA-1"), null);

    const stored = await connection.getFirst<{
      record_kind: string;
      product_code: string | null;
    }>(
      `SELECT record_kind, product_code
       FROM catalog_lookup_overlays
       WHERE base_snapshot_id = 'snapshot-1'
         AND store_code = 'STORE-1'
         AND lookup_code_normalized = 'TEA-1'`,
    );
    assert.equal(stored?.record_kind, "tombstone");
    assert.equal(stored?.product_code, null);
  });
});

test("目录换代后旧覆盖自动隔离，过期远程结果拒绝写入并可清理", async () => {
  await withMigratedDatabase(async (connection) => {
    await insertSnapshot(connection, "snapshot-1", "active");
    const repository = createRepository(connection);
    assert.equal(
      await repository.upsert({
        baseSnapshotId: "snapshot-1",
        item: item({ displayName: "Old remote tea" }),
      }),
      "applied",
    );

    await connection.run(
      `UPDATE catalog_snapshots
       SET state = 'retired'
       WHERE snapshot_id = 'snapshot-1'`,
    );
    await insertSnapshot(connection, "snapshot-2", "active");
    await insertCatalogItem(
      connection,
      "snapshot-2",
      item({
        displayName: "New snapshot tea",
        retailPriceCents: 140,
      }),
    );

    assert.deepEqual(
      await repository.findExact("STORE-1", "TEA-1"),
      item({
        displayName: "New snapshot tea",
        retailPriceCents: 140,
      }),
    );
    assert.equal(
      await repository.upsert({
        baseSnapshotId: "snapshot-1",
        item: item({ displayName: "Late old result" }),
      }),
      "stale-generation",
    );
    assert.equal(await repository.cleanupOldGenerations(), 1);
    assert.equal(
      Number(
        (
          await connection.getFirst<{ count: number | string }>(
            "SELECT COUNT(*) AS count FROM catalog_lookup_overlays",
          )
        )?.count ?? 0,
      ),
      0,
    );
  });
});

test("名称搜索以覆盖优先、tombstone 去重并保持稳定排序分页", async () => {
  await withMigratedDatabase(async (connection) => {
    await insertSnapshot(connection, "snapshot-1", "active");
    await insertCatalogItem(
      connection,
      "snapshot-1",
      item({
        lookupCode: "TEA-A",
        lookupCodeNormalized: "TEA-A",
        productCode: "P-A",
        displayName: "Apple tea",
        itemNumber: "A",
      }),
    );
    await insertCatalogItem(
      connection,
      "snapshot-1",
      item({
        lookupCode: "TEA-B",
        lookupCodeNormalized: "TEA-B",
        productCode: "P-B",
        displayName: "Berry tea",
        itemNumber: "B",
      }),
    );
    await insertCatalogItem(
      connection,
      "snapshot-1",
      item({
        lookupCode: "COFFEE-1",
        lookupCodeNormalized: "COFFEE-1",
        productCode: "P-C",
        displayName: "Coffee",
        itemNumber: "C",
      }),
    );
    await insertCatalogItem(
      connection,
      "snapshot-1",
      item({
        storeCode: "STORE-2",
        lookupCode: "TEA-X",
        lookupCodeNormalized: "TEA-X",
        productCode: "P-X",
        displayName: "Other store tea",
      }),
    );
    const repository = createRepository(connection);
    await repository.upsert({
      baseSnapshotId: "snapshot-1",
      item: item({
        lookupCode: "TEA-A",
        lookupCodeNormalized: "TEA-A",
        productCode: "P-A",
        displayName: "Zulu tea",
        itemNumber: "A",
      }),
    });
    await repository.tombstone({
      baseSnapshotId: "snapshot-1",
      storeCode: "STORE-1",
      lookupCodeNormalized: "TEA-B",
    });
    await repository.upsert({
      baseSnapshotId: "snapshot-1",
      item: item({
        lookupCode: "TEA-D",
        lookupCodeNormalized: "TEA-D",
        productCode: "P-D",
        displayName: "Mint tea",
        itemNumber: "D",
      }),
    });

    const all = await repository.searchByName("STORE-1", "tea", 10);
    assert.deepEqual(
      all.map((entry) => entry.displayName),
      ["Mint tea", "Zulu tea"],
    );
    assert.deepEqual(
      (
        await repository.searchByName("STORE-1", "tea", 1, 1)
      ).map((entry) => entry.displayName),
      ["Zulu tea"],
    );
  });
});

test("无 active 快照时使用独立代次，首次完整目录激活后自动失效", async () => {
  await withMigratedDatabase(async (connection) => {
    const repository = createRepository(connection);
    assert.equal(await repository.getActiveSnapshotId(), null);
    assert.equal(
      await repository.upsert({
        baseSnapshotId: null,
        item: item({ displayName: "Remote-only tea" }),
      }),
      "applied",
    );
    assert.equal(
      (await repository.findExact("STORE-1", "TEA-1"))?.displayName,
      "Remote-only tea",
    );

    await insertSnapshot(connection, "snapshot-1", "active");
    assert.equal(await repository.findExact("STORE-1", "TEA-1"), null);
    assert.equal(await repository.cleanupOldGenerations(), 1);
  });
});

test("精确查询和名称搜索在同一 SQLite 语句内选择 active 代次", async (context) => {
  const cases = [
    {
      name: "findExact",
      read: (repository: SqliteCatalogLookupOverlayRepository) =>
        repository.findExact("STORE-1", "TEA-1"),
    },
    {
      name: "searchByName",
      read: async (repository: SqliteCatalogLookupOverlayRepository) =>
        (await repository.searchByName("STORE-1", "tea", 10))[0] ?? null,
    },
  ] as const;

  for (const testCase of cases) {
    await context.test(testCase.name, async () => {
      const connection = new ActivateAfterActiveReadConnection(
        new DatabaseSync(":memory:"),
      );
      try {
        await applyMigrations(connection, () => T0);
        await insertSnapshot(connection, "snapshot-1", "active");
        await insertCatalogItem(
          connection,
          "snapshot-1",
          item({ displayName: "Old generation tea" }),
        );
        await insertSnapshot(connection, "snapshot-2", "retired");
        await insertCatalogItem(
          connection,
          "snapshot-2",
          item({ displayName: "New generation tea" }),
        );

        const result = await testCase.read(createRepository(connection));

        assert.equal(connection.didActivateBetweenReads, false);
        assert.equal(result?.displayName, "Old generation tea");
      } finally {
        await connection.close();
      }
    });
  }
});

test("PosDatabase 只暴露目录覆盖窄 facade，不泄露 SQLCipher 连接", async () => {
  const database = await PosDatabase.open({
    databaseName: "catalog-overlay-test.db",
    driver: new SystemSqliteDriver(),
    keyProvider: {
      async getOrCreateDatabaseKey() {
        return "ab".repeat(32);
      },
    },
    nowIso: () => T1,
  });
  try {
    assert.ok(
      database.catalogLookupOverlay() instanceof
        SqliteCatalogLookupOverlayRepository,
    );
  } finally {
    await database.close();
  }
});

class SystemSqliteDriver implements SqliteDriverPort {
  public async open(_databaseName: string): Promise<SqliteConnectionPort> {
    return new SystemSqliteConnection(new DatabaseSync(":memory:"));
  }
}

class SystemSqliteConnection implements SqliteConnectionPort {
  public constructor(private readonly database: DatabaseSync) {
    this.database.exec("PRAGMA foreign_keys = ON;");
  }

  public async exec(sql: string): Promise<void> {
    this.database.exec(sql);
  }

  public async run(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<SqlRunResult> {
    const result = this.database
      .prepare(sql)
      .run(...parameters.map(toSqlInputValue));
    return {
      changes: Number(result.changes),
      lastInsertRowId: Number(result.lastInsertRowid),
    };
  }

  public async getFirst<T extends object>(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<T | null> {
    // Node 内置 SQLite 不含 SQLCipher；仅为测试的精确探针提供有效版本。
    if (sql === "PRAGMA cipher_version;") {
      return { cipher_version: "4.6.1" } as unknown as T;
    }
    return (
      this.database
        .prepare(sql)
        .get(...parameters.map(toSqlInputValue)) as T | undefined
    ) ?? null;
  }

  public async getAll<T extends object>(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<readonly T[]> {
    return this.database
      .prepare(sql)
      .all(...parameters.map(toSqlInputValue)) as unknown as readonly T[];
  }

  public async withExclusiveTransaction<T>(
    operation: (transaction: SqliteConnectionPort) => Promise<T>,
  ): Promise<T> {
    this.database.exec("BEGIN IMMEDIATE;");
    const transaction = new TransactionConnection(this.database);
    try {
      const result = await operation(transaction);
      this.database.exec("COMMIT;");
      return result;
    } catch (error) {
      this.database.exec("ROLLBACK;");
      throw error;
    }
  }

  public async close(): Promise<void> {
    this.database.close();
  }
}

class TransactionConnection extends SystemSqliteConnection {
  public override withExclusiveTransaction<T>(): Promise<T> {
    return Promise.reject(new Error("Nested test transaction."));
  }

  public override close(): Promise<void> {
    return Promise.reject(new Error("Transaction cannot close database."));
  }
}

class ActivateAfterActiveReadConnection extends SystemSqliteConnection {
  public didActivateBetweenReads = false;
  private activationCompleted = false;

  public override async getAll<T extends object>(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<readonly T[]> {
    const rows = await super.getAll<T>(sql, parameters);
    if (
      !this.activationCompleted &&
      sql.trimStart().startsWith("SELECT snapshot_id") &&
      sql.includes("WHERE state = 'active'")
    ) {
      this.didActivateBetweenReads = true;
      this.activationCompleted = true;
      await this.run(
        `UPDATE catalog_snapshots
         SET state = 'retired'
         WHERE snapshot_id = 'snapshot-1'`,
      );
      await this.run(
        `UPDATE catalog_snapshots
         SET state = 'active'
         WHERE snapshot_id = 'snapshot-2'`,
      );
    }
    return rows;
  }
}

class ExactLookupPlanConnection extends SystemSqliteConnection {
  public exactLookupPlan: string[] = [];

  public override async getFirst<T extends object>(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<T | null> {
    if (sql.includes("LEFT JOIN candidate")) {
      const rows = await super.getAll<{ detail: string }>(
        `EXPLAIN QUERY PLAN ${sql}`,
        parameters,
      );
      this.exactLookupPlan = rows.map((row) => row.detail);
    }
    return super.getFirst<T>(sql, parameters);
  }
}

async function schemaVersion(
  connection: SqliteConnectionPort,
): Promise<number> {
  const row = await connection.getFirst<{ version: number | string | null }>(
    "SELECT MAX(version) AS version FROM schema_migrations",
  );
  return Number(row?.version ?? 0);
}

async function withDatabase(
  operation: (connection: SystemSqliteConnection) => Promise<void>,
): Promise<void> {
  const connection = new SystemSqliteConnection(new DatabaseSync(":memory:"));
  try {
    await operation(connection);
  } finally {
    await connection.close();
  }
}

function toSqlInputValue(value: SqlValue): SQLInputValue {
  return value as SQLInputValue;
}

async function withMigratedDatabase(
  operation: (connection: SystemSqliteConnection) => Promise<void>,
): Promise<void> {
  await withDatabase(async (connection) => {
    await applyMigrations(connection, () => T0);
    await operation(connection);
  });
}

function createRepository(
  connection: SqliteConnectionPort,
): SqliteCatalogLookupOverlayRepository {
  return new SqliteCatalogLookupOverlayRepository(connection, () => T1);
}

async function insertSnapshot(
  connection: SqliteConnectionPort,
  snapshotId: string,
  state: "active" | "retired",
): Promise<void> {
  await connection.run(
    `INSERT INTO catalog_snapshots (
       snapshot_id, catalog_version, checksum, state,
       downloaded_at_iso, activated_at_iso
     ) VALUES (?, ?, ?, ?, ?, ?)`,
    [snapshotId, `version-${snapshotId}`, `checksum-${snapshotId}`, state, T0, T0],
  );
}

async function insertCatalogItem(
  connection: SqliteConnectionPort,
  snapshotId: string,
  value: LocalCatalogMatch,
): Promise<void> {
  await connection.run(
    `INSERT INTO catalog_items (
       snapshot_id, store_code, lookup_code_normalized, product_code,
       reference_code, item_number, barcode, lookup_code, display_name,
       retail_price_cents, price_source, price_source_label, quantity_factor,
       tax_rate_basis_points, row_version, product_image, discount_rate,
       is_special_product, is_active, updated_at_iso
     ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 1, ?)`,
    [
      snapshotId,
      value.storeCode,
      value.lookupCodeNormalized,
      value.productCode,
      value.referenceCode,
      value.itemNumber,
      value.barcode,
      value.lookupCode,
      value.displayName,
      value.retailPriceCents,
      value.priceSource,
      value.priceSourceLabel,
      String(value.quantityFactor),
      value.taxRateBasisPoints,
      value.rowVersion,
      value.productImage,
      value.discountRate === null ? null : String(value.discountRate),
      value.isSpecialProduct ? 1 : 0,
      value.updatedAtIso,
    ],
  );
}

function item(
  overrides: Partial<LocalCatalogMatch> = {},
): LocalCatalogMatch {
  return {
    storeCode: "STORE-1",
    productCode: "P-TEA",
    referenceCode: null,
    itemNumber: "ITEM-TEA",
    displayName: "Tea",
    barcode: "012345",
    lookupCode: "TEA-1",
    lookupCodeNormalized: "TEA-1",
    retailPriceCents: 100,
    priceSource: 1,
    priceSourceLabel: "Barcode",
    quantityFactor: 1,
    taxRateBasisPoints: 1000,
    updatedAtIso: T0,
    rowVersion: "local-v1",
    productImage: null,
    discountRate: null,
    isSpecialProduct: false,
    ...overrides,
  };
}
