import assert from "node:assert/strict";
import { DatabaseSync, type SQLInputValue } from "node:sqlite";
import test from "node:test";

import type { SpecialProductItem } from "../contracts/special-products";

import { applyMigrations, POS_DATABASE_MIGRATIONS } from "./migrations";
import { SqliteSpecialProductsRepository } from "./sqlite-special-products-repository";
import type {
  SqliteConnectionPort,
  SqlRunResult,
  SqlValue,
} from "./types";

const T0 = "2026-07-28T00:00:00.000Z";
const T1 = "2026-07-28T01:00:00.000Z";

test("M17 将 M2 标记去重为门店商品设备顺序，且不改动 active catalog", async () => {
  await withDatabase(async (connection) => {
    await applyMigrations(
      connection,
      () => T0,
      POS_DATABASE_MIGRATIONS.filter((migration) => migration.version <= 16),
    );
    await seedCatalog(connection);
    await connection.run(
      `INSERT INTO special_products (
        snapshot_id, store_code, lookup_code_normalized,
        sort_order, is_marked, updated_at_iso
      ) VALUES ('snapshot-active', 'STORE-1', 'AAA', 5, 1, ?)`,
      [T0],
    );
    await connection.run(
      `INSERT INTO special_products (
        snapshot_id, store_code, lookup_code_normalized,
        sort_order, is_marked, updated_at_iso
      ) VALUES ('snapshot-active', 'STORE-1', 'AAB', 7, 1, ?)`,
      [T0],
    );
    await connection.run(
      `INSERT INTO special_products (
        snapshot_id, store_code, lookup_code_normalized,
        sort_order, is_marked, updated_at_iso
      ) VALUES ('snapshot-retired', 'STORE-1', 'STALE', 0, 1, ?)`,
      [T0],
    );
    await connection.run(
      `INSERT INTO catalog_items (
        snapshot_id, store_code, lookup_code_normalized, product_code,
        reference_code, item_number, barcode, lookup_code, display_name,
        retail_price_cents, price_source, price_source_label,
        quantity_factor, tax_rate_basis_points, row_version, product_image,
        discount_rate, is_special_product, is_active, updated_at_iso
      ) VALUES (
        'snapshot-active', 'STORE-1', 'BAD', 'PRODUCT-BAD',
        char(1), ?, char(0), 'BAD', 'Product Bad',
        100, 99, 'catalog', 'not-a-number', NULL, NULL, char(2),
        'not-a-number', 1, 1, ?
      )`,
      ["I".repeat(300), T0],
    );
    await connection.run(
      `INSERT INTO special_products (
        snapshot_id, store_code, lookup_code_normalized,
        sort_order, is_marked, updated_at_iso
      ) VALUES ('snapshot-active', 'STORE-1', 'BAD', 9, 1, ?)`,
      [T0],
    );

    const catalogCount = await scalar(
      connection,
      "SELECT COUNT(*) AS count FROM catalog_items",
    );
    await applyMigrations(
      connection,
      () => T1,
      POS_DATABASE_MIGRATIONS.filter((migration) => migration.version <= 17),
    );
    assert.equal(await schemaVersion(connection), 17);
    assert.equal(
      await scalar(
        connection,
        "SELECT COUNT(*) AS count FROM catalog_items",
      ),
      catalogCount,
    );
    assert.equal(
      await scalar(
        connection,
        `SELECT COUNT(*) AS count
         FROM catalog_snapshots
         WHERE snapshot_id = 'snapshot-active' AND state = 'active'`,
      ),
      1,
    );

    const items = await new SqliteSpecialProductsRepository(connection).list(
      "STORE-1",
      20,
      0,
    );
    assert.equal(items.length, 2);
    assert.equal(items[0]?.productCode, "PRODUCT-A");
    assert.equal(items[0]?.lookupCode, "AAA");
    assert.equal(items[0]?.priceSource, 0);
    assert.equal(items[0]?.sortOrder, 0);
    const normalized = items.find(
      (itemValue) => itemValue.productCode === "PRODUCT-BAD",
    );
    assert.equal(normalized?.referenceCode, null);
    assert.equal(normalized?.itemNumber, null);
    assert.equal(normalized?.barcode, null);
    assert.equal(normalized?.productImage, null);
    assert.equal(normalized?.discountRate, null);
    assert.equal(normalized?.quantityFactor, 1);
    assert.equal(normalized?.priceSource, 0);
  });
});

