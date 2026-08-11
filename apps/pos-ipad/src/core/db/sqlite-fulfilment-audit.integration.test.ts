import assert from "node:assert/strict";
import { DatabaseSync, type SQLInputValue } from "node:sqlite";
import test from "node:test";

import { FulfilmentService } from "../../features/fulfilment/fulfilment-service";

import { applyMigrations, POS_DATABASE_MIGRATIONS } from "./migrations";
import {
  SqliteFulfilmentStore,
  type FulfilmentAuditEvent,
  type FulfilmentInitialAuthorization,
} from "./sqlite-fulfilment-store";
import { createSqliteRepositories } from "./sqlite-repositories";
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
  correlationId = "order-1",
): FulfilmentAuditEvent {
  return {
    eventId,
    eventType,
    occurredAtIso: "2026-07-28T04:00:00.000Z",
    orderGuid: "order-1",
    correlationId,
    payload,
  };
}

function initialAuthorization(
  input: Readonly<{
    actionId: string;
    eventType: "RECEIPT_REPRINT" | "CASH_DRAWER_OPEN";
    orderGuid: string | null;
    printerId: string;
    source?: "last-receipt" | "payment-success" | "remote-history" | "installment-history";
    externalOrderGuid?: string;
  }>,
): FulfilmentInitialAuthorization {
  const isReprint = input.eventType === "RECEIPT_REPRINT";
  const source = input.source ?? "last-receipt";
  const isPaymentSuccess = isReprint && source === "payment-success";
  const isHistory =
    isReprint &&
    (source === "remote-history" || source === "installment-history");
  const context = {
    actionId: input.actionId,
    permissionCode: isReprint
      ? isHistory
        ? "Permissions.PosTerminal.History.Reprint"
        : "Permissions.PosTerminal.Receipt.PrintLast"
      : "Permissions.PosTerminal.CashDrawer.Open",
    authorizationMode: "online" as const,
    requestingCashierId: "cashier-1",
    requestingCashierName: "Cashier One",
    requestingUserGuid: "user-1",
    authorizingCashierId: "supervisor-1",
  };
  return {
    context,
    audit: {
      eventId: `audit-authorized-${input.actionId}`,
      eventType: input.eventType,
      occurredAtIso: "2026-07-28T04:00:00.000Z",
      orderGuid: input.orderGuid,
      correlationId: input.actionId,
      payload: {
        action: isReprint
          ? isPaymentSuccess
            ? "reprint-current-receipt"
            : isHistory
            ? "reprint-history-receipt"
            : "reprint-last-receipt"
          : "open-cash-drawer",
        status: "Authorized",
        reason: isReprint ? source : "MANUAL",
        source: isHistory || isPaymentSuccess ? source : "sales",
        outcome: "Succeeded",
        printerId: input.printerId,
        errorCode: null,
        requestingCashierId: context.requestingCashierId,
        requestingCashierName: context.requestingCashierName,
        requestingUserGuid: context.requestingUserGuid,
        authorizingCashierId: context.authorizingCashierId,
        permissionCode: context.permissionCode,
        authorizationMode: context.authorizationMode,
      },
      ...((isHistory || input.externalOrderGuid) && input.orderGuid
        ? {
            externalOrderGuid: input.externalOrderGuid ?? input.orderGuid,
            scopeStoreCode: "S1",
            scopeDeviceCode: "IPAD1",
          }
        : {}),
    },
  };
}

test("真实 SQLite：M32 原有本机履约事实无损升级当前外部订单身份", async () => {
  const { connection } = createHarness();
  await applyMigrations(
    connection,
    () => "2026-07-28T03:00:00.000Z",
    POS_DATABASE_MIGRATIONS.filter((migration) => migration.version <= 32),
  );
  await seedOrder(connection, "order-before-m33");
  await seedPrint(
    connection,
    "print-before-m33",
    true,
    "Printed",
    "order-before-m33",
  );

  await applyMigrations(
    connection,
    () => "2026-07-28T04:00:00.000Z",
  );

  const row = await connection.getFirst<{
    version: number;
    orderGuid: string;
    externalOrderGuid: string | null;
  }>(
    `SELECT
      (SELECT MAX(version) FROM schema_migrations) AS version,
      order_guid AS orderGuid,
      external_order_guid AS externalOrderGuid
     FROM print_jobs WHERE job_id = 'print-before-m33'`,
  );
  assert.deepEqual({ ...row }, {
    version: 40,
    orderGuid: "order-before-m33",
    externalOrderGuid: null,
  });
});

