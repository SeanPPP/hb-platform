import assert from "node:assert/strict";
import { DatabaseSync, type SQLInputValue } from "node:sqlite";
import test from "node:test";

import type {
  ApprovedPaymentOrderCommit,
  DurableCashOrderCommit,
  HeldOrderPayloadV1,
  HeldOrderRecordRepositoryPort,
  HeldOrderScope,
} from "../contracts";
import type { AuditEventDraft } from "@hb/pos-domain/core/contracts/order";

import { applyMigrations, POS_DATABASE_MIGRATIONS } from "./migrations";
import {
  SqliteApprovedPaymentOrderCommitter,
  SqliteAtomicCashOrderCommitter,
} from "./pos-database";
import { SqliteMixedPaymentTenderStore } from "./sqlite-mixed-payment-tender-store";
import { createSqliteRepositories } from "./sqlite-repositories";
import type {
  SqliteConnectionPort,
  SqlRunResult,
  SqlValue,
} from "@hb/pos-db/core/db/types";

const nowIso = "2026-07-28T08:00:00.000Z";
const scope = { storeCode: "S1", deviceCode: "IPAD-01" } as const;
const heldBy = { cashierId: "cashier-1", cashierName: "Cashier One" } as const;

class NodeSqliteConnection implements SqliteConnectionPort {
  public failNextAuditInsert = false;
  public failAuditEventType: string | null = null;
  public failRecallCompletionCas = false;
  public failRecallFenceDelete = false;
  private readonly queue = new AsyncSerialQueue();
  private transactionActive = false;

  public constructor(private readonly database: DatabaseSync) {}

  public async exec(sql: string): Promise<void> {
    this.database.exec(sql);
  }

