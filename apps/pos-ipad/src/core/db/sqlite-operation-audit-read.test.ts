import assert from "node:assert/strict";
import { DatabaseSync, type SQLInputValue } from "node:sqlite";
import test from "node:test";

import { applyMigrations } from "./migrations";
import { PosDatabase } from "./pos-database";
import {
  SqliteOperationAuditRead,
  type OperationAuditLocalScope,
} from "./sqlite-operation-audit-read";
import type {
  SqliteConnectionPort,
  SqliteDriverPort,
  SqlRunResult,
  SqlValue,
} from "./types";

const NOW = "2026-07-29T00:00:00.000Z";
const SCOPE: OperationAuditLocalScope = Object.freeze({
  storeCode: "STORE-1",
  deviceCode: "IPAD-1",
});
const SAME_ORDER = "10000000-0000-4000-8000-000000000001";
const OTHER_ORDER = "10000000-0000-4000-8000-000000000002";
const SAME_EVENT = "20000000-0000-4000-8000-000000000001";
const NULL_ORDER_EVENT = "20000000-0000-4000-8000-000000000002";
const CORRUPT_EVENT = "20000000-0000-4000-8000-000000000003";
const OTHER_EVENT = "20000000-0000-4000-8000-000000000004";

test("真实 SQLite：固定 store/device 读取审计并严格白名单映射 payload", async () => {
  await withMigratedDatabase(async (connection) => {
    await insertOrder(connection, {
      orderGuid: SAME_ORDER,
      storeCode: SCOPE.storeCode,
      deviceCode: SCOPE.deviceCode,
      cashierName: "Trusted Cashier",
    });
    await insertOrder(connection, {
      orderGuid: OTHER_ORDER,
      storeCode: "STORE-OTHER",
      deviceCode: "IPAD-OTHER",
      cashierName: "Other Cashier",
    });
    await insertAudit(connection, {
      eventId: SAME_EVENT,
      occurredAtIso: "2026-07-29T03:00:00.000Z",
      orderGuid: SAME_ORDER,
      correlationId: "corr-order",
      uploadedAtIso: "2026-07-29T03:01:00.000Z",
      payloadJson: JSON.stringify({
        cashierName: "Untrusted Payload Cashier",
        outcome: "Succeeded",
        paymentAmountCents: 1_500,
        primaryProduct: "Tea",
        productCount: 1,
        receiptNumber: "R-1",
        safeMessage: "Completed",
        items: [
          {
            actualAmountDeltaCents: 1_500,
            displayName: "Tea",
            lineIndex: 0,
            productCode: "P-1",
            quantityDelta: "1",
            ignoredSecret: "AUTHORIZATION_TOKEN_MUST_NOT_LEAK",
          },
        ],
        ignoredSecret: "RAW_PAYLOAD_SECRET",
      }),
    });
    await insertAudit(connection, {
      eventId: NULL_ORDER_EVENT,
      occurredAtIso: "2026-07-29T02:00:00.000Z",
      orderGuid: null,
      correlationId: "corr-local",
      uploadedAtIso: null,
      payloadJson: JSON.stringify({
        cashierName: "Local Cashier 0400 000 000",
        outcome: "Denied",
        reason: "Bearer token-value",
        unknown: "UNKNOWN_SECRET_VALUE",
      }),
    });
    await insertAudit(connection, {
      eventId: CORRUPT_EVENT,
      occurredAtIso: "2026-07-29T01:00:00.000Z",
      orderGuid: null,
      correlationId: "corr-corrupt",
      uploadedAtIso: null,
      payloadJson: '{"broken":',
    });
    await insertAudit(connection, {
      eventId: OTHER_EVENT,
      occurredAtIso: "2026-07-29T04:00:00.000Z",
      orderGuid: OTHER_ORDER,
      correlationId: "corr-other",
      uploadedAtIso: null,
      payloadJson: JSON.stringify({
        outcome: "Succeeded",
        safeMessage: "Cross scope",
      }),
    });

    const read = new SqliteOperationAuditRead(connection, SCOPE);
    const rows = await read.list(request());
    assert.equal(rows.length, 3);
    assert.deepEqual(
      rows.map((row) => row.eventId),
      [SAME_EVENT, NULL_ORDER_EVENT, CORRUPT_EVENT],
    );

    const order = rows[0]!;
    assert.deepEqual(order, {
      cashierName: "Trusted Cashier",
      correlationId: "corr-order",
      deviceCode: SCOPE.deviceCode,
      eventId: SAME_EVENT,
      items: [
        {
          actualAmountDeltaCents: 1_500,
          displayName: "Tea",
          lineIndex: 0,
          productCode: "P-1",
          quantityDelta: "1",
        },
      ],
      occurredAtIso: "2026-07-29T03:00:00.000Z",
      operationType: "TEST_OPERATION",
      orderGuid: SAME_ORDER,
      outcome: "Succeeded",
      paymentAmountCents: 1_500,
      primaryProduct: "Tea",
      productCount: 1,
      receiptNumber: "R-1",
      safeMessage: "Completed",
      storeCode: SCOPE.storeCode,
      uploadState: "uploaded",
    });

    const local = rows[1]!;
    assert.equal(local.cashierName, "Local Cashier [REDACTED_CONTACT]");
    assert.equal(local.safeMessage, "Bearer [REDACTED_TOKEN]");
    assert.equal(local.uploadState, "pending");

    const corrupt = rows[2]!;
    assert.deepEqual(
      {
        cashierName: corrupt.cashierName,
        items: corrupt.items,
        outcome: corrupt.outcome,
        paymentAmountCents: corrupt.paymentAmountCents,
        productCount: corrupt.productCount,
        safeMessage: corrupt.safeMessage,
      },
      {
        cashierName: null,
        items: [],
        outcome: "Unknown",
        paymentAmountCents: null,
        productCount: 0,
        safeMessage: null,
      },
    );
    const exposed = JSON.stringify(rows);
    assert.equal(exposed.includes("RAW_PAYLOAD_SECRET"), false);
    assert.equal(
      exposed.includes("AUTHORIZATION_TOKEN_MUST_NOT_LEAK"),
      false,
    );
    assert.equal(exposed.includes("UNKNOWN_SECRET_VALUE"), false);

    assert.deepEqual(
      (await read.list(request({ keyword: "tea" }))).map(
        (row) => row.eventId,
      ),
      [SAME_EVENT],
    );
    assert.equal(
      (await read.list(request({ keyword: "RAW_PAYLOAD_SECRET" })))
        .length,
      0,
    );
    assert.deepEqual(
      (await read.list(request({ uploadState: "pending" }))).map(
        (row) => row.eventId,
      ),
      [NULL_ORDER_EVENT, CORRUPT_EVENT],
    );
    assert.deepEqual(
      (await read.list(request({ uploadState: "uploaded" }))).map(
        (row) => row.eventId,
      ),
      [SAME_EVENT],
    );
    assert.equal(
      (await read.list(request({ uploadState: "rejected" }))).length,
      0,
    );
    assert.deepEqual(
      await read.get({
        ...SCOPE,
        eventId: SAME_EVENT,
        source: "local",
      }),
      order,
    );
    assert.equal(
      await read.get({
        ...SCOPE,
        eventId: OTHER_EVENT,
        source: "local",
      }),
      null,
    );
  });
});

