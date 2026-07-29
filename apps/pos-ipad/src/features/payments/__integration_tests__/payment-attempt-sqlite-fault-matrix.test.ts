import assert from "node:assert/strict";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { DatabaseSync, type SQLInputValue } from "node:sqlite";
import test from "node:test";

import type { PaymentAttempt, PaymentProvider, PaymentProviderReferences, PaymentProviderResult } from "@/core/contracts";
import { POS_DATABASE_MIGRATIONS } from "@/core/db/migrations";
import { SqlitePaymentActionBindingStore } from "@/core/db/sqlite-payment-action-binding-store";
import { createSqliteRepositories } from "@/core/db/sqlite-repositories";
import type { SqliteConnectionPort, SqlRunResult, SqlValue } from "@/core/db/types";
import {
  PaymentAttemptBlockedError,
  PaymentAttemptOfflineError,
  PaymentAttemptService,
} from "@/features/payments/payment-attempt-service";

const T0 = "2026-07-28T00:00:00.000Z";
const references = (): PaymentProviderReferences => ({
  checkoutId: null,
  paymentId: null,
  sessionId: null,
  txnRef: null,
  rfn: null,
  voucherReservationToken: null,
});

test("fault matrix: known offline binds the action but never creates an attempt or crosses a provider boundary", async () => {
  await withSqlite(async (connection) => {
    const harness = await createHarness(connection, false);

    await assert.rejects(() => harness.service.startAttempt(input()), PaymentAttemptOfflineError);

    assert.equal(await scalar(connection, "SELECT COUNT(*) AS count FROM payment_attempts"), 0);
    assert.equal(await scalar(connection, "SELECT COUNT(*) AS count FROM payment_action_bindings"), 1);
    assert.equal(harness.square.submitCalls, 0);
  });
});

test("fault matrix: response loss persists Unknown; replay/cold recovery retain the same action identity and prohibit switching provider", async () => {
  await withSqlite(async (connection) => {
    const first = await createHarness(connection, true);
    first.square.submitImpl = async () => {
      const error = new Error("timeout after provider accepted request") as Error & { code: string };
      error.code = "NETWORK_RESPONSE_LOST";
      throw error;
    };

    const unknown = await first.service.startAttempt(input());
    assert.equal(unknown.attempt.state, "Unknown");
    assert.equal(unknown.attempt.lastErrorCode, "NETWORK_RESPONSE_LOST");
    assert.equal(first.square.submitCalls, 1);

    await assert.rejects(
      () => first.service.startAttempt({ ...input(), actionId: "switch-provider", provider: "linkly-cloud" }),
      PaymentAttemptBlockedError,
    );
    assert.equal(first.linkly.submitCalls, 0);

    const restarted = await createHarness(connection, true);
    restarted.square.recoverImpl = async (attempt) => {
      assert.equal(attempt.attemptId, unknown.attempt.attemptId);
      assert.equal(attempt.idempotencyKey, unknown.attempt.idempotencyKey);
      return approved({ checkoutId: "checkout-recovered", paymentId: "payment-recovered" });
    };
    const recovered = await restarted.service.startAttempt(input());

    assert.equal(recovered.attempt.state, "Approved");
    assert.equal(recovered.attempt.attemptId, unknown.attempt.attemptId);
    assert.equal(recovered.attempt.orderGuid, "order-payment-matrix");
    assert.equal(restarted.square.submitCalls, 0);
    assert.equal(restarted.square.recoverCalls, 1);
  });
});