  public async run(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<SqlRunResult> {
    if (
      sql.includes("INSERT INTO audit_events") &&
      (this.failNextAuditInsert ||
        (this.failAuditEventType !== null &&
          parameters[1] === this.failAuditEventType))
    ) {
      this.failNextAuditInsert = false;
      this.failAuditEventType = null;
      throw new Error("simulated held audit failure");
    }
    if (
      this.failRecallCompletionCas &&
      sql.includes("UPDATE held_order_records") &&
      sql.includes("SET status = 'Recalled'")
    ) {
      this.failRecallCompletionCas = false;
      return { changes: 0, lastInsertRowId: 0 };
    }
    if (
      this.failRecallFenceDelete &&
      sql.includes("DELETE FROM terminal_cart_fences") &&
      sql.includes("kind = 'RecallActive'")
    ) {
      this.failRecallFenceDelete = false;
      return { changes: 0, lastInsertRowId: 0 };
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
    if (this.transactionActive) return Promise.reject(new Error("Nested test transaction."));
    return this.queue.enqueue(async () => {
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
    });
  }

  public async close(): Promise<void> {
    this.database.close();
  }
}

class AsyncSerialQueue {
  private tail: Promise<void> = Promise.resolve();

  public enqueue<T>(operation: () => Promise<T>): Promise<T> {
    const result = this.tail.then(operation, operation);
    this.tail = result.then(
      () => undefined,
      () => undefined,
    );
    return result;
  }
}

function payload(): HeldOrderPayloadV1 {
  return {
    version: 1,
    pricingState: {
      revision: 7,
      mode: "sale",
      asOfIso: nowIso,
      promotions: [{
        id: "promo-1",
        name: "Three for five",
        effectiveStartIso: "2026-07-01T00:00:00.000Z",
        effectiveEndIso: "2026-08-01T00:00:00.000Z",
        isExclusive: false,
        priority: 1,
        applyQuantity: 1,
        fixedPrice: { currency: "AUD", cents: 500 },
        maxApplicationsPerOrder: null,
        products: [{ productCode: "P-PROMO", unitWeight: 1 }],
      }],
      lines: [
        {
          lineId: "line-percent",
          productCode: "P-PERCENT",
          itemNumber: "100",
          lookupCode: "100",
          displayName: "Percent item",
          quantity: 1,
          unitPriceCents: 105,
          basePriceSource: "manual",
          catalogDiscountBasisPoints: 0,
          syncProvenance: {
            referenceCode: "REF-PERCENT",
            priceSource: 1,
          },
          kind: "sale",
          returnSourceKey: null,
          originalOrderGuid: null,
          originalOrderDetailGuid: null,
          discountState: { kind: "manual-percent", basisPoints: 1000 },
        },
        {
          lineId: "line-promo",
          productCode: "P-PROMO",
          itemNumber: "200",
          lookupCode: "200",
          displayName: "Promotion item",
          quantity: 1,
          unitPriceCents: 500,
          basePriceSource: "catalog",
          catalogDiscountBasisPoints: 0,
          syncProvenance: {
            referenceCode: null,
            priceSource: 2,
          },
          kind: "sale",
          returnSourceKey: null,
          originalOrderGuid: null,
          originalOrderDetailGuid: null,
          discountState: { kind: "promotion", cents: 120, promotionIds: ["promo-1"] },
        },
        {
          lineId: "line-open",
          productCode: "OPENITEM",
          itemNumber: null,
          lookupCode: "OPENITEM",
          displayName: "Open item",
          quantity: 1,
          unitPriceCents: 200,
          basePriceSource: "open-item",
          catalogDiscountBasisPoints: 0,
          syncProvenance: {
            referenceCode: "REF-OPENITEM",
            priceSource: 0,
          },
          kind: "sale",
          returnSourceKey: null,
          originalOrderGuid: null,
          originalOrderDetailGuid: null,
          discountState: { kind: "none" },
        },
      ],
    },
  };
}

function audit(eventId: string, eventType: "ORDER_HOLD" | "ORDER_RECALL"): AuditEventDraft {
  return {
    eventId,
    eventType,
    occurredAtIso: nowIso,
    orderGuid: null,
    correlationId: "hold-1",
    payload: { action: eventType === "ORDER_HOLD" ? "hold" : "recall", result: "succeeded" },
  };
}

function createRepository(connection: NodeSqliteConnection): HeldOrderRecordRepositoryPort {
  return createSqliteRepositories(connection, {
    nowIso: () => nowIso,
    createLeaseId: () => "unused-lease",
    encryptor,
  }).heldOrderRecords;
}

const encryptor = {
  async encrypt(plaintext: string) {
    return new TextEncoder().encode(plaintext);
  },
  async decrypt(ciphertext: Uint8Array) {
    return new TextDecoder().decode(ciphertext);
  },
};

async function open(): Promise<Readonly<{
  connection: NodeSqliteConnection;
  records: HeldOrderRecordRepositoryPort;
}>> {
  const connection = new NodeSqliteConnection(
    new DatabaseSync(":memory:", { enableForeignKeyConstraints: true }),
  );
  await applyMigrations(connection, () => nowIso);
  return { connection, records: createRepository(connection) };
}

async function hold(
  records: HeldOrderRecordRepositoryPort,
  holdId = "hold-1",
  heldScope: HeldOrderScope = scope,
): Promise<void> {
  await records.hold({
    holdId,
    scope: heldScope,
    heldBy,
    payload: payload(),
    heldAtIso: nowIso,
    audit: audit(`audit-hold-${holdId}`, "ORDER_HOLD"),
  });
}

async function confirmHoldClear(
  records: HeldOrderRecordRepositoryPort,
  holdId = "hold-1",
  heldScope: HeldOrderScope = scope,
): Promise<void> {
  assert.equal(
    await records.confirmHoldCartCleared({ scope: heldScope, holdId }),
    true,
  );
}

function recalledCashInput(
  recallAttemptId = "attempt-cash",
  checkoutIntentId = "intent-recall-cash",
): DurableCashOrderCommit {
  const orderGuid = "order-recall-1";
  const soldAtIso = "2026-07-28T08:05:00.000Z";
  const binding = {
    kind: "recalled" as const,
    scope,
    holdId: "hold-1",
    recallAttemptId,
  };
  return {
    intent: {
      checkoutIntentId,
      requestSignature: `signature:${scope.storeCode}:${scope.deviceCode}:hold-1:${recallAttemptId}:674`,
      cashDueCents: 674,
      changeCents: 0,
    },
    command: {
      order: {
        orderGuid,
        localSequence: 2,
        storeCode: scope.storeCode,
        deviceCode: scope.deviceCode,
        cashierId: "cashier-2",
        cashierName: "Cashier Two",
        soldAtIso,
        state: "PendingSync",
        total: { currency: "AUD", cents: 805 },
        discount: { currency: "AUD", cents: 131 },
        actualAmount: { currency: "AUD", cents: 674 },
        lines: payload().pricingState.lines.map((line) => {
          const syncProvenance = line.syncProvenance;
          if (!syncProvenance) {
            throw new Error("Test held line sync provenance is missing.");
          }
          const discountCents = line.discountState.kind === "manual-percent"
            ? 11
            : line.discountState.kind === "promotion"
              ? line.discountState.cents
              : 0;
          return {
            lineId: line.lineId,
            productCode: line.productCode,
            itemNumber: line.itemNumber,
            lookupCode: line.lookupCode,
            displayName: line.displayName,
            quantity: String(line.quantity),
            unitPrice: { currency: "AUD" as const, cents: line.unitPriceCents },
            discount: { currency: "AUD" as const, cents: discountCents },
            actualAmount: {
              currency: "AUD" as const,
              cents: line.unitPriceCents * line.quantity - discountCents,
            },
            priceSource: line.basePriceSource,
            syncProvenance,
            kind: "sale" as const,
            returnSourceKey: null,
            originalOrderGuid: null,
            originalOrderDetailGuid: null,
          };
        }),
        tenders: [{
          tenderGuid: "tender-recall-1",
          method: "cash",
          amount: { currency: "AUD", cents: 674 },
          reference: null,
          reservationToken: null,
        }],
        originalOrderGuid: null,
      },
      auditEvents: [{
        eventId: "audit-sale-recall-1",
        eventType: "SALE_COMPLETE",
        occurredAtIso: soldAtIso,
        orderGuid,
        correlationId: orderGuid,
        payload: { amountCents: 674 },
      }],
      outbox: {
        messageId: "outbox-recall-1",
        aggregateId: orderGuid,
        kind: "order-sync",
        payloadJson: JSON.stringify({ orderGuid }),
        nextAttemptAtIso: soldAtIso,
      },
      requiresDrawer: false,
      printPolicy: "never",
    },
    fulfilment: { print: null, drawer: null },
    terminalContext: binding,
    recalledHoldCompletion: {
      binding,
      recalledAtIso: soldAtIso,
      recallAudit: {
        eventId: "audit-order-recall-1",
        eventType: "ORDER_RECALL",
        occurredAtIso: soldAtIso,
        orderGuid,
        correlationId: "hold-1",
        payload: {
          source: "held-order",
          result: "succeeded",
          itemCount: 3,
          actualAmountCents: 674,
        },
      },
    },
  };
}

async function prepareRecall(
  records: HeldOrderRecordRepositoryPort,
  recallAttemptId = "attempt-cash",
): Promise<void> {
  await hold(records);
  await confirmHoldClear(records);
  assert.ok(await records.claimRecall({
    holdId: "hold-1",
    scope,
    recalledBy: { cashierId: "cashier-2", cashierName: "Cashier Two" },
    recallAttemptId,
    recallingAtIso: "2026-07-28T08:01:00.000Z",
  }));
}

async function insertApprovedDraft(
  connection: SqliteConnectionPort,
  orderGuid: string,
  amountCents: number,
): Promise<void> {
  await connection.run(
    `INSERT INTO local_orders (
      order_guid, local_sequence, store_code, device_code,
      cashier_id, cashier_name, sold_at_iso, state,
      total_cents, discount_cents, actual_amount_cents,
      original_order_guid, created_at_iso, updated_at_iso
    ) VALUES (?, 2, ?, ?, 'cashier-2', 'Cashier Two', ?, 'Draft',
      ?, 0, ?, NULL, ?, ?)`,
    [orderGuid, scope.storeCode, scope.deviceCode, nowIso, amountCents, amountCents, nowIso, nowIso],
  );
}

async function insertApprovedAttempt(
  connection: SqliteConnectionPort,
  attemptId: string,
  orderGuid: string,
  amountCents: number,
): Promise<void> {
  await connection.run(
    `INSERT INTO payment_attempts (
      attempt_id, idempotency_key, order_guid, provider, operation,
      amount_cents, state, checkout_id, payment_id, session_id, txn_ref,
      rfn, provider_payload_ciphertext, provider_receipt_ciphertext,
      provider_response_code, created_at_iso, updated_at_iso, last_error_code
    ) VALUES (?, ?, ?, 'square', 'purchase', ?, 'Approved',
      NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, ?, ?, NULL)`,
    [attemptId, `key-${attemptId}`, orderGuid, amountCents, nowIso, nowIso],
  );
}

function approvedRecallPaymentInput(
  binding: NonNullable<ApprovedPaymentOrderCommit["recalledHoldCompletion"]>["binding"],
): ApprovedPaymentOrderCommit {
  const orderGuid = "order-recall-card";
  const recalledAtIso = "2026-07-28T08:05:00.000Z";
  return {
    attemptId: "payment-attempt-recall-card",
    orderGuid,
    tenderGuid: "tender-recall-card",
    completionAuditEvents: [{
      eventId: "audit-payment-recall-card",
      eventType: "PAYMENT_COMPLETE",
      occurredAtIso: recalledAtIso,
      orderGuid,
      correlationId: "payment-attempt-recall-card",
      payload: { source: "approved-payment" },
    }],
    outbox: {
      messageId: "outbox-recall-card",
      aggregateId: orderGuid,
      kind: "order-sync",
      payloadJson: JSON.stringify({ orderGuid }),
      nextAttemptAtIso: recalledAtIso,
    },
    fulfilment: { print: null, drawer: null },
    recalledHoldCompletion: {
      binding,
      recalledAtIso,
      recallAudit: {
        eventId: "audit-order-recall-card",
        eventType: "ORDER_RECALL",
        occurredAtIso: recalledAtIso,
        orderGuid,
        correlationId: binding.holdId,
        payload: {
          source: "ipad-pos",
          action: "recall",
          result: "completed",
        },
      },
    },
  };
}

test("真实 SQLite：M8 升级 M17 保留 legacy held_orders，V2 密文保存完整定价状态且不生成订单/outbox", async () => {
  const connection = new NodeSqliteConnection(
    new DatabaseSync(":memory:", { enableForeignKeyConstraints: true }),
  );
  await applyMigrations(
    connection,
    () => nowIso,
    POS_DATABASE_MIGRATIONS.filter((migration) => migration.version <= 8),
  );
  await connection.run(
    `INSERT INTO held_orders (
      hold_id, local_sequence, cart_ciphertext, created_at_iso, updated_at_iso
    ) VALUES ('legacy-hold', 3, ?, ?, ?)`,
    [Uint8Array.of(1, 2, 3), nowIso, nowIso],
  );

  await applyMigrations(
    connection,
    () => nowIso,
    POS_DATABASE_MIGRATIONS.filter((migration) => migration.version <= 17),
  );
  const versions = await connection.getAll<{ version: number }>(
    "SELECT version FROM schema_migrations ORDER BY version",
  );
  const legacy = await connection.getFirst<{ count: number }>(
    "SELECT COUNT(*) AS count FROM held_orders WHERE hold_id = 'legacy-hold'",
  );
  assert.deepEqual(
    versions.map((row) => row.version),
    [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17],
  );
  assert.equal(Number(legacy?.count), 1);
  await assert.rejects(
    connection.run(
      `INSERT INTO held_order_records (
        hold_id, local_sequence, store_code, device_code,
        held_by_cashier_id, held_by_cashier_name, status, payload_version,
        payload_ciphertext, item_count, subtotal_cents, discount_cents,
        actual_amount_cents, held_at_iso, created_at_iso, updated_at_iso
      ) VALUES ('zero-item', 99, 'S1', 'IPAD-01', 'cashier-1', 'Cashier',
        'Pending', 1, ?, 0, 0, 0, 0, ?, ?, ?)`,
      [Uint8Array.of(1), nowIso, nowIso, nowIso],
    ),
    /CHECK constraint failed: item_count > 0/,
  );

  const records = createRepository(connection);
  await hold(records);
  const pending = await records.listPending(scope, 10);
  assert.equal(pending.length, 1);
  assert.deepEqual(
    pending[0],
    {
      holdId: "hold-1",
      localSequence: 1,
      scope,
      heldBy,
      status: "Pending",
      itemCount: 3,
      subtotalCents: 805,
      discountCents: 131,
      actualAmountCents: 674,
      heldAtIso: nowIso,
      recallingAtIso: null,
    },
  );

  // 重新创建 repository 模拟进程重启；清车 fence 必须仍可恢复。
  const reopenedRecords = createRepository(connection);
  assert.deepEqual(await reopenedRecords.getTerminalFence(scope), {
    scope,
    kind: "HoldClear",
    holdId: "hold-1",
    recallAttemptId: null,
    boundOrderGuid: null,
    createdAtIso: nowIso,
  });
  await confirmHoldClear(reopenedRecords);
  const claimed = await reopenedRecords.claimRecall({
    holdId: "hold-1",
    scope,
    recalledBy: { cashierId: "cashier-2", cashierName: "Cashier Two" },
    recallAttemptId: "attempt-1",
    recallingAtIso: "2026-07-28T08:01:00.000Z",
  });
  assert.deepEqual(claimed?.payload, payload());
  const counts = await connection.getFirst<{
    orders: number;
    tenders: number;
    outbox: number;
    audits: number;
  }>(
    `SELECT
      (SELECT COUNT(*) FROM local_orders) AS orders,
      (SELECT COUNT(*) FROM order_tenders) AS tenders,
      (SELECT COUNT(*) FROM outbox_messages) AS outbox,
      (SELECT COUNT(*) FROM audit_events) AS audits`,
  );
  assert.deepEqual({ ...counts }, { orders: 0, tenders: 0, outbox: 0, audits: 1 });
  await connection.close();
});

test("SQLite 挂单保留目录基线，旧快照缺字段按 0 恢复", async () => {
  const { connection, records } = await open();
  const conflictingPayload: HeldOrderPayloadV1 = {
    ...payload(),
    pricingState: {
      ...payload().pricingState,
      lines: payload().pricingState.lines.map((line) =>
        line.lineId === "line-promo"
          ? { ...line, catalogDiscountBasisPoints: 2_000 }
          : line,
      ),
    },
  };
  await assert.rejects(
    records.hold({
      holdId: "conflicting-baseline-hold",
      scope,
      heldBy,
      payload: conflictingPayload,
      heldAtIso: nowIso,
      audit: audit("audit-conflicting-baseline-hold", "ORDER_HOLD"),
    }),
    /catalog discount.*promotion/i,
  );

  const baselinePayload: HeldOrderPayloadV1 = {
    ...payload(),
    pricingState: {
      ...payload().pricingState,
      lines: payload().pricingState.lines.map((line) =>
        line.lineId === "line-percent"
          ? { ...line, catalogDiscountBasisPoints: 2_000 }
          : line,
      ),
    },
  };
  await records.hold({
    holdId: "baseline-hold",
    scope,
    heldBy,
    payload: baselinePayload,
    heldAtIso: nowIso,
    audit: audit("audit-baseline-hold", "ORDER_HOLD"),
  });
  const pending = await records.listPending(scope, 10);
  assert.deepEqual(
    pending.find((entry) => entry.holdId === "baseline-hold"),
    {
      holdId: "baseline-hold",
      localSequence: 1,
      scope,
      heldBy,
      status: "Pending",
      itemCount: 3,
      subtotalCents: 805,
      discountCents: 131,
      actualAmountCents: 674,
      heldAtIso: nowIso,
      recallingAtIso: null,
    },
  );
  await confirmHoldClear(records, "baseline-hold", scope);
  const claimed = await records.claimRecall({
    holdId: "baseline-hold",
    scope,
    recalledBy: heldBy,
    recallAttemptId: "attempt-baseline",
    recallingAtIso: "2026-07-28T08:01:00.000Z",
  });
  assert.equal(
    claimed?.payload.pricingState.lines.find(
      (line) => line.lineId === "line-percent",
    )?.catalogDiscountBasisPoints,
    2_000,
  );

  const legacyPayload: HeldOrderPayloadV1 = {
    ...payload(),
    pricingState: {
      ...payload().pricingState,
      lines: payload().pricingState.lines.map((line) => {
        const { catalogDiscountBasisPoints: _legacy, ...withoutBaseline } =
          line;
        return withoutBaseline;
      }),
    },
  };
  const legacyScope = { storeCode: scope.storeCode, deviceCode: "IPAD-02" } as const;
  await records.hold({
    holdId: "legacy-baseline-hold",
    scope: legacyScope,
    heldBy,
    payload: legacyPayload,
    heldAtIso: nowIso,
    audit: audit("audit-legacy-baseline-hold", "ORDER_HOLD"),
  });
  await confirmHoldClear(records, "legacy-baseline-hold", legacyScope);
  const legacyClaimed = await records.claimRecall({
    holdId: "legacy-baseline-hold",
    scope: legacyScope,
    recalledBy: heldBy,
    recallAttemptId: "attempt-legacy-baseline",
    recallingAtIso: "2026-07-28T08:02:00.000Z",
  });
  assert.deepEqual(
    legacyClaimed?.payload.pricingState.lines.map(
      (line) => line.catalogDiscountBasisPoints,
    ),
    [0, 0, 0],
  );
  await connection.close();
});

test("真实 SQLite：删除挂单先阻断发布，再按 scope/Pending/无 fence 原子物理删除", async () => {
  const { connection, records } = await open();
  await hold(records);
  await confirmHoldClear(records);
  await connection.run(
    `UPDATE held_order_records
     SET share_requested_at_iso = ?,
         share_state = 'Published', remote_revision = 7,
         remote_updated_at_iso = ?
     WHERE hold_id = 'hold-1'`,
    [nowIso, nowIso],
  );

  const staged = await records.stageDeletePending({
    holdId: "hold-1",
    scope,
    stagedAtIso: "2026-07-28T08:02:00.000Z",
  });

  assert.deepEqual(staged, {
    holdId: "hold-1",
    remoteCancellationRequired: true,
  });
  const stagedRow = await connection.getFirst<{
      share_state: string;
      publish_block_reason: string | null;
      remote_revision: number | null;
    }>(
      `SELECT share_state, publish_block_reason, remote_revision
       FROM held_order_records WHERE hold_id = 'hold-1'`,
    );
  assert.deepEqual(
    { ...stagedRow },
    {
      share_state: "Blocked",
      publish_block_reason: "LOCAL_DELETE_PENDING",
      remote_revision: 7,
    },
  );
  // 服务端取消失败时本地行仍可见并可重试，发布队列则已被耐久阻断。
  assert.equal((await records.listPending(scope, 10)).length, 1);

  assert.equal(
    await records.deleteStagedPending({ holdId: "hold-1", scope }),
    true,
  );
  assert.equal((await records.listPending(scope, 10)).length, 0);
  assert.equal(
    await connection.getFirst(
      "SELECT hold_id FROM held_order_records WHERE hold_id = 'hold-1'",
    ),
    null,
  );
  await connection.close();
});

test("真实 SQLite：有清车 fence 或已进入 Recalling 的挂单拒绝删除，未发布挂单不要求远端取消", async () => {
  const { connection, records } = await open();
  await hold(records, "fenced");
  assert.equal(
    await records.stageDeletePending({
      holdId: "fenced",
      scope,
      stagedAtIso: nowIso,
    }),
    null,
  );
  await confirmHoldClear(records, "fenced");

  const localStage = await records.stageDeletePending({
    holdId: "fenced",
    scope,
    stagedAtIso: nowIso,
  });
  assert.deepEqual(localStage, {
    holdId: "fenced",
    remoteCancellationRequired: false,
  });
  assert.equal(
    await records.deleteStagedPending({
      holdId: "fenced",
      scope: { storeCode: "S1", deviceCode: "OTHER" },
    }),
    false,
  );

  await hold(records, "recalling");
  await confirmHoldClear(records, "recalling");
  assert.ok(
    await records.claimRecall({
      holdId: "recalling",
      scope,
      recalledBy: heldBy,
      recallAttemptId: "attempt-delete-guard",
      recallingAtIso: nowIso,
    }),
  );
  assert.equal(
    await records.stageDeletePending({
      holdId: "recalling",
      scope,
      stagedAtIso: nowIso,
    }),
    null,
  );
  await connection.close();
});

test("真实 SQLite：manual-amount 0 覆盖目录折扣时摘要与召回金额一致", async () => {
  const { connection, records } = await open();
  const sourceLine = payload().pricingState.lines[0]!;
  const manualZeroPayload: HeldOrderPayloadV1 = {
    version: 1,
    pricingState: {
      revision: 8,
      mode: "sale",
      asOfIso: nowIso,
      promotions: [],
      lines: [{
        ...sourceLine,
        unitPriceCents: 1,
        basePriceSource: "catalog",
        catalogDiscountBasisPoints: 10_000,
        discountState: { kind: "manual-amount", cents: 0 },
      }],
    },
  };

  await records.hold({
    holdId: "manual-zero-hold",
    scope,
    heldBy,
    payload: manualZeroPayload,
    heldAtIso: nowIso,
    audit: audit("audit-manual-zero-hold", "ORDER_HOLD"),
  });

  const pending = await records.listPending(scope, 10);
  assert.deepEqual(
    pending.find((entry) => entry.holdId === "manual-zero-hold"),
    {
      holdId: "manual-zero-hold",
      localSequence: 1,
      scope,
      heldBy,
      status: "Pending",
      itemCount: 1,
      subtotalCents: 1,
      discountCents: 0,
      actualAmountCents: 1,
      heldAtIso: nowIso,
      recallingAtIso: null,
    },
  );

  await confirmHoldClear(records, "manual-zero-hold");
  const claimed = await records.claimRecall({
    holdId: "manual-zero-hold",
    scope,
    recalledBy: heldBy,
    recallAttemptId: "attempt-manual-zero",
    recallingAtIso: "2026-07-28T08:01:00.000Z",
  });
  assert.deepEqual(claimed?.payload, manualZeroPayload);
  await connection.close();
});

test("小数称重挂单发布/汇总不抛：0.29 × 50 = 15，itemCount 按行计 1", async () => {
  const { records } = await open();
  const decimalPayload: HeldOrderPayloadV1 = {
    version: 1,
    pricingState: {
      revision: 7,
      mode: "sale",
      asOfIso: nowIso,
      promotions: [],
      lines: [
        {
          lineId: "line-weighed",
          productCode: "P-WEIGHED",
          itemNumber: null,
          lookupCode: "W1",
          displayName: "Weighed 0.29",
          quantity: 0.29,
          unitPriceCents: 50,
          basePriceSource: "catalog",
          syncProvenance: { referenceCode: null, priceSource: 0 },
          kind: "sale",
          returnSourceKey: null,
          originalOrderGuid: null,
          originalOrderDetailGuid: null,
          discountState: { kind: "none" },
        },
      ],
    },
  };

  await records.hold({
    holdId: "hold-decimal-1",
    scope,
    heldBy,
    payload: decimalPayload,
    heldAtIso: nowIso,
    audit: audit("audit-decimal-hold", "ORDER_HOLD"),
  });

  const pending = await records.listPending(scope, 10);
  assert.equal(pending.length, 1);
  assert.deepEqual(pending[0], {
    holdId: "hold-decimal-1",
    localSequence: 1,
    scope,
    heldBy,
    status: "Pending",
    itemCount: 1,
    subtotalCents: 15,
    discountCents: 0,
    actualAmountCents: 15,
    heldAtIso: nowIso,
    recallingAtIso: null,
  });
});

test("真实 SQLite：M9 Recalling 升级 M13 时按持久 attempt 回填唯一 RecallActive fence", async () => {
  const connection = new NodeSqliteConnection(
    new DatabaseSync(":memory:", { enableForeignKeyConstraints: true }),
  );
  await applyMigrations(
    connection,
    () => nowIso,
    POS_DATABASE_MIGRATIONS.filter((migration) => migration.version <= 9),
  );
  await connection.run(
    `INSERT INTO held_order_records (
      hold_id, local_sequence, store_code, device_code,
      held_by_cashier_id, held_by_cashier_name, status, payload_version,
      payload_ciphertext, item_count, subtotal_cents, discount_cents,
      actual_amount_cents, recalling_at_iso, recall_attempt_id,
      recalling_cashier_id, recalling_cashier_name, recalled_at_iso,
      held_at_iso, created_at_iso, updated_at_iso
    ) VALUES (?, 1, ?, ?, ?, ?, 'Recalling', 1, ?, 3, 805, 131, 674,
      ?, ?, ?, ?, NULL, ?, ?, ?)`,
    [
      "hold-upgrade",
      scope.storeCode,
      scope.deviceCode,
      heldBy.cashierId,
      heldBy.cashierName,
      new TextEncoder().encode(JSON.stringify(payload())),
      "2026-07-28T08:01:00.000Z",
      "attempt-upgrade",
      "cashier-2",
      "Cashier Two",
      nowIso,
      nowIso,
      "2026-07-28T08:01:00.000Z",
    ],
  );

  await applyMigrations(
    connection,
    () => "2026-07-28T08:02:00.000Z",
    POS_DATABASE_MIGRATIONS.filter((migration) => migration.version <= 17),
  );
  const records = createRepository(connection);
  assert.deepEqual(await records.getTerminalFence(scope), {
    scope,
    kind: "RecallActive",
    holdId: "hold-upgrade",
    recallAttemptId: "attempt-upgrade",
    boundOrderGuid: null,
    createdAtIso: "2026-07-28T08:01:00.000Z",
  });
  assert.equal((await records.listRecoverable(scope)).length, 1);
  assert.equal(
    (await connection.getFirst<{ version: number }>(
      "SELECT MAX(version) AS version FROM schema_migrations",
    ))?.version,
    17,
  );
  await connection.close();
});

test("真实 SQLite：首版 V2 只接受非空 sale 购物车，规范化 scope/ID，并拒绝订单引用审计", async () => {
  const { connection, records } = await open();
  const basePayload = payload();
  const empty: HeldOrderPayloadV1 = {
    ...basePayload,
    pricingState: { ...basePayload.pricingState, lines: [] },
  };
  const returnLine: HeldOrderPayloadV1 = {
    ...basePayload,
    pricingState: {
      ...basePayload.pricingState,
      lines: [{ ...basePayload.pricingState.lines[0]!, kind: "return" }],
    },
  };
  const installment: HeldOrderPayloadV1 = {
    ...basePayload,
    pricingState: { ...basePayload.pricingState, mode: "installment" },
  };
  const firstLine = basePayload.pricingState.lines[0]!;
  const { syncProvenance: _syncProvenance, ...legacyFirstLine } =
    firstLine;
  const missingProvenance: HeldOrderPayloadV1 = {
    ...basePayload,
    pricingState: {
      ...basePayload.pricingState,
      lines: [
        legacyFirstLine,
        ...basePayload.pricingState.lines.slice(1),
      ],
    },
  };
  for (const rejected of [empty, returnLine, installment, missingProvenance]) {
    await assert.rejects(
      records.hold({
        holdId: "reject", scope, heldBy, payload: rejected, heldAtIso: nowIso,
        audit: audit("audit-reject", "ORDER_HOLD"),
      }),
      /Held cart (must contain|only supports)|Invalid held cart mode|line sync provenance/i,
    );
  }
  await assert.rejects(
    records.hold({
      holdId: "audit-order", scope, heldBy, payload: payload(), heldAtIso: nowIso,
      audit: { ...audit("audit-order", "ORDER_HOLD"), orderGuid: "completed-order" },
    }),
    /must not reference a completed order/,
  );
  await records.hold({
    holdId: "  hold-trim  ",
    scope: { storeCode: "  S1  ", deviceCode: "  IPAD-01  " },
    heldBy: { cashierId: "  cashier-1  ", cashierName: "  Cashier One  " },
    payload: payload(),
    heldAtIso: nowIso,
    audit: {
      ...audit("  audit-trim  ", "ORDER_HOLD"),
      correlationId: "  hold-trim  ",
    },
  });
  assert.deepEqual(await records.listPending(scope, 10), [{
    holdId: "hold-trim",
    localSequence: 1,
    scope,
    heldBy,
    status: "Pending",
    itemCount: 3,
    subtotalCents: 805,
    discountCents: 131,
    actualAmountCents: 674,
    heldAtIso: nowIso,
    recallingAtIso: null,
  }]);
  const savedAudit = await connection.getFirst<{ event_id: string; correlation_id: string }>(
    "SELECT event_id, correlation_id FROM audit_events WHERE event_id = 'audit-trim'",
  );
  assert.deepEqual({ ...savedAudit }, { event_id: "audit-trim", correlation_id: "hold-trim" });
  await connection.close();
});

test("真实 SQLite：并发召回 CAS 恰好一人成功；崩溃中的 Recalling 可被识别并按原 attempt 释放", async () => {
  const { connection, records } = await open();
  await hold(records);
  await confirmHoldClear(records);

  const attempts = await Promise.allSettled([
    records.claimRecall({
      holdId: "hold-1", scope, recalledBy: heldBy,
      recallAttemptId: "attempt-a", recallingAtIso: "2026-07-28T08:01:00.000Z",
    }),
    records.claimRecall({
      holdId: "hold-1", scope,
      recalledBy: { cashierId: "cashier-2", cashierName: "Cashier Two" },
      recallAttemptId: "attempt-b", recallingAtIso: "2026-07-28T08:01:01.000Z",
    }),
  ]);
  const fulfilled = attempts.filter(
    (result): result is PromiseFulfilledResult<
      Awaited<ReturnType<HeldOrderRecordRepositoryPort["claimRecall"]>>
    > => result.status === "fulfilled",
  );
  const rejected = attempts.filter(
    (result): result is PromiseRejectedResult => result.status === "rejected",
  );
  assert.equal(fulfilled.length, 1);
  assert.equal(rejected.length, 1);
  assert.match(String(rejected[0]?.reason), /active fence/);
  const successful = fulfilled[0]?.value;
  assert.ok(successful);

  // 进程在恢复购物车后崩溃时记录仍为 Recalling；启动恢复路径可列出它，
  // 并用该次持久化的 attempt id 释放回 Pending，绝不猜测完成成功。
  assert.deepEqual(
    (await records.listRecoverable(scope)).map((record) => record.hold.status),
    ["Recalling"],
  );
  assert.deepEqual((await records.listRecoverable(scope))[0]?.payload, payload());
  assert.equal(await records.releaseRecallAfterCartCleared({
    binding: {
      kind: "recalled",
      scope,
      holdId: "hold-1",
      recallAttemptId: successful.recallAttemptId,
    },
    releasedAtIso: "2026-07-28T08:02:00.000Z",
  }), true);
  assert.deepEqual(
    (await records.listPending(scope, 10)).map((record) => record.status),
    ["Pending"],
  );
  await connection.close();
});

test("真实 SQLite：recall_attempt_id 全局唯一，不能被另一笔挂单重用", async () => {
  const { connection, records } = await open();
  const otherScope = { storeCode: "S1", deviceCode: "IPAD-02" } as const;
  await hold(records, "hold-1");
  await confirmHoldClear(records, "hold-1");
  await hold(records, "hold-2", otherScope);
  await confirmHoldClear(records, "hold-2", otherScope);
  assert.ok(await records.claimRecall({
    holdId: "hold-1", scope, recalledBy: heldBy,
    recallAttemptId: "attempt-global", recallingAtIso: "2026-07-28T08:01:00.000Z",
  }));
  await assert.rejects(
    records.claimRecall({
      holdId: "hold-2", scope: otherScope, recalledBy: heldBy,
      recallAttemptId: "attempt-global", recallingAtIso: "2026-07-28T08:01:01.000Z",
    }),
    /UNIQUE constraint failed: held_order_records\.recall_attempt_id/,
  );
  assert.equal((await records.listPending(otherScope, 10))[0]?.holdId, "hold-2");
  await connection.close();
});

test("真实 SQLite：release 是 scope/hold/attempt CAS，并使用明确释放时间", async () => {
  const { connection, records } = await open();
  await hold(records);
  await confirmHoldClear(records);
  const claim = await records.claimRecall({
    holdId: "hold-1", scope, recalledBy: heldBy,
    recallAttemptId: "attempt-1", recallingAtIso: "2026-07-28T08:01:00.000Z",
  });
  assert.ok(claim);

  assert.equal(await records.releaseRecallAfterCartCleared({
    binding: {
      kind: "recalled",
      scope,
      holdId: "hold-1",
      recallAttemptId: "wrong-attempt",
    },
    releasedAtIso: "2026-07-28T08:02:00.000Z",
  }), false);
  assert.equal((await records.listRecoverable(scope))[0]?.hold.status, "Recalling");
  assert.equal(await records.releaseRecallAfterCartCleared({
    binding: {
      kind: "recalled",
      scope,
      holdId: "hold-1",
      recallAttemptId: "attempt-1",
    },
    releasedAtIso: "2026-07-28T08:03:00.000Z",
  }), true);
  assert.equal((await records.listRecoverable(scope)).length, 0);
  const released = await connection.getFirst<{
    status: string;
    updated_at_iso: string;
    fence_count: number;
  }>(
    `SELECT status, updated_at_iso,
       (SELECT COUNT(*) FROM terminal_cart_fences) AS fence_count
     FROM held_order_records WHERE hold_id = 'hold-1'`,
  );
  assert.deepEqual(
    { ...released },
    {
      status: "Pending",
      updated_at_iso: "2026-07-28T08:03:00.000Z",
      fence_count: 0,
    },
  );
  await connection.close();
});

test("真实 SQLite：密文解密或结构损坏失败关闭，挂单仍保持 Pending", async () => {
  const { connection, records } = await open();
  await hold(records);
  await confirmHoldClear(records);
  await connection.run(
    "UPDATE held_order_records SET payload_ciphertext = ? WHERE hold_id = 'hold-1'",
    [new TextEncoder().encode('{"version":1,"pricingState":{"lines":"corrupt"}}')],
  );
  await assert.rejects(
    records.claimRecall({
      holdId: "hold-1", scope, recalledBy: heldBy,
      recallAttemptId: "attempt-corrupt", recallingAtIso: "2026-07-28T08:01:00.000Z",
    }),
    /Invalid held (order payload|cart)/,
  );
  assert.equal((await records.listPending(scope, 10))[0]?.status, "Pending");
  await connection.close();
});

test("真实 SQLite：恢复列表遇到任一损坏 Recalling 记录整体失败，不静默遗漏", async () => {
  const { connection, records } = await open();
  await hold(records);
  await confirmHoldClear(records);
  assert.ok(await records.claimRecall({
    holdId: "hold-1", scope, recalledBy: heldBy,
    recallAttemptId: "attempt-recovery", recallingAtIso: "2026-07-28T08:01:00.000Z",
  }));
  await connection.run(
    "UPDATE held_order_records SET payload_ciphertext = ? WHERE hold_id = 'hold-1'",
    [new TextEncoder().encode("not-json")],
  );
  await assert.rejects(
    records.listRecoverable(scope),
    /Invalid held order payload ciphertext/,
  );
  assert.equal(
    (await connection.getFirst<{ status: string }>(
      "SELECT status FROM held_order_records WHERE hold_id = 'hold-1'",
    ))?.status,
    "Recalling",
  );
  await connection.close();
});

test("真实 SQLite：同一 scope 的 HoldClear fence 唯一，第二笔挂单、审计和序号整体回滚", async () => {
  const { connection, records } = await open();
  await hold(records, "hold-1");
  await assert.rejects(
    hold(records, "hold-2"),
    /UNIQUE constraint failed: terminal_cart_fences\.store_code/,
  );
  const counts = await connection.getFirst<{
    held: number;
    audits: number;
    fences: number;
    sequence: string;
  }>(
    `SELECT
      (SELECT COUNT(*) FROM held_order_records) AS held,
      (SELECT COUNT(*) FROM audit_events) AS audits,
      (SELECT COUNT(*) FROM terminal_cart_fences) AS fences,
      (SELECT setting_value FROM app_settings WHERE setting_key = 'local_sequence') AS sequence`,
  );
  assert.deepEqual(
    { ...counts },
    { held: 1, audits: 1, fences: 1, sequence: "1" },
  );
  await connection.close();
});

test("真实 SQLite：matching RecallActive 现金成交原子完成挂单、订单、审计、outbox、intent 并删除 fence", async () => {
  const { connection, records } = await open();
  await prepareRecall(records);
  const binding = recalledCashInput().terminalContext;
  assert.equal(binding.kind, "recalled");
  if (binding.kind !== "recalled") throw new Error("invalid test binding");
  assert.deepEqual(
    (await records.loadRecallForFence(binding))?.payload,
    payload(),
  );

  const committer = new SqliteAtomicCashOrderCommitter(
    connection,
    encryptor,
    () => "2026-07-28T08:05:00.000Z",
  );
  assert.deepEqual(await committer.completeDurableCashOrder(recalledCashInput()), {
    replayed: false,
    orderGuid: "order-recall-1",
    cashDueCents: 674,
    changeCents: 0,
  });

  const result = await connection.getFirst<{
    status: string;
    recalled_at_iso: string;
    orders: number;
    lines: number;
    tenders: number;
    outbox: number;
    intents: number;
    audits: number;
    fences: number;
  }>(
    `SELECT status, recalled_at_iso,
      (SELECT COUNT(*) FROM local_orders WHERE order_guid = 'order-recall-1') AS orders,
      (SELECT COUNT(*) FROM local_order_lines WHERE order_guid = 'order-recall-1') AS lines,
      (SELECT COUNT(*) FROM order_tenders WHERE order_guid = 'order-recall-1') AS tenders,
      (SELECT COUNT(*) FROM outbox_messages WHERE aggregate_id = 'order-recall-1') AS outbox,
      (SELECT COUNT(*) FROM cash_checkout_intents WHERE order_guid = 'order-recall-1') AS intents,
      (SELECT COUNT(*) FROM audit_events) AS audits,
      (SELECT COUNT(*) FROM terminal_cart_fences) AS fences
     FROM held_order_records WHERE hold_id = 'hold-1'`,
  );
  assert.deepEqual(
    { ...result },
    {
      status: "Recalled",
      recalled_at_iso: "2026-07-28T08:05:00.000Z",
      orders: 1,
      lines: 3,
      tenders: 1,
      outbox: 1,
      intents: 1,
      audits: 3,
      fences: 0,
    },
  );
  await connection.close();
});

test("真实 SQLite：恢复挂单的电子支付成交原子标记 Recalled 并删除 fence", async () => {
  const { connection, records } = await open();
  await prepareRecall(records, "recall-card-1");
  await insertApprovedDraft(connection, "order-recall-card", 674);
  await insertApprovedAttempt(
    connection,
    "payment-attempt-recall-card",
    "order-recall-card",
    674,
  );
  const binding = {
    kind: "recalled",
    scope,
    holdId: "hold-1",
    recallAttemptId: "recall-card-1",
  } as const;
  const committer = new SqliteApprovedPaymentOrderCommitter(
    connection,
    encryptor,
    () => "2026-07-28T08:05:00.000Z",
  );

  const committed = await committer.completeApprovedPaymentOrder(
    approvedRecallPaymentInput(binding),
  );

  assert.equal(committed.completed, true);
  const result = await connection.getFirst<{
    status: string;
    recalled_at_iso: string;
    order_state: string;
    fences: number;
    recall_audits: number;
  }>(
    `SELECT held.status, held.recalled_at_iso,
      (SELECT state FROM local_orders WHERE order_guid = 'order-recall-card') AS order_state,
      (SELECT COUNT(*) FROM terminal_cart_fences) AS fences,
      (SELECT COUNT(*) FROM audit_events WHERE event_type = 'ORDER_RECALL') AS recall_audits
     FROM held_order_records held WHERE held.hold_id = 'hold-1'`,
  );
  assert.deepEqual(
    { ...result },
    {
      status: "Recalled",
      recalled_at_iso: "2026-07-28T08:05:00.000Z",
      order_state: "PendingSync",
      fences: 0,
      recall_audits: 1,
    },
  );
  await connection.close();
});

test("真实 SQLite：恢复挂单的最终混合现金成交原子标记 Recalled", async () => {
  const { connection, records } = await open();
  await prepareRecall(records, "recall-mixed-cash-1");
  await insertApprovedDraft(connection, "order-recall-mixed-cash", 674);
  const binding = {
    kind: "recalled",
    scope,
    holdId: "hold-1",
    recallAttemptId: "recall-mixed-cash-1",
  } as const;
  const store = new SqliteMixedPaymentTenderStore(
    connection,
    {
      createTenderGuid: () => "tender-recall-mixed-cash",
      createAuditEventId: () => "audit-recall-mixed-cash-tender",
    },
    () => "2026-07-28T08:05:00.000Z",
    {
      planner: {
        async planFinalCash() {
          return {
            completionAuditEvents: [{
              eventId: "audit-payment-recall-mixed-cash",
              eventType: "PAYMENT_COMPLETE",
              occurredAtIso: "2026-07-28T08:05:00.000Z",
              orderGuid: "order-recall-mixed-cash",
              correlationId: "cash-action-recall-mixed-cash",
              payload: { source: "mixed-cash" },
            }],
            outbox: {
              messageId: "outbox-recall-mixed-cash",
              aggregateId: "order-recall-mixed-cash",
              kind: "order-sync",
              payloadJson: "{}",
              nextAttemptAtIso: "2026-07-28T08:05:00.000Z",
            },
            fulfilment: { print: null, drawer: null },
          };
        },
      },
      encryptor,
      recallCompletion: {
        async resolve() {
          const completion = approvedRecallPaymentInput(binding)
            .recalledHoldCompletion;
          if (!completion) throw new Error("missing recall completion");
          return {
            ...completion,
            recallAudit: {
              ...completion.recallAudit,
              eventId: "audit-order-recall-mixed-cash",
              orderGuid: "order-recall-mixed-cash",
            },
          };
        },
      },
    },
  );

  const committed = await store.appendCashTenderAtomically({
    actionId: "cash-action-recall-mixed-cash",
    orderGuid: "order-recall-mixed-cash",
    actor: {
      cashierId: "cashier-2",
      cashierName: "Cashier Two",
      userGuid: "user-2",
    },
    amount: { currency: "AUD", cents: 674 },
    tenderedAmount: { currency: "AUD", cents: 675 },
    change: { currency: "AUD", cents: 0 },
  });

  assert.equal(committed.truth.state, "PendingSync");
  const result = await connection.getFirst<{
    status: string;
    fences: number;
    recall_audits: number;
  }>(
    `SELECT held.status,
      (SELECT COUNT(*) FROM terminal_cart_fences) AS fences,
      (SELECT COUNT(*) FROM audit_events WHERE event_type = 'ORDER_RECALL') AS recall_audits
     FROM held_order_records held WHERE held.hold_id = 'hold-1'`,
  );
  assert.deepEqual(
    { ...result },
    { status: "Recalled", fences: 0, recall_audits: 1 },
  );
  await connection.close();
});

test("真实 SQLite：Recall CAS、ORDER_RECALL 审计或 fence 删除失败时整笔现金成交为零", async () => {
  for (const failure of ["cas", "audit", "delete"] as const) {
    const { connection, records } = await open();
    await prepareRecall(records);
    if (failure === "cas") connection.failRecallCompletionCas = true;
    else if (failure === "audit") connection.failAuditEventType = "ORDER_RECALL";
    else connection.failRecallFenceDelete = true;
    const committer = new SqliteAtomicCashOrderCommitter(
      connection,
      encryptor,
      () => "2026-07-28T08:05:00.000Z",
    );
    await assert.rejects(
      committer.completeDurableCashOrder(recalledCashInput()),
      failure === "cas"
        ? /Recalled hold changed before cash completion/
        : failure === "audit"
          ? /simulated held audit failure/
          : /Recall fence changed before cash completion/,
    );
    const counts = await connection.getFirst<{
      status: string;
      orders: number;
      outbox: number;
      intents: number;
      audits: number;
      fences: number;
    }>(
      `SELECT status,
        (SELECT COUNT(*) FROM local_orders) AS orders,
        (SELECT COUNT(*) FROM outbox_messages) AS outbox,
        (SELECT COUNT(*) FROM cash_checkout_intents) AS intents,
        (SELECT COUNT(*) FROM audit_events) AS audits,
        (SELECT COUNT(*) FROM terminal_cart_fences) AS fences
       FROM held_order_records WHERE hold_id = 'hold-1'`,
    );
    assert.deepEqual(
      { ...counts },
      {
        status: "Recalling",
        orders: 0,
        outbox: 0,
        intents: 0,
        audits: 1,
        fences: 1,
      },
      failure,
    );
    await connection.close();
  }
});

test("真实 SQLite：响应丢失重放先验签并直接返回，已删除 recall fence 不阻断原 intent", async () => {
  const { connection, records } = await open();
  await prepareRecall(records);
  const input = recalledCashInput();
  const committer = new SqliteAtomicCashOrderCommitter(
    connection,
    encryptor,
    () => "2026-07-28T08:05:00.000Z",
  );
  const first = await committer.completeDurableCashOrder(input);
  assert.equal(await records.getTerminalFence(scope), null);
  assert.deepEqual(
    await committer.completeDurableCashOrder({
      ...input,
      terminalContext: { kind: "none" },
      recalledHoldCompletion: null,
    }),
    { ...first, replayed: true },
  );
  await assert.rejects(
    committer.completeDurableCashOrder({
      ...input,
      intent: { ...input.intent, requestSignature: "changed-signature" },
    }),
    /replayed with different content/,
  );
  await connection.close();
});

test("真实 SQLite：fence 无 binding、binding 无 fence、错 scope/hold/attempt 均在订单写入前拒绝", async () => {
  const scenarios = [
    {
      name: "fence-without-binding",
      mutate: (input: DurableCashOrderCommit): DurableCashOrderCommit => ({
        ...input,
        terminalContext: { kind: "none" },
        recalledHoldCompletion: null,
      }),
      releaseFence: false,
      pattern: /blocked by an active terminal cart fence/,
    },
    {
      name: "binding-without-fence",
      mutate: (input: DurableCashOrderCommit): DurableCashOrderCommit => input,
      releaseFence: true,
      pattern: /no active terminal cart fence/,
    },
    {
      name: "wrong-attempt",
      mutate: (input: DurableCashOrderCommit): DurableCashOrderCommit => {
        if (input.terminalContext.kind !== "recalled" || !input.recalledHoldCompletion) {
          throw new Error("invalid test input");
        }
        const binding = {
          ...input.terminalContext,
          recallAttemptId: "wrong-attempt",
        };
        return {
          ...input,
          terminalContext: binding,
          recalledHoldCompletion: {
            ...input.recalledHoldCompletion,
            binding,
          },
        };
      },
      releaseFence: false,
      pattern: /does not match the active recall fence/,
    },
    {
      name: "wrong-hold",
      mutate: (input: DurableCashOrderCommit): DurableCashOrderCommit => {
        if (input.terminalContext.kind !== "recalled" || !input.recalledHoldCompletion) {
          throw new Error("invalid test input");
        }
        const binding = {
          ...input.terminalContext,
          holdId: "wrong-hold",
        };
        return {
          ...input,
          terminalContext: binding,
          recalledHoldCompletion: {
            ...input.recalledHoldCompletion,
            binding,
            recallAudit: {
              ...input.recalledHoldCompletion.recallAudit,
              correlationId: "wrong-hold",
            },
          },
        };
      },
      releaseFence: false,
      pattern: /does not match the active recall fence/,
    },
    {
      name: "wrong-scope",
      mutate: (input: DurableCashOrderCommit): DurableCashOrderCommit => {
        if (input.terminalContext.kind !== "recalled" || !input.recalledHoldCompletion) {
          throw new Error("invalid test input");
        }
        const binding = {
          ...input.terminalContext,
          scope: { ...scope, deviceCode: "IPAD-OTHER" },
        };
        return {
          ...input,
          terminalContext: binding,
          recalledHoldCompletion: {
            ...input.recalledHoldCompletion,
            binding,
          },
        };
      },
      releaseFence: false,
      pattern: /belongs to a different terminal/,
    },
  ] as const;

  for (const scenario of scenarios) {
    const { connection, records } = await open();
    await prepareRecall(records);
    const input = recalledCashInput();
    if (scenario.releaseFence) {
      const binding = input.terminalContext;
      if (binding.kind !== "recalled") throw new Error("invalid test binding");
      assert.equal(await records.releaseRecallAfterCartCleared({
        binding,
        releasedAtIso: "2026-07-28T08:02:00.000Z",
      }), true);
    }
    const committer = new SqliteAtomicCashOrderCommitter(
      connection,
      encryptor,
      () => "2026-07-28T08:05:00.000Z",
    );
    await assert.rejects(
      committer.completeDurableCashOrder(scenario.mutate(input)),
      scenario.pattern,
      scenario.name,
    );
    const counts = await connection.getFirst<{
      orders: number;
      outbox: number;
      intents: number;
    }>(
      `SELECT
        (SELECT COUNT(*) FROM local_orders) AS orders,
        (SELECT COUNT(*) FROM outbox_messages) AS outbox,
        (SELECT COUNT(*) FROM cash_checkout_intents) AS intents`,
    );
    assert.deepEqual({ ...counts }, { orders: 0, outbox: 0, intents: 0 });
    await connection.close();
  }
});

test("真实 SQLite：recalled completion 严格校验 sale、binding 和 ORDER_RECALL 审计身份", async () => {
  const { connection, records } = await open();
  await prepareRecall(records);
  const base = recalledCashInput();
  const committer = new SqliteAtomicCashOrderCommitter(
    connection,
    encryptor,
    () => "2026-07-28T08:05:00.000Z",
  );
  if (!base.recalledHoldCompletion) throw new Error("invalid test completion");
  const invalidInputs: readonly DurableCashOrderCommit[] = [
    {
      ...base,
      command: {
        ...base.command,
        order: {
          ...base.command.order,
          lines: [{
            ...base.command.order.lines[0]!,
            kind: "return",
            returnSourceKey: "return-1",
            originalOrderGuid: "old-order",
          }],
        },
      },
    },
    {
      ...base,
      recalledHoldCompletion: {
        ...base.recalledHoldCompletion,
        recallAudit: {
          ...base.recalledHoldCompletion.recallAudit,
          eventType: "SALE_COMPLETE",
        },
      },
    },
    {
      ...base,
      recalledHoldCompletion: {
        ...base.recalledHoldCompletion,
        recallAudit: {
          ...base.recalledHoldCompletion.recallAudit,
          orderGuid: "other-order",
        },
      },
    },
    {
      ...base,
      recalledHoldCompletion: {
        ...base.recalledHoldCompletion,
        recallAudit: {
          ...base.recalledHoldCompletion.recallAudit,
          correlationId: "other-hold",
        },
      },
    },
  ];
  for (const invalid of invalidInputs) {
    await assert.rejects(
      committer.completeDurableCashOrder(invalid),
      /return lines|Recall audit type, order guid, or correlation id/,
    );
  }
  assert.equal(
    Number((await connection.getFirst<{ count: number }>(
      "SELECT COUNT(*) AS count FROM local_orders",
    ))?.count),
    0,
  );
  await connection.close();
});
