/**
 * 离线商品目录 SQLite 表结构。
 *
 * 设计参考 apps/pos-ipad/src/core/db/migrations.ts 的 catalog_* 表：
 * - `offline_catalog_snapshots`：staging / active / retired 三态，每店唯一 active；
 * - `offline_catalog_items`：一售卖码一行，主键 (snapshot_id, lookup_key)；
 * - `offline_catalog_delta_deletions`：delta staging 的墓碑，激活时回放到 active。
 */
import type { SqliteConnectionPort } from "@/shared/db/types";

export const OFFLINE_CATALOG_DATABASE_NAME = "hb-offline-catalog.db";

interface OfflineCatalogMigration {
  version: number;
  name: string;
  sql: string;
}

const M1 = `
CREATE TABLE IF NOT EXISTS offline_catalog_snapshots (
  snapshot_id TEXT PRIMARY KEY,
  store_code TEXT NOT NULL CHECK (TRIM(store_code) <> ''),
  catalog_version TEXT NOT NULL CHECK (TRIM(catalog_version) <> ''),
  checksum TEXT NOT NULL,
  state TEXT NOT NULL CHECK (state IN ('staging', 'active', 'retired')),
  sync_mode TEXT NOT NULL DEFAULT 'full' CHECK (sync_mode IN ('full', 'delta')),
  generation_id TEXT NOT NULL,
  base_snapshot_id TEXT NULL,
  base_catalog_version TEXT NULL,
  generated_at_iso TEXT NOT NULL,
  downloaded_at_iso TEXT NOT NULL,
  activated_at_iso TEXT NULL
);
-- 每个分店只能有一个 active 快照；切换在单事务内 retire 旧 + 激活新。
CREATE UNIQUE INDEX IF NOT EXISTS ux_offline_catalog_snapshots_active
  ON offline_catalog_snapshots (store_code) WHERE state = 'active';
CREATE TABLE IF NOT EXISTS offline_catalog_items (
  snapshot_id TEXT NOT NULL REFERENCES offline_catalog_snapshots(snapshot_id),
  store_code TEXT NOT NULL,
  lookup_key TEXT NOT NULL,
  lookup_code TEXT NOT NULL,
  lookup_code_normalized TEXT NOT NULL,
  match_source TEXT NOT NULL,
  product_code TEXT NOT NULL,
  product_name TEXT NOT NULL,
  item_number TEXT NULL,
  barcode TEXT NULL,
  product_image TEXT NULL,
  product_type INTEGER NULL,
  grade TEXT NULL,
  local_supplier_code TEXT NULL,
  local_supplier_name TEXT NULL,
  store_name TEXT NULL,
  store_price_uuid TEXT NULL,
  purchase_price REAL NULL,
  retail_price REAL NULL,
  discount_rate REAL NULL,
  is_auto_pricing INTEGER NOT NULL CHECK (is_auto_pricing IN (0, 1)),
  is_special_product INTEGER NOT NULL CHECK (is_special_product IN (0, 1)),
  rate REAL NULL,
  strategy_source_label TEXT NULL,
  strategy_rule_label TEXT NULL,
  clearance_uuid TEXT NULL,
  clearance_barcode TEXT NULL,
  clearance_price REAL NULL,
  code_id TEXT NULL,
  code_uuid TEXT NULL,
  code_product_code TEXT NULL,
  code_item_number TEXT NULL,
  code_retail_price REAL NULL,
  code_purchase_price REAL NULL,
  code_quantity REAL NULL,
  code_type INTEGER NULL,
  code_discount_rate REAL NULL,
  code_is_auto_pricing INTEGER NULL,
  code_is_special_product INTEGER NULL,
  code_is_active INTEGER NULL,
  updated_at_iso TEXT NULL,
  row_version TEXT NOT NULL,
  PRIMARY KEY (snapshot_id, lookup_key)
);
-- 列序必须与 lookup() 的谓词一致：店码过滤写在父表 snapshots 上，items 侧只约束
-- snapshot_id 与 lookup_code_normalized。中间夹一个无谓词的 store_code 会让索引在
-- 第二列断掉，退化成按 snapshot_id 的全量扫描（40 万行门店每扫一次码付一次）。
CREATE INDEX IF NOT EXISTS ix_offline_catalog_items_lookup
  ON offline_catalog_items (snapshot_id, lookup_code_normalized);
CREATE INDEX IF NOT EXISTS ix_offline_catalog_items_product
  ON offline_catalog_items (snapshot_id, product_code);
CREATE TABLE IF NOT EXISTS offline_catalog_delta_deletions (
  snapshot_id TEXT NOT NULL REFERENCES offline_catalog_snapshots(snapshot_id),
  store_code TEXT NOT NULL,
  lookup_key TEXT NOT NULL,
  PRIMARY KEY (snapshot_id, lookup_key)
);
`;

// 已经装过 M1 的设备上，索引仍是三列版本，这里重建成与谓词匹配的两列版本。
const M2 = `
DROP INDEX IF EXISTS ix_offline_catalog_items_lookup;
CREATE INDEX IF NOT EXISTS ix_offline_catalog_items_lookup
  ON offline_catalog_items (snapshot_id, lookup_code_normalized);
`;

export const OFFLINE_CATALOG_MIGRATIONS: readonly OfflineCatalogMigration[] = [
  { version: 1, name: "M1_offline_catalog", sql: M1 },
  { version: 2, name: "M2_offline_catalog_lookup_index", sql: M2 },
];

export async function applyOfflineCatalogMigrations(
  db: SqliteConnectionPort,
  nowIso: () => string = () => new Date().toISOString(),
): Promise<void> {
  await db.exec(`
CREATE TABLE IF NOT EXISTS offline_catalog_migrations (
  version INTEGER PRIMARY KEY,
  name TEXT NOT NULL,
  applied_at_iso TEXT NOT NULL
);`);
  const applied = await db.getAll<{ version: number }>(
    "SELECT version FROM offline_catalog_migrations",
  );
  const appliedVersions = new Set(applied.map((row) => Number(row.version)));
  for (const migration of OFFLINE_CATALOG_MIGRATIONS) {
    if (appliedVersions.has(migration.version)) {
      continue;
    }
    await db.withExclusiveTransaction(async (tx) => {
      await tx.exec(migration.sql);
      await tx.run(
        "INSERT INTO offline_catalog_migrations (version, name, applied_at_iso) VALUES (?, ?, ?)",
        [migration.version, migration.name, nowIso()],
      );
    });
  }
}