test("真实 SQLite：keyword 只检查最近 500 条白名单候选且结果最多 100", async () => {
  await withMigratedDatabase(async (connection) => {
    await connection.withExclusiveTransaction(async (transaction) => {
      for (let index = 0; index <= 500; index += 1) {
        await insertAudit(transaction, {
          eventId: boundedEventId(index),
          occurredAtIso: new Date(
            Date.parse("2026-07-01T00:00:00.000Z") + index * 1_000,
          ).toISOString(),
          orderGuid: null,
          correlationId: `corr-${index}`,
          uploadedAtIso: null,
          payloadJson: JSON.stringify({
            outcome: "Succeeded",
            safeMessage:
              index === 0 ? "outside-candidate" : "inside-candidate",
          }),
        });
      }
    });

    const read = new SqliteOperationAuditRead(connection, SCOPE);
    assert.equal(
      (
        await read.list(
          request({
            keyword: "outside-candidate",
          }),
        )
      ).length,
      0,
    );
    const rows = await read.list(
      request({ keyword: "inside-candidate" }),
    );
    assert.equal(rows.length, 100);
    assert.equal(rows[0]?.eventId, boundedEventId(500));
    assert.equal(rows[99]?.eventId, boundedEventId(401));
  });
});

test("审计 facade 拒绝可变 scope、remote source、非 100 limit 与越权 get", async () => {
  await withMigratedDatabase(async (connection) => {
    const read = new SqliteOperationAuditRead(connection, SCOPE);
    await assert.rejects(
      () =>
        read.list(
          request({
            storeCode: "STORE-OTHER",
          }),
        ),
      /scope|store/i,
    );
    await assert.rejects(
      () =>
        read.list({
          ...request(),
          source: "remote",
        }),
      /local|source/i,
    );
    await assert.rejects(
      () =>
        read.list({
          ...request(),
          limit: 99 as 100,
        }),
      /limit/i,
    );
    await assert.rejects(
      () =>
        read.get({
          ...SCOPE,
          deviceCode: "IPAD-OTHER",
          eventId: SAME_EVENT,
          source: "local",
        }),
      /scope|device/i,
    );
  });

  const database = await PosDatabase.open({
    databaseName: ":memory:",
    driver: new SystemSqliteDriver(),
    keyProvider: {
      getOrCreateDatabaseKey: async () => "a".repeat(64),
    },
    nowIso: () => NOW,
  });
  try {
    assert.ok(
      database.operationAudits(SCOPE) instanceof
        SqliteOperationAuditRead,
    );
  } finally {
    await database.close();
  }
});

