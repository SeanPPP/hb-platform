import assert from "node:assert/strict";
import { DatabaseSync, type SQLInputValue } from "node:sqlite";
import test from "node:test";

import { applyMigrations } from "./migrations";
import { SqliteReturnCapacityVault } from "./sqlite-return-capacity-vault";
import { SqliteReturnExecutionLedger } from "./sqlite-return-execution-ledger";

const now = "2026-09-20T00:00:00.000Z";
const original = "11111111-1111-4111-8111-111111111111";
const detail = "22222222-2222-4222-8222-222222222222";

class TestConnection {
  private readonly db = new DatabaseSync(":memory:");
  public constructor() { this.db.exec("PRAGMA foreign_keys = ON"); }
  public async exec(sql: string): Promise<void> { this.db.exec(sql); }
  public async run(sql: string, params: readonly unknown[] = []) {
    const result = this.db.prepare(sql).run(...params as SQLInputValue[]);
    return { changes: Number(result.changes), lastInsertRowId: Number(result.lastInsertRowid) };
  }
  public async getFirst<T extends object>(sql: string, params: readonly unknown[] = []): Promise<T | null> {
    return this.db.prepare(sql).get(...params as SQLInputValue[]) as T | undefined ?? null;
  }
  public async getAll<T extends object>(sql: string, params: readonly unknown[] = []): Promise<readonly T[]> {
    return this.db.prepare(sql).all(...params as SQLInputValue[]) as T[];
  }
  public async withExclusiveTransaction<T>(operation: (connection: TestConnection) => Promise<T>): Promise<T> {
    this.db.exec("BEGIN IMMEDIATE");
    try {
      const result = await operation(this);
      this.db.exec("COMMIT");
      return result;
    } catch (error) {
      this.db.exec("ROLLBACK");
      throw error;
    }
  }
}

function draft(index: number, returnSourceKey: string, availableQuantity = 2) {
  const planLine = {
    sourceKind: "receipt", returnSourceKey, originalOrderGuid: original,
    originalOrderDetailGuid: detail, productCode: "P1", quantity: 1,
    signedAmountCents: -500,
    syncProvenance: { referenceCode: null, priceSource: 0 },
  };
  const allocation = {
    method: "cash", signedAmountCents: -500,
    originalCapacityId: `capacity-${index}`, originalOrderGuid: original,
    offlineCashProof: {
      evidenceId: `proof-${index}`, capacityId: `capacity-${index}`,
      originalOrderGuid: original, remainingCents: 500,
    },
  };
  return {
    actionId: `action-${index}`, requestFingerprint: `fingerprint-${index}`,
    returnOrderGuid: `return-order-${index}`, actionRecoveryToken: `recovery-${index}`,
    identity: {
      storeCode: "S01", deviceCode: `POS-${index}`, cashierId: "cashier",
      cashierName: "Cashier", userGuid: "user-1", sessionEpoch: "epoch-1",
    },
    plan: { sourceKind: "receipt", totalRefundCents: 500, lines: [planLine], allocations: [allocation], online: false },
    supervisorGrantKey: null, createdAtIso: now,
    lines: [{
      lineId: `line-${index}`, selectionKey: `selection-${index}`, ...planLine,
      itemNumber: "I1", lookupCode: "P1", displayName: "Product",
      unitRefundCents: 500, availableQuantity, remainingAmountCents: availableQuantity * 500,
    }],
    allocations: [{
      allocationId: `allocation-${index}`, index: 0, executionKind: "offline-cash",
      ...allocation, capacityId: `capacity-${index}`, externalAttemptId: null,
      externalAttemptKind: null, externalActionId: null, durableAttemptId: null,
      status: "created", protectedRecoveryKey: null,
    }],
  };
}

