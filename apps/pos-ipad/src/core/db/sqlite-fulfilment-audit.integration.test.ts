import assert from "node:assert/strict";
import { DatabaseSync, type SQLInputValue } from "node:sqlite";
import test from "node:test";

import { POS_DATABASE_MIGRATIONS } from "./migrations";
import {
  SqliteFulfilmentStore,
  type FulfilmentAuditEvent,
} from "./sqlite-fulfilment-store";
import type { SqliteConnectionPort, SqlRunResult, SqlValue } from "./types";

class NodeSqliteConnection implements SqliteConnectionPort {
  public failNextAuditInsert = false;
  private transactionActive = false;

  public constructor(private readonly database: DatabaseSync) {}

  public async exec(sql: string): Promise<void> {
    this.database.exec(sql);
  }

  public async run(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<SqlRunResult> {
    if (this.failNextAuditInsert && sql.includes("INSERT INTO audit_events")) {
      this.failNextAuditInsert = false;
      throw new Error("simulated audit insert failure");
    }
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
    if (this.transactionActive) throw new Error("Nested test transaction.");
    this.transactionActive = true;
    this.database.exec("BEGIN IMMEDIATE");
    try {
      const result = await operation(this);
      this.database.exec("COMMIT");
      return result;
    } catch (error) {
      this.database.exec("ROLLBACK");
      throw error;
    } finally {
      this.transactionActive = false;
    }
  }

  public async close(): Promise<void> {
    this.database.close();
  }
}

function createHarness() {
  const connection = new NodeSqliteConnection(
    new DatabaseSync(":memory:", { enableForeignKeyConstraints: true }),
  );
  const store = new SqliteFulfilmentStore(connection, {
    encryptor: {
      async encrypt(value) { return new TextEncoder().encode(value); },
      async decrypt(value) { return new TextDecoder().decode(value); },
    },
    nowIso: () => "2026-07-28T04:00:00.000Z",
    createPrintJobId: () => "unused-reprint-id",
  });
  return { connection, store };
}

async function seedOrder(
  connection: SqliteConnectionPort,
  orderGuid = "order-1",
  localSequence = 1,
  state:
    | "Draft"
    | "Completing"
    | "CompletedLocal"
    | "PendingSync"
    | "Syncing"
    | "Synced"
    | "Blocked403"
    | "Rejected" = "PendingSync",
) {
  const schema = await connection.getFirst<{ name: string }>(
    "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'local_orders'",
  );
  if (!schema) {
    await connection.exec(
      POS_DATABASE_MIGRATIONS.map((migration) => migration.sql).join("\n"),
    );
  }
  await connection.run(
    `INSERT INTO local_orders (
      order_guid, local_sequence, store_code, device_code, cashier_id, cashier_name,
      sold_at_iso, state, total_cents, discount_cents, actual_amount_cents,
      original_order_guid, created_at_iso, updated_at_iso
    ) VALUES (?, ?, 'S1', 'IPAD1', 'cashier-1', 'Cashier',
      '2026-07-28T03:00:00.000Z', ?, 500, 0, 500,
      NULL, '2026-07-28T03:00:00.000Z', '2026-07-28T03:00:00.000Z')`,
    [orderGuid, localSequence, state],
  );
}

async function seedPrint(
  connection: SqliteConnectionPort,
  jobId: string,
  isReprint: boolean,
  state: "Sending" | "Printed" = "Sending",
  orderGuid = "order-1",
) {
  await connection.run(
    `INSERT INTO print_jobs (
      job_id, order_guid, state, printer_id, receipt_ciphertext, is_reprint,
      retry_count, last_error_code, created_at_iso, updated_at_iso
    ) VALUES (?, ?, ?, 'XP-1', ?, ?, 0, NULL,
      '2026-07-28T03:00:00.000Z', '2026-07-28T03:00:00.000Z')`,
    [jobId, orderGuid, state, Uint8Array.of(1), isReprint ? 1 : 0],
  );
}

async function seedDrawer(
  connection: SqliteConnectionPort,
  eventId: string,
  state: "Requested" | "Completed" = "Requested",
) {
  await connection.run(
    `INSERT INTO drawer_events (
      event_id, order_guid, printer_id, print_job_id, state, reason, retry_count,
      requested_at_iso, completed_at_iso, last_error_code, created_at_iso, updated_at_iso
    ) VALUES (?, 'order-1', 'XP-1', NULL, ?, 'cash-sale', 0,
      '2026-07-28T03:00:00.000Z', NULL, NULL,
      '2026-07-28T03:00:00.000Z', '2026-07-28T03:00:00.000Z')`,
    [eventId, state],
  );
}

function audit(
  eventId: string,
  eventType: "RECEIPT_REPRINT" | "CASH_DRAWER_OPEN",
  payload: Readonly<Record<string, string | number | null>> = {
    action: "open",
    status: "Completed",
    outcome: "Succeeded",
    printerId: "XP-1",
  },
): FulfilmentAuditEvent {
  return {
    eventId,
    eventType,
    occurredAtIso: "2026-07-28T04:00:00.000Z",
    orderGuid: "order-1",
    correlationId: "order-1",
    payload,
  };
}

test("真实 SQLite：打印和钱箱终态 CAS 与对应审计在同一事务成功", async () => {
  const { connection, store } = createHarness();
  await seedOrder(connection);
  await seedPrint(connection, "print-success", true);
  await seedDrawer(connection, "drawer-success");

  assert.equal(
    await store.finishPrintJob(
      "print-success",
      "Sending",
      "Printed",
      null,
      audit("audit-print-success", "RECEIPT_REPRINT", {
        action: "reprint-last-receipt",
        status: "Printed",
        outcome: "Succeeded",
        printerId: "XP-1",
      }),
    ),
    true,
  );
  assert.equal(
    await store.finishDrawerEvent(
      "drawer-success",
      "Requested",
      "Completed",
      null,
      audit("audit-drawer-success", "CASH_DRAWER_OPEN"),
    ),
    true,
  );

  const row = await connection.getFirst<{
    printState: string;
    drawerState: string;
    audits: number;
  }>(
    `SELECT
      (SELECT state FROM print_jobs WHERE job_id = 'print-success') AS printState,
      (SELECT state FROM drawer_events WHERE event_id = 'drawer-success') AS drawerState,
      (SELECT COUNT(*) FROM audit_events) AS audits`,
  );
  assert.deepEqual({ ...row }, {
    printState: "Printed",
    drawerState: "Completed",
    audits: 2,
  });
});

test("真实 SQLite：重复审计 eventId 使打印状态回滚到 Sending", async () => {
  const { connection, store } = createHarness();
  await seedOrder(connection);
  await seedPrint(connection, "print-duplicate", true);
  await connection.run(
    "INSERT INTO audit_events (event_id, event_type, occurred_at_iso, order_guid, correlation_id, payload_json, uploaded_at_iso) VALUES ('audit-duplicate', 'RECEIPT_REPRINT', '2026-07-28T03:00:00.000Z', 'order-1', 'order-1', '{}', NULL)",
  );

  await assert.rejects(
    store.finishPrintJob(
      "print-duplicate",
      "Sending",
      "Printed",
      null,
      audit("audit-duplicate", "RECEIPT_REPRINT", {
        action: "reprint-last-receipt",
        status: "Printed",
        outcome: "Succeeded",
        printerId: "XP-1",
      }),
    ),
    /UNIQUE constraint failed/i,
  );
  const state = await connection.getFirst<{ state: string }>(
    "SELECT state FROM print_jobs WHERE job_id = 'print-duplicate'",
  );
  assert.equal(state?.state, "Sending");
});

test("真实 SQLite：模拟钱箱审计插入失败时状态回滚到 Requested", async () => {
  const { connection, store } = createHarness();
  await seedOrder(connection);
  await seedDrawer(connection, "drawer-audit-failure");
  connection.failNextAuditInsert = true;

  await assert.rejects(
    store.finishDrawerEvent(
      "drawer-audit-failure",
      "Requested",
      "Completed",
      null,
      audit("audit-drawer-failure", "CASH_DRAWER_OPEN"),
    ),
    /simulated audit insert failure/,
  );
  const row = await connection.getFirst<{ state: string; audits: number }>(
    "SELECT state, (SELECT COUNT(*) FROM audit_events) AS audits FROM drawer_events WHERE event_id = 'drawer-audit-failure'",
  );
  assert.deepEqual({ ...row }, { state: "Requested", audits: 0 });
});

test("真实 SQLite：CAS 冲突返回 false 且不插入审计", async () => {
  const { connection, store } = createHarness();
  await seedOrder(connection);
  await seedPrint(connection, "print-conflict", true, "Printed");
  await seedDrawer(connection, "drawer-conflict", "Completed");

  assert.equal(
    await store.finishPrintJob(
      "print-conflict",
      "Sending",
      "Printed",
      null,
      audit("audit-print-conflict", "RECEIPT_REPRINT"),
    ),
    false,
  );
  assert.equal(
    await store.finishDrawerEvent(
      "drawer-conflict",
      "Requested",
      "Completed",
      null,
      audit("audit-drawer-conflict", "CASH_DRAWER_OPEN"),
    ),
    false,
  );
  const audits = await connection.getFirst<{ count: number }>(
    "SELECT COUNT(*) AS count FROM audit_events",
  );
  assert.equal(audits?.count, 0);
});

test("真实 SQLite：敏感或错绑审计被拒绝且硬件状态回滚", async () => {
  const { connection, store } = createHarness();
  await seedOrder(connection);
  await seedPrint(connection, "print-sensitive", true);
  await seedDrawer(connection, "drawer-wrong-order");

  await assert.rejects(
    store.finishPrintJob(
      "print-sensitive",
      "Sending",
      "Failed",
      "BLE_LOST",
      audit("audit-sensitive", "RECEIPT_REPRINT", {
        action: "retry-failed-print",
        authorizationToken: "must-not-persist",
      }),
    ),
    /audit payload|sensitive/i,
  );
  await assert.rejects(
    store.finishDrawerEvent(
      "drawer-wrong-order",
      "Requested",
      "Failed",
      "OFFLINE",
      {
        ...audit("audit-wrong-order", "CASH_DRAWER_OPEN"),
        orderGuid: "another-order",
      },
    ),
    /order/i,
  );

  const row = await connection.getFirst<{
    printState: string;
    drawerState: string;
    audits: number;
  }>(
    `SELECT
      (SELECT state FROM print_jobs WHERE job_id = 'print-sensitive') AS printState,
      (SELECT state FROM drawer_events WHERE event_id = 'drawer-wrong-order') AS drawerState,
      (SELECT COUNT(*) FROM audit_events) AS audits`,
  );
  assert.deepEqual({ ...row }, {
    printState: "Sending",
    drawerState: "Requested",
    audits: 0,
  });
});

test("真实 SQLite：仅普通非重打打印允许 audit=null", async () => {
  const { connection, store } = createHarness();
  await seedOrder(connection);
  await seedPrint(connection, "print-automatic", false);
  await seedPrint(connection, "print-reprint", true);

  assert.equal(
    await store.finishPrintJob(
      "print-automatic",
      "Sending",
      "Printed",
      null,
      null,
    ),
    true,
  );
  await assert.rejects(
    store.finishPrintJob(
      "print-reprint",
      "Sending",
      "Printed",
      null,
      null,
    ),
    /reprint.*audit/i,
  );

  const row = await connection.getFirst<{
    automaticState: string;
    reprintState: string;
    audits: number;
  }>(
    `SELECT
      (SELECT state FROM print_jobs WHERE job_id = 'print-automatic') AS automaticState,
      (SELECT state FROM print_jobs WHERE job_id = 'print-reprint') AS reprintState,
      (SELECT COUNT(*) FROM audit_events) AS audits`,
  );
  assert.deepEqual({ ...row }, {
    automaticState: "Printed",
    reprintState: "Sending",
    audits: 0,
  });
});

test("真实 SQLite：指定已完成订单无需历史 Printed 作业也能创建重打", async () => {
  const { connection, store } = createHarness();
  await seedOrder(connection, "order-target", 1, "PendingSync");

  const reprint = await store.createLastReceiptReprint({
    orderGuid: "order-target",
    receiptBytes: Uint8Array.of(29, 33),
    printerId: "XP-1",
  });

  assert.equal(reprint?.orderGuid, "order-target");
  const row = await connection.getFirst<{
    orderGuid: string;
    state: string;
    isReprint: number;
  }>(
    "SELECT order_guid AS orderGuid, state, is_reprint AS isReprint FROM print_jobs WHERE job_id = 'unused-reprint-id'",
  );
  assert.deepEqual({ ...row }, {
    orderGuid: "order-target",
    state: "Queued",
    isReprint: 1,
  });
});

test("真实 SQLite：历史 Printed 作业不能把指定重打改绑到旧订单", async () => {
  const { connection, store } = createHarness();
  await seedOrder(connection, "order-old", 1, "Synced");
  await seedOrder(connection, "order-target", 2, "Blocked403");
  await seedPrint(connection, "print-old", false, "Printed", "order-old");

  const reprint = await store.createLastReceiptReprint({
    orderGuid: "order-target",
    receiptBytes: Uint8Array.of(29, 33),
    printerId: "XP-1",
  });

  assert.equal(reprint?.orderGuid, "order-target");
  const row = await connection.getFirst<{ orderGuid: string }>(
    "SELECT order_guid AS orderGuid FROM print_jobs WHERE job_id = 'unused-reprint-id'",
  );
  assert.equal(row?.orderGuid, "order-target");
});

test("真实 SQLite：指定订单不存在或尚未完成时不创建重打", async () => {
  const { connection, store } = createHarness();
  await seedOrder(connection, "order-old", 1, "Synced");
  await seedOrder(connection, "order-draft", 2, "Draft");
  await seedPrint(connection, "print-old", false, "Printed", "order-old");

  assert.equal(
    await store.createLastReceiptReprint({
      orderGuid: "order-missing",
      receiptBytes: Uint8Array.of(29, 33),
      printerId: "XP-1",
    }),
    null,
  );
  assert.equal(
    await store.createLastReceiptReprint({
      orderGuid: "order-draft",
      receiptBytes: Uint8Array.of(29, 33),
      printerId: "XP-1",
    }),
    null,
  );
  const row = await connection.getFirst<{ count: number }>(
    "SELECT COUNT(*) AS count FROM print_jobs WHERE is_reprint = 1",
  );
  assert.equal(row?.count, 0);
});
