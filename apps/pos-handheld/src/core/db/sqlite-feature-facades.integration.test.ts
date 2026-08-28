import assert from "node:assert/strict";
import { DatabaseSync, type SQLInputValue } from "node:sqlite";
import test from "node:test";

import { applyMigrations, POS_DATABASE_MIGRATIONS } from "./migrations";
import { PosDatabase } from "./pos-database";
import { SqliteFulfilmentStore } from "./sqlite-fulfilment-store";
import { SqliteLocalSyncHistoryStore } from "./sqlite-local-sync-history-store";
import { SqliteMixedPaymentOrderTruthStore } from "./sqlite-mixed-payment-order-truth-store";
import { SqliteOfflineReturnCapacity } from "@hb/pos-db/core/db/sqlite-offline-return-capacity";
import { SqlitePaymentActionBindingStore } from "./sqlite-payment-action-binding-store";
import type {
  SqliteConnectionPort,
  SqlRunResult,
  SqlValue,
} from "@hb/pos-db/core/db/types";

import type { CartSnapshot } from "@/core/contracts";
import type {
  PaymentActionBinding,
  PaymentActionBindingPort,
} from "@/features/payments";
import type { MixedPaymentOrderTruthPort } from "@/features/payments/mixed";
import type {
  LocalSyncHistoryPort,
  LocalSyncHistorySupportContext,
} from "@/features/sync-history";

const nowIso = "2026-07-28T06:00:00.000Z";
const supportContext: LocalSyncHistorySupportContext = {
  appId: "com.hbweb.posipad",
  appVersion: "0.1.0",
  deviceCode: "IPAD-01",
  storeCode: "S1",
};