test("历史双来源键共用已完成额度和处理中预留", async () => {
  const db = new TestConnection();
  await applyMigrations(db as never, () => now);
  const ledger = new SqliteReturnExecutionLedger(
    db as never,
    {
      encrypt: async (value: string) => new TextEncoder().encode(value),
      decrypt: async (value: Uint8Array) => new TextDecoder().decode(value),
    },
    { createTenderGuid: () => "tender", createAuditEventId: () => "audit" },
    () => now,
  );
  for (let index = 1; index <= 3; index += 1) {
    await db.run(
      `INSERT INTO return_tender_capacities
       (capacity_id, original_order_guid, method, original_amount_cents,
        remaining_amount_cents, observed_at_iso, created_at_iso, updated_at_iso)
       VALUES (?, ?, 'cash', 500, 500, ?, ?, ?)`,
      [`capacity-${index}`, original, now, now, now],
    );
  }
  const localKey = `local-receipt:${original}:${detail}`;
  const remoteKey = `receipt:${original}:${detail}`;
  await ledger.prepareOrLoad(draft(1, localKey) as never);
  await markCompleted(db, 1, localKey);
  await ledger.prepareOrLoad(draft(2, remoteKey) as never);

  const vault = new SqliteReturnCapacityVault(db as never, {
    encrypt: async (value: string) => new TextEncoder().encode(value),
    decrypt: async (value: Uint8Array) => new TextDecoder().decode(value),
  }, () => now);
  await assert.rejects(() => vault.seedOrLoad({
    capacityId: "new-cash-lookup", originalOrderGuid: original,
    method: "cash", originalAmountCents: 500, remainingAmountCents: 500,
    protectedContext: null, observedAtIso: now,
  }), /already reserved/);

  // 第二份历史别名仍在处理中时，第三次查询即使换键也不能预留同一件。
  await assert.rejects(() => ledger.prepareOrLoad(draft(3, `legacy:${original}:${detail}`) as never));
  await markCompleted(db, 2, remoteKey);
  await assert.rejects(() => ledger.prepareOrLoad(draft(3, `legacy:${original}:${detail}`) as never));
  const rows = await db.getAll<{ return_source_key: string; remaining_quantity: string }>(
    "SELECT return_source_key, remaining_quantity FROM return_capacity ORDER BY return_source_key",
  );
  assert.deepEqual(rows.map((row) => row.remaining_quantity), ["1", "0"]);
});

test("已同步的旧退款不重复扣除新远端快照中的可退量", async () => {
  const db = new TestConnection();
  await applyMigrations(db as never, () => now);
  const ledger = new SqliteReturnExecutionLedger(db as never, {
    encrypt: async (value: string) => new TextEncoder().encode(value),
    decrypt: async (value: Uint8Array) => new TextDecoder().decode(value),
  }, { createTenderGuid: () => "tender", createAuditEventId: () => "audit" }, () => now);
  for (let index = 1; index <= 4; index += 1) {
    await db.run(`INSERT INTO return_tender_capacities
      (capacity_id, original_order_guid, method, original_amount_cents,
       remaining_amount_cents, observed_at_iso, created_at_iso, updated_at_iso)
      VALUES (?, ?, 'cash', 500, 500, ?, ?, ?)`, [`capacity-${index}`, original, now, now, now]);
  }
  const localKey = `local-receipt:${original}:${detail}`;
  const remoteKey = `receipt:${original}:${detail}`;
  await ledger.prepareOrLoad(draft(1, localKey, 5) as never);
  await markCompleted(db, 1, localKey);
  await ledger.prepareOrLoad(draft(2, localKey, 5) as never);
  await markCompleted(db, 2, localKey);
  await ledger.prepareOrLoad(draft(3, remoteKey, 3) as never);
  await ledger.prepareOrLoad(draft(4, remoteKey, 3) as never);
  const remote = await db.getFirst<{ original_quantity: string; remaining_quantity: string }>(
    "SELECT original_quantity, remaining_quantity FROM return_capacity WHERE return_source_key = ?", [remoteKey]);
  assert.equal(remote?.original_quantity, "5");
  assert.equal(remote?.remaining_quantity, "3");
});

