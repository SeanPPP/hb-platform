import assert from "node:assert/strict";
import { DatabaseSync, type SQLInputValue } from "node:sqlite";
import test from "node:test";

import { POS_DATABASE_MIGRATIONS } from "./migrations";
import { ReceiptCompletionSettlementRepository } from "./receipt-completion-settlement-repository";
import type {
  SqliteConnectionPort,
  SqlRunResult,
  SqlValue,
} from "./types";

class NodeSqliteConnection implements SqliteConnectionPort {
  public constructor(private readonly database: DatabaseSync) {}

  public async exec(sql: string): Promise<void> {
    this.database.exec(sql);
  }

  public async run(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<SqlRunResult> {
    const result = this.database
      .prepare(sql)
      .run(...parameters as readonly SQLInputValue[]);
    return {
      changes: Number(result.changes),
      lastInsertRowId: Number(result.lastInsertRowid),
    };
  }

  public async getFirst<T extends object>(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<T | null> {
    const row = this.database
      .prepare(sql)
      .get(...parameters as readonly SQLInputValue[]);
    return row === undefined ? null : row as unknown as T;
  }

  public async getAll<T extends object>(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<readonly T[]> {
    return this.database
      .prepare(sql)
      .all(...parameters as readonly SQLInputValue[]) as unknown as readonly T[];
  }

  public async withExclusiveTransaction<T>(
    operation: (transaction: SqliteConnectionPort) => Promise<T>,
  ): Promise<T> {
    this.database.exec("BEGIN IMMEDIATE");
    try {
      const result = await operation(this);
      this.database.exec("COMMIT");
      return result;
    } catch (error) {
      this.database.exec("ROLLBACK");
      throw error;
    }
  }

  public async close(): Promise<void> {
    this.database.close();
  }
}

async function harness() {
  const connection = new NodeSqliteConnection(
    new DatabaseSync(":memory:", { enableForeignKeyConstraints: true }),
  );
  await connection.exec(
    POS_DATABASE_MIGRATIONS.map((migration) => migration.sql).join("\n"),
  );
  await connection.run(
    `INSERT INTO local_orders (
      order_guid, local_sequence, store_code, device_code, cashier_id,
      cashier_name, sold_at_iso, state, total_cents, discount_cents,
      actual_amount_cents, original_order_guid, created_at_iso, updated_at_iso
    ) VALUES (?, 1, 'S1', 'IPAD1', 'C1', 'Alice', ?, 'PendingSync',
      500, 0, 500, NULL, ?, ?)`,
    [
      "order-1",
      "2026-07-28T00:00:00.000Z",
      "2026-07-28T00:00:00.000Z",
      "2026-07-28T00:00:00.000Z",
    ],
  );
  return {
    connection,
    repository: new ReceiptCompletionSettlementRepository(connection),
  };
}

async function insertAudit(
  connection: SqliteConnectionPort,
  eventId: string,
  payload: unknown,
  eventType = "SALE_COMPLETE",
): Promise<void> {
  await connection.run(
    `INSERT INTO audit_events (
      event_id, event_type, occurred_at_iso, order_guid, correlation_id,
      payload_json, uploaded_at_iso
    ) VALUES (?, ?, ?, 'order-1', 'order-1', ?, NULL)`,
    [
      eventId,
      eventType,
      "2026-07-28T00:00:00.000Z",
      typeof payload === "string" ? payload : JSON.stringify(payload),
    ],
  );
}

test("真实 SQLite：只从唯一现金完成审计读取整数找零", async () => {
  const { connection, repository } = await harness();
  try {
    await insertAudit(connection, "audit-1", {
      checkoutIntentId: "intent-1",
      changeCents: 235,
    });

    assert.deepEqual(await repository.getByOrderGuid("order-1"), {
      cashChangeCents: 235,
    });
    assert.equal(await repository.getByOrderGuid("unknown-order"), null);
  } finally {
    await connection.close();
  }
});

test("真实 SQLite：重复、损坏、负数或非整数完成审计全部 fail closed", async () => {
  for (const payload of [
    "{broken-json",
    { changeCents: -1 },
    { changeCents: 1.5 },
    { cashChangeCents: 0 },
  ]) {
    const { connection, repository } = await harness();
    try {
      await insertAudit(connection, "audit-invalid", payload);
      assert.equal(await repository.getByOrderGuid("order-1"), null);
    } finally {
      await connection.close();
    }
  }

  const { connection, repository } = await harness();
  try {
    await insertAudit(connection, "audit-1", { changeCents: 0 });
    await insertAudit(
      connection,
      "audit-2",
      { changeCents: 0 },
      "RETURN_REFUND_COMPLETE",
    );
    assert.equal(await repository.getByOrderGuid("order-1"), null);
  } finally {
    await connection.close();
  }
});
