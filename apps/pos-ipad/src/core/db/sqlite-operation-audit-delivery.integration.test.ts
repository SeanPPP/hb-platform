import assert from "node:assert/strict";
import { DatabaseSync, type SQLInputValue } from "node:sqlite";
import test from "node:test";

import type {
  HbposTransport,
  HbposTransportRequest,
  HbposTransportResponse,
} from "../api/hbpos-api";
import type { LocalOrder } from "../contracts/order";
import type { OrderRepositoryPort } from "../contracts/repositories";
import { HbposAuditBatchAdapter } from "../sync/hbpos-sync-adapters";

import { applyMigrations, POS_DATABASE_MIGRATIONS } from "./migrations";
import { createSqliteRepositories } from "./sqlite-repositories";
import type { SqliteConnectionPort, SqlRunResult, SqlValue } from "./types";

const NOW = "2026-08-01T00:00:00.000Z";
const RETRY_AT = "2026-08-01T00:05:00.000Z";

class NodeSqliteConnection implements SqliteConnectionPort {
  private transactionActive = false;

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
    return row === undefined ? null : row as T;
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

class UnexpectedAuditTransport implements HbposTransport {
  public calls = 0;

  public async request<T>(
    _request: HbposTransportRequest,
  ): Promise<HbposTransportResponse<T>> {
    this.calls += 1;
    throw new Error("Invalid local audit events must not reach the transport.");
  }
}

class EmptyOrders implements OrderRepositoryPort {
  public async nextLocalSequence(): Promise<number> { return 1; }
  public async saveDraft(): Promise<void> {}
  public async getByGuid(): Promise<LocalOrder | null> { return null; }
  public async listLocal(): Promise<readonly LocalOrder[]> { return []; }
  public async transition(): Promise<boolean> { return true; }
}

async function createHarness() {
  const connection = new NodeSqliteConnection(
    new DatabaseSync(":memory:", { enableForeignKeyConstraints: true }),
  );
  await applyMigrations(connection, () => NOW);
  const repositories = createSqliteRepositories(connection, {
    nowIso: () => NOW,
    createLeaseId: () => "unused-lease",
    auditScope: { storeCode: "S1", deviceCode: "IPAD-1" },
    encryptor: {
      async encrypt(value) { return new TextEncoder().encode(value); },
      async decrypt(value) { return new TextDecoder().decode(value); },
    },
  });
  return { connection, repositories };
}

function createConnection(): NodeSqliteConnection {
  return new NodeSqliteConnection(
    new DatabaseSync(":memory:", { enableForeignKeyConstraints: true }),
  );
}

async function insertLegacyAuditWithoutScope(
  connection: SqliteConnectionPort,
  eventId: string,
  orderGuid: string,
): Promise<void> {
  await connection.run(
    `INSERT INTO audit_events (
      event_id, event_type, occurred_at_iso, order_guid, correlation_id,
      payload_json, uploaded_at_iso, delivery_state, attempt_count,
      next_attempt_at_iso, last_error_code
    ) VALUES (?, 'OPERATION', ?, ?, ?, '{}', NULL, 'pending', 0, ?, NULL)`,
    [eventId, NOW, orderGuid, eventId, NOW],
  );
}

async function insertAudit(
  connection: SqliteConnectionPort,
  input: Readonly<{
    eventId: string;
    occurredAtIso: string;
    nextAttemptAtIso: string;
    scopeStoreCode: string | null;
    scopeDeviceCode: string | null;
    orderGuid?: string | null;
    attemptCount?: number;
  }>,
): Promise<void> {
  await connection.run(
    `INSERT INTO audit_events (
      event_id, event_type, occurred_at_iso, order_guid, correlation_id,
      payload_json, uploaded_at_iso, delivery_state, attempt_count,
      next_attempt_at_iso, last_error_code, scope_store_code, scope_device_code
    ) VALUES (?, 'OPERATION', ?, ?, ?, '{}', NULL, 'pending', ?, ?, NULL, ?, ?)`,
    [
      input.eventId,
      input.occurredAtIso,
      input.orderGuid ?? null,
      input.eventId,
      input.attemptCount ?? 0,
      input.nextAttemptAtIso,
      input.scopeStoreCode,
      input.scopeDeviceCode,
    ],
  );
}

async function insertLocalOrder(
  connection: SqliteConnectionPort,
  orderGuid: string,
  storeCode: string,
  deviceCode: string,
  localSequence = 1,
): Promise<void> {
  await connection.run(
    `INSERT INTO local_orders (
      order_guid, local_sequence, store_code, device_code, cashier_id, cashier_name,
      sold_at_iso, state, total_cents, discount_cents, actual_amount_cents,
      original_order_guid, created_at_iso, updated_at_iso
    ) VALUES (?, ?, ?, ?, 'cashier-1', 'Cashier', ?, 'Draft', 0, 0, 0, NULL, ?, ?)`,
    [orderGuid, localSequence, storeCode, deviceCode, NOW, NOW, NOW],
  );
}

type AuditSnapshot = Readonly<{
  rowId: number;
  eventId: string | null;
  eventType: string;
  occurredAtIso: string;
  orderGuid: string | null;
  correlationId: string;
  payloadJson: string;
  uploadedAtIso: string | null;
  deliveryState: string;
  attemptCount: number;
  nextAttemptAtIso: string | null;
  lastErrorCode: string | null;
  scopeStoreCode: string | null;
  scopeDeviceCode: string | null;
}>;

type AuditFacts = Omit<
  AuditSnapshot,
  "rowId" | "scopeStoreCode" | "scopeDeviceCode"
>;

async function readAuditFactsByEventId(
  connection: SqliteConnectionPort,
  eventId: string,
): Promise<AuditFacts> {
  const row = await connection.getFirst<AuditFacts>(
    `SELECT
       event_id AS eventId,
       event_type AS eventType,
       occurred_at_iso AS occurredAtIso,
       order_guid AS orderGuid,
       correlation_id AS correlationId,
       payload_json AS payloadJson,
       uploaded_at_iso AS uploadedAtIso,
       delivery_state AS deliveryState,
       attempt_count AS attemptCount,
       next_attempt_at_iso AS nextAttemptAtIso,
       last_error_code AS lastErrorCode
     FROM audit_events
     WHERE event_id = ?`,
    [eventId],
  );
  assert.ok(row, `audit event ${eventId} should still exist`);
  return { ...row };
}

async function readAuditSnapshotByEventId(
  connection: SqliteConnectionPort,
  eventId: string,
): Promise<AuditSnapshot> {
  const row = await connection.getFirst<AuditSnapshot>(
    `SELECT
       rowid AS rowId,
       event_id AS eventId,
       event_type AS eventType,
       occurred_at_iso AS occurredAtIso,
       order_guid AS orderGuid,
       correlation_id AS correlationId,
       payload_json AS payloadJson,
       uploaded_at_iso AS uploadedAtIso,
       delivery_state AS deliveryState,
       attempt_count AS attemptCount,
       next_attempt_at_iso AS nextAttemptAtIso,
       last_error_code AS lastErrorCode,
       scope_store_code AS scopeStoreCode,
       scope_device_code AS scopeDeviceCode
     FROM audit_events
     WHERE event_id = ?`,
    [eventId],
  );
  assert.ok(row, `audit event ${eventId} should still exist`);
  return { ...row };
}

function factsFromSnapshot(snapshot: AuditSnapshot): AuditFacts {
  return {
    eventId: snapshot.eventId,
    eventType: snapshot.eventType,
    occurredAtIso: snapshot.occurredAtIso,
    orderGuid: snapshot.orderGuid,
    correlationId: snapshot.correlationId,
    payloadJson: snapshot.payloadJson,
    uploadedAtIso: snapshot.uploadedAtIso,
    deliveryState: snapshot.deliveryState,
    attemptCount: snapshot.attemptCount,
    nextAttemptAtIso: snapshot.nextAttemptAtIso,
    lastErrorCode: snapshot.lastErrorCode,
  };
}

async function readAuditSnapshotByRowId(
  connection: SqliteConnectionPort,
  rowId: number,
): Promise<AuditSnapshot> {
  const row = await connection.getFirst<AuditSnapshot>(
    `SELECT
       rowid AS rowId,
       event_id AS eventId,
       event_type AS eventType,
       occurred_at_iso AS occurredAtIso,
       order_guid AS orderGuid,
       correlation_id AS correlationId,
       payload_json AS payloadJson,
       uploaded_at_iso AS uploadedAtIso,
       delivery_state AS deliveryState,
       attempt_count AS attemptCount,
       next_attempt_at_iso AS nextAttemptAtIso,
       last_error_code AS lastErrorCode,
       scope_store_code AS scopeStoreCode,
       scope_device_code AS scopeDeviceCode
     FROM audit_events
     WHERE rowid = ?`,
    [rowId],
  );
  assert.ok(row, `audit rowid ${rowId} should still exist`);
  return { ...row };
}

async function createLegacyProtectionHarness(
  recursiveTriggers: boolean,
  legacyRowId?: number,
) {
  const connection = createConnection();
  const throughM29 = POS_DATABASE_MIGRATIONS.filter(
    (migration) => migration.version <= 29,
  );
  await applyMigrations(connection, () => NOW, throughM29);
  await insertLocalOrder(connection, "legacy-order", "S-LEGACY", "IPAD-LEGACY");
  await insertLegacyAuditWithoutScope(connection, "legacy-audit", "legacy-order");
  if (legacyRowId !== undefined) {
    await connection.run(
      "UPDATE audit_events SET rowid = ? WHERE event_id = 'legacy-audit'",
      [legacyRowId],
    );
    if (legacyRowId < 0) {
      // SQLite 只按当前最大 rowid 分配隐式值；保留正常正数 high-water，
      // 才能让本测试聚焦显式负 rowid 抢占，而不把隐式 -1 混入攻击面。
      await connection.run(
        `INSERT INTO audit_events (
          rowid, event_id, event_type, occurred_at_iso, order_guid,
          correlation_id, payload_json, uploaded_at_iso, delivery_state,
          attempt_count, next_attempt_at_iso, last_error_code
        ) VALUES (
          1, 'positive-rowid-anchor', 'OPERATION', ?, 'legacy-order',
          'positive-rowid-anchor', '{}', NULL, 'pending', 0, ?, NULL
        )`,
        [NOW, NOW],
      );
    }
  }
  await applyMigrations(connection, () => NOW);
  await connection.exec(
    `PRAGMA recursive_triggers = ${recursiveTriggers ? "ON" : "OFF"};`,
  );

  await insertAudit(connection, {
    eventId: "scoped-attacker",
    occurredAtIso: NOW,
    nextAttemptAtIso: NOW,
    scopeStoreCode: "S-OTHER",
    scopeDeviceCode: "IPAD-OTHER",
  });
  const stateUpdate = await connection.run(
    `UPDATE audit_events
     SET delivery_state = 'rejected', attempt_count = 1,
         next_attempt_at_iso = ?, last_error_code = 'TEST_STATE_UPDATE'
     WHERE event_id = 'scoped-attacker'`,
    [RETRY_AT],
  );
  assert.equal(stateUpdate.changes, 1);

  const baseline = await connection.getFirst<AuditSnapshot>(
    `SELECT
       rowid AS rowId,
       event_id AS eventId,
       event_type AS eventType,
       occurred_at_iso AS occurredAtIso,
       order_guid AS orderGuid,
       correlation_id AS correlationId,
       payload_json AS payloadJson,
       uploaded_at_iso AS uploadedAtIso,
       delivery_state AS deliveryState,
       attempt_count AS attemptCount,
       next_attempt_at_iso AS nextAttemptAtIso,
       last_error_code AS lastErrorCode,
       scope_store_code AS scopeStoreCode,
       scope_device_code AS scopeDeviceCode
     FROM audit_events
     WHERE event_id = 'legacy-audit'`,
  );
  assert.ok(baseline);

  const repositories = createSqliteRepositories(connection, {
    nowIso: () => NOW,
    createLeaseId: () => "unused-lease",
    auditScope: { storeCode: "S-LEGACY", deviceCode: "IPAD-LEGACY" },
    encryptor: {
      async encrypt(value) { return new TextEncoder().encode(value); },
      async decrypt(value) { return new TextDecoder().decode(value); },
    },
  });
  return { connection, baseline: { ...baseline }, repositories };
}

async function seedBackedOffHead(
  connection: SqliteConnectionPort,
): Promise<void> {
  for (let index = 1; index <= 8; index += 1) {
    await insertAudit(connection, {
      eventId: `event-${String(index).padStart(2, "0")}`,
      occurredAtIso: `2026-08-01T00:00:${String(index).padStart(2, "0")}.000Z`,
      nextAttemptAtIso: RETRY_AT,
      scopeStoreCode: "S1",
      scopeDeviceCode: "IPAD-1",
      attemptCount: 1,
    });
  }
  await insertAudit(connection, {
    eventId: "event-09",
    occurredAtIso: "2026-08-01T00:00:09.000Z",
    nextAttemptAtIso: NOW,
    scopeStoreCode: "S1",
    scopeDeviceCode: "IPAD-1",
  });
}

test("真实 SQLite：退避队头阻止后续已到期员工审计越过 FIFO", async () => {
  const { connection, repositories } = await createHarness();
  await seedBackedOffHead(connection);

  const ready = await repositories.auditDelivery.listReady(8);

  assert.deepEqual(ready.map((event) => event.eventId), []);
  await connection.close();
});

test("真实 SQLite：员工审计下次唤醒以最老 pending 队头为准", async () => {
  const { connection, repositories } = await createHarness();
  await seedBackedOffHead(connection);

  assert.equal(await repositories.auditDelivery.nextReadyAtIso(), RETRY_AT);
  await connection.close();
});

test("真实 SQLite：M32 拒绝不完整 scope，冻结非空 scope，并保留一次性订单回填", async () => {
  const { connection } = await createHarness();
  await connection.exec("PRAGMA recursive_triggers = OFF;");

  await assert.rejects(
    insertAudit(connection, {
      eventId: "partial-scope",
      occurredAtIso: NOW,
      nextAttemptAtIso: NOW,
      scopeStoreCode: "S1",
      scopeDeviceCode: null,
    }),
    /AUDIT_SCOPE_INVALID/,
  );
  await assert.rejects(
    insertAudit(connection, {
      eventId: "blank-scope",
      occurredAtIso: NOW,
      nextAttemptAtIso: NOW,
      scopeStoreCode: " ",
      scopeDeviceCode: "IPAD-1",
    }),
    /AUDIT_SCOPE_INVALID/,
  );

  for (const eventId of ["ordinary", "daily-close", "voucher"]) {
    await insertAudit(connection, {
      eventId,
      occurredAtIso: NOW,
      nextAttemptAtIso: NOW,
      scopeStoreCode: "S1",
      scopeDeviceCode: "IPAD-1",
    });
  }
  await assert.rejects(
    connection.run(
      "UPDATE audit_events SET scope_store_code = 'S2' WHERE event_id = 'ordinary'",
    ),
    /AUDIT_SCOPE_IMMUTABLE/,
  );

  await insertLocalOrder(connection, "order-m30", "S-ORDER", "IPAD-ORDER");
  await insertAudit(connection, {
    eventId: "m30-order-audit",
    occurredAtIso: NOW,
    nextAttemptAtIso: NOW,
    scopeStoreCode: null,
    scopeDeviceCode: null,
    orderGuid: "order-m30",
  });
  const m30Scope = await connection.getFirst<{
    storeCode: string | null;
    deviceCode: string | null;
  }>(
    `SELECT scope_store_code AS storeCode, scope_device_code AS deviceCode
     FROM audit_events WHERE event_id = 'm30-order-audit'`,
  );
  assert.deepEqual({ ...m30Scope }, {
    storeCode: "S-ORDER",
    deviceCode: "IPAD-ORDER",
  });
  assert.deepEqual(
    { ...await connection.getFirst<{ count: number }>(
      "SELECT COUNT(*) AS count FROM audit_scope_insert_guard",
    ) },
    { count: 0 },
  );

  await connection.exec("PRAGMA recursive_triggers = ON;");
  await insertLocalOrder(connection, "order-m32-recursive", "S-R", "IPAD-R", 2);
  await insertAudit(connection, {
    eventId: "m32-order-audit-recursive",
    occurredAtIso: NOW,
    nextAttemptAtIso: NOW,
    scopeStoreCode: null,
    scopeDeviceCode: null,
    orderGuid: "order-m32-recursive",
  });
  assert.deepEqual(
    { ...await connection.getFirst<{
      storeCode: string | null;
      deviceCode: string | null;
    }>(
      `SELECT scope_store_code AS storeCode, scope_device_code AS deviceCode
       FROM audit_events WHERE event_id = 'm32-order-audit-recursive'`,
    ) },
    { storeCode: "S-R", deviceCode: "IPAD-R" },
  );

  await insertAudit(connection, {
    eventId: "legacy-unproven",
    occurredAtIso: NOW,
    nextAttemptAtIso: NOW,
    scopeStoreCode: null,
    scopeDeviceCode: null,
  });
  const legacyScope = await connection.getFirst<{
    storeCode: string | null;
    deviceCode: string | null;
  }>(
    `SELECT scope_store_code AS storeCode, scope_device_code AS deviceCode
     FROM audit_events WHERE event_id = 'legacy-unproven'`,
  );
  assert.deepEqual({ ...legacyScope }, { storeCode: null, deviceCode: null });
  await connection.close();
});

test("真实 SQLite：新鲜 M32 拒绝完整 scope 的 NULL/空 eventId 且不遗留 guard", async () => {
  const { connection } = await createHarness();

  for (const eventId of [null, "   "] as const) {
    await assert.rejects(
      connection.run(
        `INSERT INTO audit_events (
          event_id, event_type, occurred_at_iso, order_guid, correlation_id,
          payload_json, uploaded_at_iso, delivery_state, attempt_count,
          next_attempt_at_iso, last_error_code,
          scope_store_code, scope_device_code
        ) VALUES (
          ?, 'OPERATION', ?, NULL, 'invalid-event-correlation',
          '{}', NULL, 'pending', 0, ?, NULL, 'S-VALID', 'IPAD-VALID'
        )`,
        [eventId, NOW, NOW],
      ),
      /AUDIT_EVENT_ID_INVALID/,
    );
  }
  assert.deepEqual(
    { ...await connection.getFirst<{ count: number }>(
      "SELECT COUNT(*) AS count FROM audit_scope_insert_guard",
    ) },
    { count: 0 },
  );
  await connection.close();
});

for (const sourceVersion of [29, 31] as const) {
  test(`真实 SQLite：M${sourceVersion} 历史 NULL eventId 升级后被隔离且 listReady 安全`, async () => {
    const connection = createConnection();
    await applyMigrations(
      connection,
      () => NOW,
      POS_DATABASE_MIGRATIONS.filter(
        (migration) => migration.version <= sourceVersion,
      ),
    );

    const scopeColumns = sourceVersion >= 30
      ? ", scope_store_code, scope_device_code"
      : "";
    const scopeValues = sourceVersion >= 30
      ? ", 'S-HISTORY', 'IPAD-HISTORY'"
      : "";
    await connection.run(
      `INSERT INTO audit_events (
        event_id, event_type, occurred_at_iso, order_guid, correlation_id,
        payload_json, uploaded_at_iso, delivery_state, attempt_count,
        next_attempt_at_iso, last_error_code${scopeColumns}
      ) VALUES (
        NULL, 'HISTORICAL_NULL_ID', ?, NULL, 'historical-null-correlation',
        '{"source":"history"}', NULL, 'pending', 2, ?, NULL${scopeValues}
      )`,
      [NOW, RETRY_AT],
    );

    await applyMigrations(connection, () => NOW);

    assert.deepEqual(
      { ...await connection.getFirst<{
        eventType: string;
        occurredAtIso: string;
        orderGuid: string | null;
        correlationId: string;
        payloadJson: string;
        uploadedAtIso: string | null;
        deliveryState: string;
        attemptCount: number;
        nextAttemptAtIso: string | null;
        lastErrorCode: string | null;
        scopeStoreCode: string | null;
        scopeDeviceCode: string | null;
      }>(
        `SELECT
           event_type AS eventType,
           occurred_at_iso AS occurredAtIso,
           order_guid AS orderGuid,
           correlation_id AS correlationId,
           payload_json AS payloadJson,
           uploaded_at_iso AS uploadedAtIso,
           delivery_state AS deliveryState,
           attempt_count AS attemptCount,
           next_attempt_at_iso AS nextAttemptAtIso,
           last_error_code AS lastErrorCode,
           scope_store_code AS scopeStoreCode,
           scope_device_code AS scopeDeviceCode
         FROM audit_events
         WHERE event_id IS NULL`,
      ) },
      {
        eventType: "HISTORICAL_NULL_ID",
        occurredAtIso: NOW,
        orderGuid: null,
        correlationId: "historical-null-correlation",
        payloadJson: '{"source":"history"}',
        uploadedAtIso: null,
        deliveryState: "rejected",
        attemptCount: 2,
        nextAttemptAtIso: RETRY_AT,
        lastErrorCode: "AUDIT_EVENT_ID_INVALID",
        scopeStoreCode: sourceVersion >= 30 ? "S-HISTORY" : null,
        scopeDeviceCode: sourceVersion >= 30 ? "IPAD-HISTORY" : null,
      },
    );

    const repositories = createSqliteRepositories(connection, {
      nowIso: () => NOW,
      createLeaseId: () => "unused-lease",
      auditScope: { storeCode: "S-HISTORY", deviceCode: "IPAD-HISTORY" },
      encryptor: {
        async encrypt(value) { return new TextEncoder().encode(value); },
        async decrypt(value) { return new TextDecoder().decode(value); },
      },
    });
    assert.deepEqual(await repositories.auditDelivery.listReady(8), []);
    await connection.close();
  });
}

test("真实 SQLite：有效 scoped audit 的 eventId 不可更新为 NULL/空值", async () => {
  const { connection } = await createHarness();
  await insertAudit(connection, {
    eventId: "valid-event-id",
    occurredAtIso: NOW,
    nextAttemptAtIso: NOW,
    scopeStoreCode: "S1",
    scopeDeviceCode: "IPAD-1",
  });
  const baseline = await readAuditSnapshotByEventId(connection, "valid-event-id");

  for (const invalidEventId of [null, "  "] as const) {
    await assert.rejects(
      connection.run(
        "UPDATE audit_events SET event_id = ? WHERE event_id = 'valid-event-id'",
        [invalidEventId],
      ),
      /AUDIT_EVENT_ID_INVALID/,
    );
    assert.deepEqual(
      await readAuditSnapshotByEventId(connection, "valid-event-id"),
      baseline,
    );
  }
  await connection.close();
});

test("真实 SQLite：BLOB eventId 升级隔离且新写入拒绝，数字 affinity 仍转为文本", async () => {
  const connection = createConnection();
  await applyMigrations(
    connection,
    () => NOW,
    POS_DATABASE_MIGRATIONS.filter((migration) => migration.version <= 31),
  );
  await connection.run(
    `INSERT INTO audit_events (
      event_id, event_type, occurred_at_iso, order_guid, correlation_id,
      payload_json, uploaded_at_iso, delivery_state, attempt_count,
      next_attempt_at_iso, last_error_code,
      scope_store_code, scope_device_code
    ) VALUES (
      CAST(X'626C6F622D6576656E74' AS BLOB), 'HISTORICAL_BLOB_ID', ?, NULL,
      'historical-blob-correlation', '{}', NULL, 'pending', 0, ?, NULL,
      'S-BLOB', 'IPAD-BLOB'
    )`,
    [NOW, NOW],
  );

  await applyMigrations(connection, () => NOW);

  assert.deepEqual(
    { ...await connection.getFirst<{
      storageType: string;
      deliveryState: string;
      lastErrorCode: string | null;
    }>(
      `SELECT TYPEOF(event_id) AS storageType,
              delivery_state AS deliveryState,
              last_error_code AS lastErrorCode
       FROM audit_events
       WHERE event_id = CAST(X'626C6F622D6576656E74' AS BLOB)`,
    ) },
    {
      storageType: "blob",
      deliveryState: "rejected",
      lastErrorCode: "AUDIT_EVENT_ID_INVALID",
    },
  );

  const repositories = createSqliteRepositories(connection, {
    nowIso: () => NOW,
    createLeaseId: () => "unused-lease",
    auditScope: { storeCode: "S-BLOB", deviceCode: "IPAD-BLOB" },
    encryptor: {
      async encrypt(value) { return new TextEncoder().encode(value); },
      async decrypt(value) { return new TextDecoder().decode(value); },
    },
  });
  assert.deepEqual(await repositories.auditDelivery.listReady(8), []);

  await assert.rejects(
    connection.run(
      `INSERT INTO audit_events (
        event_id, event_type, occurred_at_iso, order_guid, correlation_id,
        payload_json, uploaded_at_iso, delivery_state, attempt_count,
        next_attempt_at_iso, last_error_code,
        scope_store_code, scope_device_code
      ) VALUES (
        CAST(X'66726573682D626C6F62' AS BLOB), 'OPERATION', ?, NULL,
        'fresh-blob-correlation', '{}', NULL, 'pending', 0, ?, NULL,
        'S-BLOB', 'IPAD-BLOB'
      )`,
      [NOW, NOW],
    ),
    /AUDIT_EVENT_ID_INVALID/,
  );

  await insertAudit(connection, {
    eventId: "blob-update-target",
    occurredAtIso: NOW,
    nextAttemptAtIso: NOW,
    scopeStoreCode: "S-BLOB",
    scopeDeviceCode: "IPAD-BLOB",
  });
  const baseline = await readAuditSnapshotByEventId(
    connection,
    "blob-update-target",
  );
  await assert.rejects(
    connection.run(
      `UPDATE audit_events
       SET event_id = CAST(X'7570646174652D626C6F62' AS BLOB)
       WHERE event_id = 'blob-update-target'`,
    ),
    /AUDIT_EVENT_ID_INVALID/,
  );
  assert.deepEqual(
    await readAuditSnapshotByEventId(connection, "blob-update-target"),
    baseline,
  );

  await connection.run(
    `INSERT INTO audit_events (
      event_id, event_type, occurred_at_iso, order_guid, correlation_id,
      payload_json, uploaded_at_iso, delivery_state, attempt_count,
      next_attempt_at_iso, last_error_code,
      scope_store_code, scope_device_code
    ) VALUES (
      12345, 'OPERATION', ?, NULL, 'numeric-correlation', '{}', NULL,
      'pending', 0, ?, NULL, 'S-BLOB', 'IPAD-BLOB'
    )`,
    [NOW, NOW],
  );
  assert.deepEqual(
    { ...await connection.getFirst<{ eventId: string; storageType: string }>(
      `SELECT event_id AS eventId, TYPEOF(event_id) AS storageType
       FROM audit_events WHERE event_id = '12345'`,
    ) },
    { eventId: "12345", storageType: "text" },
  );
  await connection.close();
});

test("真实 SQLite：普通非 UUID eventId 可入库并由上传适配器逐条拒绝", async () => {
  const { connection, repositories } = await createHarness();
  await insertAudit(connection, {
    eventId: "ordinary-non-uuid",
    occurredAtIso: NOW,
    nextAttemptAtIso: NOW,
    scopeStoreCode: "S1",
    scopeDeviceCode: "IPAD-1",
  });

  const ready = await repositories.auditDelivery.listReady(8);
  assert.deepEqual(ready.map((event) => event.eventId), ["ordinary-non-uuid"]);

  const transport = new UnexpectedAuditTransport();
  const adapter = new HbposAuditBatchAdapter(
    transport,
    new EmptyOrders(),
    {
      storeCode: "S1",
      deviceCode: "IPAD-1",
      appVersion: "test",
      instanceId: "test-instance",
    },
  );
  assert.deepEqual(await adapter.upload(ready), {
    kind: "acknowledged",
    uploadedEventIds: [],
    rejected: [
      { eventId: "ordinary-non-uuid", code: "AUDIT_EVENT_INVALID" },
    ],
  });
  assert.equal(transport.calls, 0);
  await connection.close();
});

test("真实 SQLite：旧库升级后 legacy NULL scope 不能通过普通 SQL 解封投递", async () => {
  const connection = createConnection();
  const throughM29 = POS_DATABASE_MIGRATIONS.filter(
    (migration) => migration.version <= 29,
  );
  await applyMigrations(connection, () => NOW, throughM29);
  await insertLocalOrder(connection, "legacy-order", "S-LEGACY", "IPAD-LEGACY");
  await insertLegacyAuditWithoutScope(connection, "legacy-audit", "legacy-order");

  await applyMigrations(connection, () => NOW);

  await assert.rejects(
    connection.run(
      `UPDATE audit_events
       SET scope_store_code = 'S-LEGACY', scope_device_code = 'IPAD-LEGACY'
       WHERE event_id = 'legacy-audit'`,
    ),
    /AUDIT_SCOPE_IMMUTABLE/,
  );
  assert.deepEqual(
    { ...await connection.getFirst<{ count: number }>(
      "SELECT COUNT(*) AS count FROM audit_scope_insert_guard",
    ) },
    { count: 0 },
  );
  await assert.rejects(
    connection.run(
      `INSERT INTO audit_scope_insert_guard (
        event_id, scope_store_code, scope_device_code
      ) VALUES ('legacy-audit', 'S-LEGACY', 'IPAD-LEGACY')`,
    ),
    /AUDIT_SCOPE_GUARD_FORBIDDEN/,
  );
  await assert.rejects(
    connection.run(
      `UPDATE audit_events
       SET scope_store_code = 'S-LEGACY', scope_device_code = 'IPAD-LEGACY'
       WHERE event_id = 'legacy-audit'`,
    ),
    /AUDIT_SCOPE_IMMUTABLE/,
  );

  const repositories = createSqliteRepositories(connection, {
    nowIso: () => NOW,
    createLeaseId: () => "unused-lease",
    auditScope: { storeCode: "S-LEGACY", deviceCode: "IPAD-LEGACY" },
    encryptor: {
      async encrypt(value) { return new TextEncoder().encode(value); },
      async decrypt(value) { return new TextDecoder().decode(value); },
    },
  });
  assert.deepEqual(await repositories.auditDelivery.listReady(8), []);
  await connection.close();
});

for (const recursiveTriggers of [false, true] as const) {
  test(`真实 SQLite：recursive_triggers ${recursiveTriggers ? "ON" : "OFF"} 时 legacy audit 不可被普通 DML 替换、删除或抢占`, async () => {
    const { connection, baseline, repositories } =
      await createLegacyProtectionHarness(recursiveTriggers);

    await assert.rejects(
      insertAudit(connection, {
        eventId: "scoped-attacker",
        occurredAtIso: NOW,
        nextAttemptAtIso: NOW,
        scopeStoreCode: "S-OTHER",
        scopeDeviceCode: "IPAD-OTHER",
      }),
      /UNIQUE constraint failed/,
    );

    const replacementValues = `(
      'legacy-audit', 'REPLACED', '${NOW}', 'legacy-order',
      'replacement-correlation', '{"replacement":true}', NULL,
      'pending', 0, '${NOW}', NULL, NULL, NULL
    )`;
    const attacks: readonly Readonly<{
      name: string;
      run: () => Promise<SqlRunResult>;
      expectedError: RegExp;
    }>[] = [
      {
        name: "INSERT OR REPLACE",
        expectedError: /UNIQUE constraint failed: audit_events\.event_id/,
        run: () => connection.run(
          `INSERT OR REPLACE INTO audit_events (
            event_id, event_type, occurred_at_iso, order_guid, correlation_id,
            payload_json, uploaded_at_iso, delivery_state, attempt_count,
            next_attempt_at_iso, last_error_code,
            scope_store_code, scope_device_code
          ) VALUES ${replacementValues}`,
        ),
      },
      {
        name: "REPLACE",
        expectedError: /UNIQUE constraint failed: audit_events\.event_id/,
        run: () => connection.run(
          `REPLACE INTO audit_events (
            event_id, event_type, occurred_at_iso, order_guid, correlation_id,
            payload_json, uploaded_at_iso, delivery_state, attempt_count,
            next_attempt_at_iso, last_error_code,
            scope_store_code, scope_device_code
          ) VALUES ${replacementValues}`,
        ),
      },
      {
        name: "UPSERT DO UPDATE",
        expectedError: /UNIQUE constraint failed: audit_events\.event_id/,
        run: () => connection.run(
          `INSERT INTO audit_events (
            event_id, event_type, occurred_at_iso, order_guid, correlation_id,
            payload_json, uploaded_at_iso, delivery_state, attempt_count,
            next_attempt_at_iso, last_error_code,
            scope_store_code, scope_device_code
          ) VALUES ${replacementValues}
          ON CONFLICT(event_id) DO UPDATE SET
            event_type = excluded.event_type,
            occurred_at_iso = excluded.occurred_at_iso,
            order_guid = excluded.order_guid,
            correlation_id = excluded.correlation_id,
            payload_json = excluded.payload_json`,
        ),
      },
      {
        name: "INSERT OR REPLACE legacy rowid takeover",
        expectedError: /UNIQUE constraint failed: audit_events\.rowid/,
        run: () => connection.run(
          `INSERT OR REPLACE INTO audit_events (
            rowid, event_id, event_type, occurred_at_iso, order_guid,
            correlation_id, payload_json, uploaded_at_iso, delivery_state,
            attempt_count, next_attempt_at_iso, last_error_code,
            scope_store_code, scope_device_code
          ) VALUES (
            ${baseline.rowId}, 'rowid-replacement', 'REPLACED', '${NOW}', NULL,
            'rowid-replacement-correlation', '{"replacement":true}', NULL,
            'pending', 0, '${NOW}', NULL, 'S-ROWID', 'IPAD-ROWID'
          )`,
        ),
      },
      {
        name: "DELETE",
        expectedError: /AUDIT_LEGACY_FACT_IMMUTABLE/,
        run: () => connection.run(
          "DELETE FROM audit_events WHERE event_id = 'legacy-audit'",
        ),
      },
      {
        name: "legacy event_id UPDATE",
        expectedError: /AUDIT_LEGACY_FACT_IMMUTABLE/,
        run: () => connection.run(
          `UPDATE audit_events
           SET event_id = 'legacy-renamed'
           WHERE event_id = 'legacy-audit'`,
        ),
      },
      {
        name: "legacy rowid UPDATE",
        expectedError: /AUDIT_LEGACY_FACT_IMMUTABLE/,
        run: () => connection.run(
          `UPDATE audit_events
           SET rowid = ${baseline.rowId + 1_000}
           WHERE event_id = 'legacy-audit'`,
        ),
      },
      {
        name: "UPDATE OR REPLACE legacy identity takeover",
        expectedError: /AUDIT_LEGACY_FACT_IMMUTABLE/,
        run: () => connection.run(
          `UPDATE OR REPLACE audit_events
           SET event_id = 'legacy-audit'
          WHERE event_id = 'scoped-attacker'`,
        ),
      },
      {
        name: "UPDATE OR REPLACE legacy rowid takeover",
        expectedError: /AUDIT_LEGACY_FACT_IMMUTABLE/,
        run: () => connection.run(
          `UPDATE OR REPLACE audit_events
           SET rowid = ${baseline.rowId}
           WHERE event_id = 'scoped-attacker'`,
        ),
      },
      {
        name: "UPDATE OR REPLACE legacy _rowid_ takeover",
        expectedError: /AUDIT_LEGACY_FACT_IMMUTABLE/,
        run: () => connection.run(
          `UPDATE OR REPLACE audit_events
           SET _rowid_ = ${baseline.rowId}
           WHERE event_id = 'scoped-attacker'`,
        ),
      },
      {
        name: "UPDATE OR REPLACE legacy oid takeover",
        expectedError: /AUDIT_LEGACY_FACT_IMMUTABLE/,
        run: () => connection.run(
          `UPDATE OR REPLACE audit_events
           SET oid = ${baseline.rowId}
           WHERE event_id = 'scoped-attacker'`,
        ),
      },
    ];

    for (const attack of attacks) {
      await assert.rejects(
        attack.run(),
        attack.expectedError,
        attack.name,
      );
      assert.deepEqual(
        await readAuditSnapshotByRowId(connection, baseline.rowId),
        baseline,
        `${attack.name} must not mutate the legacy audit fact`,
      );
      assert.deepEqual(await repositories.auditDelivery.listReady(8), []);
      assert.deepEqual(
        { ...await connection.getFirst<{ count: number }>(
          "SELECT COUNT(*) AS count FROM audit_scope_insert_guard",
        ) },
        { count: 0 },
      );
      assert.deepEqual(
        { ...await connection.getFirst<{
          count: number;
          attemptCount: number | null;
          deliveryState: string | null;
          lastErrorCode: string | null;
        }>(
          `SELECT COUNT(*) AS count,
                  MAX(attempt_count) AS attemptCount,
                  MAX(delivery_state) AS deliveryState,
                  MAX(last_error_code) AS lastErrorCode
           FROM audit_events
           WHERE event_id = 'scoped-attacker'
             AND scope_store_code = 'S-OTHER'
             AND scope_device_code = 'IPAD-OTHER'`,
        ) },
        {
          count: 1,
          attemptCount: 1,
          deliveryState: "rejected",
          lastErrorCode: "TEST_STATE_UPDATE",
        },
      );
    }
    await connection.close();
  });
}

for (const recursiveTriggers of [false, true] as const) {
  test(`真实 SQLite：recursive_triggers ${recursiveTriggers ? "ON" : "OFF"} 时显式负 rowid 不能替换 legacy audit`, async () => {
    const { connection, baseline, repositories } =
      await createLegacyProtectionHarness(recursiveTriggers, -2);
    assert.equal(baseline.rowId, -2);

    await assert.rejects(
      connection.run(
        `INSERT OR REPLACE INTO audit_events (
          rowid, event_id, event_type, occurred_at_iso, order_guid,
          correlation_id, payload_json, uploaded_at_iso, delivery_state,
          attempt_count, next_attempt_at_iso, last_error_code,
          scope_store_code, scope_device_code
        ) VALUES (
          -2, 'negative-rowid-replacement', 'REPLACED', '${NOW}', NULL,
          'negative-rowid-correlation', '{"replacement":true}', NULL,
          'pending', 0, '${NOW}', NULL, 'S-ROWID', 'IPAD-ROWID'
        )`,
      ),
      /UNIQUE constraint failed: audit_events\.rowid/,
    );
    assert.deepEqual(
      await readAuditSnapshotByRowId(connection, baseline.rowId),
      baseline,
    );
    assert.deepEqual(await repositories.auditDelivery.listReady(8), []);
    assert.deepEqual(
      { ...await connection.getFirst<{ count: number }>(
        "SELECT COUNT(*) AS count FROM audit_scope_insert_guard",
      ) },
      { count: 0 },
    );
    await connection.close();
  });
}

for (const recursiveTriggers of [false, true] as const) {
  test(`真实 SQLite：recursive_triggers ${recursiveTriggers ? "ON" : "OFF"} 时 M29 rowid=-1 原子迁移且别名替换全部失败`, async () => {
    const connection = createConnection();
    await applyMigrations(
      connection,
      () => NOW,
      POS_DATABASE_MIGRATIONS.filter((migration) => migration.version <= 29),
    );
    await insertLocalOrder(
      connection,
      "minus-one-order",
      "S-MINUS-ONE",
      "IPAD-MINUS-ONE",
    );
    await insertLegacyAuditWithoutScope(
      connection,
      "minus-one-legacy",
      "minus-one-order",
    );
    await connection.run(
      "UPDATE audit_events SET rowid = -1 WHERE event_id = 'minus-one-legacy'",
    );
    const factsBeforeUpgrade = await readAuditFactsByEventId(
      connection,
      "minus-one-legacy",
    );

    await applyMigrations(connection, () => NOW);
    await connection.exec(
      `PRAGMA recursive_triggers = ${recursiveTriggers ? "ON" : "OFF"};`,
    );

    const upgraded = await readAuditSnapshotByEventId(
      connection,
      "minus-one-legacy",
    );
    assert.ok(upgraded.rowId > 0, "M32 must move reserved rowid=-1 to a positive unused rowid");
    assert.deepEqual(factsFromSnapshot(upgraded), factsBeforeUpgrade);
    assert.equal(upgraded.scopeStoreCode, null);
    assert.equal(upgraded.scopeDeviceCode, null);

    await insertAudit(connection, {
      eventId: "implicit-after-minus-one-rehome",
      occurredAtIso: NOW,
      nextAttemptAtIso: NOW,
      scopeStoreCode: "S-MINUS-ONE",
      scopeDeviceCode: "IPAD-MINUS-ONE",
    });
    const implicit = await readAuditSnapshotByEventId(
      connection,
      "implicit-after-minus-one-rehome",
    );
    assert.ok(implicit.rowId > 0);
    await connection.run(
      `UPDATE audit_events
       SET delivery_state = 'rejected', last_error_code = 'TEST_COMPLETE'
       WHERE event_id = 'implicit-after-minus-one-rehome'`,
    );

    const repositories = createSqliteRepositories(connection, {
      nowIso: () => NOW,
      createLeaseId: () => "unused-lease",
      auditScope: { storeCode: "S-MINUS-ONE", deviceCode: "IPAD-MINUS-ONE" },
      encryptor: {
        async encrypt(value) { return new TextEncoder().encode(value); },
        async decrypt(value) { return new TextDecoder().decode(value); },
      },
    });
    assert.deepEqual(await repositories.auditDelivery.listReady(8), []);
    assert.deepEqual(
      { ...await connection.getFirst<{ count: number }>(
        "SELECT COUNT(*) AS count FROM audit_scope_insert_guard",
      ) },
      { count: 0 },
    );

    const rowIdAliases = ["rowid", "_rowid_", "oid"] as const;
    const replacementStatements = ["INSERT OR REPLACE", "REPLACE"] as const;
    for (const replacementStatement of replacementStatements) {
      for (const rowIdAlias of rowIdAliases) {
        const attackId = `${replacementStatement.toLowerCase().replaceAll(" ", "-")}-${rowIdAlias}`;
        await assert.rejects(
          connection.run(
            `${replacementStatement} INTO audit_events (
              ${rowIdAlias}, event_id, event_type, occurred_at_iso, order_guid,
              correlation_id, payload_json, uploaded_at_iso, delivery_state,
              attempt_count, next_attempt_at_iso, last_error_code,
              scope_store_code, scope_device_code
            ) VALUES (
              -1, ?, 'REPLACED', ?, NULL, ?, '{"replacement":true}', NULL,
              'pending', 0, ?, NULL, 'S-ATTACK', 'IPAD-ATTACK'
            )`,
            [attackId, NOW, `${attackId}-correlation`, NOW],
          ),
          /AUDIT_ROWID_RESERVED/,
          `${replacementStatement} ${rowIdAlias}`,
        );
        assert.deepEqual(
          await readAuditSnapshotByEventId(connection, "minus-one-legacy"),
          upgraded,
        );
        assert.deepEqual(
          { ...await connection.getFirst<{ count: number }>(
            "SELECT COUNT(*) AS count FROM audit_events WHERE rowid = -1",
          ) },
          { count: 0 },
        );
        assert.deepEqual(
          { ...await connection.getFirst<{ count: number }>(
            "SELECT COUNT(*) AS count FROM audit_scope_insert_guard",
          ) },
          { count: 0 },
        );
      }
    }
    await connection.close();
  });
}

test("真实 SQLite：M32 rowid 重定位遇到最大整数时失败关闭并回滚整次升级", async () => {
  const connection = createConnection();
  await applyMigrations(
    connection,
    () => NOW,
    POS_DATABASE_MIGRATIONS.filter((migration) => migration.version <= 29),
  );
  await insertLocalOrder(
    connection,
    "overflow-order",
    "S-OVERFLOW",
    "IPAD-OVERFLOW",
  );
  await insertLegacyAuditWithoutScope(
    connection,
    "minus-one-overflow",
    "overflow-order",
  );
  await connection.run(
    "UPDATE audit_events SET rowid = -1 WHERE event_id = 'minus-one-overflow'",
  );
  await connection.run(
    `INSERT INTO audit_events (
      rowid, event_id, event_type, occurred_at_iso, order_guid, correlation_id,
      payload_json, uploaded_at_iso, delivery_state, attempt_count,
      next_attempt_at_iso, last_error_code
    ) VALUES (
      9223372036854775807, 'max-rowid-audit', 'OPERATION', ?,
      'overflow-order', 'max-rowid-audit', '{}', NULL,
      'pending', 0, ?, NULL
    )`,
    [NOW, NOW],
  );
  const minusOneFacts = await readAuditFactsByEventId(
    connection,
    "minus-one-overflow",
  );
  const maxFacts = await readAuditFactsByEventId(connection, "max-rowid-audit");

  await assert.rejects(
    applyMigrations(connection, () => NOW),
    /AUDIT_ROWID_REHOME_OVERFLOW/,
  );

  assert.deepEqual(
    { ...await connection.getFirst<{ version: number }>(
      "SELECT MAX(version) AS version FROM schema_migrations",
    ) },
    { version: 29 },
  );
  assert.deepEqual(
    { ...await connection.getFirst<{ count: number }>(
      "SELECT COUNT(*) AS count FROM audit_events WHERE rowid = -1",
    ) },
    { count: 1 },
  );
  assert.deepEqual(
    { ...await connection.getFirst<{ count: number }>(
      "SELECT COUNT(*) AS count FROM audit_events WHERE rowid = 9223372036854775807",
    ) },
    { count: 1 },
  );
  assert.deepEqual(
    await readAuditFactsByEventId(connection, "minus-one-overflow"),
    minusOneFacts,
  );
  assert.deepEqual(
    await readAuditFactsByEventId(connection, "max-rowid-audit"),
    maxFacts,
  );
  await connection.close();
});

test("真实 SQLite：新鲜数据库完整应用到 M40 且 guard 为空", async () => {
  const connection = createConnection();

  await applyMigrations(connection, () => NOW);

  assert.deepEqual(
    { ...await connection.getFirst<{ version: number }>(
      "SELECT MAX(version) AS version FROM schema_migrations",
    ) },
    { version: 40 },
  );
  assert.deepEqual(
    { ...await connection.getFirst<{ count: number }>(
      "SELECT COUNT(*) AS count FROM audit_scope_insert_guard",
    ) },
    { count: 0 },
  );
  await connection.close();
});