test("不同历史基线的两个已消费别名无法还原时序时拒绝继续退款", async () => {
  const db = new TestConnection();
  await applyMigrations(db as never, () => now);
  const ledger = new SqliteReturnExecutionLedger(db as never, {
    encrypt: async (value: string) => new TextEncoder().encode(value),
    decrypt: async (value: Uint8Array) => new TextDecoder().decode(value),
  }, { createTenderGuid: () => "tender", createAuditEventId: () => "audit" }, () => now);
  for (let index = 1; index <= 3; index += 1) {
    await db.run(`INSERT INTO return_tender_capacities
      (capacity_id, original_order_guid, method, original_amount_cents,
       remaining_amount_cents, observed_at_iso, created_at_iso, updated_at_iso)
      VALUES (?, ?, 'cash', 500, 500, ?, ?, ?)`, [`capacity-${index}`, original, now, now, now]);
  }
  const first = `local-receipt:${original}:${detail}`;
  const second = `receipt:${original}:${detail}`;
  await ledger.prepareOrLoad(draft(1, first, 4) as never);
  await markCompleted(db, 1, first);
  await ledger.prepareOrLoad(draft(2, second, 5) as never);
  // 重造旧版曾按新快照另开原始容量的状态，不能猜测两份快照覆盖了哪些完成记录。
  await db.run("UPDATE return_capacity SET original_quantity = '5' WHERE return_source_key = ?", [second]);
  await db.run("UPDATE return_amount_capacity SET original_amount_cents = 2500 WHERE return_source_key = ?", [second]);
  await markCompleted(db, 2, second);
  await assert.rejects(() => ledger.prepareOrLoad(draft(3, `third:${original}:${detail}`, 5) as never),
    /ambiguous source balances/);
});

async function markCompleted(db: TestConnection, index: number, key: string): Promise<void> {
  await db.run("UPDATE return_line_capacity_reservations SET state = 'Committed' WHERE action_id = ?", [`action-${index}`]);
  await db.run("UPDATE return_capacity SET remaining_quantity = CAST(CAST(remaining_quantity AS INTEGER) - 1 AS TEXT) WHERE return_source_key = ?", [key]);
  await db.run("UPDATE return_amount_capacity SET remaining_amount_cents = remaining_amount_cents - 500 WHERE return_source_key = ?", [key]);
  await db.run("UPDATE return_tender_capacities SET remaining_amount_cents = remaining_amount_cents - 500 WHERE capacity_id = ?", [`capacity-${index}`]);
  await db.run("UPDATE return_action_allocations SET status = 'completed', capacity_reservation_state = 'Committed' WHERE action_id = ?", [`action-${index}`]);
  await db.run("UPDATE return_actions SET state = 'completed', completed_at_iso = ? WHERE action_id = ?", [now, `action-${index}`]);
}

test("两份旧付款 ID 各自完成退款后累计收紧同一原支付余额", async () => {
  const db = new TestConnection();
  await applyMigrations(db as never, () => now);
  const ledger = new SqliteReturnExecutionLedger(db as never, {
    encrypt: async (value: string) => new TextEncoder().encode(value),
    decrypt: async (value: Uint8Array) => new TextDecoder().decode(value),
  }, { createTenderGuid: () => "tender", createAuditEventId: () => "audit" }, () => now);
  for (let index = 1; index <= 2; index += 1) {
    await db.run(`INSERT INTO return_tender_capacities
      (capacity_id, original_order_guid, method, original_amount_cents,
       remaining_amount_cents, observed_at_iso, created_at_iso, updated_at_iso)
      VALUES (?, ?, 'cash', 1000, 1000, ?, ?, ?)`, [`capacity-${index}`, original, now, now, now]);
    await ledger.prepareOrLoad(draft(index, `receipt:${original}:${detail}`) as never);
    await markCompleted(db, index, `receipt:${original}:${detail}`);
  }
  const vault = new SqliteReturnCapacityVault(db as never, {
    encrypt: async (value: string) => new TextEncoder().encode(value),
    decrypt: async (value: Uint8Array) => new TextDecoder().decode(value),
  }, () => now);
  const result = await vault.seedOrLoad({
    capacityId: "new-cash-lookup", originalOrderGuid: original,
    method: "cash", originalAmountCents: 1000, remainingAmountCents: 1000,
    protectedContext: null, observedAtIso: now,
  });
  assert.equal(result.remainingAmountCents, 0);
});

