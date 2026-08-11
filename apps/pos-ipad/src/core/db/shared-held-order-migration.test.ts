import assert from "node:assert/strict";
import { DatabaseSync } from "node:sqlite";
import test from "node:test";

import {
  NodeSqliteConnection,
  TEST_NOW_ISO,
  insertHeldOrderRow,
} from "../../features/shared-held-orders/shared-held-order-test-support";

import { applyMigrations, POS_DATABASE_MIGRATIONS } from "./migrations";

const ENCRYPTED_LEGACY_PAYLOAD = new TextEncoder().encode(
  "legacy-ciphertext-bytes",
);

test("M39 升级 M40 保留旧挂单行并默认 NeedsEvaluation", async () => {
  const connection = new NodeSqliteConnection(
    new DatabaseSync(":memory:", { enableForeignKeyConstraints: true }),
  );
  try {
    await applyMigrations(
      connection,
      () => TEST_NOW_ISO,
      POS_DATABASE_MIGRATIONS.filter((migration) => migration.version <= 39),
    );
    await insertHeldOrderRow(connection, {
      holdId: "hold-legacy-1",
      payloadCiphertext: ENCRYPTED_LEGACY_PAYLOAD,
    });

    // 追加完整迁移链（只应补跑 M40）。
    await applyMigrations(connection, () => TEST_NOW_ISO);

    const applied = await connection.getFirst<{ version: number }>(
      "SELECT version FROM schema_migrations WHERE version = 40",
    );
    assert.equal(applied?.version, 40);

    const row = await connection.getFirst<{
      hold_id: string;
      share_state: string;
      remote_revision: number | null;
      publish_attempt_count: number;
      next_publish_at_iso: string | null;
      publish_error_code: string | null;
      publish_block_reason: string | null;
      remote_updated_at_iso: string | null;
      payload_ciphertext: Uint8Array;
    }>("SELECT * FROM held_order_records WHERE hold_id = ?", [
      "hold-legacy-1",
    ]);
    assert.ok(row);
    assert.equal(row.hold_id, "hold-legacy-1");
    assert.equal(row.share_state, "NeedsEvaluation");
    assert.equal(row.publish_attempt_count, 0);
    assert.equal(row.remote_revision, null);
    assert.equal(row.next_publish_at_iso, null);
    assert.equal(row.publish_error_code, null);
    assert.equal(row.publish_block_reason, null);
    assert.equal(row.remote_updated_at_iso, null);
    // 迁移不解密、不猜测、不改写旧密文。
    assert.deepEqual(row.payload_ciphertext, ENCRYPTED_LEGACY_PAYLOAD);
  } finally {
    await connection.close();
  }
});

test("M40 幂等：重复 applyMigrations 不重复应用且数据不变", async () => {
  const connection = new NodeSqliteConnection(
    new DatabaseSync(":memory:", { enableForeignKeyConstraints: true }),
  );
  try {
    await applyMigrations(connection, () => TEST_NOW_ISO);
    await insertHeldOrderRow(connection, {
      holdId: "hold-idem-1",
      payloadCiphertext: ENCRYPTED_LEGACY_PAYLOAD,
    });

    await applyMigrations(connection, () => TEST_NOW_ISO);

    const count = await connection.getFirst<{ n: number }>(
      "SELECT COUNT(*) AS n FROM schema_migrations WHERE version = 40",
    );
    assert.equal(count?.n, 1);
    const row = await connection.getFirst<{
      share_state: string;
      publish_attempt_count: number;
    }>(
      "SELECT share_state, publish_attempt_count FROM held_order_records WHERE hold_id = ?",
      ["hold-idem-1"],
    );
    assert.equal(row?.share_state, "NeedsEvaluation");
    assert.equal(row?.publish_attempt_count, 0);
  } finally {
    await connection.close();
  }
});