test("fault matrix: Submitted, Pending, and Unknown cold recovery query the original provider only", async () => {
  for (const state of ["Submitted", "Pending", "Unknown"] as const) {
    await withSqlite(async (connection) => {
      const initial = await createHarness(connection, true);
      const created = await initial.repositories.payments.insertIfUnblocked(attempt({ state: "Created" }));
      assert.equal(created, null);
      const expected = await initial.repositories.payments.get("attempt-seeded");
      assert.ok(expected);
      assert.equal(
        await initial.repositories.payments.compareAndUpdate(
          expected,
          { ...expected, state, updatedAtIso: "2026-07-28T00:00:01.000Z" },
        ),
        true,
      );

      const restarted = await createHarness(connection, true);
      restarted.square.recoverImpl = async (value) => {
        assert.equal(value.state, state);
        return approved({ paymentId: `payment-${state.toLowerCase()}` });
      };
      const recovered = await restarted.service.recoverAttempt("attempt-seeded");

      assert.equal(recovered.attempt.state, "Approved");
      assert.equal(restarted.square.submitCalls, 0);
      assert.equal(restarted.square.recoverCalls, 1);
      assert.equal(restarted.linkly.submitCalls, 0);
      assert.equal(restarted.linkly.recoverCalls, 0);
    });
  }
});

test("fault matrix: Approved local-completion crash keeps its OrderGuid blocked and duplicate action replay is side-effect free", async () => {
  await withSqlite(async (connection) => {
    const first = await createHarness(connection, true);
    first.square.submitImpl = async () => approved({ paymentId: "payment-approved" });
    const approvedAttempt = await first.service.startAttempt(input());

    const restarted = await createHarness(connection, true);
    const replay = await restarted.service.startAttempt(input());
    assert.equal(replay.attempt.attemptId, approvedAttempt.attempt.attemptId);
    assert.equal(restarted.square.submitCalls, 0);
    assert.equal(restarted.square.recoverCalls, 0);
    await assert.rejects(
      () => restarted.service.startAttempt({ ...input(), actionId: "new-action-after-approved" }),
      PaymentAttemptBlockedError,
    );
  });
});

async function createHarness(connection: NodeSqliteConnection, online: boolean) {
  await insertDraftOrder(connection, "order-payment-matrix");
  const repositories = createSqliteRepositories(connection, {
    nowIso: () => T0,
    createLeaseId: () => "lease-payment-matrix",
    encryptor: {
      async encrypt(value) { return new TextEncoder().encode(value); },
      async decrypt(value) { return new TextDecoder().decode(value); },
    },
  });
  const square = new FakeProvider("square");
  const linkly = new FakeProvider("linkly-cloud");
  let id = 0;
  const service = new PaymentAttemptService({
    ledger: repositories.payments,
    actionBindings: new SqlitePaymentActionBindingStore(connection),
    drafts: {
      async assertPersisted(orderGuid) {
        assert.ok(await repositories.orders.getByGuid(orderGuid), "payment must retain its local order draft");
      },
    },
    providers: {
      get(provider) {
        return provider === "square" ? square : linkly;
      },
    },
    connectivity: { isOnline: async () => online },
    createAttemptId: () => `attempt-${++id === 1 ? "seeded" : id}`,
    createIdempotencyKey: () => `idempotency-${id}`,
    nowIso: () => T0,
  });
  return { service, repositories, square, linkly };
}

function input() {
  return {
    actionId: "payment-action-1",
    orderGuid: "order-payment-matrix",
    provider: "square" as const,
    operation: "purchase" as const,
    amount: { currency: "AUD" as const, cents: 500 },
  };
}

function attempt(overrides: Partial<PaymentAttempt>): PaymentAttempt {
  return {
    attemptId: "attempt-seeded",
    idempotencyKey: "idempotency-seeded",
    orderGuid: "order-payment-matrix",
    provider: "square",
    operation: "purchase",
    amount: { currency: "AUD", cents: 500 },
    state: "Created",
    references: references(),
    createdAtIso: T0,
    updatedAtIso: T0,
    lastErrorCode: null,
    ...overrides,
  };
}

function approved(overrides: Partial<PaymentProviderReferences> = {}): PaymentProviderResult {
  return { state: "Approved", references: { ...references(), ...overrides }, receiptText: null, responseCode: "APPROVED" };
}

