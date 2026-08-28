import assert from "node:assert/strict";
import { DatabaseSync, type SQLInputValue } from "node:sqlite";
import test from "node:test";

import { POS_DATABASE_MIGRATIONS } from "./migrations";
import { ReceiptCompletionSettlementRepository } from "./receipt-completion-settlement-repository";
import type {
  SqliteConnectionPort,
  SqlRunResult,
  SqlValue,
} from "@hb/pos-db/core/db/types";

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
  correlationId = "order-1",
): Promise<void> {
  await connection.run(
    `INSERT INTO audit_events (
      event_id, event_type, occurred_at_iso, order_guid, correlation_id,
      payload_json, uploaded_at_iso
    ) VALUES (?, ?, ?, 'order-1', ?, ?, NULL)`,
    [
      eventId,
      eventType,
      "2026-07-28T00:00:00.000Z",
      correlationId,
      typeof payload === "string" ? payload : JSON.stringify(payload),
    ],
  );
}

async function insertCashTender(
  connection: SqliteConnectionPort,
  tenderGuid: string,
  amountCents: number,
): Promise<void> {
  await connection.run(
    `INSERT INTO order_tenders (
      tender_guid, order_guid, method, amount_cents,
      payment_attempt_id, created_at_iso
    ) VALUES (?, 'order-1', 'cash', ?, NULL, ?)`,
    [tenderGuid, amountCents, "2026-07-28T00:00:00.000Z"],
  );
}