class NodeSqliteConnection implements SqliteConnectionPort {
  private transactionActive = false;

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
    // Node 内置 SQLite 不含 SQLCipher；仅为测试的精确探针提供有效版本。
    if (sql === "PRAGMA cipher_version;") {
      return { cipher_version: "4.6.1" } as unknown as T;
    }
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

async function openDatabase() {
  const connection = new NodeSqliteConnection(
    new DatabaseSync(":memory:", { enableForeignKeyConstraints: true }),
  );
  const database = await PosDatabase.open({
    databaseName: "test-pos.db",
    driver: { async open() { return connection; } },
    keyProvider: {
      async getOrCreateDatabaseKey() {
        return "ab".repeat(32);
      },
    },
    nowIso: () => nowIso,
  });
  return { connection, database };
}

test("真实 SQLite：已记录 M1-M6 的旧 drawer schema 缺 updated_at_iso 时可无损升级 M7", async () => {
  const connection = new NodeSqliteConnection(
    new DatabaseSync(":memory:", { enableForeignKeyConstraints: true }),
  );
  await connection.exec(`
    CREATE TABLE schema_migrations (
      version INTEGER PRIMARY KEY,
      name TEXT NOT NULL,
      applied_at_iso TEXT NOT NULL
    );
    INSERT INTO schema_migrations (version, name, applied_at_iso) VALUES
      (1, 'legacy-m1', '2026-07-20T00:00:00.000Z'),
      (2, 'legacy-m2', '2026-07-20T00:00:00.000Z'),
      (3, 'legacy-m3', '2026-07-20T00:00:00.000Z'),
      (4, 'legacy-m4', '2026-07-20T00:00:00.000Z'),
      (5, 'legacy-m5', '2026-07-20T00:00:00.000Z'),
      (6, 'legacy-m6', '2026-07-20T00:00:00.000Z');
    CREATE TABLE print_jobs (
      job_id TEXT PRIMARY KEY,
      order_guid TEXT NULL,
      printer_id TEXT NOT NULL
    );
    CREATE TABLE drawer_events (
      event_id TEXT PRIMARY KEY,
      print_job_id TEXT NULL,
      state TEXT NOT NULL,
      last_error_code TEXT NULL,
      created_at_iso TEXT NOT NULL
    );
    INSERT INTO print_jobs (job_id, order_guid, printer_id)
      VALUES ('legacy-print', 'legacy-order', 'XP-LEGACY');
    INSERT INTO drawer_events (
      event_id, print_job_id, state, last_error_code, created_at_iso
    ) VALUES (
      'legacy-drawer', 'legacy-print', 'Required', NULL,
      '2026-07-20T01:02:03.000Z'
    );
  `);

  await applyMigrations(
    connection,
    () => nowIso,
    POS_DATABASE_MIGRATIONS.filter((migration) => migration.version <= 10),
  );

  const columns = await connection.getAll<{ name: string }>(
    "PRAGMA table_info('drawer_events')",
  );
  const drawer = await connection.getFirst<{
    event_id: string;
    printer_id: string;
    state: string;
    created_at_iso: string;
    updated_at_iso: string;
  }>(
    `SELECT event_id, printer_id, state, created_at_iso, updated_at_iso
     FROM drawer_events WHERE event_id = 'legacy-drawer'`,
  );
  const versions = await connection.getAll<{ version: number; name: string }>(
    "SELECT version, name FROM schema_migrations ORDER BY version",
  );

  assert.equal(columns.some((column) => column.name === "updated_at_iso"), true);
  assert.equal(columns.some((column) => column.name === "printer_id"), true);
  assert.deepEqual({ ...drawer }, {
    event_id: "legacy-drawer",
    printer_id: "XP-LEGACY",
    state: "Required",
    created_at_iso: "2026-07-20T01:02:03.000Z",
    updated_at_iso: "2026-07-20T01:02:03.000Z",
  });
  assert.deepEqual(
    versions.map((version) => version.version),
    [1, 2, 3, 4, 5, 6, 7, 8, 9, 10],
  );
  assert.deepEqual(
    versions.slice(0, 6).map((version) => version.name),
    ["legacy-m1", "legacy-m2", "legacy-m3", "legacy-m4", "legacy-m5", "legacy-m6"],
  );
});

test("真实 SQLite：旧 drawer schema 同时缺 created_at_iso/updated_at_iso 时可原地升级 M7", async () => {
  const connection = new NodeSqliteConnection(
    new DatabaseSync(":memory:", { enableForeignKeyConstraints: true }),
  );
  try {
    await connection.exec(`
      CREATE TABLE schema_migrations (
        version INTEGER PRIMARY KEY,
        name TEXT NOT NULL,
        applied_at_iso TEXT NOT NULL
      );
      INSERT INTO schema_migrations (version, name, applied_at_iso) VALUES
        (1, 'legacy-m1', '2026-07-20T00:00:00.000Z'),
        (2, 'legacy-m2', '2026-07-20T00:00:00.000Z'),
        (3, 'legacy-m3', '2026-07-20T00:00:00.000Z'),
        (4, 'legacy-m4', '2026-07-20T00:00:00.000Z'),
        (5, 'legacy-m5', '2026-07-20T00:00:00.000Z'),
        (6, 'legacy-m6', '2026-07-20T00:00:00.000Z');
      CREATE TABLE print_jobs (
        job_id TEXT PRIMARY KEY,
        order_guid TEXT NULL,
        printer_id TEXT NOT NULL
      );
      CREATE TABLE drawer_events (
        event_id TEXT PRIMARY KEY,
        print_job_id TEXT NULL,
        state TEXT NOT NULL,
        last_error_code TEXT NULL
      );
      INSERT INTO print_jobs (job_id, order_guid, printer_id)
        VALUES ('legacy-print-no-times', 'legacy-order-no-times', 'XP-LEGACY');
      INSERT INTO drawer_events (
        event_id, print_job_id, state, last_error_code
      ) VALUES (
        'legacy-drawer-no-times', 'legacy-print-no-times', 'Required', NULL
      );
    `);

    await applyMigrations(
      connection,
      () => nowIso,
      POS_DATABASE_MIGRATIONS.filter((migration) => migration.version <= 10),
    );

    const columns = await connection.getAll<{ name: string }>(
      "PRAGMA table_info('drawer_events')",
    );
    const drawer = await connection.getFirst<{
      event_id: string;
      printer_id: string;
      state: string;
      created_at_iso: string;
      updated_at_iso: string;
    }>(
      `SELECT event_id, printer_id, state, created_at_iso, updated_at_iso
       FROM drawer_events WHERE event_id = 'legacy-drawer-no-times'`,
    );

    assert.equal(columns.some((column) => column.name === "created_at_iso"), true);
    assert.equal(columns.some((column) => column.name === "updated_at_iso"), true);
    assert.deepEqual({ ...drawer }, {
      event_id: "legacy-drawer-no-times",
      printer_id: "XP-LEGACY",
      state: "Required",
      created_at_iso: nowIso,
      updated_at_iso: nowIso,
    });
  } finally {
    await connection.close();
  }
});

test("真实 SQLite：已记录 M7 的不完整 drawer schema 原地修复后可执行首笔现金履约", async () => {
  const connection = new NodeSqliteConnection(
    new DatabaseSync(":memory:", { enableForeignKeyConstraints: true }),
  );
  try {
    await connection.exec(`
      CREATE TABLE schema_migrations (
        version INTEGER PRIMARY KEY,
        name TEXT NOT NULL,
        applied_at_iso TEXT NOT NULL
      );
      INSERT INTO schema_migrations (version, name, applied_at_iso) VALUES
        (1, 'legacy-m1', '2026-07-20T00:00:00.000Z'),
        (2, 'legacy-m2', '2026-07-20T00:00:00.000Z'),
        (3, 'legacy-m3', '2026-07-20T00:00:00.000Z'),
        (4, 'legacy-m4', '2026-07-20T00:00:00.000Z'),
        (5, 'legacy-m5', '2026-07-20T00:00:00.000Z'),
        (6, 'legacy-m6', '2026-07-20T00:00:00.000Z'),
        (7, 'legacy-m7', '2026-07-20T00:00:00.000Z');
      CREATE TABLE print_jobs (
        job_id TEXT PRIMARY KEY,
        order_guid TEXT NULL,
        state TEXT NOT NULL,
        printer_id TEXT NOT NULL,
        receipt_ciphertext BLOB NOT NULL,
        is_reprint INTEGER NOT NULL DEFAULT 0,
        retry_count INTEGER NOT NULL DEFAULT 0,
        last_error_code TEXT NULL,
        created_at_iso TEXT NOT NULL,
        updated_at_iso TEXT NOT NULL
      );
      CREATE TABLE drawer_events (
        event_id TEXT PRIMARY KEY,
        state TEXT NOT NULL,
        printer_id TEXT NULL,
        created_at_iso TEXT NOT NULL,
        updated_at_iso TEXT NOT NULL
      );
      INSERT INTO drawer_events (
        event_id, state, printer_id, created_at_iso, updated_at_iso
      ) VALUES (
        'legacy-irrecoverable', 'Required', NULL,
        '2026-07-20T01:02:03.000Z', '2026-07-20T01:02:03.000Z'
      );
    `);

    await applyMigrations(
      connection,
      () => nowIso,
      POS_DATABASE_MIGRATIONS.filter((migration) => migration.version <= 10),
    );
    // 当前 fulfilment facade 只会在全部生产迁移后创建；本测试仅隔离验证 M7 钱箱修复。
    await connection.exec(
      "ALTER TABLE print_jobs ADD COLUMN external_order_guid TEXT NULL",
    );

    const store = new SqliteFulfilmentStore(connection, {
      encryptor: {
        async encrypt(value) {
          return new TextEncoder().encode(value);
        },
        async decrypt(value) {
          return new TextDecoder().decode(value);
        },
      },
      nowIso: () => nowIso,
      createPrintJobId: () => "unused-reprint-id",
    });
    await store.enqueueCashFulfilment({
      print: {
        jobId: "first-cash-print",
        orderGuid: "first-cash-order",
        printerId: "XP-FIRST",
        receiptBytes: Uint8Array.from([0x1b, 0x40]),
        isReprint: false,
      },
      drawer: {
        eventId: "first-cash-drawer",
        orderGuid: "first-cash-order",
        printerId: "XP-FIRST",
        printJobId: "first-cash-print",
        reason: "cash-sale",
      },
    });

    const columns = await connection.getAll<{ name: string }>(
      "PRAGMA table_info('drawer_events')",
    );
    const legacy = await connection.getFirst<{
      event_id: string;
      state: string;
      last_error_code: string;
    }>(
      `SELECT event_id, state, last_error_code
       FROM drawer_events WHERE event_id = 'legacy-irrecoverable'`,
    );
    const required = await store.listRequiredDrawerEvents();
    const claimed = await store.claimRequiredDrawerEvent("first-cash-drawer");
    const versions = await connection.getAll<{ version: number }>(
      "SELECT version FROM schema_migrations ORDER BY version",
    );

    assert.deepEqual(
      columns.map((column) => column.name).sort(),
      [
        "completed_at_iso",
        "created_at_iso",
        "event_id",
        "last_error_code",
        "order_guid",
        "print_job_id",
        "printer_id",
        "reason",
        "requested_at_iso",
        "retry_count",
        "state",
        "updated_at_iso",
      ].sort(),
    );
    assert.deepEqual({ ...legacy }, {
      event_id: "legacy-irrecoverable",
      state: "Unknown",
      last_error_code: "DRAWER_PRINTER_BINDING_MISSING_MIGRATION",
    });
    assert.deepEqual(required, [{
      eventId: "first-cash-drawer",
      orderGuid: "first-cash-order",
      printerId: "XP-FIRST",
      state: "Required",
      reason: "cash-sale",
      retryCount: 0,
    }]);
    assert.deepEqual(claimed, {
      eventId: "first-cash-drawer",
      orderGuid: "first-cash-order",
      printerId: "XP-FIRST",
      state: "Requested",
      reason: "cash-sale",
      retryCount: 0,
    });
    assert.deepEqual(
      versions.map((version) => version.version),
      [1, 2, 3, 4, 5, 6, 7, 8, 9, 10],
    );
  } finally {
    await connection.close();
  }
});

test("真实 SQLite：drawer 关键列类型异常时 M7 不落版本且兼容 DDL 全部回滚", async () => {
  const connection = new NodeSqliteConnection(
    new DatabaseSync(":memory:", { enableForeignKeyConstraints: true }),
  );
  try {
    await connection.exec(`
      CREATE TABLE schema_migrations (
        version INTEGER PRIMARY KEY,
        name TEXT NOT NULL,
        applied_at_iso TEXT NOT NULL
      );
      INSERT INTO schema_migrations (version, name, applied_at_iso) VALUES
        (1, 'legacy-m1', '2026-07-20T00:00:00.000Z'),
        (2, 'legacy-m2', '2026-07-20T00:00:00.000Z'),
        (3, 'legacy-m3', '2026-07-20T00:00:00.000Z'),
        (4, 'legacy-m4', '2026-07-20T00:00:00.000Z'),
        (5, 'legacy-m5', '2026-07-20T00:00:00.000Z'),
        (6, 'legacy-m6', '2026-07-20T00:00:00.000Z');
      CREATE TABLE print_jobs (
        job_id TEXT PRIMARY KEY,
        printer_id TEXT NOT NULL
      );
      CREATE TABLE drawer_events (
        event_id TEXT PRIMARY KEY,
        retry_count TEXT NULL
      );
      INSERT INTO drawer_events (event_id, retry_count)
        VALUES ('legacy-bad-type', 'not-a-number');
    `);

    await assert.rejects(
      applyMigrations(connection, () => nowIso),
      /DRAWER_EVENTS_SCHEMA_INVALID:TYPE_retry_count/,
    );

    const versions = await connection.getAll<{ version: number }>(
      "SELECT version FROM schema_migrations ORDER BY version",
    );
    const columns = await connection.getAll<{ name: string }>(
      "PRAGMA table_info('drawer_events')",
    );
    assert.deepEqual(
      versions.map((version) => version.version),
      [1, 2, 3, 4, 5, 6],
    );
    assert.deepEqual(
      columns.map((column) => column.name),
      ["event_id", "retry_count"],
    );
  } finally {
    await connection.close();
  }
});

test("真实 SQLite：已记录 M7 的异常 drawer 结构在 M8 前 fail closed", async () => {
  const connection = new NodeSqliteConnection(
    new DatabaseSync(":memory:", { enableForeignKeyConstraints: true }),
  );
  try {
    await connection.exec(`
      CREATE TABLE schema_migrations (
        version INTEGER PRIMARY KEY,
        name TEXT NOT NULL,
        applied_at_iso TEXT NOT NULL
      );
      INSERT INTO schema_migrations (version, name, applied_at_iso) VALUES
        (1, 'legacy-m1', '2026-07-20T00:00:00.000Z'),
        (2, 'legacy-m2', '2026-07-20T00:00:00.000Z'),
        (3, 'legacy-m3', '2026-07-20T00:00:00.000Z'),
        (4, 'legacy-m4', '2026-07-20T00:00:00.000Z'),
        (5, 'legacy-m5', '2026-07-20T00:00:00.000Z'),
        (6, 'legacy-m6', '2026-07-20T00:00:00.000Z'),
        (7, 'legacy-m7', '2026-07-20T00:00:00.000Z');
      CREATE TABLE print_jobs (
        job_id TEXT PRIMARY KEY,
        printer_id TEXT NOT NULL
      );
      CREATE TABLE drawer_events (
        event_id TEXT PRIMARY KEY,
        retry_count TEXT NULL
      );
    `);

    await assert.rejects(
      applyMigrations(connection, () => nowIso),
      /DRAWER_EVENTS_SCHEMA_INVALID:TYPE_retry_count/,
    );

    const versions = await connection.getAll<{ version: number }>(
      "SELECT version FROM schema_migrations ORDER BY version",
    );
    const columns = await connection.getAll<{ name: string }>(
      "PRAGMA table_info('drawer_events')",
    );
    assert.deepEqual(
      versions.map((version) => version.version),
      [1, 2, 3, 4, 5, 6, 7],
    );
    assert.deepEqual(
      columns.map((column) => column.name),
      ["event_id", "retry_count"],
    );
  } finally {
    await connection.close();
  }
});

test("真实 SQLite：fresh 与已安装 M7 数据库都原子升级 M8-M17，旧 tender 不猜测 reversal link", async () => {
  const fresh = await openDatabase();
  const freshTables = await listTables(fresh.connection);
  assert.equal(freshTables.includes("payment_action_bindings"), true);
  assert.equal(freshTables.includes("payment_tender_reversal_links"), true);

  const legacyConnection = new NodeSqliteConnection(
    new DatabaseSync(":memory:", { enableForeignKeyConstraints: true }),
  );
  await applyMigrations(
    legacyConnection,
    () => "2026-07-27T00:00:00.000Z",
    POS_DATABASE_MIGRATIONS.filter((migration) => migration.version <= 7),
  );
  await seedOrder(legacyConnection, {
    orderGuid: "legacy-mixed",
    localSequence: 1,
    state: "Completing",
    soldAtIso: "2026-07-27T01:00:00.000Z",
    actualAmountCents: 500,
  });
  await seedTender(
    legacyConnection,
    "legacy-source",
    "legacy-mixed",
    "cash",
    500,
    1,
  );
  await seedTender(
    legacyConnection,
    "legacy-negative-without-link",
    "legacy-mixed",
    "cash",
    -500,
    2,
  );

  await applyMigrations(
    legacyConnection,
    () => nowIso,
    POS_DATABASE_MIGRATIONS.filter((migration) => migration.version <= 17),
  );

  const versions = await legacyConnection.getAll<{ version: number }>(
    "SELECT version FROM schema_migrations ORDER BY version",
  );
  const reversalCount = await legacyConnection.getFirst<{ count: number }>(
    "SELECT COUNT(*) AS count FROM payment_tender_reversal_links",
  );
  assert.deepEqual(
    versions.map((row) => row.version),
    [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17],
  );
  assert.equal(Number(reversalCount?.count), 0);
});

test("真实 SQLite：PaymentActionBinding bind-or-get 不覆盖旧事实，ID 冲突与直接改删均 fail closed", async () => {
  const { connection, database } = await openDatabase();
  await seedOrder(connection, {
    orderGuid: "binding-order",
    localSequence: 11,
    state: "Draft",
    soldAtIso: "2026-07-28T01:00:00.000Z",
    actualAmountCents: 1_000,
  });
  const bindings: PaymentActionBindingPort =
    database.paymentActionBindings();
  const proposed = paymentBinding();

  assert.deepEqual(await bindings.bindOrGet(proposed), proposed);
  assert.deepEqual(await bindings.bindOrGet({ ...proposed }), proposed);
  assert.deepEqual(await bindings.getByAttempt(proposed.attemptId), proposed);
  assert.deepEqual(
    await bindings.bindOrGet({
      ...proposed,
      requestSignature: "[\"square\",\"purchase\",\"AUD\",900]",
      attemptId: "must-not-overwrite-attempt",
      idempotencyKey: "must-not-overwrite-idempotency",
    }),
    proposed,
  );
  await assert.rejects(
    bindings.bindOrGet({
      ...proposed,
      actionId: "action-attempt-collision",
      requestSignature: "[\"square\",\"purchase\",\"AUD\",200]",
      idempotencyKey: "idempotency-other",
    }),
    /UNIQUE constraint failed: payment_action_bindings\.attempt_id/,
  );
  await assert.rejects(
    bindings.bindOrGet({
      ...proposed,
      actionId: "action-idempotency-collision",
      requestSignature: "[\"square\",\"purchase\",\"AUD\",300]",
      attemptId: "attempt-other",
    }),
    /UNIQUE constraint failed: payment_action_bindings\.idempotency_key/,
  );
  await assert.rejects(
    connection.run(
      "UPDATE payment_action_bindings SET request_signature = 'changed' WHERE order_guid = ? AND action_id = ?",
      [proposed.orderGuid, proposed.actionId],
    ),
    /PAYMENT_ACTION_BINDING_IMMUTABLE/,
  );
  await assert.rejects(
    connection.run(
      "DELETE FROM payment_action_bindings WHERE order_guid = ? AND action_id = ?",
      [proposed.orderGuid, proposed.actionId],
    ),
    /PAYMENT_ACTION_BINDING_IMMUTABLE/,
  );
  const persisted = await connection.getAll<Record<string, SqlValue>>(
    "SELECT * FROM payment_action_bindings",
  );
  assert.equal(persisted.length, 1);
  assert.equal(persisted[0]?.request_signature, proposed.requestSignature);
  assert.deepEqual(
    JSON.parse(String(persisted[0]?.audit_actor_json)),
    {
      requestingCashierId: "cashier-alice",
      requestingCashierName: "Alice",
      requestingUserGuid: "user-alice",
    },
  );
});

test("真实 SQLite：M28 PaymentActionBinding actor 要求完整三字段并保持不可变", async () => {
  const { connection } = await openDatabase();
  await seedOrder(connection, {
    orderGuid: "binding-actor-order",
    localSequence: 111,
    state: "Draft",
    soldAtIso: "2026-07-28T01:00:00.000Z",
    actualAmountCents: 1_000,
  });

  const insert = (actionId: string, actorJson: string) =>
    connection.run(
      `INSERT INTO payment_action_bindings (
        order_guid, action_id, request_signature, attempt_id,
        idempotency_key, created_at_iso, audit_actor_json
      ) VALUES (?, ?, '["square","purchase","AUD",1000]', ?, ?, ?, ?)`,
      [
        "binding-actor-order",
        actionId,
        `attempt-${actionId}`,
        `idempotency-${actionId}`,
        nowIso,
        actorJson,
      ],
    );

  await assert.rejects(
    connection.run(
      `INSERT INTO payment_action_bindings (
        order_guid, action_id, request_signature, attempt_id,
        idempotency_key, created_at_iso, audit_actor_json
      ) VALUES (?, ?, '["square","purchase","AUD",1000]', ?, ?, ?, NULL)`,
      [
        "binding-actor-order",
        "missing-actor",
        "attempt-missing-actor",
        "idempotency-missing-actor",
        nowIso,
      ],
    ),
    /PAYMENT_ACTION_BINDING_ACTOR_REQUIRED/,
  );
  await assert.rejects(
    insert(
      "missing-cashier-id",
      JSON.stringify({
        requestingCashierName: "Alice",
        requestingUserGuid: "user-alice",
      }),
    ),
    /CHECK constraint failed/,
  );
  await assert.rejects(
    insert(
      "missing-user-guid",
      JSON.stringify({
        requestingCashierId: "cashier-alice",
        requestingCashierName: "Alice",
      }),
    ),
    /CHECK constraint failed/,
  );
  await insert(
    "complete-actor",
    JSON.stringify({
      requestingCashierId: "cashier-alice",
      requestingCashierName: "Alice",
      requestingUserGuid: "user-alice",
    }),
  );
  await assert.rejects(
    connection.run(
      `UPDATE payment_action_bindings
       SET audit_actor_json = ?
       WHERE order_guid = ? AND action_id = ?`,
      [
        JSON.stringify({
          requestingCashierId: "cashier-bob",
          requestingCashierName: "Bob",
          requestingUserGuid: "user-bob",
        }),
        "binding-actor-order",
        "complete-actor",
      ],
    ),
    /PAYMENT_ACTION_BINDING_IMMUTABLE/,
  );
});

test("真实 SQLite：M25 历史 PaymentActionBinding NULL actor 经 M28 升级后只整体回退订单员工", async () => {
  const connection = new NodeSqliteConnection(
    new DatabaseSync(":memory:", { enableForeignKeyConstraints: true }),
  );
  await applyMigrations(
    connection,
    () => nowIso,
    POS_DATABASE_MIGRATIONS.filter((migration) => migration.version <= 25),
  );
  await seedOrder(connection, {
    orderGuid: "binding-legacy-actor-order",
    localSequence: 112,
    state: "Draft",
    soldAtIso: "2026-07-28T01:00:00.000Z",
    actualAmountCents: 1_000,
  });
  await connection.run(
    `INSERT INTO payment_action_bindings (
      order_guid, action_id, request_signature, attempt_id,
      idempotency_key, created_at_iso
    ) VALUES (?, ?, ?, ?, ?, ?)`,
    [
      "binding-legacy-actor-order",
      "legacy-action",
      "[\"square\",\"purchase\",\"AUD\",1000]",
      "legacy-attempt",
      "legacy-idempotency",
      nowIso,
    ],
  );

  await applyMigrations(
    connection,
    () => nowIso,
    POS_DATABASE_MIGRATIONS.filter((migration) => migration.version <= 28),
  );

  const binding = await new SqlitePaymentActionBindingStore(
    connection,
  ).getByAttempt("legacy-attempt");
  assert.deepEqual(binding?.actor, {
    cashierId: "cashier-1",
    cashierName: "Cashier",
    userGuid: null,
  });
  const stored = await connection.getFirst<{ audit_actor_json: unknown }>(
    `SELECT audit_actor_json
     FROM payment_action_bindings
     WHERE attempt_id = 'legacy-attempt'`,
  );
  assert.equal(stored?.audit_actor_json, null);
});

test("真实 SQLite：PaymentActionBinding 严格拒绝空值、控制字符、超长值、非法时间与凭据形态", async () => {
  const { connection, database } = await openDatabase();
  await seedOrder(connection, {
    orderGuid: "binding-validation-order",
    localSequence: 12,
    state: "Draft",
    soldAtIso: "2026-07-28T01:00:00.000Z",
    actualAmountCents: 1_000,
  });
  const bindings = database.paymentActionBindings();
  const proposed = paymentBinding({
    orderGuid: "binding-validation-order",
  });

  await assert.rejects(
    bindings.bindOrGet({ ...proposed, actionId: " " }),
    /actionId/,
  );
  await assert.rejects(
    bindings.bindOrGet({ ...proposed, requestSignature: "bad\u0000signature" }),
    /requestSignature/,
  );
  await assert.rejects(
    bindings.bindOrGet({ ...proposed, idempotencyKey: "x".repeat(257) }),
    /idempotencyKey/,
  );
  await assert.rejects(
    bindings.bindOrGet({ ...proposed, createdAtIso: "not-an-iso-date" }),
    /createdAtIso/,
  );
  await assert.rejects(
    bindings.bindOrGet({
      ...proposed,
      requestSignature: "token=do-not-store-this-value",
    }),
    /requestSignature/,
  );
  await assert.rejects(
    bindings.bindOrGet({
      ...proposed,
      authorizationToken: "must-not-cross-the-port",
    } as PaymentActionBinding),
    /unexpected field/,
  );
});

test("真实 SQLite：MixedPaymentOrderTruth facade 返回状态和全部 tender，读取不改 local_sequence", async () => {
  const { connection, database } = await openDatabase();
  await seedOrder(connection, {
    orderGuid: "order-mixed",
    localSequence: 41,
    state: "Completing",
    soldAtIso: "2026-07-28T01:00:00.000Z",
    actualAmountCents: 1_000,
  });
  await seedTender(connection, "tender-card", "order-mixed", "card", 400, 1);
  await seedTender(connection, "tender-cash", "order-mixed", "cash", 700, 2);
  await seedTender(connection, "tender-reversal", "order-mixed", "card", -100, 3);
  const port: MixedPaymentOrderTruthPort =
    database.mixedPaymentOrderTruth();

  const truth = await port.getPaymentTruth("order-mixed");

  assert.deepEqual(truth, {
    orderGuid: "order-mixed",
    state: "Completing",
    actualAmount: { currency: "AUD", cents: 1_000 },
    tenders: [
      tenderTruth("tender-card", "card", 400),
      tenderTruth("tender-cash", "cash", 700),
      tenderTruth("tender-reversal", "card", -100),
    ],
    reversalLinks: [],
  });
  assert.equal(await port.getPaymentTruth("missing-order"), null);
  const persisted = await connection.getFirst<{
    local_sequence: number;
    state: string;
  }>(
    "SELECT local_sequence, state FROM local_orders WHERE order_guid = 'order-mixed'",
  );
  assert.deepEqual({ ...persisted }, {
    local_sequence: 41,
    state: "Completing",
  });
});

test("真实 SQLite：reversal link 只接受同单同 method 等额反向 tender，关联不可改删且 truth 可读取", async () => {
  const { connection, database } = await openDatabase();
  await seedOrder(connection, {
    orderGuid: "reversal-order",
    localSequence: 51,
    state: "Completing",
    soldAtIso: "2026-07-28T01:00:00.000Z",
    actualAmountCents: 2_000,
  });
  await seedOrder(connection, {
    orderGuid: "other-order",
    localSequence: 52,
    state: "Completing",
    soldAtIso: "2026-07-28T01:01:00.000Z",
    actualAmountCents: 1_000,
  });
  await seedReversalTenderFixture(connection);

  await insertReversalLink(connection, {
    orderGuid: "reversal-order",
    actionId: "reverse-action-1",
    sourceTenderGuid: "source-1",
    reversalTenderGuid: "reversal-1",
  });

  const truth = await database
    .mixedPaymentOrderTruth()
    .getPaymentTruth("reversal-order");
  assert.deepEqual(truth?.reversalLinks, [
    {
      actionId: "reverse-action-1",
      sourceTenderGuid: "source-1",
      reversalTenderGuid: "reversal-1",
    },
  ]);
  await assert.rejects(
    insertReversalLink(connection, {
      orderGuid: "reversal-order",
      actionId: "reverse-source-again",
      sourceTenderGuid: "source-1",
      reversalTenderGuid: "reversal-duplicate-source",
    }),
    /UNIQUE constraint failed: payment_tender_reversal_links\.source_tender_guid/,
  );
  await assert.rejects(
    insertReversalLink(connection, {
      orderGuid: "reversal-order",
      actionId: "reverse-action-1",
      sourceTenderGuid: "source-2",
      reversalTenderGuid: "reversal-2",
    }),
    /UNIQUE constraint failed: payment_tender_reversal_links\.order_guid, payment_tender_reversal_links\.action_id/,
  );
  await assert.rejects(
    insertReversalLink(connection, {
      orderGuid: "reversal-order",
      actionId: "reverse-reversal-again",
      sourceTenderGuid: "source-3",
      reversalTenderGuid: "reversal-1",
    }),
    /UNIQUE constraint failed: payment_tender_reversal_links\.reversal_tender_guid/,
  );
  await assert.rejects(
    insertReversalLink(connection, {
      orderGuid: "reversal-order",
      actionId: "reverse-wrong-amount",
      sourceTenderGuid: "source-2",
      reversalTenderGuid: "reversal-wrong-amount",
    }),
    /PAYMENT_TENDER_REVERSAL_LINK_INVALID/,
  );
  await assert.rejects(
    insertReversalLink(connection, {
      orderGuid: "reversal-order",
      actionId: "reverse-wrong-method",
      sourceTenderGuid: "source-2",
      reversalTenderGuid: "reversal-wrong-method",
    }),
    /PAYMENT_TENDER_REVERSAL_LINK_INVALID/,
  );
  await assert.rejects(
    insertReversalLink(connection, {
      orderGuid: "reversal-order",
      actionId: "reverse-cross-order",
      sourceTenderGuid: "source-2",
      reversalTenderGuid: "other-reversal",
    }),
    /PAYMENT_TENDER_REVERSAL_LINK_INVALID/,
  );
  await assert.rejects(
    insertReversalLink(connection, {
      orderGuid: "reversal-order",
      actionId: "reverse-existing-reversal",
      sourceTenderGuid: "reversal-1",
      reversalTenderGuid: "positive-again",
    }),
    /PAYMENT_TENDER_REVERSAL_LINK_INVALID/,
  );
  await assert.rejects(
    connection.run(
      "UPDATE payment_tender_reversal_links SET action_id = 'changed' WHERE order_guid = 'reversal-order'",
    ),
    /PAYMENT_TENDER_REVERSAL_LINK_IMMUTABLE/,
  );
  await assert.rejects(
    connection.run(
      "DELETE FROM payment_tender_reversal_links WHERE order_guid = 'reversal-order'",
    ),
    /PAYMENT_TENDER_REVERSAL_LINK_IMMUTABLE/,
  );
});

test("真实 SQLite：同步历史按 local_sequence DESC 稳定分页，筛选和 pending 总数不受页大小影响", async () => {
  const { connection, database } = await openDatabase();
  await seedHistory(connection);
  const port: LocalSyncHistoryPort =
    database.localSyncHistory(supportContext);

  const first = await port.listLocalSyncHistory({
    limit: 2,
    beforeLocalSequence: null,
    filters: { dateFromIso: null, dateToIso: null, states: [] },
  });
  const second = await port.listLocalSyncHistory({
    limit: 2,
    beforeLocalSequence: first.nextBeforeLocalSequence,
    filters: { dateFromIso: null, dateToIso: null, states: [] },
  });
  const filtered = await port.listLocalSyncHistory({
    limit: 10,
    beforeLocalSequence: null,
    filters: {
      dateFromIso: "2026-07-28T03:00:00.000Z",
      dateToIso: "2026-07-28T05:00:00.000Z",
      states: ["PendingSync", "CompletedLocal"],
    },
  });

  assert.deepEqual(first.orders.map((order) => order.localSequence), [105, 104]);
  assert.equal(first.nextBeforeLocalSequence, 104);
  assert.equal(first.pendingCount, 2);
  assert.deepEqual(second.orders.map((order) => order.localSequence), [103, 102]);
  assert.equal(second.nextBeforeLocalSequence, 102);
  assert.deepEqual(filtered.orders.map((order) => order.localSequence), [105, 103]);
  assert.equal(filtered.pendingCount, 2);
  assert.deepEqual(first.orders[0]?.tenders, [
    { method: "card", amountCents: 500 },
    { method: "card", amountCents: -100 },
    { method: "cash", amountCents: 600 },
  ]);
  assert.deepEqual(
    Object.keys(first.orders[0]?.tenders[0] ?? {}).sort(),
    ["amountCents", "method"],
  );
  assert.equal(first.orders[1]?.outbox?.lastErrorCode, null);
  assert.equal(first.orders.some((order) => order.orderGuid === "order-draft"), false);
});

test("真实 SQLite：支持导出在同一有界快照中返回精确匹配数和最多 limit 条订单", async () => {
  const { connection, database } = await openDatabase();
  await seedHistory(connection);
  const port: LocalSyncHistoryPort =
    database.localSyncHistory(supportContext);

  const snapshot =
    await port.getLocalSyncHistorySupportSnapshot({
      limit: 3,
      filters: {
        dateFromIso: null,
        dateToIso: null,
        states: [],
      },
    });

  assert.equal(snapshot.totalMatchingCount, 7);
  assert.deepEqual(
    snapshot.orders.map((order) => order.localSequence),
    [105, 104, 103],
  );
  assert.deepEqual(snapshot.orders[0]?.tenders, [
    { method: "card", amountCents: 500 },
    { method: "card", amountCents: -100 },
    { method: "cash", amountCents: 600 },
  ]);
});

test("真实 SQLite：手动补传只推进 eligible pending order-sync，blocked/rejected/leased 和订单事实均不变", async () => {
  const { connection, database } = await openDatabase();
  await seedHistory(connection);
  const port: LocalSyncHistoryPort =
    database.localSyncHistory(supportContext);
  const beforeOrders = await snapshotOrders(connection);
  const beforeOutbox = await snapshotOutbox(connection);

  const restored = await port.restoreExistingOrderOutboxToPending([
    "order-105",
    "order-104",
    "order-103",
    "order-102",
    "order-101",
    "order-099",
    "order-098",
    "missing-order",
  ]);

  assert.deepEqual(restored, {
    restoredOrderGuids: ["order-105", "order-103"],
    skippedOrderGuids: [
      "order-104",
      "order-102",
      "order-101",
      "order-099",
      "order-098",
      "missing-order",
    ],
  });
  const afterOrders = await snapshotOrders(connection);
  const afterOutbox = await snapshotOutbox(connection);
  assert.deepEqual(afterOrders, beforeOrders);
  for (const orderGuid of ["order-105", "order-103"]) {
    const before = beforeOutbox.find((row) => row.aggregate_id === orderGuid);
    const after = afterOutbox.find((row) => row.aggregate_id === orderGuid);
    assert.equal(after?.state, "pending");
    assert.equal(after?.next_attempt_at_iso, nowIso);
    assert.equal(after?.last_error_code, null);
    assert.equal(after?.attempt_count, before?.attempt_count);
    assert.equal(after?.payload_json, before?.payload_json);
  }
  for (const orderGuid of [
    "order-104",
    "order-102",
    "order-101",
    "order-099",
    "order-098",
  ]) {
    assert.deepEqual(
      afterOutbox.find((row) => row.aggregate_id === orderGuid),
      beforeOutbox.find((row) => row.aggregate_id === orderGuid),
    );
  }
  assert.equal(
    afterOutbox.some((row) => row.aggregate_id === "missing-order"),
    false,
  );
});

test("真实 SQLite：501 笔手动补传在同一事务内全部恢复", async () => {
  const { connection, database } = await openDatabase();
  const orderGuids = Array.from(
    { length: 501 },
    (_, index) => `bulk-order-${index + 1}`,
  );
  await connection.withExclusiveTransaction(async (transaction) => {
    for (const [index, orderGuid] of orderGuids.entries()) {
      await seedOrder(transaction, {
        orderGuid,
        localSequence: 10_000 + index,
        state: "PendingSync",
        soldAtIso: "2026-07-28T05:00:00.000Z",
        actualAmountCents: 1_000,
      });
      await seedOutbox(transaction, {
        messageId: `bulk-outbox-${index + 1}`,
        orderGuid,
        state: "pending",
        attemptCount: 1,
        lastErrorCode: "NETWORK_TIMEOUT",
      });
    }
  });
  const port = database.localSyncHistory(supportContext);

  const restored =
    await port.restoreExistingOrderOutboxToPending(orderGuids);

  assert.equal(restored.restoredOrderGuids.length, 501);
  assert.deepEqual(restored.skippedOrderGuids, []);
  const row = await connection.getFirst<{ restored_count: number }>(
    `SELECT COUNT(*) AS restored_count
     FROM outbox_messages
     WHERE aggregate_id LIKE 'bulk-order-%'
       AND state = 'pending'
       AND next_attempt_at_iso = ?
       AND last_error_code IS NULL`,
    [nowIso],
  );
  assert.equal(row?.restored_count, 501);
});

test("真实 SQLite：support context 只由构造参数注入且 PosDatabase 不暴露裸连接", async () => {
  const { database } = await openDatabase();
  const mixed: MixedPaymentOrderTruthPort =
    database.mixedPaymentOrderTruth();
  const history: LocalSyncHistoryPort =
    database.localSyncHistory(supportContext);

  assert.deepEqual(await history.getSupportContext(), supportContext);
  assert.ok(mixed instanceof SqliteMixedPaymentOrderTruthStore);
  assert.ok(history instanceof SqliteLocalSyncHistoryStore);
  assert.equal("getPaymentTruth" in database, false);
  assert.equal("listLocalSyncHistory" in database, false);
});

test("真实 SQLite：离线退货容量按相同来源聚合并且查询不扣减", async () => {
  const { connection, database } = await openDatabase();
  await seedReturnCapacity(connection, [
    ["return-1", "order-1", "detail-1", "3"],
    ["return-2", "order-2", null, "1"],
  ]);
  const before = await readReturnCapacities(connection);
  const changesBefore = await readTotalChanges(connection);
  const capacity = database.offlineReturnCapacity();
  const snapshot = returnSnapshot([
    returnLine("line-1", "return-1", "order-1", "detail-1", "1"),
    returnLine("line-2", "return-1", "order-1", "detail-1", "2"),
    returnLine("line-3", "return-2", "order-2", null, "1"),
  ]);

  assert.equal(await capacity.hasCapacity(snapshot), true);
  assert.equal(await capacity.hasCapacity(snapshot), true);
  assert.deepEqual(await readReturnCapacities(connection), before);
  assert.equal(await readTotalChanges(connection), changesBefore);
  assert.equal("connection" in capacity, false);
});

test("真实 SQLite：离线退货容量对空单、混合、非整数、耗尽和身份错配全部返回 false", async () => {
  const { connection, database } = await openDatabase();
  await seedReturnCapacity(connection, [
    ["return-1", "order-1", "detail-1", "3"],
    ["return-null", "order-null", null, "1"],
  ]);
  const capacity = database.offlineReturnCapacity();
  const valid = returnLine(
    "line-valid",
    "return-1",
    "order-1",
    "detail-1",
    "1",
  );
  const scenarios: readonly CartSnapshot[] = [
    returnSnapshot([]),
    returnSnapshot([
      valid,
      {
        ...returnLine(
          "line-sale",
          "return-1",
          "order-1",
          "detail-1",
          "1",
        ),
        kind: "sale",
      },
    ]),
    { ...returnSnapshot([valid]), mode: "sale" },
    returnSnapshot([{ ...valid, quantity: "1.5" }]),
    returnSnapshot([{ ...valid, quantity: "1e0" }]),
    returnSnapshot([{ ...valid, quantity: "4" }]),
    returnSnapshot([
      { ...valid, quantity: "2" },
      returnLine(
        "line-aggregate-overflow",
        "return-1",
        "order-1",
        "detail-1",
        "2",
      ),
    ]),
    returnSnapshot([{ ...valid, originalOrderGuid: "wrong-order" }]),
    returnSnapshot([{ ...valid, originalOrderDetailGuid: "wrong-detail" }]),
    returnSnapshot([{ ...valid, originalOrderDetailGuid: null }]),
    returnSnapshot([
      valid,
      {
        ...returnLine(
          "line-conflict",
          "return-1",
          "order-1",
          "wrong-detail",
          "1",
        ),
      },
    ]),
    returnSnapshot([{
      ...valid,
      returnSourceKey: null,
    }]),
    returnSnapshot([
      returnLine(
        "line-null-mismatch",
        "return-null",
        "order-null",
        "detail-unexpected",
        "1",
      ),
    ]),
  ];

  for (const snapshot of scenarios) {
    assert.equal(await capacity.hasCapacity(snapshot), false);
  }
  assert.deepEqual(await readReturnCapacities(connection), [
    {
      return_source_key: "return-1",
      original_order_guid: "order-1",
      original_order_detail_guid: "detail-1",
      remaining_quantity: "3",
    },
    {
      return_source_key: "return-null",
      original_order_guid: "order-null",
      original_order_detail_guid: null,
      remaining_quantity: "1",
    },
  ]);
});

test("离线退货容量读取故障原样上抛，不能伪装为业务容量不足", async () => {
  const marker = new Error("return capacity database unavailable");
  const connection = new class extends NodeSqliteConnection {
    public override async getFirst<T extends object>(
      _sql: string,
      _parameters: readonly SqlValue[] = [],
    ): Promise<T | null> {
      throw marker;
    }
  }(new DatabaseSync(":memory:", { enableForeignKeyConstraints: true }));
  const capacity = new SqliteOfflineReturnCapacity(connection);

  await assert.rejects(
    () => capacity.hasCapacity(returnSnapshot([
      returnLine("line-1", "return-1", "order-1", "detail-1", "1"),
    ])),
    (error: unknown) => error === marker,
  );
  await connection.close();
});

type SeedOrderInput = Readonly<{
  orderGuid: string;
  localSequence: number;
  state: string;
  soldAtIso: string;
  actualAmountCents: number;
}>;

async function seedOrder(
  connection: SqliteConnectionPort,
  input: SeedOrderInput,
): Promise<void> {
  await connection.run(
    `INSERT INTO local_orders (
      order_guid, local_sequence, store_code, device_code, cashier_id, cashier_name,
      sold_at_iso, state, total_cents, discount_cents, actual_amount_cents,
      original_order_guid, created_at_iso, updated_at_iso
    ) VALUES (?, ?, 'S1', 'IPAD-01', 'cashier-1', 'Cashier', ?, ?,
      ?, 0, ?, NULL, ?, ?)`,
    [
      input.orderGuid,
      input.localSequence,
      input.soldAtIso,
      input.state,
      input.actualAmountCents,
      input.actualAmountCents,
      input.soldAtIso,
      input.soldAtIso,
    ],
  );
}

async function seedTender(
  connection: SqliteConnectionPort,
  tenderGuid: string,
  orderGuid: string,
  method: "cash" | "card" | "voucher",
  amountCents: number,
  offsetMinutes: number,
): Promise<void> {
  await connection.run(
    `INSERT INTO order_tenders (
      tender_guid, order_guid, method, amount_cents, payment_attempt_id, created_at_iso
    ) VALUES (?, ?, ?, ?, NULL, ?)`,
    [
      tenderGuid,
      orderGuid,
      method,
      amountCents,
      `2026-07-28T00:${String(offsetMinutes).padStart(2, "0")}:00.000Z`,
    ],
  );
}

async function seedOutbox(
  connection: SqliteConnectionPort,
  input: Readonly<{
    messageId: string;
    orderGuid: string;
    state: string;
    attemptCount: number;
    lastErrorCode: string | null;
  }>,
): Promise<void> {
  await connection.run(
    `INSERT INTO outbox_messages (
      message_id, aggregate_id, kind, payload_json, state, attempt_count,
      next_attempt_at_iso, lease_id, lease_expires_at_iso, last_error_code,
      created_at_iso, updated_at_iso
    ) VALUES (?, ?, 'order-sync', ?, ?, ?,
      '2026-07-29T00:00:00.000Z', NULL, NULL, ?,
      '2026-07-28T00:00:00.000Z', '2026-07-28T00:00:00.000Z')`,
    [
      input.messageId,
      input.orderGuid,
      JSON.stringify({ orderGuid: input.orderGuid }),
      input.state,
      input.attemptCount,
      input.lastErrorCode,
    ],
  );
}

async function seedHistory(connection: SqliteConnectionPort): Promise<void> {
  const orders: readonly SeedOrderInput[] = [
    orderSeed(105, "PendingSync", 5),
    orderSeed(104, "Blocked403", 4),
    orderSeed(103, "CompletedLocal", 3),
    orderSeed(102, "Synced", 2),
    orderSeed(101, "Rejected", 1),
    orderSeed(100, "Draft", 0),
    orderSeed(99, "Blocked403", 0),
    orderSeed(98, "PendingSync", 0),
  ];
  for (const order of orders) await seedOrder(connection, order);

  await seedTender(connection, "tender-105-card", "order-105", "card", 500, 1);
  await seedTender(connection, "tender-105-reversal", "order-105", "card", -100, 2);
  await seedTender(connection, "tender-105-cash", "order-105", "cash", 600, 3);

  await seedOutbox(connection, {
    messageId: "outbox-105",
    orderGuid: "order-105",
    state: "pending",
    attemptCount: 2,
    lastErrorCode: "HTTP_500",
  });
  await seedOutbox(connection, {
    messageId: "outbox-104",
    orderGuid: "order-104",
    state: "blocked403",
    attemptCount: 3,
    lastErrorCode: "AUTHORIZATION_TOKEN_BAD",
  });
  await seedOutbox(connection, {
    messageId: "outbox-103",
    orderGuid: "order-103",
    state: "pending",
    attemptCount: 1,
    lastErrorCode: "NETWORK_TIMEOUT",
  });
  await seedOutbox(connection, {
    messageId: "outbox-102",
    orderGuid: "order-102",
    state: "succeeded",
    attemptCount: 1,
    lastErrorCode: null,
  });
  await seedOutbox(connection, {
    messageId: "outbox-101",
    orderGuid: "order-101",
    state: "rejected",
    attemptCount: 1,
    lastErrorCode: "BUSINESS_REJECTED",
  });
  await seedOutbox(connection, {
    messageId: "outbox-099",
    orderGuid: "order-099",
    state: "pending",
    attemptCount: 4,
    lastErrorCode: "HTTP_403",
  });
  await seedOutbox(connection, {
    messageId: "outbox-098",
    orderGuid: "order-098",
    state: "blocked403",
    attemptCount: 2,
    lastErrorCode: "HTTP_403",
  });
}

function orderSeed(
  localSequence: number,
  state: string,
  hour: number,
): SeedOrderInput {
  return {
    orderGuid: `order-${String(localSequence).padStart(3, "0")}`,
    localSequence,
    state,
    soldAtIso: `2026-07-28T${String(hour).padStart(2, "0")}:00:00.000Z`,
    actualAmountCents: 1_000,
  };
}

function tenderTruth(
  tenderGuid: string,
  method: "cash" | "card" | "voucher",
  cents: number,
) {
  return {
    tenderGuid,
    method,
    amount: { currency: "AUD", cents },
    reference: null,
    reservationToken: null,
  };
}

function paymentBinding(
  overrides: Partial<PaymentActionBinding> = {},
): PaymentActionBinding {
  return {
    orderGuid: "binding-order",
    actionId: "action-1",
    requestSignature: "[\"square\",\"purchase\",\"AUD\",1000]",
    attemptId: "attempt-1",
    idempotencyKey: "idempotency-1",
    createdAtIso: "2026-07-28T06:00:00.000Z",
    actor: {
      cashierId: "cashier-alice",
      cashierName: "Alice",
      userGuid: "user-alice",
    },
    ...overrides,
  };
}

async function listTables(
  connection: SqliteConnectionPort,
): Promise<readonly string[]> {
  const rows = await connection.getAll<{ name: string }>(
    "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name",
  );
  return rows.map((row) => row.name);
}

async function seedReversalTenderFixture(
  connection: SqliteConnectionPort,
): Promise<void> {
  const tenders = [
    ["source-1", "reversal-order", "cash", 500],
    ["reversal-1", "reversal-order", "cash", -500],
    ["reversal-duplicate-source", "reversal-order", "cash", -500],
    ["source-2", "reversal-order", "cash", 300],
    ["reversal-2", "reversal-order", "cash", -300],
    ["source-3", "reversal-order", "cash", 500],
    ["reversal-wrong-amount", "reversal-order", "cash", -200],
    ["reversal-wrong-method", "reversal-order", "card", -300],
    ["other-reversal", "other-order", "cash", -300],
    ["positive-again", "reversal-order", "cash", 500],
  ] as const;
  for (const [index, [tenderGuid, orderGuid, method, amountCents]] of tenders.entries()) {
    await seedTender(
      connection,
      tenderGuid,
      orderGuid,
      method,
      amountCents,
      index + 1,
    );
  }
}

function insertReversalLink(
  connection: SqliteConnectionPort,
  input: Readonly<{
    orderGuid: string;
    actionId: string;
    sourceTenderGuid: string;
    reversalTenderGuid: string;
  }>,
): Promise<SqlRunResult> {
  return connection.run(
    `INSERT INTO payment_tender_reversal_links (
      order_guid, action_id, source_tender_guid, reversal_tender_guid, created_at_iso
    ) VALUES (?, ?, ?, ?, ?)`,
    [
      input.orderGuid,
      input.actionId,
      input.sourceTenderGuid,
      input.reversalTenderGuid,
      nowIso,
    ],
  );
}

async function snapshotOrders(connection: SqliteConnectionPort) {
  return connection.getAll<Record<string, SqlValue>>(
    `SELECT order_guid, local_sequence, state, total_cents, discount_cents,
      actual_amount_cents FROM local_orders ORDER BY local_sequence DESC`,
  );
}

async function snapshotOutbox(connection: SqliteConnectionPort) {
  return connection.getAll<{
    aggregate_id: string;
    kind: string;
    state: string;
    attempt_count: number;
    next_attempt_at_iso: string;
    last_error_code: string | null;
    payload_json: string;
  }>(
    `SELECT aggregate_id, kind, state, attempt_count, next_attempt_at_iso,
      last_error_code, payload_json
     FROM outbox_messages
     ORDER BY aggregate_id DESC`,
  );
}

function returnLine(
  lineId: string,
  returnSourceKey: string,
  originalOrderGuid: string,
  originalOrderDetailGuid: string | null,
  quantity: string,
): CartSnapshot["lines"][number] {
  return {
    lineId,
    productCode: "P1",
    itemNumber: null,
    lookupCode: "123",
    displayName: "Returned item",
    quantity,
    unitPrice: { currency: "AUD", cents: 500 },
    discount: { currency: "AUD", cents: 0 },
    actualAmount: { currency: "AUD", cents: -500 },
    priceSource: "catalog",
    kind: "return",
    returnSourceKey,
    originalOrderGuid,
    originalOrderDetailGuid,
  };
}

function returnSnapshot(
  lines: CartSnapshot["lines"],
): CartSnapshot {
  return {
    revision: 1,
    mode: "return",
    lines,
    subtotal: { currency: "AUD", cents: -500 },
    discount: { currency: "AUD", cents: 0 },
    actualAmount: { currency: "AUD", cents: -500 },
  };
}

async function seedReturnCapacity(
  connection: SqliteConnectionPort,
  rows: readonly (readonly [
    returnSourceKey: string,
    originalOrderGuid: string,
    originalOrderDetailGuid: string | null,
    remainingQuantity: string,
  ])[],
): Promise<void> {
  for (const row of rows) {
    await connection.run(
      `INSERT INTO return_capacity (
        return_source_key, original_order_guid, original_order_detail_guid,
        original_quantity, remaining_quantity, updated_at_iso
      ) VALUES (?, ?, ?, ?, ?, ?)`,
      [row[0], row[1], row[2], row[3], row[3], nowIso],
    );
  }
}

async function readReturnCapacities(
  connection: SqliteConnectionPort,
): Promise<readonly Readonly<{
  return_source_key: string;
  original_order_guid: string;
  original_order_detail_guid: string | null;
  remaining_quantity: string;
}>[]> {
  const rows = await connection.getAll<{
    return_source_key: string;
    original_order_guid: string;
    original_order_detail_guid: string | null;
    remaining_quantity: string;
  }>(
    `SELECT return_source_key, original_order_guid,
      original_order_detail_guid, remaining_quantity
     FROM return_capacity ORDER BY return_source_key`,
  );
  return rows.map((row) => ({ ...row }));
}

async function readTotalChanges(
  connection: SqliteConnectionPort,
): Promise<number> {
  const row = await connection.getFirst<{ change_count: number }>(
    "SELECT total_changes() AS change_count",
  );
  if (!row) throw new Error("Unable to read SQLite total_changes().");
  return Number(row.change_count);
}
