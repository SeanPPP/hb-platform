import assert from "node:assert/strict";
import { DatabaseSync, type SQLInputValue } from "node:sqlite";
import test from "node:test";

import { POS_DATABASE_MIGRATIONS } from "./migrations";
import { createSqliteRepositories } from "./sqlite-repositories";
import type {
  SqliteConnectionPort,
  SqlRunResult,
  SqlValue,
} from "@hb/pos-db/core/db/types";

import type { LocalOrderState } from "@/core/contracts";

const nowIso = "2026-07-28T06:00:00.000Z";

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

test("真实 SQLite：order-sync 租约与订单状态按成功、重试、403、拒绝原子推进，audit-batch 不动订单", async () => {
  const connection = await openConnection();
  try {
    const cases = [
      ["succeeded", "CompletedLocal"],
      ["retry", "PendingSync"],
      ["blocked", "PendingSync"],
      ["rejected", "PendingSync"],
      ["expired", "Syncing"],
      ["audit", "PendingSync"],
    ] as const;
    for (const [index, [suffix, state]] of cases.entries()) {
      await seedOrder(connection, `order-${suffix}`, index + 1, state);
    }
    await seedOutbox(connection, "message-succeeded", "order-succeeded", "order-sync");
    await seedOutbox(connection, "message-retry", "order-retry", "order-sync");
    await seedOutbox(connection, "message-blocked", "order-blocked", "order-sync");
    await seedOutbox(connection, "message-rejected", "order-rejected", "order-sync");
    await seedOutbox(connection, "message-audit", "order-audit", "audit-batch");
    await seedOutbox(connection, "message-expired", "order-expired", "order-sync", {
      state: "leased",
      leaseId: "expired-owner",
      leaseExpiresAtIso: "2026-07-28T05:59:59.000Z",
      attemptCount: 1,
    });

    let leaseSequence = 0;
    const repositories = createSqliteRepositories(connection, {
      nowIso: () => nowIso,
      createLeaseId: () => `lease-${++leaseSequence}`,
      encryptor: {
        async encrypt(value) { return new TextEncoder().encode(value); },
        async decrypt(value) { return new TextDecoder().decode(value); },
      },
    });

    const leased = await repositories.outbox.leaseReady(10, 60);
    const leaseByMessage = new Map(leased.map((lease) => [lease.messageId, lease]));
    assert.equal(leased.length, 6);
    assert.equal(await readOrderState(connection, "order-succeeded"), "Syncing");
    assert.equal(await readOrderState(connection, "order-retry"), "Syncing");
    assert.equal(await readOrderState(connection, "order-blocked"), "Syncing");
    assert.equal(await readOrderState(connection, "order-rejected"), "Syncing");
    assert.equal(await readOrderState(connection, "order-expired"), "Syncing");
    assert.equal(await readOrderState(connection, "order-audit"), "PendingSync");
    assert.equal(leaseByMessage.get("message-expired")?.attemptCount, 2);

    await repositories.outbox.markSucceeded(requiredLease(leaseByMessage, "message-succeeded"));
    await repositories.outbox.releaseRetry(
      requiredLease(leaseByMessage, "message-retry"),
      "2026-07-28T06:01:00.000Z",
      "SYNC_NETWORK",
    );
    await repositories.outbox.markBlocked403(
      requiredLease(leaseByMessage, "message-blocked"),
      "DEVICE_DISABLED",
    );
    await repositories.outbox.markRejected(
      requiredLease(leaseByMessage, "message-rejected"),
      "ORDER_REJECTED",
    );
    // AlreadySynced 也走相同的 markSucceeded，本地必须收敛到 Synced。
    await repositories.outbox.markSucceeded(requiredLease(leaseByMessage, "message-expired"));
    await repositories.outbox.markSucceeded(requiredLease(leaseByMessage, "message-audit"));

    assert.equal(await readOrderState(connection, "order-succeeded"), "Synced");
    assert.equal(await readOrderState(connection, "order-retry"), "PendingSync");
    assert.equal(await readOrderState(connection, "order-blocked"), "Blocked403");
    assert.equal(await readOrderState(connection, "order-rejected"), "Rejected");
    assert.equal(await readOrderState(connection, "order-expired"), "Synced");
    assert.equal(await readOrderState(connection, "order-audit"), "PendingSync");
    const outboxStates = await connection.getAll<{ message_id: string; state: string }>(
      "SELECT message_id, state FROM outbox_messages ORDER BY message_id",
    );
    assert.deepEqual(
      outboxStates.map((row) => ({ ...row })),
      [
        { message_id: "message-audit", state: "succeeded" },
        { message_id: "message-blocked", state: "blocked403" },
        { message_id: "message-expired", state: "succeeded" },
        { message_id: "message-rejected", state: "rejected" },
        { message_id: "message-retry", state: "pending" },
        { message_id: "message-succeeded", state: "succeeded" },
      ],
    );
  } finally {
    await connection.close();
  }
});