test("旧版多笔现金 tender 无身份时拒绝把分行余额当成汇总余额", async () => {
  const db = new TestConnection();
  await applyMigrations(db as never, () => now);
  for (let index = 1; index <= 2; index += 1) {
    await db.run(`INSERT INTO return_tender_capacities
      (capacity_id, original_order_guid, method, original_amount_cents,
       remaining_amount_cents, observed_at_iso, created_at_iso, updated_at_iso)
      VALUES (?, ?, 'cash', 500, 500, ?, ?, ?)`, [`legacy-cash-${index}`, original, now, now, now]);
  }
  const vault = new SqliteReturnCapacityVault(db as never, {
    encrypt: async (value: string) => new TextEncoder().encode(value),
    decrypt: async (value: Uint8Array) => new TextDecoder().decode(value),
  }, () => now);
  await assert.rejects(() => vault.seedOrLoad({
    capacityId: "grouped-cash", originalOrderGuid: original,
    method: "cash", originalAmountCents: 1000, remainingAmountCents: 1000,
    protectedContext: null, observedAtIso: now,
  }), /ambiguous aggregation/);
  assert.equal((await db.getFirst<{ total: number }>(
    "SELECT COUNT(*) AS total FROM return_tender_capacities",
  ))?.total, 2);
});

test("旧随机付款容量 ID 在联网重查后仍复用已扣减额度", async () => {
  const db = new TestConnection();
  await applyMigrations(db as never, () => now);
  const vault = new SqliteReturnCapacityVault(db as never, {
    encrypt: async (value: string) => new TextEncoder().encode(value),
    decrypt: async (value: Uint8Array) => new TextDecoder().decode(value),
  }, () => now);
  const cash = {
    originalOrderGuid: original, method: "cash" as const,
    originalAmountCents: 1000, remainingAmountCents: 1000,
    protectedContext: null, observedAtIso: now,
  };
  await vault.seedOrLoad({ ...cash, capacityId: "legacy-cash-capacity" });
  await db.run("UPDATE return_tender_capacities SET remaining_amount_cents = 600 WHERE capacity_id = 'legacy-cash-capacity'");
  assert.deepEqual(
    await vault.seedOrLoad({ ...cash, capacityId: "new-random-id" }),
    { capacityId: "legacy-cash-capacity", originalOrderGuid: original, method: "cash", originalAmountCents: 1000, remainingAmountCents: 600, observedAtIso: now },
  );
  const square = {
    originalOrderGuid: original, method: "card" as const,
    originalAmountCents: 1000, remainingAmountCents: 1000,
    protectedContext: { version: 1, provider: "square", paymentId: "payment-1" },
    observedAtIso: now,
  };
  await vault.seedOrLoad({ ...square, capacityId: "legacy-square-capacity" });
  await db.run("UPDATE return_tender_capacities SET remaining_amount_cents = 500 WHERE capacity_id = 'legacy-square-capacity'");
  const reloaded = await vault.seedOrLoad({ ...square, capacityId: "new-square-random-id" });
  assert.equal(reloaded.capacityId, "legacy-square-capacity");
  assert.equal(reloaded.remainingAmountCents, 500);
  assert.equal(await db.getFirst<{ total: number }>("SELECT COUNT(*) AS total FROM return_tender_capacities" ).then((row) => row?.total), 2);
});