test("已记录 M2 的早期目录窄表可原子升级到当前 schema，且保留非目录业务数据", async () => {
  await withDatabase(async (connection) => {
    await applyMigrations(
      connection,
      () => T0,
      POS_DATABASE_MIGRATIONS.filter((migration) => migration.version <= 16),
    );
    await replaceCatalogWithLegacyM2(connection);
    await connection.run(
      `INSERT INTO app_settings (
        setting_key, setting_value, updated_at_iso
      ) VALUES ('legacy-setting', 'preserved', ?)`,
      [T0],
    );
    await connection.run(
      `INSERT INTO local_orders (
        order_guid, local_sequence, store_code, device_code,
        cashier_id, cashier_name, sold_at_iso, state,
        total_cents, discount_cents, actual_amount_cents,
        original_order_guid, created_at_iso, updated_at_iso
      ) VALUES (
        'legacy-order', 1, 'STORE-1', 'IPAD-1',
        'cashier-1', 'Cashier', ?, 'CompletedLocal',
        100, 0, 100, NULL, ?, ?
      )`,
      [T0, T0, T0],
    );

    await applyMigrations(connection, () => T1);

    assert.equal(await schemaVersion(connection), 22);
    assert.deepEqual(
      (
        await connection.getAll<{ name: unknown }>(
          "PRAGMA table_info('catalog_items')",
        )
      ).map((column) => column.name),
      [
        "snapshot_id",
        "store_code",
        "lookup_code_normalized",
        "product_code",
        "reference_code",
        "item_number",
        "barcode",
        "lookup_code",
        "display_name",
        "retail_price_cents",
        "price_source",
        "price_source_label",
        "quantity_factor",
        "tax_rate_basis_points",
        "row_version",
        "product_image",
        "discount_rate",
        "is_special_product",
        "is_active",
        "updated_at_iso",
      ],
    );
    assert.deepEqual(
      (
        await connection.getAll<{ pk: unknown }>(
          "PRAGMA table_info('catalog_items')",
        )
      ).map((column) => Number(column.pk)),
      [1, 2, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
    );
    assert.equal(
      await scalar(
        connection,
        "SELECT COUNT(*) AS count FROM sqlite_master WHERE type = 'table' AND name IN ('catalog_barcodes', 'catalog_prices')",
      ),
      0,
    );
    assert.equal(
      await scalar(connection, "SELECT COUNT(*) AS count FROM catalog_items"),
      0,
    );
    assert.equal(
      await scalar(connection, "SELECT COUNT(*) AS count FROM special_products"),
      0,
    );
    assert.deepEqual(
      (
        await connection.getAll<{ name: unknown }>(
          `SELECT name
           FROM sqlite_master
           WHERE type = 'index'
             AND name IN (
               'ix_catalog_items_active_lookup',
               'ix_catalog_items_active_search',
               'ix_special_products_snapshot_sort',
               'ux_catalog_snapshots_single_active'
             )
           ORDER BY name`,
        )
      ).map((row) => row.name),
      [
        "ix_catalog_items_active_lookup",
        "ix_catalog_items_active_search",
        "ix_special_products_snapshot_sort",
        "ux_catalog_snapshots_single_active",
      ],
    );
    assert.equal(
      await scalar(
        connection,
        "SELECT COUNT(*) AS count FROM app_settings WHERE setting_key = 'legacy-setting' AND setting_value = 'preserved'",
      ),
      1,
    );
    assert.equal(
      await scalar(
        connection,
        "SELECT COUNT(*) AS count FROM local_orders WHERE order_guid = 'legacy-order' AND total_cents = 100",
      ),
      1,
    );

    await applyMigrations(connection, () => T1);
    assert.equal(await schemaVersion(connection), 22);
  });
});

test("模拟器已确认的快照复合键旧 M2 可升级并清空可下载缓存", async () => {
  await withDatabase(async (connection) => {
    await applyMigrations(
      connection,
      () => T0,
      POS_DATABASE_MIGRATIONS.filter((migration) => migration.version <= 16),
    );
    await replaceCatalogWithSnapshotScopedLegacyM2(connection);
    await connection.run(
      `INSERT INTO app_settings (
        setting_key, setting_value, updated_at_iso
      ) VALUES ('snapshot-legacy-setting', 'preserved', ?)`,
      [T0],
    );

    await applyMigrations(connection, () => T1);

    assert.equal(await schemaVersion(connection), 22);
    assert.equal(
      await scalar(
        connection,
        "SELECT COUNT(*) AS count FROM sqlite_master WHERE type = 'table' AND name IN ('catalog_barcodes', 'catalog_prices')",
      ),
      0,
    );
    assert.equal(
      await scalar(connection, "SELECT COUNT(*) AS count FROM catalog_items"),
      0,
    );
    assert.equal(
      await scalar(connection, "SELECT COUNT(*) AS count FROM special_products"),
      0,
    );
    assert.equal(
      await scalar(
        connection,
        "SELECT COUNT(*) AS count FROM app_settings WHERE setting_key = 'snapshot-legacy-setting' AND setting_value = 'preserved'",
      ),
      1,
    );
  });
});

test("旧 M2 修复后若 M17 失败，目录 schema、缓存和版本号全部回滚", async () => {
  await withDatabase(async (connection) => {
    await applyMigrations(
      connection,
      () => T0,
      POS_DATABASE_MIGRATIONS.filter((migration) => migration.version <= 16),
    );
    await replaceCatalogWithLegacyM2(connection);

    await assert.rejects(
      applyMigrations(connection, () => T1, [
        {
          version: 17,
          name: "M17_forced_failure",
          sql: "SELECT missing_column FROM missing_table;",
        },
      ]),
      /no such table: missing_table/,
    );

    assert.equal(await schemaVersion(connection), 16);
    assert.deepEqual(
      (
        await connection.getAll<{ name: unknown }>(
          "PRAGMA table_info('catalog_items')",
        )
      ).map((column) => column.name),
      [
        "product_code",
        "snapshot_id",
        "item_number",
        "display_name",
        "department_code",
        "tax_rate_basis_points",
        "is_active",
        "updated_at_iso",
      ],
    );
    assert.equal(
      await scalar(
        connection,
        "SELECT COUNT(*) AS count FROM catalog_items WHERE product_code = 'LEGACY-PRODUCT'",
      ),
      1,
    );
    assert.equal(
      await scalar(
        connection,
        "SELECT COUNT(*) AS count FROM special_products WHERE product_code = 'LEGACY-PRODUCT'",
      ),
      1,
    );
  });
});

test("未知目录 schema 明确拒绝，且不删除任何目录缓存", async () => {
  await withDatabase(async (connection) => {
    await applyMigrations(
      connection,
      () => T0,
      POS_DATABASE_MIGRATIONS.filter((migration) => migration.version <= 16),
    );
    await replaceCatalogWithLegacyM2(connection);
    await connection.exec(
      "ALTER TABLE special_products ADD COLUMN unknown_revision TEXT NULL;",
    );

    await assert.rejects(
      applyMigrations(
        connection,
        () => T1,
        POS_DATABASE_MIGRATIONS.filter((migration) => migration.version <= 17),
      ),
      /CATALOG_SCHEMA_INVALID:UNSUPPORTED_SHAPE/,
    );

    assert.equal(await schemaVersion(connection), 16);
    assert.equal(
      await scalar(
        connection,
        "SELECT COUNT(*) AS count FROM catalog_items WHERE product_code = 'LEGACY-PRODUCT'",
      ),
      1,
    );
    assert.equal(
      await scalar(
        connection,
        "SELECT COUNT(*) AS count FROM special_products WHERE product_code = 'LEGACY-PRODUCT'",
      ),
      1,
    );
    assert.deepEqual(
      (
        await connection.getAll<{ name: unknown }>(
          "PRAGMA table_info('special_products')",
        )
      ).map((column) => column.name),
      [
        "product_code",
        "snapshot_id",
        "sort_order",
        "is_marked",
        "updated_at_iso",
        "unknown_revision",
      ],
    );
  });
});

test("下载全量替换按商品去重；事务中任一写入失败时完整保留旧集合", async () => {
  await withDatabase(async (connection) => {
    await applyMigrations(connection, () => T0);
    const repository = new SqliteSpecialProductsRepository(connection);
    await repository.replaceDownloaded("STORE-1", [
      item("OLD-A", "OLD-A"),
      item("OLD-B", "OLD-B"),
    ]);
    await repository.replaceDownloaded("STORE-1", [
      item("PRODUCT-A", "ZZZ", 2),
      item("PRODUCT-A", "AAA", 2),
      item("PRODUCT-B", "BBB", 4),
    ]);
    assert.deepEqual(
      (await repository.list("STORE-1", 20, 0)).map((entry) => [
        entry.productCode,
        entry.lookupCode,
        entry.priceSource,
        entry.sortOrder,
      ]),
      [
        ["PRODUCT-A", "AAA", 2, 0],
        ["PRODUCT-B", "BBB", 4, 1],
      ],
    );

    await connection.exec(`
      CREATE TRIGGER fail_special_product_download
      BEFORE INSERT ON local_special_products
      FOR EACH ROW
      WHEN NEW.product_code = 'FAIL'
      BEGIN
        SELECT RAISE(ABORT, 'SPECIAL_PRODUCT_TEST_FAILURE');
      END;
    `);
    await assert.rejects(
      repository.replaceDownloaded("STORE-1", [
        item("NEW", "NEW"),
        item("FAIL", "FAIL"),
      ]),
      /SPECIAL_PRODUCT_TEST_FAILURE/,
    );
    assert.deepEqual(
      (await repository.list("STORE-1", 20, 0)).map(
        (entry) => entry.productCode,
      ),
      ["PRODUCT-A", "PRODUCT-B"],
    );
  });
});

test("候选搜索使用 active catalog；mark 和完整排列顺序跨实例持久化", async () => {
  await withDatabase(async (connection) => {
    await applyMigrations(connection, () => T0);
    await seedCatalog(connection);
    const repository = new SqliteSpecialProductsRepository(connection);

    const candidates = await repository.searchCandidates(
      "STORE-1",
      "product",
      20,
    );
    assert.deepEqual(
      candidates.map((entry) => entry.productCode),
      ["PRODUCT-A", "PRODUCT-B"],
    );

    await repository.applyMark("STORE-1", "PRODUCT-A", true, [
      item("PRODUCT-A", "ZZZ"),
      item("PRODUCT-A", "AAA"),
    ]);
    await repository.applyMark("STORE-1", "PRODUCT-B", true, [
      item("PRODUCT-B", "BBB"),
    ]);
    await repository.saveOrder("STORE-1", ["PRODUCT-B", "PRODUCT-A"]);
    await assert.rejects(
      repository.saveOrder("STORE-1", ["PRODUCT-A"]),
      /complete unique permutation/,
    );

    const restarted = new SqliteSpecialProductsRepository(connection);
    assert.deepEqual(
      (await restarted.list("STORE-1", 1, 0)).map(
        (entry) => entry.productCode,
      ),
      ["PRODUCT-B"],
    );
    assert.deepEqual(
      (await restarted.list("STORE-1", 10, 1)).map(
        (entry) => entry.productCode,
      ),
      ["PRODUCT-A"],
    );

    await restarted.applyMark("STORE-1", "PRODUCT-B", false, []);
    assert.deepEqual(
      (await restarted.list("STORE-1", 20, 0)).map((entry) => [
        entry.productCode,
        entry.sortOrder,
      ]),
      [["PRODUCT-A", 0]],
    );
  });
});

function item(
  productCode: string,
  lookupCode: string,
  priceSource: SpecialProductItem["priceSource"] = 0,
): Omit<SpecialProductItem, "sortOrder"> {
  return {
    storeCode: "STORE-1",
    productCode,
    referenceCode: `REF-${lookupCode}`,
    itemNumber: `ITEM-${lookupCode}`,
    displayName: `Product ${productCode}`,
    barcode: `BAR-${lookupCode}`,
    lookupCode,
    retailPriceCents: 1234,
    priceSource,
    quantityFactor: 1,
    productImage: null,
    discountRate: null,
  };
}

async function replaceCatalogWithLegacyM2(
  connection: SqliteConnectionPort,
): Promise<void> {
  await connection.exec(`
    DROP TABLE special_products;
    DROP TABLE catalog_promotions;
    DROP TABLE catalog_items;
    DROP TABLE catalog_snapshots;

    CREATE TABLE catalog_snapshots (
      snapshot_id TEXT PRIMARY KEY,
      catalog_version TEXT NOT NULL UNIQUE,
      checksum TEXT NOT NULL,
      state TEXT NOT NULL CHECK (state IN ('staging', 'active', 'retired')),
      downloaded_at_iso TEXT NOT NULL,
      activated_at_iso TEXT NULL
    );
    CREATE TABLE catalog_items (
      product_code TEXT PRIMARY KEY,
      snapshot_id TEXT NOT NULL REFERENCES catalog_snapshots(snapshot_id),
      item_number TEXT NULL,
      display_name TEXT NOT NULL,
      department_code TEXT NULL,
      tax_rate_basis_points INTEGER NOT NULL,
      is_active INTEGER NOT NULL CHECK (is_active IN (0, 1)),
      updated_at_iso TEXT NOT NULL
    );
    CREATE TABLE catalog_barcodes (
      barcode TEXT PRIMARY KEY,
      product_code TEXT NOT NULL REFERENCES catalog_items(product_code),
      barcode_type TEXT NOT NULL,
      updated_at_iso TEXT NOT NULL
    );
    CREATE TABLE catalog_prices (
      price_id TEXT PRIMARY KEY,
      product_code TEXT NOT NULL REFERENCES catalog_items(product_code),
      price_cents INTEGER NOT NULL,
      valid_from_iso TEXT NULL,
      valid_until_iso TEXT NULL,
      source_version TEXT NOT NULL
    );
    CREATE TABLE catalog_promotions (
      promotion_id TEXT PRIMARY KEY,
      snapshot_id TEXT NOT NULL REFERENCES catalog_snapshots(snapshot_id),
      definition_json TEXT NOT NULL,
      valid_from_iso TEXT NULL,
      valid_until_iso TEXT NULL,
      priority INTEGER NOT NULL
    );
    CREATE TABLE special_products (
      product_code TEXT PRIMARY KEY REFERENCES catalog_items(product_code),
      snapshot_id TEXT NOT NULL REFERENCES catalog_snapshots(snapshot_id),
      sort_order INTEGER NOT NULL,
      is_marked INTEGER NOT NULL CHECK (is_marked IN (0, 1)),
      updated_at_iso TEXT NOT NULL
    );
    CREATE INDEX ix_catalog_items_snapshot
      ON catalog_items (snapshot_id, is_active);
    CREATE INDEX ix_catalog_barcodes_product
      ON catalog_barcodes (product_code);
    CREATE INDEX ix_catalog_prices_product_valid
      ON catalog_prices (product_code, valid_from_iso, valid_until_iso);
    CREATE INDEX ix_special_products_snapshot_sort
      ON special_products (snapshot_id, sort_order);
  `);
  await connection.run(
    `INSERT INTO catalog_snapshots (
      snapshot_id, catalog_version, checksum, state,
      downloaded_at_iso, activated_at_iso
    ) VALUES ('legacy-snapshot', 'legacy-v1', 'legacy-checksum', 'active', ?, ?)`,
    [T0, T0],
  );
  await connection.run(
    `INSERT INTO catalog_items (
      product_code, snapshot_id, item_number, display_name,
      department_code, tax_rate_basis_points, is_active, updated_at_iso
    ) VALUES (
      'LEGACY-PRODUCT', 'legacy-snapshot', 'LEGACY-ITEM',
      'Legacy Product', 'LEGACY-DEPT', 1000, 1, ?
    )`,
    [T0],
  );
  await connection.run(
    `INSERT INTO catalog_barcodes (
      barcode, product_code, barcode_type, updated_at_iso
    ) VALUES ('LEGACY-BARCODE', 'LEGACY-PRODUCT', 'EAN13', ?)`,
    [T0],
  );
  await connection.run(
    `INSERT INTO catalog_prices (
      price_id, product_code, price_cents,
      valid_from_iso, valid_until_iso, source_version
    ) VALUES (
      'legacy-price', 'LEGACY-PRODUCT', 1234, NULL, NULL, 'legacy-v1'
    )`,
  );
  await connection.run(
    `INSERT INTO special_products (
      product_code, snapshot_id, sort_order, is_marked, updated_at_iso
    ) VALUES ('LEGACY-PRODUCT', 'legacy-snapshot', 0, 1, ?)`,
    [T0],
  );
}

async function replaceCatalogWithSnapshotScopedLegacyM2(
  connection: SqliteConnectionPort,
): Promise<void> {
  await connection.exec(`
    DROP TABLE special_products;
    DROP TABLE catalog_promotions;
    DROP TABLE catalog_items;
    DROP TABLE catalog_snapshots;

    CREATE TABLE catalog_snapshots (
      snapshot_id TEXT PRIMARY KEY,
      catalog_version TEXT NOT NULL UNIQUE,
      checksum TEXT NOT NULL,
      state TEXT NOT NULL CHECK (state IN ('staging', 'active', 'retired')),
      downloaded_at_iso TEXT NOT NULL,
      activated_at_iso TEXT NULL
    );
    CREATE TABLE catalog_items (
      snapshot_id TEXT NOT NULL REFERENCES catalog_snapshots(snapshot_id),
      product_code TEXT NOT NULL,
      item_number TEXT NULL,
      display_name TEXT NOT NULL,
      department_code TEXT NULL,
      tax_rate_basis_points INTEGER NOT NULL,
      is_active INTEGER NOT NULL CHECK (is_active IN (0, 1)),
      updated_at_iso TEXT NOT NULL,
      PRIMARY KEY (snapshot_id, product_code)
    );
    CREATE TABLE catalog_barcodes (
      snapshot_id TEXT NOT NULL,
      barcode TEXT NOT NULL,
      product_code TEXT NOT NULL,
      barcode_type TEXT NOT NULL,
      updated_at_iso TEXT NOT NULL,
      PRIMARY KEY (snapshot_id, barcode),
      FOREIGN KEY (snapshot_id, product_code)
        REFERENCES catalog_items(snapshot_id, product_code)
    );
    CREATE TABLE catalog_prices (
      snapshot_id TEXT NOT NULL,
      price_id TEXT NOT NULL,
      product_code TEXT NOT NULL,
      price_cents INTEGER NOT NULL,
      valid_from_iso TEXT NULL,
      valid_until_iso TEXT NULL,
      source_version TEXT NOT NULL,
      PRIMARY KEY (snapshot_id, price_id),
      FOREIGN KEY (snapshot_id, product_code)
        REFERENCES catalog_items(snapshot_id, product_code)
    );
    CREATE TABLE catalog_promotions (
      snapshot_id TEXT NOT NULL REFERENCES catalog_snapshots(snapshot_id),
      promotion_id TEXT NOT NULL,
      definition_json TEXT NOT NULL,
      valid_from_iso TEXT NULL,
      valid_until_iso TEXT NULL,
      priority INTEGER NOT NULL,
      PRIMARY KEY (snapshot_id, promotion_id)
    );
    CREATE TABLE special_products (
      snapshot_id TEXT NOT NULL,
      product_code TEXT NOT NULL,
      sort_order INTEGER NOT NULL,
      is_marked INTEGER NOT NULL CHECK (is_marked IN (0, 1)),
      updated_at_iso TEXT NOT NULL,
      PRIMARY KEY (snapshot_id, product_code),
      FOREIGN KEY (snapshot_id, product_code)
        REFERENCES catalog_items(snapshot_id, product_code)
    );
    CREATE INDEX ix_catalog_items_snapshot
      ON catalog_items (snapshot_id, is_active);
    CREATE INDEX ix_catalog_barcodes_product
      ON catalog_barcodes (snapshot_id, product_code);
    CREATE INDEX ix_catalog_prices_product_valid
      ON catalog_prices (
        snapshot_id, product_code, valid_from_iso, valid_until_iso
      );
    CREATE INDEX ix_special_products_snapshot_sort
      ON special_products (snapshot_id, sort_order);
  `);
  await connection.run(
    `INSERT INTO catalog_snapshots (
      snapshot_id, catalog_version, checksum, state,
      downloaded_at_iso, activated_at_iso
    ) VALUES (
      'snapshot-legacy', 'snapshot-legacy-v1',
      'snapshot-legacy-checksum', 'active', ?, ?
    )`,
    [T0, T0],
  );
  await connection.run(
    `INSERT INTO catalog_items (
      snapshot_id, product_code, item_number, display_name,
      department_code, tax_rate_basis_points, is_active, updated_at_iso
    ) VALUES (
      'snapshot-legacy', 'SNAPSHOT-LEGACY-PRODUCT',
      'SNAPSHOT-LEGACY-ITEM', 'Snapshot Legacy Product',
      'SNAPSHOT-LEGACY-DEPT', 1000, 1, ?
    )`,
    [T0],
  );
  await connection.run(
    `INSERT INTO catalog_barcodes (
      snapshot_id, barcode, product_code, barcode_type, updated_at_iso
    ) VALUES (
      'snapshot-legacy', 'SNAPSHOT-LEGACY-BARCODE',
      'SNAPSHOT-LEGACY-PRODUCT', 'EAN13', ?
    )`,
    [T0],
  );
  await connection.run(
    `INSERT INTO catalog_prices (
      snapshot_id, price_id, product_code, price_cents,
      valid_from_iso, valid_until_iso, source_version
    ) VALUES (
      'snapshot-legacy', 'snapshot-legacy-price',
      'SNAPSHOT-LEGACY-PRODUCT', 1234, NULL, NULL, 'snapshot-legacy-v1'
    )`,
  );
  await connection.run(
    `INSERT INTO special_products (
      snapshot_id, product_code, sort_order, is_marked, updated_at_iso
    ) VALUES (
      'snapshot-legacy', 'SNAPSHOT-LEGACY-PRODUCT', 0, 1, ?
    )`,
    [T0],
  );
}

async function seedCatalog(connection: SqliteConnectionPort): Promise<void> {
  await connection.run(
    `INSERT INTO catalog_snapshots (
      snapshot_id, catalog_version, checksum, state,
      downloaded_at_iso, activated_at_iso
    ) VALUES ('snapshot-active', 'v1', 'checksum', 'active', ?, ?)`,
    [T0, T0],
  );
  for (const [lookupCode, productCode, displayName] of [
    ["AAA", "PRODUCT-A", "Product Alpha"],
    ["AAB", "PRODUCT-A", "Product Alpha Variant"],
    ["BBB", "PRODUCT-B", "Product Beta"],
  ] as const) {
    await connection.run(
      `INSERT INTO catalog_items (
        snapshot_id, store_code, lookup_code_normalized, product_code,
        reference_code, item_number, barcode, lookup_code, display_name,
        retail_price_cents, price_source, price_source_label,
        quantity_factor, tax_rate_basis_points, row_version, product_image,
        discount_rate, is_special_product, is_active, updated_at_iso
      ) VALUES (
        'snapshot-active', 'STORE-1', ?, ?, ?, ?, ?, ?, ?,
        1234, 0, 'catalog', '1', NULL, NULL, NULL, NULL, 0, 1, ?
      )`,
      [
        lookupCode,
        productCode,
        `REF-${lookupCode}`,
        `ITEM-${lookupCode}`,
        `BAR-${lookupCode}`,
        lookupCode,
        displayName,
        T0,
      ],
    );
  }
  await connection.run(
    `INSERT INTO catalog_snapshots (
      snapshot_id, catalog_version, checksum, state,
      downloaded_at_iso, activated_at_iso
    ) VALUES ('snapshot-retired', 'v0', 'old-checksum', 'retired', ?, ?)`,
    [T0, T0],
  );
  await connection.run(
    `INSERT INTO catalog_items (
      snapshot_id, store_code, lookup_code_normalized, product_code,
      reference_code, item_number, barcode, lookup_code, display_name,
      retail_price_cents, price_source, price_source_label,
      quantity_factor, tax_rate_basis_points, row_version, product_image,
      discount_rate, is_special_product, is_active, updated_at_iso
    ) VALUES (
      'snapshot-retired', 'STORE-1', 'STALE', 'PRODUCT-STALE',
      'REF-STALE', 'ITEM-STALE', 'BAR-STALE', 'STALE', 'Stale Product',
      999, 0, 'catalog', '1', NULL, NULL, NULL, NULL, 1, 1, ?
    )`,
    [T0],
  );
}

async function schemaVersion(
  connection: SqliteConnectionPort,
): Promise<number> {
  return Number(
    (
      await connection.getFirst<{ version: unknown }>(
        "SELECT MAX(version) AS version FROM schema_migrations",
      )
    )?.version,
  );
}

async function scalar(
  connection: SqliteConnectionPort,
  sql: string,
): Promise<number> {
  return Number(
    (
      await connection.getFirst<{ count: unknown }>(sql)
    )?.count,
  );
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