test("真实 SQLite：order-sync 租约遇到非法订单状态时整体回滚", async () => {
  const connection = await openConnection();
  try {
    await seedOrder(connection, "order-invalid", 1, "Rejected");
    await seedOutbox(connection, "message-invalid", "order-invalid", "order-sync");
    const repositories = repositoriesFor(connection);

    await assert.rejects(
      () => repositories.outbox.leaseReady(10, 60),
      /cannot enter Syncing|invalid order state/i,
    );
    assert.equal(await readOrderState(connection, "order-invalid"), "Rejected");
    assert.deepEqual({ ...await readOutboxLeaseState(connection, "message-invalid") }, {
      state: "pending",
      lease_id: null,
      attempt_count: 0,
    });
  } finally {
    await connection.close();
  }
});

test("真实 SQLite：完成时订单 CAS 失败或租约失效不会留下 outbox/order 分裂", async () => {
  const connection = await openConnection();
  try {
    await seedOrder(connection, "order-order-cas", 1, "PendingSync");
    await seedOutbox(connection, "message-order-cas", "order-order-cas", "order-sync");
    await seedOrder(connection, "order-lease-cas", 2, "PendingSync");
    await seedOutbox(connection, "message-lease-cas", "order-lease-cas", "order-sync");
    const repositories = repositoriesFor(connection);
    const leased = await repositories.outbox.leaseReady(10, 60);
    const orderCasLease = leased.find((lease) => lease.messageId === "message-order-cas");
    const leaseCasLease = leased.find((lease) => lease.messageId === "message-lease-cas");
    assert.ok(orderCasLease);
    assert.ok(leaseCasLease);

    await connection.run(
      "UPDATE local_orders SET state = 'PendingSync' WHERE order_guid = ?",
      ["order-order-cas"],
    );
    await assert.rejects(
      () => repositories.outbox.markSucceeded(orderCasLease),
      /cannot leave Syncing|invalid order state/i,
    );
    assert.equal(await readOrderState(connection, "order-order-cas"), "PendingSync");
    assert.equal(
      (await readOutboxLeaseState(connection, "message-order-cas")).state,
      "leased",
    );

    await connection.run(
      "UPDATE outbox_messages SET lease_id = 'replacement-owner' WHERE message_id = ?",
      ["message-lease-cas"],
    );
    await assert.rejects(
      () => repositories.outbox.markSucceeded(leaseCasLease),
      /no longer owned/i,
    );
    assert.equal(await readOrderState(connection, "order-lease-cas"), "Syncing");
    assert.deepEqual({ ...await readOutboxLeaseState(connection, "message-lease-cas") }, {
      state: "leased",
      lease_id: "replacement-owner",
      attempt_count: 1,
    });
  } finally {
    await connection.close();
  }
});

test("真实 SQLite：下一可租时间同时覆盖 pending 重试与崩溃租约，忽略终态消息", async () => {
  const connection = await openConnection();
  try {
    await seedOutbox(
      connection,
      "message-pending",
      "order-pending",
      "audit-batch",
      { nextAttemptAtIso: "2026-07-28T06:02:00.000Z" },
    );
    await seedOutbox(
      connection,
      "message-leased",
      "order-leased",
      "audit-batch",
      {
        state: "leased",
        leaseId: "crashed-owner",
        leaseExpiresAtIso: "2026-07-28T06:01:00.000Z",
        nextAttemptAtIso: "2026-07-28T05:59:00.000Z",
      },
    );
    await seedOutbox(
      connection,
      "message-terminal",
      "order-terminal",
      "audit-batch",
    );
    await connection.run(
      "UPDATE outbox_messages SET state = 'rejected' WHERE message_id = ?",
      ["message-terminal"],
    );
    const repositories = repositoriesFor(connection);

    assert.equal(
      await repositories.outbox.nextReadyAtIso?.(),
      "2026-07-28T06:01:00.000Z",
    );
  } finally {
    await connection.close();
  }
});