class FakeProvider {
  public submitCalls = 0;
  public recoverCalls = 0;
  public submitImpl: (attempt: PaymentAttempt) => Promise<PaymentProviderResult> = async () => approved();
  public recoverImpl: (attempt: PaymentAttempt) => Promise<PaymentProviderResult> = async () => approved();
  public constructor(public readonly provider: PaymentProvider) {}
  public async submit(attempt: PaymentAttempt): Promise<PaymentProviderResult> { this.submitCalls += 1; return this.submitImpl(attempt); }
  public async recover(attempt: PaymentAttempt): Promise<PaymentProviderResult> { this.recoverCalls += 1; return this.recoverImpl(attempt); }
  public async cancel(): Promise<PaymentProviderResult> { return { state: "Cancelled", references: references(), receiptText: null, responseCode: null }; }
  public async refund(): Promise<PaymentProviderResult> { return approved(); }
}

class NodeSqliteConnection implements SqliteConnectionPort {
  private readonly database: DatabaseSync;
  public constructor(path: string) { this.database = new DatabaseSync(path); this.database.exec("PRAGMA foreign_keys = ON"); }
  public async exec(sql: string): Promise<void> { this.database.exec(sql); }
  public async run(sql: string, parameters: readonly SqlValue[] = []): Promise<SqlRunResult> {
    const result = this.database.prepare(sql).run(...parameters as readonly SQLInputValue[]);
    return { changes: Number(result.changes), lastInsertRowId: Number(result.lastInsertRowid) };
  }
  public async getFirst<T extends object>(sql: string, parameters: readonly SqlValue[] = []): Promise<T | null> {
    const row = this.database.prepare(sql).get(...parameters as readonly SQLInputValue[]);
    return row === undefined ? null : row as T;
  }
  public async getAll<T extends object>(sql: string, parameters: readonly SqlValue[] = []): Promise<readonly T[]> {
    return this.database.prepare(sql).all(...parameters as readonly SQLInputValue[]) as unknown as readonly T[];
  }
  public async withExclusiveTransaction<T>(operation: (transaction: SqliteConnectionPort) => Promise<T>): Promise<T> {
    this.database.exec("BEGIN IMMEDIATE");
    try { const result = await operation(this); this.database.exec("COMMIT"); return result; }
    catch (error) { this.database.exec("ROLLBACK"); throw error; }
  }
  public async close(): Promise<void> { this.database.close(); }
}

async function withSqlite(operation: (connection: NodeSqliteConnection) => Promise<void>): Promise<void> {
  const folder = mkdtempSync(join(tmpdir(), "hb-pos-ipad-payment-matrix-"));
  const connection = new NodeSqliteConnection(join(folder, "pos.db"));
  try {
    await connection.exec(POS_DATABASE_MIGRATIONS.map((migration) => migration.sql).join("\n"));
    await operation(connection);
  } finally {
    await connection.close();
    rmSync(folder, { recursive: true, force: true });
  }
}

async function insertDraftOrder(connection: NodeSqliteConnection, orderGuid: string): Promise<void> {
  const current = await scalar(connection, "SELECT COUNT(*) AS count FROM local_orders WHERE order_guid = ?", [orderGuid]);
  if (current > 0) return;
  await connection.run(
    "INSERT INTO local_orders (order_guid, local_sequence, store_code, device_code, cashier_id, cashier_name, sold_at_iso, state, total_cents, discount_cents, actual_amount_cents, original_order_guid, created_at_iso, updated_at_iso) VALUES (?, 1, 'S1', 'IPAD1', 'cashier-1', 'Cashier', ?, 'Draft', 500, 0, 500, NULL, ?, ?)",
    [orderGuid, T0, T0, T0],
  );
}

async function scalar(connection: NodeSqliteConnection, sql: string, parameters: readonly SqlValue[] = []): Promise<number> {
  const row = await connection.getFirst<{ count: unknown }>(sql, parameters);
  return Number(row?.count ?? 0);
}
