import assert from "node:assert/strict";
import test from "node:test";

import { createSqliteRepositories } from "./sqlite-repositories";
import type { SqliteConnectionPort, SqlRunResult, SqlValue } from "./types";

class FakeConnection implements SqliteConnectionPort {
  public readonly runs: { sql: string; parameters: readonly SqlValue[] }[] = [];
  public rows: Record<string, unknown>[] = [];
  public firstRow: Record<string, unknown> | null = null;
  public firstRows: (Record<string, unknown> | null)[] = [];
  public nextChanges = 1;
  public async exec(): Promise<void> {}
  public async run(sql: string, parameters: readonly SqlValue[] = []): Promise<SqlRunResult> { this.runs.push({ sql, parameters }); return { changes: this.nextChanges, lastInsertRowId: 0 }; }
  public async getFirst<T extends object>(): Promise<T | null> {
    return (this.firstRows.length > 0 ? this.firstRows.shift() : this.firstRow) as T | null;
  }
  public async getAll<T extends object>(): Promise<readonly T[]> { return this.rows as T[]; }
  public async withExclusiveTransaction<T>(operation: (transaction: SqliteConnectionPort) => Promise<T>): Promise<T> { return operation(this); }
  public async close(): Promise<void> {}
}

test("outbox 租约含 lease_id；完成动作必须校验当前租约", async () => {
  const db = new FakeConnection();
  db.rows = [{ message_id: "m1", aggregate_id: "order1", kind: "order-sync", state: "pending", payload_json: "{}", attempt_count: 2 }];
  const repos = createSqliteRepositories(db, { nowIso: () => "2026-07-28T00:00:00.000Z", createLeaseId: () => "lease-1", encryptor: { async encrypt(value) { return new TextEncoder().encode(value); }, async decrypt(value) { return new TextDecoder().decode(value); } } });
  const [leased] = await repos.outbox.leaseReady(10, 60);
  assert.equal(leased?.leaseId, "lease-1");
  assert.ok(db.runs.some(entry => entry.sql.includes("state='leased'")));
  db.firstRow = { aggregate_id: "order1", kind: "order-sync", state: "leased", lease_id: "lease-1" };
  await repos.outbox.markSucceeded(leased!);
  assert.ok(db.runs.at(-1)?.sql.includes("state='leased' AND lease_id=?"));
  db.firstRow = { aggregate_id: "order1", kind: "order-sync", state: "leased", lease_id: "replacement-owner" };
  await assert.rejects(() => repos.outbox.markRejected(leased!, "REJECTED"), /no longer owned/);
});

test("held cart 和 voucher reservation token 只经 encryptor 写入密文列", async () => {
  const db = new FakeConnection();
  db.firstRows = [{ state: "Draft" }, null];
  const plaintext: string[] = [];
  const repos = createSqliteRepositories(db, { nowIso: () => "2026-07-28T00:00:00.000Z", createLeaseId: () => "lease", encryptor: { async encrypt(value) { plaintext.push(value); return new TextEncoder().encode(value); }, async decrypt(value) { return new TextDecoder().decode(value); } } });
  await repos.heldOrders.hold("hold", { revision: 1, mode: "sale", lines: [], subtotal: { currency: "AUD", cents: 0 }, discount: { currency: "AUD", cents: 0 }, actualAmount: { currency: "AUD", cents: 0 } }, 1);
  await repos.payments.insertIfUnblocked({ attemptId: "p1", idempotencyKey: "i1", orderGuid: "o1", provider: "voucher", operation: "purchase", amount: { currency: "AUD", cents: 10 }, state: "Created", references: { checkoutId: null, paymentId: null, sessionId: null, txnRef: null, rfn: null, voucherReservationToken: "secret-voucher" }, createdAtIso: "2026-07-28T00:00:00.000Z", updatedAtIso: "2026-07-28T00:00:00.000Z", lastErrorCode: null });
  assert.equal(plaintext.length, 2);
  assert.ok(db.runs.some(entry => entry.sql.includes("cart_ciphertext")));
  assert.ok(db.runs.some(entry => entry.sql.includes("provider_payload_ciphertext")));
});

test("普通订单仓储只接受并原子写入冻结同步来源", async () => {
  const db = new FakeConnection();
  const repos = createSqliteRepositories(db, {
    nowIso: () => "2026-07-28T00:00:00.000Z",
    createLeaseId: () => "lease",
    encryptor: {
      async encrypt(value) {
        return new TextEncoder().encode(value);
      },
      async decrypt(value) {
        return new TextDecoder().decode(value);
      },
    },
  });
  const order = {
    orderGuid: "order-1",
    localSequence: 1,
    storeCode: "S1",
    deviceCode: "D1",
    cashierId: "C1",
    cashierName: "Cashier",
    soldAtIso: "2026-07-28T00:00:00.000Z",
    state: "Draft" as const,
    total: { currency: "AUD" as const, cents: 500 },
    discount: { currency: "AUD" as const, cents: 0 },
    actualAmount: { currency: "AUD" as const, cents: 500 },
    originalOrderGuid: null,
    lines: [
      {
        lineId: "line-1",
        productCode: "P1",
        itemNumber: null,
        lookupCode: "123",
        displayName: "Item",
        quantity: "1",
        unitPrice: { currency: "AUD" as const, cents: 500 },
        discount: { currency: "AUD" as const, cents: 0 },
        actualAmount: { currency: "AUD" as const, cents: 500 },
        priceSource: "catalog" as const,
        syncProvenance: {
          referenceCode: null,
          priceSource: 4 as const,
        },
        kind: "sale" as const,
        returnSourceKey: null,
        originalOrderGuid: null,
        originalOrderDetailGuid: null,
      },
    ],
    tenders: [],
  };

  await repos.orders.saveDraft(order);
  const lineInsert = db.runs.find((entry) =>
    entry.sql.includes("INSERT INTO local_order_lines"));
  assert.match(lineInsert?.sql ?? "", /reference_code/);
  assert.match(lineInsert?.sql ?? "", /sync_price_source/);
  assert.equal(lineInsert?.parameters.at(-2), null);
  assert.equal(lineInsert?.parameters.at(-1), 4);

  db.runs.length = 0;
  const { syncProvenance: _syncProvenance, ...legacyLine } =
    order.lines[0]!;
  await assert.rejects(
    () =>
      repos.orders.saveDraft({
        ...order,
        orderGuid: "order-legacy",
        lines: [legacyLine],
      }),
    /line sync provenance/i,
  );
  assert.equal(db.runs.length, 0);
});