test("设备重注册后，旧订单与非订单员工审计保持 pending 且不进入新作用域批次", async () => {
  const connection = await openConnection();
  try {
    await seedOrder(connection, "order-old-terminal", 1, "PendingSync");
    const oldScope = { storeCode: "S1", deviceCode: "IPAD1" } as const;
    const newScope = { storeCode: "S1", deviceCode: "IPAD2" } as const;
    const oldRepositories = repositoriesForScope(connection, oldScope);
    await oldRepositories.audit.append([
      {
        eventId: "audit-old-non-order",
        eventType: "CASHIER_LOGIN",
        occurredAtIso: nowIso,
        orderGuid: null,
        correlationId: "audit-old-non-order",
        payload: {},
      },
      {
        eventId: "audit-old-order",
        eventType: "SALE_COMPLETE",
        occurredAtIso: nowIso,
        orderGuid: "order-old-terminal",
        correlationId: "audit-old-order",
        payload: {},
      },
    ]);
    // M30 之前的行没有可信 scope；即使时间已到也必须保留 pending 而非猜归属。
    await connection.run(
      `INSERT INTO audit_events (
        event_id, event_type, occurred_at_iso, order_guid, correlation_id,
        payload_json, uploaded_at_iso, delivery_state, attempt_count,
        next_attempt_at_iso, last_error_code
      ) VALUES (?, 'CASHIER_LOGIN', ?, NULL, ?, '{}', NULL, 'pending', 0, ?, NULL)`,
      ["audit-legacy-unscoped", nowIso, "audit-legacy-unscoped", nowIso],
    );

    const newRepositories = repositoriesForScope(connection, newScope);
    assert.deepEqual(await newRepositories.auditDelivery.listReady(8), []);
    // 旧 scope 的 pending 不得被当前 runtime 视为下一次 ready，避免 0ms timer 自旋。
    assert.equal(await newRepositories.auditDelivery.nextReadyAtIso(), null);
    assert.deepEqual(
      (await oldRepositories.auditDelivery.listReady(8)).map((event) => event.eventId),
      ["audit-old-non-order", "audit-old-order"],
    );
    const states = await connection.getAll<{
      event_id: string;
      delivery_state: string;
      scope_store_code: string | null;
      scope_device_code: string | null;
    }>(
      `SELECT event_id, delivery_state, scope_store_code, scope_device_code
       FROM audit_events
       WHERE scope_store_code IS NOT NULL
       ORDER BY event_id`,
    );
    assert.deepEqual(states.map((state) => ({ ...state })), [
      {
        event_id: "audit-old-non-order",
        delivery_state: "pending",
        scope_store_code: oldScope.storeCode,
        scope_device_code: oldScope.deviceCode,
      },
      {
        event_id: "audit-old-order",
        delivery_state: "pending",
        scope_store_code: oldScope.storeCode,
        scope_device_code: oldScope.deviceCode,
      },
    ]);
    const legacy = await connection.getFirst<{
      delivery_state: string;
      scope_store_code: string | null;
      scope_device_code: string | null;
    }>(
      `SELECT delivery_state, scope_store_code, scope_device_code
       FROM audit_events
       WHERE event_id = 'audit-legacy-unscoped'`,
    );
    assert.deepEqual({ ...legacy }, {
      delivery_state: "pending",
      scope_store_code: null,
      scope_device_code: null,
    });
  } finally {
    await connection.close();
  }
});

async function openConnection(): Promise<NodeSqliteConnection> {
  const connection = new NodeSqliteConnection(
    new DatabaseSync(":memory:", { enableForeignKeyConstraints: true }),
  );
  await connection.exec(POS_DATABASE_MIGRATIONS.map((migration) => migration.sql).join("\n"));
  return connection;
}

