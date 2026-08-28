import assert from "node:assert/strict";
import { mkdtemp, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { DatabaseSync, type SQLInputValue } from "node:sqlite";
import test from "node:test";

import {
  SqliteCatalogSnapshotRepository,
  type CatalogStoredItem,
} from "./catalog-repository";
import { applyMigrations } from "./migrations";
import type {
  SqliteConnectionPort,
  SqlRunResult,
  SqlValue,
} from "@hb/pos-db/core/db/types";

class RecordingConnection implements SqliteConnectionPort {
  public readonly runs: { sql: string; parameters: readonly SqlValue[] }[] = [];
  public transactions = 0;
  public async exec(): Promise<void> {}
  public async run(sql: string, parameters: readonly SqlValue[] = []): Promise<SqlRunResult> {
    this.runs.push({ sql, parameters });
    return { changes: 1, lastInsertRowId: 0 };
  }
  public async getFirst<T extends object>(sql: string): Promise<T | null> {
    if (sql.includes("COUNT(*)")) return { item_count: 2 } as T;
    if (sql.includes("SELECT state FROM catalog_snapshots")) return { state: "staging" } as T;
    return null;
  }
  public async getAll<T extends object>(_sql: string, _parameters?: readonly SqlValue[]): Promise<readonly T[]> { return []; }
  public async withExclusiveTransaction<T>(operation: (transaction: SqliteConnectionPort) => Promise<T>): Promise<T> {
    this.transactions += 1;
    return operation(this);
  }
  public async close(): Promise<void> {}
}

class LookupConnection extends RecordingConnection {
  public queries: string[] = [];
  public override async getFirst<T extends object>(sql: string): Promise<T | null> {
    this.queries.push(sql);
    if (sql.includes("i.lookup_code_normalized = ?")) {
      return {
        store_code: "S1",
        product_code: "BARCODE",
        reference_code: null,
        item_number: "I1",
        display_name: "Exact barcode",
        barcode: "0123",
        lookup_code: "0123",
        lookup_code_normalized: "0123",
        price_cents: 150,
        price_source: 1,
        price_source_label: "Barcode",
        quantity_factor: "1",
        tax_rate_basis_points: null,
        updated_at_iso: null,
        row_version: null,
        product_image: null,
        discount_rate_basis_points: null,
        is_special_product: 0,
      } as T;
    }
    return {
      store_code: "S1",
      product_code: "PRODUCT",
      reference_code: null,
      item_number: "0123",
      display_name: "Product code",
      barcode: null,
      lookup_code: "I1",
      lookup_code_normalized: "I1",
      price_cents: 200,
      price_source: 0,
      price_source_label: "Default",
      quantity_factor: "1",
      tax_rate_basis_points: null,
      updated_at_iso: null,
      row_version: null,
      product_image: null,
      discount_rate_basis_points: null,
      is_special_product: 0,
    } as T;
  }
  public override async getAll<T extends object>(sql: string): Promise<readonly T[]> {
    this.queries.push(sql);
    return [
      { store_code: "S1", product_code: "A", reference_code: null, item_number: null, display_name: "Same", barcode: null, lookup_code: "A-1", lookup_code_normalized: "A-1", price_cents: 100, price_source: 0, price_source_label: "Default", quantity_factor: "1", tax_rate_basis_points: null, updated_at_iso: null, row_version: null, product_image: null, discount_rate_basis_points: null, is_special_product: 0 },
      { store_code: "S1", product_code: "B", reference_code: null, item_number: null, display_name: "Same", barcode: null, lookup_code: "B-1", lookup_code_normalized: "B-1", price_cents: 100, price_source: 0, price_source_label: "Default", quantity_factor: "1", tax_rate_basis_points: null, updated_at_iso: null, row_version: null, product_image: null, discount_rate_basis_points: null, is_special_product: 0 },
    ] as T[];
  }
}

class ActiveCollisionConnection extends RecordingConnection {
  public override async getFirst<T extends object>(sql: string): Promise<T | null> {
    if (sql.includes("SELECT state FROM catalog_snapshots")) return { state: "active" } as T;
    return null;
  }
}

class PromotionConnection extends RecordingConnection {
  public readonly queries: {
    sql: string;
    parameters: readonly SqlValue[];
  }[] = [];

  public constructor(
    private readonly scopeRows: readonly Record<string, unknown>[],
    private readonly promotionRows: readonly Record<string, unknown>[],
  ) {
    super();
  }

  public override async getAll<T extends object>(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<readonly T[]> {
    this.queries.push({ sql, parameters });
    if (sql.includes("FROM catalog_snapshots")) {
      return this.scopeRows as T[];
    }
    if (sql.includes("FROM catalog_promotions")) {
      return this.promotionRows as T[];
    }
    throw new Error(`Unexpected query: ${sql}`);
  }
}

test("目录 activate 在单个独占事务内先退役旧快照再启用已完整校验的新快照", async () => {
  const connection = new RecordingConnection();
  const repository = new SqliteCatalogSnapshotRepository(connection);

  await repository.activate("new", 2, "2026-07-28T00:00:00.000Z");

  assert.equal(connection.transactions, 1);
  assert.match(connection.runs[0]?.sql ?? "", /UPDATE catalog_snapshots SET state = 'retired'/);
  assert.match(connection.runs[1]?.sql ?? "", /UPDATE catalog_snapshots[\s\S]*state = 'active'/);
});

test("暂存页以快照和规范化售卖码为身份，保留同商品的多条售卖项", async () => {
  const connection = new RecordingConnection();
  const repository = new SqliteCatalogSnapshotRepository(connection);

  await repository.appendPage("staging", [
    {
      storeCode: "S1", productCode: "P1", referenceCode: null, itemNumber: "I1", displayName: "Item", barcode: "0123",
      lookupCode: "0123", lookupCodeNormalized: "0123", retailPriceCents: 100, priceSource: 1, priceSourceLabel: "Barcode",
      quantityFactor: 1, taxRateBasisPoints: null, updatedAtIso: null, rowVersion: null, productImage: null, discountRate: null,
      isSpecialProduct: false,
    },
    {
      storeCode: "S1", productCode: "P1", referenceCode: "SET-P1", itemNumber: "I1", displayName: "Item set", barcode: null,
      lookupCode: "SET-01", lookupCodeNormalized: "SET-01", retailPriceCents: 250, priceSource: 3, priceSourceLabel: "Set",
      quantityFactor: 2, taxRateBasisPoints: null, updatedAtIso: null, rowVersion: "v2", productImage: null, discountRate: 0.05,
      isSpecialProduct: true,
    },
  ]);

  const entries = connection.runs.filter((entry) => entry.sql.includes("INSERT INTO catalog_items ("));
  // 中文注释：批量写入后一次 run 携带全部商品的多行 VALUES，身份参数按 19 参数/行连续排列。
  assert.equal(entries.length, 1);
  assert.match(entries[0]?.sql ?? "", /store_code, lookup_code_normalized/);
  assert.match(entries[0]?.sql ?? "", /VALUES\s*\([^)]*\),\s*\(/);
  assert.deepEqual(entries[0]?.parameters.slice(0, 4), ["staging", "S1", "0123", "P1"]);
  assert.deepEqual(entries[0]?.parameters.slice(19, 23), ["staging", "S1", "SET-01", "P1"]);
  assert.equal(connection.runs.some((entry) => entry.sql.includes("INSERT INTO catalog_barcodes")), false);
});

test("快照 ID 碰撞 active 时拒绝开始暂存，绝不删除可收银目录", async () => {
  const connection = new ActiveCollisionConnection();
  const repository = new SqliteCatalogSnapshotRepository(connection);

  await assert.rejects(
    () => repository.beginStaging({ snapshotId: "active", catalogVersion: "v1", checksum: "x", downloadedAtIso: "2026-07-28T00:00:00.000Z" }),
    /collision/i,
  );
  assert.equal(connection.runs.length, 0);
});

test("本地扫码按规范化售卖码精确命中，并按名称、货号、售卖码稳定分页排序", async () => {
  const connection = new LookupConnection();
  const repository = new SqliteCatalogSnapshotRepository(connection);

  const exact = await repository.findExact("0123");
  const named = await repository.searchByName("same", 20, 0);

  assert.equal(exact?.productCode, "BARCODE");
  assert.deepEqual(named.map((entry) => entry.productCode), ["A", "B"]);
  assert.ok(connection.queries.some((sql) => sql.includes("i.lookup_code_normalized = ?")));
  assert.ok(connection.queries.some((sql) => sql.includes("ORDER BY i.display_name COLLATE NOCASE ASC, COALESCE(i.item_number, '') COLLATE NOCASE ASC, i.lookup_code_normalized ASC")));
});

test("活动促销只从已证明归属门店的唯一 active 快照稳定读取", async () => {
  const connection = new PromotionConnection(
    [{ snapshot_id: "snapshot-1", store_code: "S1" }],
    [
      {
        snapshot_id: "snapshot-1",
        promotion_id: "PROMO-10",
        definition_json: '{"promotionId":"PROMO-10"}',
        priority: 10,
      },
      {
        snapshot_id: "snapshot-1",
        promotion_id: "PROMO-20",
        definition_json: '{"promotionId":"PROMO-20"}',
        priority: 20,
      },
    ],
  );
  const repository = new SqliteCatalogSnapshotRepository(connection);

  const snapshot = await repository.loadActivePromotions("S1");

  assert.deepEqual(snapshot, {
    snapshotId: "snapshot-1",
    storeCode: "S1",
    promotions: [
      { promotionId: "PROMO-10", definitionJson: '{"promotionId":"PROMO-10"}' },
      { promotionId: "PROMO-20", definitionJson: '{"promotionId":"PROMO-20"}' },
    ],
  });
  assert.deepEqual(connection.queries[0]?.parameters, ["S1", "S1"]);
  assert.match(connection.queries[0]?.sql ?? "", /EXISTS[\s\S]*catalog_items[\s\S]*items\.store_code = \?/);
  assert.match(connection.queries[1]?.sql ?? "", /ORDER BY priority ASC, promotion_id ASC/);
});

test("多个 active 快照或跨门店目录归属均 fail-closed", async () => {
  const multipleActive = new SqliteCatalogSnapshotRepository(
    new PromotionConnection(
      [
        { snapshot_id: "snapshot-1", store_code: "S1" },
        { snapshot_id: "snapshot-2", store_code: "S1" },
      ],
      [],
    ),
  );
  await assert.rejects(
    () => multipleActive.loadActivePromotions("S1"),
    /multiple active/i,
  );

  const crossStore = new SqliteCatalogSnapshotRepository(
    new PromotionConnection([{ snapshot_id: "snapshot-1", store_code: "S2" }], []),
  );
  await assert.rejects(
    () => crossStore.loadActivePromotions("S1"),
    /store/i,
  );

  const noStoreMembershipConnection = new PromotionConnection(
    [{ snapshot_id: "snapshot-1", store_code: null }],
    [],
  );
  const noStoreMembership = new SqliteCatalogSnapshotRepository(
    noStoreMembershipConnection,
  );
  assert.equal(await noStoreMembership.loadActivePromotions("S1"), null);
  assert.equal(noStoreMembershipConnection.queries.length, 1);
});

test("损坏的促销行 fail-closed，绝不把另一快照或空定义带入购物车", async () => {
  const wrongSnapshot = new SqliteCatalogSnapshotRepository(
    new PromotionConnection(
      [{ snapshot_id: "snapshot-1", store_code: "S1" }],
      [
        {
          snapshot_id: "snapshot-2",
          promotion_id: "PROMO-1",
          definition_json: '{"promotionId":"PROMO-1"}',
          priority: 1,
        },
      ],
    ),
  );
  await assert.rejects(
    () => wrongSnapshot.loadActivePromotions("S1"),
    /snapshot/i,
  );

  const emptyDefinition = new SqliteCatalogSnapshotRepository(
    new PromotionConnection(
      [{ snapshot_id: "snapshot-1", store_code: "S1" }],
      [
        {
          snapshot_id: "snapshot-1",
          promotion_id: "PROMO-1",
          definition_json: " ",
          priority: 1,
        },
      ],
    ),
  );
  await assert.rejects(
    () => emptyDefinition.loadActivePromotions("S1"),
    /definition/i,
  );
});

test("delta staging 只保存变更，并在单事务原地更新 active 物理快照", async () => {
  await withRealCatalogDatabase(async (connection) => {
    await seedSnapshot(connection, {
      snapshotId: "physical-active",
      catalogVersion: "v1",
      state: "active",
    });
    await seedCatalogItem(
      connection,
      "physical-active",
      storedItem({ lookupCodeNormalized: "A", lookupCode: "A" }),
    );
    await seedCatalogItem(
      connection,
      "physical-active",
      storedItem({
        lookupCodeNormalized: "B",
        lookupCode: "B",
        isSpecialProduct: true,
      }),
    );
    await seedCatalogItem(
      connection,
      "physical-active",
      storedItem({ lookupCodeNormalized: "C", lookupCode: "C" }),
    );
    await connection.run(
      `INSERT INTO catalog_promotions (
         snapshot_id, promotion_id, definition_json,
         valid_from_iso, valid_until_iso, priority
       ) VALUES (?, 'OLD', '{"promotionId":"OLD"}', NULL, NULL, 1)`,
      ["physical-active"],
    );
    const repository = new SqliteCatalogSnapshotRepository(connection);

    await repository.beginDeltaStaging({
      sourceSnapshotId: "physical-active",
      baseCatalogVersion: "v1",
      snapshotId: "generation-2",
      catalogVersion: "v2",
      checksum: "delta-v2",
      downloadedAtIso: "2026-07-30T00:00:00.000Z",
    });
    assert.equal(
      await scalarCount(
        connection,
        "SELECT COUNT(*) AS count FROM catalog_items WHERE snapshot_id = 'generation-2'",
      ),
      0,
    );

    await repository.appendDeltaBatch("generation-2", {
      items: [
        storedItem({
          lookupCodeNormalized: "A",
          lookupCode: "A",
          displayName: "A updated",
          isSpecialProduct: true,
        }),
        storedItem({
          lookupCodeNormalized: "D",
          lookupCode: "D",
          displayName: "D new",
        }),
      ],
      deletedLookups: [
        { storeCode: "S1", lookupCodeNormalized: "B" },
      ],
    });
    await repository.replacePromotions("generation-2", [
      {
        promotionId: "NEW",
        definitionJson: '{"promotionId":"NEW"}',
        validFromIso: null,
        validUntilIso: null,
        priority: 1,
      },
    ]);

    const active = await repository.activateDelta({
      sourceSnapshotId: "physical-active",
      baseCatalogVersion: "v1",
      stagingSnapshotId: "generation-2",
      expectedItemCount: 3,
      activatedAtIso: "2026-07-30T01:00:00.000Z",
    });

    assert.equal(active.snapshotId, "physical-active");
    assert.equal(active.generationId, "generation-2");
    assert.equal(active.catalogVersion, "v2");
    assert.equal(active.itemCount, 3);
    assert.equal(
      (
        await connection.getFirst<{ generation_id: string }>(
          `SELECT generation_id
           FROM catalog_snapshots
           WHERE snapshot_id = 'physical-active'`,
        )
      )?.generation_id,
      "generation-2",
    );
    assert.deepEqual(
      (
        await connection.getAll<{
          lookup_code_normalized: string;
          display_name: string;
        }>(
          `SELECT lookup_code_normalized, display_name
           FROM catalog_items
           WHERE snapshot_id = 'physical-active'
           ORDER BY lookup_code_normalized`,
        )
      ).map((row) => [row.lookup_code_normalized, row.display_name]),
      [
        ["A", "A updated"],
        ["C", "Item C"],
        ["D", "D new"],
      ],
    );
    assert.deepEqual(
      (
        await connection.getAll<{ lookup_code_normalized: string }>(
          `SELECT lookup_code_normalized
           FROM special_products
           WHERE snapshot_id = 'physical-active'
           ORDER BY lookup_code_normalized`,
        )
      ).map((row) => ({ ...row })),
      [{ lookup_code_normalized: "A" }],
    );
    assert.deepEqual(
      (
        await connection.getAll<{ promotion_id: string }>(
          `SELECT promotion_id
           FROM catalog_promotions
           WHERE snapshot_id = 'physical-active'`,
        )
      ).map((row) => ({ ...row })),
      [{ promotion_id: "NEW" }],
    );
    assert.equal(
      await scalarCount(
        connection,
        "SELECT COUNT(*) AS count FROM catalog_snapshots WHERE snapshot_id = 'generation-2'",
      ),
      0,
    );
    assert.equal(
      await scalarCount(
        connection,
        "SELECT COUNT(*) AS count FROM catalog_delta_deletions",
      ),
      0,
    );
  });
});

test("delta 数量或基线校验失败时完整回滚并保留旧 active", async () => {
  await withRealCatalogDatabase(async (connection) => {
    await seedSnapshot(connection, {
      snapshotId: "physical-active",
      catalogVersion: "v1",
      state: "active",
    });
    await seedCatalogItem(connection, "physical-active", storedItem());
    const repository = new SqliteCatalogSnapshotRepository(connection);
    await repository.beginDeltaStaging({
      sourceSnapshotId: "physical-active",
      baseCatalogVersion: "v1",
      snapshotId: "generation-2",
      catalogVersion: "v2",
      checksum: "delta-v2",
      downloadedAtIso: "2026-07-30T00:00:00.000Z",
    });
    await repository.appendDeltaBatch("generation-2", {
      items: [],
      deletedLookups: [
        { storeCode: "S1", lookupCodeNormalized: "ITEM" },
      ],
    });

    await assert.rejects(
      () =>
        repository.activateDelta({
          sourceSnapshotId: "physical-active",
          baseCatalogVersion: "v1",
          stagingSnapshotId: "generation-2",
          expectedItemCount: 99,
          activatedAtIso: "2026-07-30T01:00:00.000Z",
        }),
      /count/i,
    );
    const activeAfterRollback = await connection.getFirst<{
        catalog_version: string;
        generation_id: string;
        state: string;
      }>(
        `SELECT catalog_version, generation_id, state
         FROM catalog_snapshots
         WHERE snapshot_id = 'physical-active'`,
      );
    assert.deepEqual(
      activeAfterRollback ? { ...activeAfterRollback } : null,
      {
        catalog_version: "v1",
        generation_id: "physical-active",
        state: "active",
      },
    );
    assert.equal(
      await scalarCount(
        connection,
        "SELECT COUNT(*) AS count FROM catalog_items WHERE snapshot_id = 'physical-active'",
      ),
      1,
    );
    assert.equal(
      await scalarCount(
        connection,
        "SELECT COUNT(*) AS count FROM catalog_delta_deletions WHERE snapshot_id = 'generation-2'",
      ),
      1,
    );

    await connection.run(
      `UPDATE catalog_snapshots
       SET catalog_version = 'v1-replaced'
       WHERE snapshot_id = 'physical-active'`,
    );
    await assert.rejects(
      () =>
        repository.activateDelta({
          sourceSnapshotId: "physical-active",
          baseCatalogVersion: "v1",
          stagingSnapshotId: "generation-2",
          expectedItemCount: 0,
          activatedAtIso: "2026-07-30T01:00:00.000Z",
        }),
      (error: unknown) => {
        assert.equal(
          (error as Readonly<{ code?: unknown }>).code,
          "CATALOG_DELTA_BASE_CHANGED",
        );
        return true;
      },
    );
    assert.equal(
      (
        await connection.getFirst<{ catalog_version: string }>(
          `SELECT catalog_version
           FROM catalog_snapshots
           WHERE snapshot_id = 'physical-active'`,
        )
      )?.catalog_version,
      "v1-replaced",
    );
  });
});

test("retired janitor 每次最多回收 500 行并最终只保留当前完整目录", async () => {
  await withRealCatalogDatabase(async (connection) => {
    await seedSnapshot(connection, {
      snapshotId: "retired-large",
      catalogVersion: "v1",
      state: "retired",
    });
    for (let index = 0; index < 501; index += 1) {
      const lookup = `ITEM-${String(index).padStart(3, "0")}`;
      await seedCatalogItem(
        connection,
        "retired-large",
        storedItem({ lookupCode: lookup, lookupCodeNormalized: lookup }),
      );
    }
    await seedSnapshot(connection, {
      snapshotId: "current",
      catalogVersion: "v2",
      state: "active",
    });
    await seedCatalogItem(connection, "current", storedItem());
    const repository = new SqliteCatalogSnapshotRepository(connection);

    assert.equal(await repository.cleanupRetiredBatch(500), 500);
    assert.equal(
      await scalarCount(
        connection,
        "SELECT COUNT(*) AS count FROM catalog_items WHERE snapshot_id = 'retired-large'",
      ),
      1,
    );
    assert.equal(
      await scalarCount(
        connection,
        "SELECT COUNT(*) AS count FROM catalog_snapshots WHERE snapshot_id = 'retired-large'",
      ),
      1,
    );

    assert.equal(await repository.cleanupRetiredBatch(500), 2);
    assert.equal(await repository.cleanupRetiredBatch(500), 0);
    assert.deepEqual(
      (
        await connection.getAll<{ snapshot_id: string; state: string }>(
          "SELECT snapshot_id, state FROM catalog_snapshots ORDER BY snapshot_id",
        )
      ).map((row) => ({ ...row })),
      [{ snapshot_id: "current", state: "active" }],
    );
  });
});

test("staging janitor 断电重开后分批清理残留，绝不删除 active 或 retired", async () => {
  const directory = await mkdtemp(join(tmpdir(), "hbpos-catalog-staging-"));
  const databasePath = join(directory, "catalog.sqlite");
  let connection: RealCatalogConnection | null = null;
  try {
    connection = new RealCatalogConnection(new DatabaseSync(databasePath));
    await applyMigrations(connection, () => "2026-07-30T00:00:00.000Z");
    await seedSnapshot(connection, {
      snapshotId: "active-current",
      catalogVersion: "v2",
      state: "active",
    });
    await seedCatalogItem(connection, "active-current", storedItem());
    await seedSnapshot(connection, {
      snapshotId: "staging-crashed",
      catalogVersion: "v3",
      state: "staging",
    });
    await seedSnapshot(connection, {
      snapshotId: "retired-history",
      catalogVersion: "v1",
      state: "retired",
    });
    for (let index = 0; index < 501; index += 1) {
      const lookup = `STAGING-${String(index).padStart(3, "0")}`;
      await seedCatalogItem(
        connection,
        "staging-crashed",
        storedItem({ lookupCode: lookup, lookupCodeNormalized: lookup }),
      );
    }
    await connection.close();
    connection = new RealCatalogConnection(new DatabaseSync(databasePath));
    const repository = new SqliteCatalogSnapshotRepository(connection);

    assert.equal(
      await repository.discardStagingBatch("staging-crashed", 500),
      500,
    );
    assert.equal(
      await scalarCount(
        connection,
        "SELECT COUNT(*) AS count FROM catalog_items WHERE snapshot_id = 'active-current'",
      ),
      1,
    );
    assert.equal(
      await scalarCount(
        connection,
        "SELECT COUNT(*) AS count FROM catalog_snapshots WHERE snapshot_id = 'retired-history'",
      ),
      1,
    );
    assert.equal(await repository.cleanupStagingBatch(500), 2);
    assert.equal(await repository.cleanupStagingBatch(500), 0);
    assert.equal(
      await scalarCount(
        connection,
        "SELECT COUNT(*) AS count FROM catalog_snapshots WHERE state = 'staging'",
      ),
      0,
    );
  } finally {
    await connection?.close();
    await rm(directory, { recursive: true, force: true });
  }
});

class RealCatalogConnection implements SqliteConnectionPort {
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
    const transaction = new RealCatalogTransaction(this.database);
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

class RealCatalogTransaction extends RealCatalogConnection {
  public override withExclusiveTransaction<T>(): Promise<T> {
    return Promise.reject(new Error("Nested catalog test transaction."));
  }

  public override close(): Promise<void> {
    return Promise.reject(new Error("Transaction cannot close catalog database."));
  }
}

async function withRealCatalogDatabase(
  operation: (connection: RealCatalogConnection) => Promise<void>,
): Promise<void> {
  const connection = new RealCatalogConnection(new DatabaseSync(":memory:"));
  try {
    await applyMigrations(connection, () => "2026-07-30T00:00:00.000Z");
    await operation(connection);
  } finally {
    await connection.close();
  }
}

function toSqlInputValue(value: SqlValue): SQLInputValue {
  return value as SQLInputValue;
}

async function seedSnapshot(
  connection: SqliteConnectionPort,
  input: Readonly<{
    snapshotId: string;
    catalogVersion: string;
    state: "staging" | "active" | "retired";
  }>,
): Promise<void> {
  await connection.run(
    `INSERT INTO catalog_snapshots (
       snapshot_id, catalog_version, checksum, state,
       downloaded_at_iso, activated_at_iso, generation_id,
       sync_mode, base_snapshot_id, base_catalog_version
     ) VALUES (?, ?, ?, ?, ?, ?, ?, 'full', NULL, NULL)`,
    [
      input.snapshotId,
      input.catalogVersion,
      `checksum-${input.catalogVersion}`,
      input.state,
      "2026-07-30T00:00:00.000Z",
      "2026-07-30T00:00:00.000Z",
      input.snapshotId,
    ],
  );
}

async function seedCatalogItem(
  connection: SqliteConnectionPort,
  snapshotId: string,
  item: CatalogStoredItem,
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
      String(item.quantityFactor),
      item.taxRateBasisPoints,
      item.rowVersion,
      item.productImage,
      item.discountRate === null ? null : String(item.discountRate),
      item.isSpecialProduct ? 1 : 0,
      item.updatedAtIso,
    ],
  );
  if (item.isSpecialProduct) {
    await connection.run(
      `INSERT INTO special_products (
         snapshot_id, store_code, lookup_code_normalized,
         sort_order, is_marked, updated_at_iso
       ) VALUES (?, ?, ?, 0, 1, ?)`,
      [
        snapshotId,
        item.storeCode,
        item.lookupCodeNormalized,
        item.updatedAtIso,
      ],
    );
  }
}

function storedItem(
  overrides: Partial<CatalogStoredItem> = {},
): CatalogStoredItem {
  const lookupCodeNormalized = overrides.lookupCodeNormalized ?? "ITEM";
  return {
    storeCode: "S1",
    productCode: `P-${lookupCodeNormalized}`,
    referenceCode: null,
    itemNumber: null,
    displayName: `Item ${lookupCodeNormalized}`,
    barcode: null,
    lookupCode: lookupCodeNormalized,
    lookupCodeNormalized,
    retailPriceCents: 100,
    priceSource: 0,
    priceSourceLabel: "product",
    quantityFactor: 1,
    taxRateBasisPoints: null,
    updatedAtIso: "2026-07-30T00:00:00.000Z",
    rowVersion: null,
    productImage: null,
    discountRate: null,
    isSpecialProduct: false,
    ...overrides,
  };
}

async function scalarCount(
  connection: SqliteConnectionPort,
  sql: string,
): Promise<number> {
  const row = await connection.getFirst<{ count: number | string }>(sql);
  return Number(row?.count ?? 0);
}

test("appendPage 按 50 行/批多行写入，0 条不执行 SQL，参数数严格等于 19×行数", async () => {
  const connection = new RecordingConnection();
  const repository = new SqliteCatalogSnapshotRepository(connection);

  await repository.appendPage("staging", []);
  assert.equal(connection.runs.length, 0);

  let before = connection.runs.length;
  await repository.appendPage("staging", [storedItem({ lookupCodeNormalized: "L1" })]);
  let itemRuns = connection.runs.slice(before).filter((entry) => entry.sql.includes("INSERT INTO catalog_items ("));
  assert.equal(itemRuns.length, 1);
  assert.equal(itemRuns[0]?.parameters.length, 19);
  assert.equal((itemRuns[0]?.sql.match(/\?/g) ?? []).length, 19);

  before = connection.runs.length;
  await repository.appendPage(
    "staging",
    Array.from({ length: 51 }, (_, index) => storedItem({ lookupCodeNormalized: `L${index}` })),
  );
  itemRuns = connection.runs.slice(before).filter((entry) => entry.sql.includes("INSERT INTO catalog_items ("));
  // 中文注释：51 条拆为 50 + 1 两批多行 VALUES。
  assert.equal(itemRuns.length, 2);
  assert.equal(itemRuns[0]?.parameters.length, 50 * 19);
  assert.equal(itemRuns[1]?.parameters.length, 1 * 19);
  assert.equal(
    itemRuns.reduce((sum, entry) => sum + entry.parameters.length, 0),
    51 * 19,
  );

  before = connection.runs.length;
  await repository.appendPage(
    "staging",
    Array.from({ length: 500 }, (_, index) => storedItem({ lookupCodeNormalized: `M${index}` })),
  );
  itemRuns = connection.runs.slice(before).filter((entry) => entry.sql.includes("INSERT INTO catalog_items ("));
  assert.equal(itemRuns.length, 10);
  assert.equal(
    itemRuns.reduce((sum, entry) => sum + entry.parameters.length, 0),
    500 * 19,
  );
});

test("appendPage 的特殊商品多行批量写入，含特殊标记的商品按 4 参数/行写入", async () => {
  const connection = new RecordingConnection();
  const repository = new SqliteCatalogSnapshotRepository(connection);

  await repository.appendPage("staging", [
    storedItem({ lookupCodeNormalized: "SP1", isSpecialProduct: true, updatedAtIso: "2026-07-30T00:00:00.000Z" }),
    storedItem({ lookupCodeNormalized: "SP2", isSpecialProduct: true }),
    storedItem({ lookupCodeNormalized: "N1" }),
  ]);

  const specialRuns = connection.runs.filter((entry) => entry.sql.includes("INSERT INTO special_products ("));
  assert.equal(specialRuns.length, 1);
  assert.equal(specialRuns[0]?.parameters.length, 2 * 4);
  assert.deepEqual(specialRuns[0]?.parameters.slice(0, 4), ["staging", "S1", "SP1", "2026-07-30T00:00:00.000Z"]);
});

test("appendDeltaBatch 的 upsert 与特殊商品走批量写入，删除操作保持逐条", async () => {
  // 中文注释：appendDeltaBatch 先校验 staging 的 delta 资格，需模拟 sync_mode=delta 的元数据行。
  class DeltaEligibleConnection extends RecordingConnection {
    public override async getFirst<T extends object>(sql: string): Promise<T | null> {
      if (sql.includes("SELECT state, sync_mode")) {
        return { state: "staging", sync_mode: "delta" } as T;
      }
      return super.getFirst(sql);
    }
  }
  const connection = new DeltaEligibleConnection();
  const repository = new SqliteCatalogSnapshotRepository(connection);

  await repository.appendDeltaBatch("staging-delta", {
    items: [
      storedItem({ lookupCodeNormalized: "D1", isSpecialProduct: true }),
      storedItem({ lookupCodeNormalized: "D2" }),
    ],
    deletedLookups: [{ storeCode: "S1", lookupCodeNormalized: "GONE" }],
  });

  const upsertRuns = connection.runs.filter((entry) => entry.sql.includes("INSERT INTO catalog_items ("));
  assert.equal(upsertRuns.length, 1);
  assert.equal(upsertRuns[0]?.parameters.length, 2 * 19);
  assert.match(upsertRuns[0]?.sql ?? "", /ON CONFLICT[\s\S]*DO UPDATE SET/);
  const specialRuns = connection.runs.filter((entry) => entry.sql.includes("INSERT INTO special_products ("));
  assert.equal(specialRuns.length, 1);
  assert.equal(specialRuns[0]?.parameters.length, 1 * 4);
  // 中文注释：每个 upsert 商品前都要清理 delta_deletions 与特殊商品旧行，保持逐条语义；
  // 额外 1 次 special_products 清理来自 deletedLookup 的 tombstone 路径。
  const deletionRuns = connection.runs.filter((entry) => entry.sql.includes("DELETE FROM catalog_delta_deletions"));
  assert.equal(deletionRuns.length, 2);
  const specialDeletionRuns = connection.runs.filter((entry) => entry.sql.includes("DELETE FROM special_products"));
  assert.equal(specialDeletionRuns.length, 3);
  const tombstoneRuns = connection.runs.filter((entry) => entry.sql.includes("INSERT INTO catalog_delta_deletions"));
  assert.equal(tombstoneRuns.length, 1);
});