test("真实 SQLite：M33 升级后允许分期历史外部订单审计并恢复来源", async () => {
  const { connection, store } = createHarness();
  await applyMigrations(
    connection,
    () => "2026-07-28T03:00:00.000Z",
    POS_DATABASE_MIGRATIONS.filter((migration) => migration.version <= 33),
  );
  const orderGuid = "30000000-0000-4000-8000-000000000003";
  const authorization = initialAuthorization({
    actionId: "action-installment-history-reprint",
    eventType: "RECEIPT_REPRINT",
    orderGuid,
    printerId: "XP-INSTALLMENT",
    source: "installment-history",
  });
  const input = {
    orderGuid,
    receiptBytes: Uint8Array.of(29, 33, 82),
    printerId: "XP-INSTALLMENT",
  };

  await assert.rejects(
    store.createLastReceiptReprint(input, authorization),
    /AUDIT_EXTERNAL_ORDER_INVALID/u,
  );
  assert.deepEqual(
    { ...await connection.getFirst<{ jobs: number; audits: number }>(
      `SELECT
        (SELECT COUNT(*) FROM print_jobs) AS jobs,
        (SELECT COUNT(*) FROM audit_events) AS audits`,
    ) },
    { jobs: 0, audits: 0 },
  );

  await applyMigrations(
    connection,
    () => "2026-07-28T04:00:00.000Z",
  );
  const created = await store.createLastReceiptReprint(input, authorization);
  const claimed = await store.claimQueuedPrintJob(authorization.context.actionId);
  const row = await connection.getFirst<{
    version: number;
    printExternalOrderGuid: string;
    auditExternalOrderGuid: string;
    source: string;
  }>(
    `SELECT
      (SELECT MAX(version) FROM schema_migrations) AS version,
      print.external_order_guid AS printExternalOrderGuid,
      audit.external_order_guid AS auditExternalOrderGuid,
      json_extract(audit.payload_json, '$.source') AS source
     FROM print_jobs AS print
     JOIN audit_events AS audit ON audit.correlation_id = print.job_id
     WHERE print.job_id = ?`,
    [authorization.context.actionId],
  );

  assert.equal(created?.reprintSource, "installment-history");
  assert.equal(claimed?.reprintSource, "installment-history");
  assert.deepEqual({ ...row }, {
    version: 40,
    printExternalOrderGuid: orderGuid,
    auditExternalOrderGuid: orderGuid,
    source: "installment-history",
  });
  await assert.rejects(
    connection.run(
      `INSERT INTO audit_events (
        event_id, event_type, occurred_at_iso, order_guid, external_order_guid,
        correlation_id, payload_json, uploaded_at_iso,
        scope_store_code, scope_device_code
      ) VALUES (
        'audit-disallowed-external-source', 'RECEIPT_REPRINT',
        '2026-07-28T04:01:00.000Z', NULL, ?, 'disallowed-external-source', ?, NULL,
        'S1', 'IPAD1'
      )`,
      [orderGuid, JSON.stringify({
        action: "reprint-history-receipt",
        source: "local-history",
      })],
    ),
    /AUDIT_EXTERNAL_ORDER_INVALID/u,
  );
});

test("真实 SQLite：M35 只新增付款成功页分期外部订单审计", async () => {
  const { connection, store } = createHarness();
  await applyMigrations(
    connection,
    () => "2026-08-04T01:00:00.000Z",
    POS_DATABASE_MIGRATIONS.filter((migration) => migration.version <= 34),
  );
  const orderGuid = "30000000-0000-4000-8000-000000000035";
  const authorization = initialAuthorization({
    actionId: "action-payment-success-installment-reprint",
    eventType: "RECEIPT_REPRINT",
    orderGuid,
    printerId: "XP-PAYMENT-INSTALLMENT",
    source: "payment-success",
    externalOrderGuid: orderGuid,
  });
  const input = {
    orderGuid,
    receiptBytes: Uint8Array.of(29, 33, 82),
    printerId: "XP-PAYMENT-INSTALLMENT",
  };

  await assert.rejects(
    store.createLastReceiptReprint(input, authorization),
    /AUDIT_EXTERNAL_ORDER_INVALID/u,
  );
  await applyMigrations(
    connection,
    () => "2026-08-04T01:01:00.000Z",
    POS_DATABASE_MIGRATIONS.filter((migration) => migration.version <= 35),
  );

  const created = await store.createLastReceiptReprint(input, authorization);
  const row = await connection.getFirst<{
    version: number;
    printExternalOrderGuid: string;
    auditExternalOrderGuid: string;
    source: string;
  }>(
    `SELECT
      (SELECT MAX(version) FROM schema_migrations) AS version,
      print.external_order_guid AS printExternalOrderGuid,
      audit.external_order_guid AS auditExternalOrderGuid,
      json_extract(audit.payload_json, '$.source') AS source
     FROM print_jobs AS print
     JOIN audit_events AS audit ON audit.correlation_id = print.job_id
     WHERE print.job_id = ?`,
    [authorization.context.actionId],
  );

  assert.equal(created?.reprintSource, "payment-success");
  assert.deepEqual({ ...row }, {
    version: 35,
    printExternalOrderGuid: orderGuid,
    auditExternalOrderGuid: orderGuid,
    source: "payment-success",
  });
  await connection.close();
});

