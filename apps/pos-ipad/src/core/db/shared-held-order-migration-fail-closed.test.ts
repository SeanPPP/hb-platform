import assert from "node:assert/strict";
import { DatabaseSync } from "node:sqlite";
import test from "node:test";

import {
  NodeSqliteConnection,
  TEST_NOW_ISO,
  insertHeldOrderRow,
} from "../../features/shared-held-orders/shared-held-order-test-support";

import { applyMigrations, POS_DATABASE_MIGRATIONS } from "./migrations";

/**
 * M40 迁移入口 fail-closed 聚焦证据：
 * - 升级只加列/建表，绝不解析、解密或猜测旧 payload（含空/垃圾密文）；
 * - M40 落库的 claim 状态机 trigger 在裸 SQL 直改时同样拒绝非法转移。
 */

test("M40 对空/垃圾旧密文 fail-closed：不解析不解密，原样保留且 NeedsEvaluation", async () => {
  const connection = new NodeSqliteConnection(
    new DatabaseSync(":memory:", { enableForeignKeyConstraints: true }),
  );
  try {
    await applyMigrations(
      connection,
      () => TEST_NOW_ISO,
      POS_DATABASE_MIGRATIONS.filter((migration) => migration.version <= 39),
    );
    const garbage = new TextEncoder().encode("\u0000\xffbroken-not-json");
    const empty = new Uint8Array();
    await insertHeldOrderRow(connection, {
      holdId: "hold-garbage",
      payloadCiphertext: garbage,
    });
    await insertHeldOrderRow(connection, {
      holdId: "hold-empty",
      payloadCiphertext: empty,
    });

    await applyMigrations(connection, () => TEST_NOW_ISO);

    for (const [holdId, expectedPayload] of [
      ["hold-garbage", garbage],
      ["hold-empty", empty],
    ] as const) {
      const row = await connection.getFirst<{
        share_state: string;
        publish_attempt_count: number;
        payload_ciphertext: Uint8Array;
      }>(
        `SELECT share_state, publish_attempt_count, payload_ciphertext
         FROM held_order_records WHERE hold_id = ?`,
        [holdId],
      );
      assert.ok(row);
      assert.equal(row.share_state, "NeedsEvaluation");
      assert.equal(row.publish_attempt_count, 0);
      assert.deepEqual(row.payload_ciphertext, expectedPayload);
    }
  } finally {
    await connection.close();
  }
});

test("M40 claim 状态机 trigger fail-closed：裸 SQL 非法转移直接 ABORT", async () => {
  const connection = new NodeSqliteConnection(
    new DatabaseSync(":memory:", { enableForeignKeyConstraints: true }),
  );
  try {
    await applyMigrations(connection, () => TEST_NOW_ISO);
    await insertHeldOrderRow(connection, {
      holdId: "hold-trigger",
      payloadCiphertext: new TextEncoder().encode("{}"),
    });
    await connection.run(
      `INSERT INTO shared_held_order_claim_records (
        claim_guid, hold_guid, recall_attempt_id, store_code, device_code,
        source, state, prepare_idempotency_key, payload_version,
        payload_ciphertext, prepared_expires_at_iso, held_at_iso,
        held_by_cashier_id, held_by_cashier_name, created_at_iso, updated_at_iso
      ) VALUES (?, ?, ?, ?, ?, 'OfflineOrigin', 'Prepared', ?, 1, ?, ?, ?, ?, ?, ?, ?)`,
      [
        "claim-trigger",
        "hold-trigger",
        "recall-attempt-1",
        "S1",
        "IPAD-01",
        "prepare-key-1",
        new TextEncoder().encode("ciphertext"),
        TEST_NOW_ISO,
        TEST_NOW_ISO,
        "cashier-1",
        "Cashier One",
        TEST_NOW_ISO,
        TEST_NOW_ISO,
      ],
    );

    // Prepared -> Active 必须同时写入 activate key；缺 key 直接 ABORT。
    await assert.rejects(
      connection.run(
        `UPDATE shared_held_order_claim_records SET state = 'Active'
         WHERE claim_guid = ?`,
        ["claim-trigger"],
      ),
      /SHARED_HELD_ORDER_CLAIM_TRANSITION_INVALID/,
    );

    // 不可变字段（claim_guid）被改写时 ABORT。
    await assert.rejects(
      connection.run(
        `UPDATE shared_held_order_claim_records SET claim_guid = 'claim-other'
         WHERE claim_guid = ?`,
        ["claim-trigger"],
      ),
      /SHARED_HELD_ORDER_CLAIM_IMMUTABLE/,
    );
  } finally {
    await connection.close();
  }
});