async function insertApprovedTender(
  connection: SqliteConnectionPort,
  tenderGuid: string,
  method: "card" | "voucher",
  amountCents: number,
  attemptId: string,
): Promise<void> {
  await connection.run(
    `INSERT INTO order_tenders (
      tender_guid, order_guid, method, amount_cents,
      payment_attempt_id, created_at_iso
    ) VALUES (?, 'order-1', ?, ?, ?, ?)`,
    [
      tenderGuid,
      method,
      amountCents,
      attemptId,
      "2026-07-28T00:00:00.000Z",
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

test("真实 SQLite：只兼容同一 action 的最终完成、现金入账和当前 tender", async () => {
  const { connection, repository } = await harness();
  try {
    await insertCashTender(connection, "tender-final", 100);
    await insertAudit(
      connection,
      "audit-mixed-cash",
      {
        appliedCents: 100,
        changeCents: 400,
        tenderedCents: 500,
        tenderGuid: "tender-final",
      },
      "MIXED_CASH_TENDER_APPENDED",
      "action-final",
    );
    await insertAudit(
      connection,
      "audit-mixed-complete",
      { method: "cash", amountCents: 100 },
      "PAYMENT_MIXED_CASH_COMPLETE",
      "action-final",
    );

    assert.deepEqual(await repository.getByOrderGuid("order-1"), {
      cashChangeCents: 400,
    });
  } finally {
    await connection.close();
  }
});

test("真实 SQLite：最终现金按五分舍入验证小票找零", async () => {
  for (const scenario of [
    { name: "兼容旧版逐分实收", appliedCents: 699, tenderedCents: 699, changeCents: 0 },
    { name: "向上舍入", appliedCents: 699, tenderedCents: 700, changeCents: 0 },
    { name: "向下舍入", appliedCents: 701, tenderedCents: 700, changeCents: 0 },
    { name: "舍入后找零", appliedCents: 699, tenderedCents: 1_000, changeCents: 300 },
  ]) {
    const { connection, repository } = await harness();
    try {
      await insertCashTender(connection, "tender-final", scenario.appliedCents);
      await insertAudit(
        connection,
        "audit-mixed-cash",
        {
          appliedCents: scenario.appliedCents,
          changeCents: scenario.changeCents,
          tenderedCents: scenario.tenderedCents,
          tenderGuid: "tender-final",
        },
        "MIXED_CASH_TENDER_APPENDED",
        "action-final",
      );
      await insertAudit(
        connection,
        "audit-mixed-complete",
        { method: "cash", amountCents: scenario.appliedCents },
        "PAYMENT_MIXED_CASH_COMPLETE",
        "action-final",
      );

      assert.deepEqual(
        await repository.getByOrderGuid("order-1"),
        { cashChangeCents: scenario.changeCents },
        scenario.name,
      );
    } finally {
      await connection.close();
    }
  }
});

test("真实 SQLite：精确部分现金后允许最终现金按五分舍入", async () => {
  const { connection, repository } = await harness();
  try {
    await insertCashTender(connection, "tender-cash-partial", 101);
    await insertAudit(
      connection,
      "audit-cash-partial",
      {
        appliedCents: 101,
        changeCents: 399,
        tenderedCents: 500,
        tenderGuid: "tender-cash-partial",
      },
      "MIXED_CASH_TENDER_APPENDED",
      "action-cash-partial",
    );
    await insertCashTender(connection, "tender-cash-final", 399);
    await insertAudit(
      connection,
      "audit-cash-final",
      {
        appliedCents: 399,
        changeCents: 0,
        tenderedCents: 400,
        tenderGuid: "tender-cash-final",
      },
      "MIXED_CASH_TENDER_APPENDED",
      "action-cash-final",
    );
    await insertAudit(
      connection,
      "audit-mixed-complete",
      { method: "cash", amountCents: 399 },
      "PAYMENT_MIXED_CASH_COMPLETE",
      "action-cash-final",
    );

    assert.deepEqual(await repository.getByOrderGuid("order-1"), {
      cashChangeCents: 399,
    });
  } finally {
    await connection.close();
  }
});

test("真实 SQLite：部分现金后由卡完成时核对全部有效 tender 并返回现金找零", async () => {
  const { connection, repository } = await harness();
  try {
    await insertCashTender(connection, "tender-cash-partial", 100);
    await insertAudit(
      connection,
      "audit-cash-partial",
      {
        appliedCents: 100,
        changeCents: 400,
        tenderedCents: 500,
        tenderGuid: "tender-cash-partial",
      },
      "MIXED_CASH_TENDER_APPENDED",
      "action-cash-partial",
    );
    await insertApprovedTender(
      connection,
      "tender-card-final",
      "card",
      400,
      "attempt-card-final",
    );
    await insertAudit(
      connection,
      "audit-card-complete",
      {
        attemptId: "attempt-card-final",
        provider: "terminal_api",
        method: "card",
        amountCents: 400,
      },
      "PAYMENT_APPROVED_COMPLETE",
      "attempt-card-final",
    );

    assert.deepEqual(await repository.getByOrderGuid("order-1"), {
      cashChangeCents: 400,
    });
  } finally {
    await connection.close();
  }
});

test("真实 SQLite：卡完成前的部分现金不得套用最终现金舍入", async () => {
  const { connection, repository } = await harness();
  try {
    await insertCashTender(connection, "tender-cash-partial", 699);
    await insertAudit(
      connection,
      "audit-cash-partial",
      {
        appliedCents: 699,
        changeCents: 0,
        tenderedCents: 700,
        tenderGuid: "tender-cash-partial",
      },
      "MIXED_CASH_TENDER_APPENDED",
      "action-cash-partial",
    );
    await insertApprovedTender(
      connection,
      "tender-card-final",
      "card",
      1,
      "attempt-card-final",
    );
    await insertAudit(
      connection,
      "audit-card-complete",
      {
        attemptId: "attempt-card-final",
        provider: "terminal_api",
        method: "card",
        amountCents: 1,
      },
      "PAYMENT_APPROVED_COMPLETE",
      "attempt-card-final",
    );

    assert.equal(await repository.getByOrderGuid("order-1"), null);
  } finally {
    await connection.close();
  }
});

test("真实 SQLite：卡完成缺现金 append 或最终 tender 不一致时 fail closed", async () => {
  for (const mismatch of ["cash-append", "final-tender"] as const) {
    const { connection, repository } = await harness();
    try {
      await insertCashTender(connection, "tender-cash-partial", 100);
      if (mismatch !== "cash-append") {
        await insertAudit(
          connection,
          "audit-cash-partial",
          {
            appliedCents: 100,
            changeCents: 0,
            tenderedCents: 100,
            tenderGuid: "tender-cash-partial",
          },
          "MIXED_CASH_TENDER_APPENDED",
          "action-cash-partial",
        );
      }
      await insertApprovedTender(
        connection,
        "tender-card-final",
        "card",
        mismatch === "final-tender" ? 399 : 400,
        "attempt-card-final",
      );
      await insertAudit(
        connection,
        "audit-card-complete",
        {
          attemptId: "attempt-card-final",
          provider: "terminal_api",
          method: "card",
          amountCents: 400,
        },
        "PAYMENT_APPROVED_COMPLETE",
        "attempt-card-final",
      );

      assert.equal(await repository.getByOrderGuid("order-1"), null, mismatch);
    } finally {
      await connection.close();
    }
  }
});

test("真实 SQLite：混合现金审计缺字段、金额不自洽或不唯一时 fail closed", async () => {
  for (const payload of [
    { appliedCents: 100, changeCents: 399, tenderedCents: 500 },
    { appliedCents: 100, changeCents: 400 },
    { appliedCents: -1, changeCents: 0, tenderedCents: -1 },
    { appliedCents: 100.5, changeCents: 399.5, tenderedCents: 500 },
  ]) {
    const { connection, repository } = await harness();
    try {
      await insertAudit(
        connection,
        "audit-mixed-invalid",
        { ...payload, tenderGuid: "tender-final" },
        "MIXED_CASH_TENDER_APPENDED",
        "action-final",
      );
      await insertCashTender(connection, "tender-final", 100);
      await insertAudit(
        connection,
        "audit-mixed-complete",
        { method: "cash", amountCents: 100 },
        "PAYMENT_MIXED_CASH_COMPLETE",
        "action-final",
      );
      assert.equal(await repository.getByOrderGuid("order-1"), null);
    } finally {
      await connection.close();
    }
  }

  const { connection, repository } = await harness();
  try {
    const payload = {
      appliedCents: 100,
      changeCents: 400,
      tenderedCents: 500,
      tenderGuid: "tender-final",
    };
    await insertCashTender(connection, "tender-final", 100);
    await insertAudit(
      connection,
      "audit-mixed-1",
      payload,
      "MIXED_CASH_TENDER_APPENDED",
      "action-final",
    );
    await insertAudit(
      connection,
      "audit-mixed-2",
      payload,
      "MIXED_CASH_TENDER_APPENDED",
      "action-final",
    );
    await insertAudit(
      connection,
      "audit-mixed-complete",
      { method: "cash", amountCents: 100 },
      "PAYMENT_MIXED_CASH_COMPLETE",
      "action-final",
    );
    assert.equal(await repository.getByOrderGuid("order-1"), null);
  } finally {
    await connection.close();
  }
});

test("真实 SQLite：缺最终完成、action 不同、tender 不一致或已撤销时 fail closed", async () => {
  {
    const { connection, repository } = await harness();
    try {
      await insertCashTender(connection, "tender-final", 100);
      await insertAudit(
        connection,
        "audit-append-only",
        { appliedCents: 100, changeCents: 400, tenderedCents: 500, tenderGuid: "tender-final" },
        "MIXED_CASH_TENDER_APPENDED",
        "action-final",
      );
      assert.equal(await repository.getByOrderGuid("order-1"), null);
    } finally {
      await connection.close();
    }
  }

  for (const mismatch of ["action", "amount", "tender"] as const) {
    const { connection, repository } = await harness();
    try {
      await insertCashTender(connection, "tender-final", mismatch === "tender" ? 99 : 100);
      await insertAudit(
        connection,
        "audit-append",
        { appliedCents: 100, changeCents: 400, tenderedCents: 500, tenderGuid: "tender-final" },
        "MIXED_CASH_TENDER_APPENDED",
        mismatch === "action" ? "action-append" : "action-final",
      );
      await insertAudit(
        connection,
        "audit-complete",
        { method: "cash", amountCents: mismatch === "amount" ? 99 : 100 },
        "PAYMENT_MIXED_CASH_COMPLETE",
        "action-final",
      );
      assert.equal(await repository.getByOrderGuid("order-1"), null, mismatch);
    } finally {
      await connection.close();
    }
  }

  {
    const { connection, repository } = await harness();
    try {
      await insertCashTender(connection, "tender-final", 100);
      await insertCashTender(connection, "tender-reversal", -100);
      await connection.run(
        `INSERT INTO payment_tender_reversal_links (
          order_guid, action_id, source_tender_guid,
          reversal_tender_guid, created_at_iso
        ) VALUES ('order-1', 'reverse-action', 'tender-final', 'tender-reversal', ?)`,
        ["2026-07-28T00:01:00.000Z"],
      );
      await insertAudit(
        connection,
        "audit-append",
        { appliedCents: 100, changeCents: 400, tenderedCents: 500, tenderGuid: "tender-final" },
        "MIXED_CASH_TENDER_APPENDED",
        "action-final",
      );
      await insertAudit(
        connection,
        "audit-complete",
        { method: "cash", amountCents: 100 },
        "PAYMENT_MIXED_CASH_COMPLETE",
        "action-final",
      );
      assert.equal(await repository.getByOrderGuid("order-1"), null);
    } finally {
      await connection.close();
    }
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