test("真实 SQLite：跨终端远程重打不伪造本机订单，重启恢复原 scope 且 Ambiguous 不重放", async () => {
  const { connection, store } = createHarness();
  await connection.exec(
    POS_DATABASE_MIGRATIONS.map((migration) => migration.sql).join("\n"),
  );
  const orderGuid = "10000000-0000-4000-8000-000000000001";
  const authorization = initialAuthorization({
    actionId: "action-remote-history-reprint",
    eventType: "RECEIPT_REPRINT",
    orderGuid,
    printerId: "XP-REMOTE",
    source: "remote-history",
  });

  await assert.rejects(
    store.createLastReceiptReprint(
      {
        orderGuid,
        receiptBytes: Uint8Array.of(29, 33, 82),
        printerId: "XP-REMOTE",
      },
      {
        ...authorization,
        audit: {
          ...authorization.audit,
          externalOrderGuid: "20000000-0000-4000-8000-000000000002",
        },
      },
    ),
    /external audit order does not match/u,
  );

  const created = await store.createLastReceiptReprint({
    orderGuid,
    receiptBytes: Uint8Array.of(29, 33, 82),
    printerId: "XP-REMOTE",
  }, authorization);
  assert.equal(created?.state, "Queued");

  let printCalls = 0;
  const restarted = new FulfilmentService({
    store,
    printer: {
      async connect() {},
      async print() {
        printCalls += 1;
        return { status: "ambiguous", errorCode: "DRIVER_UNKNOWN" };
      },
    },
    drawer: { async open() { throw new Error("drawer must not be called"); } },
    nowIso: () => "2026-07-28T05:00:00.000Z",
    createAuditId: () => "audit-remote-history-terminal",
    createCorrelationId: () => "unused-correlation",
    // 中文注释：模拟重启后注册身份变化；终态必须沿用首份授权审计的原 scope。
    auditScope: { storeCode: "S2", deviceCode: "IPAD2" },
    async prepareLastReceiptReprint() { return null; },
  });

  await restarted.drainAutomaticQueue();
  await restarted.drainAutomaticQueue();
  assert.equal(printCalls, 1);

  const row = await connection.getFirst<{
    localOrders: number;
    localOrderGuid: string | null;
    externalOrderGuid: string;
    state: string;
    audits: number;
    scopedAudits: number;
  }>(
    `SELECT
      (SELECT COUNT(*) FROM local_orders) AS localOrders,
      order_guid AS localOrderGuid,
      external_order_guid AS externalOrderGuid,
      state,
      (SELECT COUNT(*) FROM audit_events
       WHERE external_order_guid = ?) AS audits,
      (SELECT COUNT(*) FROM audit_events
       WHERE external_order_guid = ?
         AND scope_store_code = 'S1'
         AND scope_device_code = 'IPAD1') AS scopedAudits
     FROM print_jobs WHERE job_id = ?`,
    [orderGuid, orderGuid, authorization.context.actionId],
  );
  assert.deepEqual({ ...row }, {
    localOrders: 0,
    localOrderGuid: null,
    externalOrderGuid: orderGuid,
    state: "Ambiguous",
    audits: 2,
    scopedAudits: 2,
  });
  const delivery = createSqliteRepositories(connection, {
    nowIso: () => "2026-07-28T05:01:00.000Z",
    createLeaseId: () => "unused-lease",
    encryptor: {
      async encrypt(value) { return new TextEncoder().encode(value); },
      async decrypt(value) { return new TextDecoder().decode(value); },
    },
    auditScope: { storeCode: "S1", deviceCode: "IPAD1" },
  }).auditDelivery;
  const pending = await delivery.listReady(10);
  assert.deepEqual(
    pending.map((event) => ({
      orderGuid: event.orderGuid,
      scope: event.auditScope,
    })),
    [
      {
        orderGuid,
        scope: { storeCode: "S1", deviceCode: "IPAD1" },
      },
      {
        orderGuid,
        scope: { storeCode: "S1", deviceCode: "IPAD1" },
      },
    ],
  );
});

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
      }, "print-success"),
    ),
    true,
  );
  assert.equal(
    await store.finishDrawerEvent(
      "drawer-success",
      "Requested",
      "Completed",
      null,
      audit(
        "audit-drawer-success",
        "CASH_DRAWER_OPEN",
        undefined,
        "drawer-success",
      ),
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

test("真实 SQLite：手动开箱直接 Requested、orderGuid 为空、幂等重入且终态与审计原子收口", async () => {
  const { connection, store } = createHarness();
  await seedOrder(connection);
  const authorization = initialAuthorization({
    actionId: "manual-drawer-action",
    eventType: "CASH_DRAWER_OPEN",
    orderGuid: null,
    printerId: "XP-MANUAL",
  });

  const first = await store.beginManualDrawerOpen({
    eventId: "manual-drawer-action",
    printerId: "XP-MANUAL",
    reason: "MANUAL",
  }, authorization);
  const replay = await store.beginManualDrawerOpen({
    eventId: "manual-drawer-action",
    printerId: "XP-MANUAL",
    reason: "MANUAL",
  }, authorization);
  assert.equal(first.kind, "created");
  assert.equal(replay.kind, "existing");
  assert.deepEqual(await store.listRequiredDrawerEvents(), []);

  assert.equal(
    await store.finishDrawerEvent(
      "manual-drawer-action",
      "Requested",
      "Completed",
      null,
      {
        eventId: "audit-manual-drawer",
        eventType: "CASH_DRAWER_OPEN",
        occurredAtIso: "2026-07-28T04:00:00.000Z",
        orderGuid: null,
        correlationId: "manual-drawer-action",
        payload: {
          action: "open-cash-drawer",
          status: "Completed",
          reason: "MANUAL",
          source: "sales",
          outcome: "Succeeded",
          printerId: "XP-MANUAL",
          requestingCashierId: "cashier-1",
          authorizingCashierId: "supervisor-1",
          permissionCode: "Permissions.PosTerminal.CashDrawer.Open",
          authorizationMode: "online",
        },
      },
    ),
    true,
  );

  const row = await connection.getFirst<{
    orderGuid: string | null;
    state: string;
    reason: string;
    audits: number;
  }>(
    `SELECT order_guid AS orderGuid, state, reason,
      (SELECT COUNT(*) FROM audit_events WHERE event_id = 'audit-manual-drawer') AS audits
     FROM drawer_events WHERE event_id = 'manual-drawer-action'`,
  );
  assert.deepEqual({ ...row }, {
    orderGuid: null,
    state: "Completed",
    reason: "MANUAL",
    audits: 1,
  });
});

test("真实 SQLite：授权重打与首份审计同事务，崩溃恢复终态仍关联原 actionId 和授权身份", async () => {
  const { connection, store } = createHarness();
  await seedOrder(connection, "order-authorized-reprint");
  const authorization = initialAuthorization({
    actionId: "action-authorized-reprint",
    eventType: "RECEIPT_REPRINT",
    orderGuid: "order-authorized-reprint",
    printerId: "XP-AUTHORIZED",
  });
  const created = await store.createLastReceiptReprint(
    {
      orderGuid: "order-authorized-reprint",
      receiptBytes: Uint8Array.of(29, 33, 82),
      printerId: "XP-AUTHORIZED",
    },
    authorization,
  );
  assert.equal(created?.jobId, authorization.context.actionId);
  assert.equal(created?.state, "Queued");

  const beforeRecovery = await connection.getFirst<{
    jobs: number;
    audits: number;
  }>(
    `SELECT
      (SELECT COUNT(*) FROM print_jobs WHERE job_id = 'action-authorized-reprint') AS jobs,
      (SELECT COUNT(*) FROM audit_events WHERE correlation_id = 'action-authorized-reprint') AS audits`,
  );
  assert.deepEqual({ ...beforeRecovery }, { jobs: 1, audits: 1 });

  const printed: string[] = [];
  const restarted = new FulfilmentService({
    store,
    printer: {
      async connect() {},
      async print(jobId) {
        printed.push(jobId);
        return { status: "printed", errorCode: null };
      },
    },
    drawer: {
      async open() {
        throw new Error("drawer must not be called");
      },
    },
    nowIso: () => "2026-07-28T04:01:00.000Z",
    createAuditId: () => "audit-terminal-authorized-reprint",
    createCorrelationId: () => "must-not-be-used",
    async prepareLastReceiptReprint() {
      throw new Error("recovery must not prepare another receipt");
    },
  });

  assert.deepEqual(await restarted.drainAutomaticQueue(), {
    printed: 1,
    drawersOpened: 0,
  });
  assert.deepEqual(printed, ["action-authorized-reprint"]);
  const audits = await connection.getAll<{
    correlationId: string;
    payloadJson: string;
  }>(
    `SELECT correlation_id AS correlationId, payload_json AS payloadJson
     FROM audit_events
     WHERE correlation_id = 'action-authorized-reprint'
     ORDER BY rowid ASC`,
  );
  assert.deepEqual(
    audits.map((row) => {
      const payload = JSON.parse(row.payloadJson) as Record<string, unknown>;
      return {
        correlationId: row.correlationId,
        status: payload.status,
        requestingCashierId: payload.requestingCashierId,
        requestingCashierName: payload.requestingCashierName,
        requestingUserGuid: payload.requestingUserGuid,
        authorizingCashierId: payload.authorizingCashierId,
        permissionCode: payload.permissionCode,
        authorizationMode: payload.authorizationMode,
      };
    }),
    [
      {
        correlationId: "action-authorized-reprint",
        status: "Authorized",
        requestingCashierId: "cashier-1",
        requestingCashierName: "Cashier One",
        requestingUserGuid: "user-1",
        authorizingCashierId: "supervisor-1",
        permissionCode: "Permissions.PosTerminal.Receipt.PrintLast",
        authorizationMode: "online",
      },
      {
        correlationId: "action-authorized-reprint",
        status: "Printed",
        requestingCashierId: "cashier-1",
        requestingCashierName: "Cashier One",
        requestingUserGuid: "user-1",
        authorizingCashierId: "supervisor-1",
        permissionCode: "Permissions.PosTerminal.Receipt.PrintLast",
        authorizationMode: "online",
      },
    ],
  );
});

test("真实 SQLite：Requested 手动开箱不在重启后补脉冲，首份授权审计仍保留", async () => {
  const { connection, store } = createHarness();
  await seedOrder(connection);
  const authorization = initialAuthorization({
    actionId: "action-requested-drawer",
    eventType: "CASH_DRAWER_OPEN",
    orderGuid: null,
    printerId: "XP-MANUAL",
  });
  await store.beginManualDrawerOpen(
    {
      eventId: authorization.context.actionId,
      printerId: "XP-MANUAL",
      reason: "MANUAL",
    },
    authorization,
  );
  let drawerCalls = 0;
  const restarted = new FulfilmentService({
    store,
    printer: {
      async connect() {},
      async print() {
        return { status: "printed", errorCode: null };
      },
    },
    drawer: {
      async open() {
        drawerCalls += 1;
        return { status: "completed", errorCode: null };
      },
    },
    nowIso: () => "2026-07-28T04:01:00.000Z",
    createAuditId: () => "unused-terminal-audit",
    createCorrelationId: () => "must-not-be-used",
    async prepareLastReceiptReprint() {
      return null;
    },
  });

  assert.deepEqual(await restarted.drainAutomaticQueue(), {
    printed: 0,
    drawersOpened: 0,
  });
  assert.equal(drawerCalls, 0);
  const row = await connection.getFirst<{
    state: string;
    audits: number;
    status: string;
  }>(
    `SELECT state,
      (SELECT COUNT(*) FROM audit_events WHERE correlation_id = 'action-requested-drawer') AS audits,
      (SELECT json_extract(payload_json, '$.status') FROM audit_events
       WHERE correlation_id = 'action-requested-drawer' LIMIT 1) AS status
     FROM drawer_events WHERE event_id = 'action-requested-drawer'`,
  );
  assert.deepEqual({ ...row }, {
    state: "Requested",
    audits: 1,
    status: "Authorized",
  });
});

test("真实 SQLite：手动钱箱重试从首份授权审计恢复完整员工，而非当前会话", async () => {
  const { connection, store } = createHarness();
  await seedOrder(connection);
  const authorization = initialAuthorization({
    actionId: "action-drawer-retry-actor",
    eventType: "CASH_DRAWER_OPEN",
    orderGuid: null,
    printerId: "XP-MANUAL",
  });
  await store.beginManualDrawerOpen(
    {
      eventId: authorization.context.actionId,
      printerId: "XP-MANUAL",
      reason: "MANUAL",
    },
    authorization,
  );
  await store.finishDrawerEvent(
    authorization.context.actionId,
    "Requested",
    "Failed",
    "PULSE_TIMEOUT",
    {
      eventId: "audit-drawer-retry-failed",
      eventType: "CASH_DRAWER_OPEN",
      occurredAtIso: "2026-07-28T04:01:00.000Z",
      orderGuid: null,
      correlationId: authorization.context.actionId,
      payload: {
        action: "open-cash-drawer",
        status: "Failed",
        reason: "MANUAL",
        source: "sales",
        outcome: "Failed",
        printerId: "XP-MANUAL",
        errorCode: "PULSE_TIMEOUT",
      },
    },
  );
  const restarted = new FulfilmentService({
    store,
    printer: {
      async connect() {},
      async print() {
        return { status: "printed", errorCode: null };
      },
    },
    drawer: {
      async open() {
        return { status: "completed", errorCode: null };
      },
    },
    nowIso: () => "2026-07-28T04:02:00.000Z",
    createAuditId: () => "audit-drawer-retry-completed",
    createCorrelationId: () => "unused-correlation",
    async prepareLastReceiptReprint() {
      return null;
    },
  });

  assert.deepEqual(
    await restarted.retryFailedDrawer(authorization.context.actionId),
    { state: "Completed", errorCode: null },
  );
  const payload = await connection.getFirst<{ payloadJson: string }>(
    `SELECT payload_json AS payloadJson
     FROM audit_events
     WHERE event_id = 'audit-drawer-retry-completed'`,
  );
  assert.deepEqual(
    JSON.parse(payload?.payloadJson ?? "{}"),
    {
      action: "open-cash-drawer",
      status: "Completed",
      reason: "MANUAL",
      source: "sales",
      outcome: "Succeeded",
      printerId: "XP-MANUAL",
      errorCode: null,
      requestingCashierId: "cashier-1",
      requestingCashierName: "Cashier One",
      requestingUserGuid: "user-1",
      authorizingCashierId: "supervisor-1",
      permissionCode: "Permissions.PosTerminal.CashDrawer.Open",
      authorizationMode: "online",
    },
  );
});

test("真实 SQLite：首次授权审计插入失败时重打 job 与手动开箱 event 都整体回滚", async () => {
  const { connection, store } = createHarness();
  await seedOrder(connection, "order-atomic-authorization");
  connection.failNextAuditInsert = true;
  await assert.rejects(
    store.createLastReceiptReprint(
      {
        orderGuid: "order-atomic-authorization",
        receiptBytes: Uint8Array.of(29, 33, 82),
        printerId: "XP-AUTHORIZED",
      },
      initialAuthorization({
        actionId: "action-atomic-reprint",
        eventType: "RECEIPT_REPRINT",
        orderGuid: "order-atomic-authorization",
        printerId: "XP-AUTHORIZED",
      }),
    ),
    /simulated audit insert failure/,
  );
  connection.failNextAuditInsert = true;
  await assert.rejects(
    store.beginManualDrawerOpen(
      {
        eventId: "action-atomic-drawer",
        printerId: "XP-MANUAL",
        reason: "MANUAL",
      },
      initialAuthorization({
        actionId: "action-atomic-drawer",
        eventType: "CASH_DRAWER_OPEN",
        orderGuid: null,
        printerId: "XP-MANUAL",
      }),
    ),
    /simulated audit insert failure/,
  );
  const row = await connection.getFirst<{
    jobs: number;
    drawers: number;
    audits: number;
  }>(
    `SELECT
      (SELECT COUNT(*) FROM print_jobs WHERE job_id = 'action-atomic-reprint') AS jobs,
      (SELECT COUNT(*) FROM drawer_events WHERE event_id = 'action-atomic-drawer') AS drawers,
      (SELECT COUNT(*) FROM audit_events WHERE correlation_id IN (
        'action-atomic-reprint', 'action-atomic-drawer'
      )) AS audits`,
  );
  assert.deepEqual({ ...row }, { jobs: 0, drawers: 0, audits: 0 });
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
      }, "print-duplicate"),
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
      audit(
        "audit-drawer-failure",
        "CASH_DRAWER_OPEN",
        undefined,
        "drawer-audit-failure",
      ),
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