function request(
  overrides: Partial<
    Parameters<SqliteOperationAuditRead["list"]>[0]
  > = {},
): Parameters<SqliteOperationAuditRead["list"]>[0] {
  return {
    ...SCOPE,
    keyword: null,
    limit: 100,
    source: "local",
    uploadState: null,
    ...overrides,
  };
}

function boundedEventId(index: number): string {
  return `30000000-0000-4000-8000-${String(index).padStart(12, "0")}`;
}

async function insertOrder(
  connection: SqliteConnectionPort,
  input: Readonly<{
    orderGuid: string;
    storeCode: string;
    deviceCode: string;
    cashierName: string;
  }>,
): Promise<void> {
  await connection.run(
    `INSERT INTO local_orders (
      order_guid, local_sequence, store_code, device_code,
      cashier_id, cashier_name, sold_at_iso, state, total_cents,
      discount_cents, actual_amount_cents, original_order_guid,
      created_at_iso, updated_at_iso
    ) VALUES (?, ?, ?, ?, 'cashier-1', ?, ?, 'Completed', 100, 0, 100, NULL, ?, ?)`,
    [
      input.orderGuid,
      input.orderGuid === SAME_ORDER ? 1 : 2,
      input.storeCode,
      input.deviceCode,
      input.cashierName,
      NOW,
      NOW,
      NOW,
    ],
  );
}

async function insertAudit(
  connection: SqliteConnectionPort,
  input: Readonly<{
    eventId: string;
    occurredAtIso: string;
    orderGuid: string | null;
    correlationId: string;
    payloadJson: string;
    uploadedAtIso: string | null;
  }>,
): Promise<void> {
  await connection.run(
    `INSERT INTO audit_events (
      event_id, event_type, occurred_at_iso, order_guid,
      correlation_id, payload_json, uploaded_at_iso
    ) VALUES (?, 'TEST_OPERATION', ?, ?, ?, ?, ?)`,
    [
      input.eventId,
      input.occurredAtIso,
      input.orderGuid,
      input.correlationId,
      input.payloadJson,
      input.uploadedAtIso,
    ],
  );
}

class SystemSqliteDriver implements SqliteDriverPort {
  public async open(_databaseName: string): Promise<SqliteConnectionPort> {
    return new SystemSqliteConnection(new DatabaseSync(":memory:"));
  }
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
      .run(...parameters.map(toSqliteValue));
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
      (this.database
        .prepare(sql)
        .get(...parameters.map(toSqliteValue)) as T | undefined) ?? null
    );
  }

  public async getAll<T extends object>(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<readonly T[]> {
    return this.database
      .prepare(sql)
      .all(...parameters.map(toSqliteValue)) as T[];
  }

  public async withExclusiveTransaction<T>(
    operation: (transaction: SqliteConnectionPort) => Promise<T>,
  ): Promise<T> {
    this.database.exec("BEGIN IMMEDIATE;");
    try {
      const value = await operation(
        new TransactionConnection(this.database),
      );
      this.database.exec("COMMIT;");
      return value;
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
    throw new Error("Nested transaction is not supported.");
  }

  public override async close(): Promise<void> {
    throw new Error("Transaction cannot close the database.");
  }
}

function toSqliteValue(value: SqlValue): SQLInputValue {
  return value;
}

async function withMigratedDatabase(
  operation: (connection: SqliteConnectionPort) => Promise<void>,
): Promise<void> {
  const database = new DatabaseSync(":memory:");
  const connection = new SystemSqliteConnection(database);
  try {
    await applyMigrations(connection, () => NOW);
    await operation(connection);
  } finally {
    await connection.close();
  }
}