test("全新库完整迁移后 M40 列、claim 表与 fence 索引齐备且枚举受约束", async () => {
  const connection = new NodeSqliteConnection(
    new DatabaseSync(":memory:", { enableForeignKeyConstraints: true }),
  );
  try {
    await applyMigrations(connection, () => TEST_NOW_ISO);
    await insertHeldOrderRow(connection, {
      holdId: "hold-check-1",
      payloadCiphertext: ENCRYPTED_LEGACY_PAYLOAD,
    });
    await connection.run(
      `INSERT INTO local_orders (
        order_guid, local_sequence, store_code, device_code,
        cashier_id, cashier_name, sold_at_iso, state,
        total_cents, discount_cents, actual_amount_cents,
        original_order_guid, created_at_iso, updated_at_iso
      ) VALUES ('order-check-1', 9001, 'S1', 'IPAD-01', 'c-1', 'C', ?,
        'PendingSync', 100, 0, 100, NULL, ?, ?)`,
      [TEST_NOW_ISO, TEST_NOW_ISO, TEST_NOW_ISO],
    );

    const columns = await connection.getAll<{ name: string }>(
      "PRAGMA table_info(held_order_records)",
    );
    const names = new Set(columns.map((column) => column.name));
    for (const column of [
      "share_state",
      "remote_revision",
      "publish_attempt_count",
      "next_publish_at_iso",
      "publish_error_code",
      "publish_block_reason",
      "remote_updated_at_iso",
      "is_synthetic_shared_claim",
    ]) {
      assert.ok(names.has(column), `missing M40 column ${column}`);
    }

    const orderColumns = await connection.getAll<{ name: string }>(
      "PRAGMA table_info(local_orders)",
    );
    const orderNames = new Set(orderColumns.map((column) => column.name));
    assert.ok(
      orderNames.has("is_shared_held_origin"),
      "missing M40 local_orders.is_shared_held_origin",
    );

    const claimTable = await connection.getFirst<{ name: string }>(
      "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'shared_held_order_claim_records'",
    );
    assert.ok(claimTable);
    const claimColumns = await connection.getAll<{ name: string }>(
      "PRAGMA table_info(shared_held_order_claim_records)",
    );
    const claimNames = new Set(claimColumns.map((column) => column.name));
    assert.ok(
      claimNames.has("recall_attempt_id"),
      "missing M40 claim recall_attempt_id",
    );
    assert.ok(
      claimNames.has("supersede_idempotency_key"),
      "missing M40 claim supersede_idempotency_key",
    );
    const fenceIndex = await connection.getFirst<{ name: string }>(
      "SELECT name FROM sqlite_master WHERE type = 'index' AND name = 'ux_shared_held_order_claim_terminal_fence'",
    );
    assert.ok(fenceIndex);
    const sourceTable = await connection.getFirst<{ name: string }>(
      "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'order_held_order_sources'",
    );
    assert.ok(sourceTable);

    // 非法 share_state / claim state 被 CHECK 拒绝。
    await assert.rejects(
      connection.run(
        "UPDATE held_order_records SET share_state = 'Bogus' WHERE hold_id = 'hold-check-1'",
      ),
    );
    await assert.rejects(
      connection.run(
        `INSERT INTO shared_held_order_claim_records (
          claim_guid, hold_guid, recall_attempt_id, store_code, device_code, source, state,
          prepare_idempotency_key, payload_version, payload_ciphertext,
          prepared_expires_at_iso, held_at_iso, held_by_cashier_id,
          held_by_cashier_name, created_at_iso, updated_at_iso
        ) VALUES ('c-1', 'h-1', 'ra-1', 'S1', 'D1', 'OfflineOrigin', 'Bogus',
          'k-1', 1, X'01', ?, ?, 'c-1', 'Cashier', ?, ?)`,
        [TEST_NOW_ISO, TEST_NOW_ISO, TEST_NOW_ISO, TEST_NOW_ISO],
      ),
    );

    // 普通订单来源标记默认 0，且只允许 0/1。
    const marker = await connection.getFirst<{ is_shared_held_origin: number }>(
      "SELECT is_shared_held_origin FROM local_orders WHERE order_guid = 'order-check-1'",
    );
    assert.equal(marker?.is_shared_held_origin, 0);
    await assert.rejects(
      connection.run(
        "UPDATE local_orders SET is_shared_held_origin = 2 WHERE order_guid = 'order-check-1'",
      ),
    );
  } finally {
    await connection.close();
  }
});