function repositoriesFor(connection: SqliteConnectionPort) {
  return createSqliteRepositories(connection, {
    nowIso: () => nowIso,
    createLeaseId: () => "lease-test",
    encryptor: {
      async encrypt(value) { return new TextEncoder().encode(value); },
      async decrypt(value) { return new TextDecoder().decode(value); },
    },
  });
}

function repositoriesForScope(
  connection: SqliteConnectionPort,
  auditScope: Readonly<{ storeCode: string; deviceCode: string }>,
) {
  return createSqliteRepositories(connection, {
    nowIso: () => nowIso,
    createLeaseId: () => "lease-audit-scope",
    encryptor: {
      async encrypt(value) { return new TextEncoder().encode(value); },
      async decrypt(value) { return new TextDecoder().decode(value); },
    },
    auditScope,
  });
}

async function seedOrder(
  connection: SqliteConnectionPort,
  orderGuid: string,
  localSequence: number,
  state: LocalOrderState,
): Promise<void> {
  await connection.run(
    `INSERT INTO local_orders (
      order_guid, local_sequence, store_code, device_code, cashier_id, cashier_name,
      sold_at_iso, state, total_cents, discount_cents, actual_amount_cents,
      original_order_guid, created_at_iso, updated_at_iso
    ) VALUES (?, ?, 'S1', 'IPAD1', 'cashier-1', 'Cashier', ?, ?, 500, 0, 500, NULL, ?, ?)`,
    [
      orderGuid,
      localSequence,
      "2026-07-28T05:00:00.000Z",
      state,
      "2026-07-28T05:00:00.000Z",
      "2026-07-28T05:00:00.000Z",
    ],
  );
}

async function seedOutbox(
  connection: SqliteConnectionPort,
  messageId: string,
  aggregateId: string,
  kind: "order-sync" | "audit-batch",
  overrides: Readonly<{
    state?: "pending" | "leased";
    leaseId?: string | null;
    leaseExpiresAtIso?: string | null;
    attemptCount?: number;
    nextAttemptAtIso?: string;
  }> = {},
): Promise<void> {
  await connection.run(
    `INSERT INTO outbox_messages (
      message_id, aggregate_id, kind, payload_json, state, attempt_count,
      next_attempt_at_iso, lease_id, lease_expires_at_iso, last_error_code,
      created_at_iso, updated_at_iso
    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, NULL, ?, ?)`,
    [
      messageId,
      aggregateId,
      kind,
      JSON.stringify({ orderGuid: aggregateId }),
      overrides.state ?? "pending",
      overrides.attemptCount ?? 0,
      overrides.nextAttemptAtIso ?? "2026-07-28T05:00:00.000Z",
      overrides.leaseId ?? null,
      overrides.leaseExpiresAtIso ?? null,
      `2026-07-28T05:${String(overrides.attemptCount ?? 0).padStart(2, "0")}:00.000Z`,
      "2026-07-28T05:00:00.000Z",
    ],
  );
}

async function readOrderState(
  connection: SqliteConnectionPort,
  orderGuid: string,
): Promise<string | null> {
  const row = await connection.getFirst<{ state: string }>(
    "SELECT state FROM local_orders WHERE order_guid = ?",
    [orderGuid],
  );
  return row?.state ?? null;
}

async function readOutboxLeaseState(
  connection: SqliteConnectionPort,
  messageId: string,
): Promise<Readonly<{ state: string; lease_id: string | null; attempt_count: number }>> {
  const row = await connection.getFirst<{
    state: string;
    lease_id: string | null;
    attempt_count: number;
  }>(
    "SELECT state, lease_id, attempt_count FROM outbox_messages WHERE message_id = ?",
    [messageId],
  );
  if (!row) throw new Error(`Missing outbox row: ${messageId}`);
  return row;
}

function requiredLease(
  leases: ReadonlyMap<string, Awaited<ReturnType<ReturnType<typeof repositoriesFor>["outbox"]["leaseReady"]>>[number]>,
  messageId: string,
) {
  const lease = leases.get(messageId);
  if (!lease) throw new Error(`Missing lease: ${messageId}`);
  return lease;
}
