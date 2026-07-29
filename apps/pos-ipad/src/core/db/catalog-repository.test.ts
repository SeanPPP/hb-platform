import assert from "node:assert/strict";
import test from "node:test";

import { SqliteCatalogSnapshotRepository } from "./catalog-repository";
import type { SqliteConnectionPort, SqlRunResult, SqlValue } from "./types";

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
  assert.equal(entries.length, 2);
  assert.match(entries[0]?.sql ?? "", /store_code, lookup_code_normalized/);
  assert.deepEqual(entries[0]?.parameters.slice(0, 4), ["staging", "S1", "0123", "P1"]);
  assert.deepEqual(entries[1]?.parameters.slice(0, 4), ["staging", "S1", "SET-01", "P1"]);
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