test("M40 订单来源表：RemoteClaim/OfflineOrigin 约束、不可变、FK 严格", async () => {
  const connection = new NodeSqliteConnection(
    new DatabaseSync(":memory:", { enableForeignKeyConstraints: true }),
  );
  try {
    await applyMigrations(connection, () => TEST_NOW_ISO);
    await connection.run(
      `INSERT INTO local_orders (
        order_guid, local_sequence, store_code, device_code,
        cashier_id, cashier_name, sold_at_iso, state,
        total_cents, discount_cents, actual_amount_cents,
        original_order_guid, created_at_iso, updated_at_iso
      ) VALUES ('order-src-1', 9002, 'S1', 'IPAD-01', 'c-1', 'C', ?,
        'PendingSync', 100, 0, 100, NULL, ?, ?)`,
      [TEST_NOW_ISO, TEST_NOW_ISO, TEST_NOW_ISO],
    );
    await connection.run(
      `INSERT INTO local_orders (
        order_guid, local_sequence, store_code, device_code,
        cashier_id, cashier_name, sold_at_iso, state,
        total_cents, discount_cents, actual_amount_cents,
        original_order_guid, created_at_iso, updated_at_iso
      ) VALUES ('order-src-2', 9003, 'S1', 'IPAD-01', 'c-1', 'C', ?,
        'PendingSync', 100, 0, 100, NULL, ?, ?)`,
      [TEST_NOW_ISO, TEST_NOW_ISO, TEST_NOW_ISO],
    );
    await connection.run(
      `INSERT INTO shared_held_order_claim_records (
        claim_guid, hold_guid, recall_attempt_id, store_code, device_code,
        source, state, prepare_idempotency_key, payload_version,
        payload_ciphertext, prepared_expires_at_iso, held_at_iso,
        held_by_cashier_id, held_by_cashier_name, created_at_iso, updated_at_iso
      ) VALUES ('claim-src-1', 'hold-src-1', 'ra-src-1', 'S1', 'IPAD-01',
        'RemoteClaim', 'Prepared', 'k-src-1', 1, X'01', ?, ?, 'c-1',
        'Cashier', ?, ?)`,
      [TEST_NOW_ISO, TEST_NOW_ISO, TEST_NOW_ISO, TEST_NOW_ISO],
    );

    // OfflineOrigin：claim_guid 必须为空。
    await connection.run(
      `INSERT INTO order_held_order_sources (
        order_guid, hold_guid, claim_guid, source_kind, created_at_iso
      ) VALUES ('order-src-1', 'hold-src-1', NULL, 2, ?)`,
      [TEST_NOW_ISO],
    );
    await assert.rejects(
      connection.run(
        `INSERT INTO order_held_order_sources (
          order_guid, hold_guid, claim_guid, source_kind, created_at_iso
        ) VALUES ('order-src-bad-1', 'hold-src-1', 'claim-src-1', 2, ?)`,
        [TEST_NOW_ISO],
      ),
    );
    await assert.rejects(
      connection.run(
        `INSERT INTO order_held_order_sources (
          order_guid, hold_guid, claim_guid, source_kind, created_at_iso
        ) VALUES ('order-src-bad-2', 'hold-src-1', NULL, 1, ?)`,
        [TEST_NOW_ISO],
      ),
    );

    // RemoteClaim：claim_guid 必须非空且引用真实 claim 行。
    await connection.run(
      `INSERT INTO order_held_order_sources (
        order_guid, hold_guid, claim_guid, source_kind, created_at_iso
      ) VALUES ('order-src-2', 'hold-src-1', 'claim-src-1', 1, ?)`,
      [TEST_NOW_ISO],
    );
    await assert.rejects(
      connection.run(
        `INSERT INTO order_held_order_sources (
          order_guid, hold_guid, claim_guid, source_kind, created_at_iso
        ) VALUES ('order-src-bad-3', 'hold-src-1', 'claim-missing', 1, ?)`,
        [TEST_NOW_ISO],
      ),
    );

    // 来源行不可变：任何 UPDATE 都触发 ABORT。
    await assert.rejects(
      connection.run(
        `UPDATE order_held_order_sources
         SET source_kind = 1 WHERE order_guid = 'order-src-1'`,
      ),
    );
    await assert.rejects(
      connection.run(
        `UPDATE order_held_order_sources
         SET hold_guid = 'hold-other' WHERE order_guid = 'order-src-1'`,
      ),
    );
    // 同一订单只能有一个来源（PK）。
    await assert.rejects(
      connection.run(
        `INSERT INTO order_held_order_sources (
          order_guid, hold_guid, claim_guid, source_kind, created_at_iso
        ) VALUES ('order-src-1', 'hold-src-1', NULL, 2, ?)`,
        [TEST_NOW_ISO],
      ),
    );
  } finally {
    await connection.close();
  }
});

test("M40 server_revision 支持超过 int.MaxValue 的 64 位整数", async () => {
  const connection = new NodeSqliteConnection(
    new DatabaseSync(":memory:", { enableForeignKeyConstraints: true }),
  );
  try {
    await applyMigrations(connection, () => TEST_NOW_ISO);
    const bigRevision = 2_147_483_653; // int.MaxValue + 6
    await connection.run(
      `INSERT INTO shared_held_order_claim_records (
        claim_guid, hold_guid, recall_attempt_id, store_code, device_code,
        source, state, prepare_idempotency_key, payload_version,
        payload_ciphertext, server_revision, prepared_expires_at_iso,
        held_at_iso, held_by_cashier_id, held_by_cashier_name,
        created_at_iso, updated_at_iso
      ) VALUES ('claim-big-1', 'hold-big-1', 'ra-big-1', 'S1', 'IPAD-01',
        'RemoteClaim', 'Prepared', 'k-big-1', 1, X'01', ?, ?, ?, 'c-1',
        'Cashier', ?, ?)`,
      [bigRevision, TEST_NOW_ISO, TEST_NOW_ISO, TEST_NOW_ISO, TEST_NOW_ISO],
    );
    const row = await connection.getFirst<{ server_revision: number }>(
      "SELECT server_revision FROM shared_held_order_claim_records WHERE claim_guid = 'claim-big-1'",
    );
    assert.equal(row?.server_revision, bigRevision);
  } finally {
    await connection.close();
  }
});
