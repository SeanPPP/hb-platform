import assert from "node:assert/strict";
import { DatabaseSync, type SQLInputValue } from "node:sqlite";
import test from "node:test";

import type {
  ApprovedPaymentOrderCommit,
  DurableCashOrderCommit,
  HeldOrderPayloadV1,
  HeldOrderRecordRepositoryPort,
} from "../contracts";

import { applyMigrations } from "./migrations";
import {
  SqliteApprovedPaymentOrderCommitter,
  SqliteAtomicCashOrderCommitter,
} from "./pos-database";
import {
  SqliteMixedPaymentTenderStore,
  type MixedCashFinalCompletionDependencies,
} from "./sqlite-mixed-payment-tender-store";
import { createSqliteRepositories } from "./sqlite-repositories";
import type {
  SqliteConnectionPort,
  SqlRunResult,
  SqlValue,
} from "@hb/pos-db/core/db/types";

import {
  SqliteSharedHeldOrderClaimRepository,
} from "@/features/shared-held-orders/shared-held-order-claim-repository";
import {
  fakeEncryptor,
} from "@/features/shared-held-orders/shared-held-order-test-support";
import {
  normalizeSharedSaleCartV1,
  type SharedSaleCartV1,
} from "@hb/pos-domain/features/shared-held-orders/shared-sale-cart-v1";

const NOW = "2026-07-28T08:00:00.000Z";
const SOLD_AT = "2026-07-28T08:05:00.000Z";
const SCOPE = { storeCode: "S1", deviceCode: "IPAD-01" } as const;
const HELD_BY = { cashierId: "cashier-1", cashierName: "Cashier One" } as const;

const ENCRYPTOR = {
  async encrypt(plaintext: string): Promise<Uint8Array> {
    return new TextEncoder().encode(plaintext);
  },
  async decrypt(ciphertext: Uint8Array): Promise<string> {
    return new TextDecoder().decode(ciphertext);
  },
};

/** 追踪来源表 SELECT，并在 RemoteClaim 完成 CAS 上注入失败。 */
class TrackingConnection implements SqliteConnectionPort {
  public sourceSelectQueries = 0;
  public failNextClaimComplete = false;
  private transactionActive = false;
  private readonly queue = new AsyncSerialQueue();

  public constructor(private readonly database: DatabaseSync) {}

  private track(sql: string): void {
    if (
      /SELECT/i.test(sql) &&
      sql.includes("order_held_order_sources")
    ) {
      this.sourceSelectQueries += 1;
    }
  }

  public async exec(sql: string): Promise<void> {
    this.database.exec(sql);
  }

  public async run(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<SqlRunResult> {
    this.track(sql);
    if (
      this.failNextClaimComplete &&
      sql.includes("shared_held_order_claim_records") &&
      sql.includes("SET state = 'Completed'")
    ) {
      this.failNextClaimComplete = false;
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
    this.track(sql);
    const row = this.database
      .prepare(sql)
      .get(...parameters as readonly SQLInputValue[]);
    return row === undefined ? null : row as T;
  }

  public async getAll<T extends object>(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<readonly T[]> {
    this.track(sql);
    return this.database
      .prepare(sql)
      .all(...parameters as readonly SQLInputValue[]) as unknown as readonly T[];
  }

  public async withExclusiveTransaction<T>(
    operation: (transaction: SqliteConnectionPort) => Promise<T>,
  ): Promise<T> {
    if (this.transactionActive) {
      return Promise.reject(new Error("Nested test transaction."));
    }
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

function sharedCart(): SharedSaleCartV1 {
  return normalizeSharedSaleCartV1({
    version: 1,
    pricingState: {
      revision: 7,
      mode: "sale",
      asOfIso: NOW,
      promotions: [],
      lines: [
        {
          lineId: "line-shared-1",
          productCode: "P-SHARED",
          itemNumber: "100",
          lookupCode: "100",
          displayName: "Shared item",
          quantity: 1,
          unitPriceCents: 674,
          basePriceSource: "catalog",
          syncProvenance: { referenceCode: null, priceSource: 0 },
          kind: "sale",
          returnSourceKey: null,
          originalOrderGuid: null,
          originalOrderDetailGuid: null,
          discountState: { mode: "none" },
        },
      ],
    },
  });
}

function legacyPayload(): HeldOrderPayloadV1 {
  const cart = sharedCart();
  const line = cart.pricingState.lines[0]!;
  return {
    version: 1,
    pricingState: {
      revision: cart.pricingState.revision,
      mode: "sale",
      asOfIso: cart.pricingState.asOfIso,
      promotions: [],
      lines: [{
        lineId: line.lineId,
        productCode: line.productCode,
        itemNumber: line.itemNumber,
        lookupCode: line.lookupCode,
        displayName: line.displayName,
        quantity: line.quantity,
        unitPriceCents: line.unitPriceCents,
        basePriceSource: line.basePriceSource,
        syncProvenance: line.syncProvenance ?? {
          referenceCode: null,
          priceSource: 0,
        },
        kind: "sale",
        returnSourceKey: null,
        originalOrderGuid: null,
        originalOrderDetailGuid: null,
        discountState: { kind: "none" },
      }],
    },
  };
}

async function open(
  encryptor: typeof ENCRYPTOR = ENCRYPTOR,
): Promise<Readonly<{
  connection: TrackingConnection;
  records: HeldOrderRecordRepositoryPort;
}>> {
  const connection = new TrackingConnection(
    new DatabaseSync(":memory:", { enableForeignKeyConstraints: true }),
  );
  await applyMigrations(connection, () => NOW);
  return {
    connection,
    records: createSqliteRepositories(connection, {
      nowIso: () => NOW,
      createLeaseId: () => "lease",
      encryptor,
    }).heldOrderRecords,
  };
}

async function holdAndRecall(
  records: HeldOrderRecordRepositoryPort,
  recallAttemptId: string,
  holdId = "hold-1",
): Promise<void> {
  await records.hold({
    holdId,
    scope: SCOPE,
    heldBy: HELD_BY,
    payload: legacyPayload(),
    heldAtIso: NOW,
    audit: {
      eventId: `audit-hold-${holdId}`,
      eventType: "ORDER_HOLD",
      occurredAtIso: NOW,
      orderGuid: null,
      correlationId: holdId,
      payload: { action: "hold", result: "succeeded" },
    },
  });
  await records.confirmHoldCartCleared({ scope: SCOPE, holdId });
  assert.ok(
    await records.claimRecall({
      holdId,
      scope: SCOPE,
      recalledBy: { cashierId: "cashier-2", cashierName: "Cashier Two" },
      recallAttemptId,
      recallingAtIso: "2026-07-28T08:01:00.000Z",
    }),
  );
}

async function prepareRemoteClaim(
  connection: TrackingConnection,
  holdId: string,
  recallAttemptId: string,
  claimGuid: string,
): Promise<void> {
  const claims = new SqliteSharedHeldOrderClaimRepository(
    connection,
    fakeEncryptor,
  );
  assertPrepared(await claims.prepareClaim({
    claimGuid,
    holdGuid: holdId,
    recallAttemptId,
    scope: SCOPE,
    source: "RemoteClaim",
    prepareIdempotencyKey: `prepare-${claimGuid}`,
    payload: sharedCart(),
    preparedExpiresAtIso: "2026-07-28T08:30:00.000Z",
    heldAtIso: NOW,
    heldBy: { cashierId: "cashier-1", cashierName: "Cashier One" },
    createdAtIso: "2026-07-28T08:02:00.000Z",
  }), claimGuid);
}

test("RemoteClaim synthetic 行写入本地 payload 格式，并兼容读取旧 shared payload", async () => {
  const { connection, records } = await open(fakeEncryptor);
  const holdId = "hold-synthetic-payload";
  const recallAttemptId = "attempt-synthetic-payload";
  await prepareRemoteClaim(
    connection,
    holdId,
    recallAttemptId,
    "claim-synthetic-payload",
  );

  const written = await connection.getFirst<{ payload_ciphertext: Uint8Array }>(
    "SELECT payload_ciphertext FROM held_order_records WHERE hold_id = ?",
    [holdId],
  );
  assert.ok(written?.payload_ciphertext instanceof Uint8Array);
  const writtenPayload = JSON.parse(
    await fakeEncryptor.decrypt(written.payload_ciphertext),
  ) as { pricingState: { lines: { discountState: unknown }[] } };
  assert.deepEqual(writtenPayload.pricingState.lines[0]?.discountState, {
    kind: "none",
  });

  // 兼容修复前已落库的 synthetic 行：旧版本直接复用了 shared wire payload。
  await connection.run(
    "UPDATE held_order_records SET payload_ciphertext = ? WHERE hold_id = ?",
    [await fakeEncryptor.encrypt(JSON.stringify(sharedCart())), holdId],
  );
  const recoverable = await records.listRecoverable(SCOPE);
  assert.equal(recoverable.length, 1);
  assert.equal(recoverable[0]?.hold.holdId, holdId);
  assert.deepEqual(
    recoverable[0]?.payload.pricingState.lines[0]?.discountState,
    { kind: "none" },
  );
});

function assertPrepared(
  result: { outcome: string },
  claimGuid: string,
): void {
  assert.equal(result.outcome, "prepared");
}

function recalledCashInput(
  overrides: Partial<DurableCashOrderCommit> = {},
): DurableCashOrderCommit {
  const orderGuid = "order-recall-cash";
  const binding = {
    kind: "recalled" as const,
    scope: SCOPE,
    holdId: "hold-1",
    recallAttemptId: "attempt-cash",
  };
  const line = sharedCart().pricingState.lines[0]!;
  return {
    intent: {
      checkoutIntentId: "intent-recall-cash",
      requestSignature: "signature-recall-cash",
      cashDueCents: 674,
      changeCents: 0,
    },
    command: {
      order: {
        orderGuid,
        localSequence: 2,
        storeCode: SCOPE.storeCode,
        deviceCode: SCOPE.deviceCode,
        cashierId: "cashier-2",
        cashierName: "Cashier Two",
        soldAtIso: SOLD_AT,
        state: "PendingSync",
        total: { currency: "AUD", cents: 674 },
        discount: { currency: "AUD", cents: 0 },
        actualAmount: { currency: "AUD", cents: 674 },
        lines: [{
          lineId: line.lineId,
          productCode: line.productCode,
          itemNumber: line.itemNumber,
          lookupCode: line.lookupCode,
          displayName: line.displayName,
          quantity: String(line.quantity),
          unitPrice: { currency: "AUD", cents: line.unitPriceCents },
          discount: { currency: "AUD", cents: 0 },
          actualAmount: { currency: "AUD", cents: line.unitPriceCents },
          priceSource: line.basePriceSource,
          syncProvenance: line.syncProvenance ?? {
            referenceCode: null,
            priceSource: 0,
          },
          kind: "sale" as const,
          returnSourceKey: null,
          originalOrderGuid: null,
          originalOrderDetailGuid: null,
        }],
        tenders: [{
          tenderGuid: "tender-recall-cash",
          method: "cash",
          amount: { currency: "AUD", cents: 674 },
          reference: null,
          reservationToken: null,
        }],
        originalOrderGuid: null,
      },
      auditEvents: [{
        eventId: "audit-sale-recall-cash",
        eventType: "SALE_COMPLETE",
        occurredAtIso: SOLD_AT,
        orderGuid,
        correlationId: orderGuid,
        payload: { amountCents: 674 },
      }],
      outbox: {
        messageId: "outbox-recall-cash",
        aggregateId: orderGuid,
        kind: "order-sync",
        payloadJson: JSON.stringify({ orderGuid }),
        nextAttemptAtIso: SOLD_AT,
      },
      requiresDrawer: false,
      printPolicy: "never",
    },
    fulfilment: { print: null, drawer: null },
    terminalContext: binding,
    recalledHoldCompletion: {
      binding,
      recalledAtIso: SOLD_AT,
      recallAudit: {
        eventId: "audit-order-recall-cash",
        eventType: "ORDER_RECALL",
        occurredAtIso: SOLD_AT,
        orderGuid,
        correlationId: "hold-1",
        payload: {
          source: "held-order",
          result: "succeeded",
          itemCount: 1,
          actualAmountCents: 674,
        },
      },
    },
    ...overrides,
  };
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
    [orderGuid, SCOPE.storeCode, SCOPE.deviceCode, SOLD_AT, amountCents, amountCents, NOW, NOW],
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
    [attemptId, `key-${attemptId}`, orderGuid, amountCents, NOW, NOW],
  );
}

function approvedRecallPaymentInput(
  orderGuid: string,
  attemptId: string,
  tenderGuid: string,
  holdId: string,
  recallAttemptId: string,
): ApprovedPaymentOrderCommit {
  const binding = {
    kind: "recalled" as const,
    scope: SCOPE,
    holdId,
    recallAttemptId,
  };
  return {
    attemptId,
    orderGuid,
    tenderGuid,
    completionAuditEvents: [{
      eventId: `audit-payment-${tenderGuid}`,
      eventType: "PAYMENT_COMPLETE",
      occurredAtIso: SOLD_AT,
      orderGuid,
      correlationId: attemptId,
      payload: { source: "approved-payment" },
    }],
    outbox: {
      messageId: `outbox-${tenderGuid}`,
      aggregateId: orderGuid,
      kind: "order-sync",
      payloadJson: JSON.stringify({ orderGuid }),
      nextAttemptAtIso: SOLD_AT,
    },
    fulfilment: { print: null, drawer: null },
    recalledHoldCompletion: {
      binding,
      recalledAtIso: SOLD_AT,
      recallAudit: {
        eventId: `audit-order-recall-${tenderGuid}`,
        eventType: "ORDER_RECALL",
        occurredAtIso: SOLD_AT,
        orderGuid,
        correlationId: holdId,
        payload: {
          source: "held-order",
          result: "succeeded",
          itemCount: 1,
          actualAmountCents: 674,
        },
      },
    },
  };
}

function mixedDependencies(
  orderGuid: string,
  binding: NonNullable<
    ApprovedPaymentOrderCommit["recalledHoldCompletion"]
  >["binding"],
): MixedCashFinalCompletionDependencies {
  return {
    planner: {
      async planFinalCash() {
        return {
          completionAuditEvents: [{
            eventId: `audit-payment-mixed-${orderGuid}`,
            eventType: "PAYMENT_COMPLETE",
            occurredAtIso: SOLD_AT,
            orderGuid,
            correlationId: `cash-action-${orderGuid}`,
            payload: { source: "mixed-cash" },
          }],
          outbox: {
            messageId: `outbox-mixed-${orderGuid}`,
            aggregateId: orderGuid,
            kind: "order-sync",
            payloadJson: "{}",
            nextAttemptAtIso: SOLD_AT,
          },
          fulfilment: { print: null, drawer: null },
        };
      },
    },
    encryptor: ENCRYPTOR,
    recallCompletion: {
      async resolve(inputOrderGuid, actor) {
        const completion = approvedRecallPaymentInput(
          inputOrderGuid,
          "unused-attempt",
          "unused-tender",
          binding.holdId,
          binding.recallAttemptId,
        ).recalledHoldCompletion;
        if (!completion) throw new Error("missing recall completion");
        return {
          ...completion,
          recallAudit: {
            ...completion.recallAudit,
            eventId: `audit-order-recall-mixed-${inputOrderGuid}`,
            orderGuid: inputOrderGuid,
          },
        };
      },
    },
  };
}

async function assertSource(
  connection: SqliteConnectionPort,
  orderGuid: string,
  expected: Readonly<{
    holdGuid: string;
    claimGuid: string | null;
    sourceKind: 1 | 2;
  }>,
): Promise<void> {
  const row = await connection.getFirst<{
    hold_guid: string;
    claim_guid: string | null;
    source_kind: number;
  }>(
    "SELECT hold_guid, claim_guid, source_kind FROM order_held_order_sources WHERE order_guid = ?",
    [orderGuid],
  );
  assert.ok(row);
  assert.deepEqual(
    { ...row },
    {
      hold_guid: expected.holdGuid,
      claim_guid: expected.claimGuid,
      source_kind: expected.sourceKind,
    },
  );
  const marker = await connection.getFirst<{ is_shared_held_origin: number }>(
    "SELECT is_shared_held_origin FROM local_orders WHERE order_guid = ?",
    [orderGuid],
  );
  assert.equal(marker?.is_shared_held_origin, 1);
}

test("普通现金订单完成不查询、不写入来源表", async () => {
  const { connection } = await open();
  const committer = new SqliteAtomicCashOrderCommitter(
    connection,
    ENCRYPTOR,
    () => SOLD_AT,
  );
  const input = recalledCashInput({
    intent: {
      checkoutIntentId: "intent-ordinary-cash",
      requestSignature: "signature-ordinary-cash",
      cashDueCents: 674,
      changeCents: 0,
    },
    command: {
      ...recalledCashInput().command,
      order: {
        ...recalledCashInput().command.order,
        orderGuid: "order-ordinary-cash",
        tenders: [{
          ...recalledCashInput().command.order.tenders[0]!,
          tenderGuid: "tender-ordinary-cash",
        }],
      },
      auditEvents: [{
        ...recalledCashInput().command.auditEvents[0]!,
        orderGuid: "order-ordinary-cash",
      }],
      outbox: {
        ...recalledCashInput().command.outbox,
        aggregateId: "order-ordinary-cash",
      },
    },
    terminalContext: { kind: "none" },
    recalledHoldCompletion: null,
  });

  await committer.completeDurableCashOrder(input);
  assert.equal(connection.sourceSelectQueries, 0);
  assert.equal(
    await connection.getFirst(
      "SELECT order_guid FROM order_held_order_sources WHERE order_guid = 'order-ordinary-cash'",
    ),
    null,
  );
  const marker = await connection.getFirst<{ is_shared_held_origin: number }>(
    "SELECT is_shared_held_origin FROM local_orders WHERE order_guid = 'order-ordinary-cash'",
  );
  assert.equal(marker?.is_shared_held_origin, 0);
});

test("现金取单完成：OfflineOrigin 来源与 outbox 原子写入", async () => {
  const { connection, records } = await open();
  await holdAndRecall(records, "attempt-cash");
  const committer = new SqliteAtomicCashOrderCommitter(
    connection,
    ENCRYPTOR,
    () => SOLD_AT,
  );
  const result = await committer.completeDurableCashOrder(recalledCashInput());
  assert.deepEqual(result, {
    replayed: false,
    orderGuid: "order-recall-cash",
    cashDueCents: 674,
    changeCents: 0,
  });
  await assertSource(connection, "order-recall-cash", {
    holdGuid: "hold-1",
    claimGuid: null,
    sourceKind: 2,
  });
  const held = await connection.getFirst<{ status: string }>(
    "SELECT status FROM held_order_records WHERE hold_id = 'hold-1'",
  );
  assert.equal(held?.status, "Recalled");
  assert.equal(
    (await connection.getFirst<{ n: number }>(
      "SELECT COUNT(*) AS n FROM terminal_cart_fences",
    ))?.n,
    0,
  );
});

test("现金取单完成：durable OfflineOrigin claim 不得伪装成 RemoteClaim", async () => {
  const { connection, records } = await open();
  await records.hold({
    holdId: "hold-1",
    scope: SCOPE,
    heldBy: HELD_BY,
    payload: legacyPayload(),
    heldAtIso: NOW,
    audit: {
      eventId: "audit-hold-offline-claim",
      eventType: "ORDER_HOLD",
      occurredAtIso: NOW,
      orderGuid: null,
      correlationId: "hold-1",
      payload: { action: "hold", result: "succeeded" },
    },
  });
  await records.confirmHoldCartCleared({ scope: SCOPE, holdId: "hold-1" });
  const claims = new SqliteSharedHeldOrderClaimRepository(
    connection,
    fakeEncryptor,
  );
  assertPrepared(
    await claims.prepareClaim({
      claimGuid: "claim-offline-origin",
      holdGuid: "hold-1",
      recallAttemptId: "attempt-cash",
      scope: SCOPE,
      source: "OfflineOrigin",
      prepareIdempotencyKey: "prepare-offline-origin",
      payload: sharedCart(),
      preparedExpiresAtIso: "2026-07-28T08:30:00.000Z",
      heldAtIso: NOW,
      heldBy: HELD_BY,
      createdAtIso: "2026-07-28T08:01:00.000Z",
    }),
    "claim-offline-origin",
  );
  assert.equal(
    await claims.activatePreparedClaim({
      claimGuid: "claim-offline-origin",
      prepareIdempotencyKey: "prepare-offline-origin",
      activateIdempotencyKey: "activate-offline-origin",
      serverRevision: null,
      activatedAtIso: "2026-07-28T08:02:00.000Z",
    }),
    true,
  );

  const committer = new SqliteAtomicCashOrderCommitter(
    connection,
    ENCRYPTOR,
    () => SOLD_AT,
  );
  await committer.completeDurableCashOrder(recalledCashInput());

  await assertSource(connection, "order-recall-cash", {
    holdGuid: "hold-1",
    claimGuid: null,
    sourceKind: 2,
  });
  assert.equal(
    (await claims.getClaim("claim-offline-origin"))?.state,
    "Completed",
  );
});

test("现金 RemoteClaim（Active）：来源 RemoteClaim + claim 绑定 Completed", async () => {
  const { connection } = await open();
  await prepareRemoteClaim(
    connection,
    "hold-1",
    "attempt-cash-remote",
    "claim-cash-remote",
  );
  const claims = new SqliteSharedHeldOrderClaimRepository(
    connection,
    fakeEncryptor,
  );
  assert.equal(
    await claims.activatePreparedClaim({
      claimGuid: "claim-cash-remote",
      prepareIdempotencyKey: "prepare-claim-cash-remote",
      activateIdempotencyKey: "activate-cash-remote",
      serverRevision: 2_147_483_653,
      activatedAtIso: NOW,
    }),
    true,
  );

  const committer = new SqliteAtomicCashOrderCommitter(
    connection,
    ENCRYPTOR,
    () => SOLD_AT,
  );
  await committer.completeDurableCashOrder(
    recalledCashInput({
      intent: {
        checkoutIntentId: "intent-recall-cash-remote",
        requestSignature: "signature-recall-cash-remote",
        cashDueCents: 674,
        changeCents: 0,
      },
      command: {
        ...recalledCashInput().command,
        order: {
          ...recalledCashInput().command.order,
          orderGuid: "order-recall-cash-remote",
        },
        auditEvents: [{
          ...recalledCashInput().command.auditEvents[0]!,
          orderGuid: "order-recall-cash-remote",
        }],
        outbox: {
          ...recalledCashInput().command.outbox,
          aggregateId: "order-recall-cash-remote",
        },
      },
      recalledHoldCompletion: {
        ...recalledCashInput().recalledHoldCompletion!,
        binding: {
          kind: "recalled",
          scope: SCOPE,
          holdId: "hold-1",
          recallAttemptId: "attempt-cash-remote",
        },
        recallAudit: {
          ...recalledCashInput().recalledHoldCompletion!.recallAudit,
          orderGuid: "order-recall-cash-remote",
          correlationId: "hold-1",
        },
      },
      terminalContext: {
        kind: "recalled",
        scope: SCOPE,
        holdId: "hold-1",
        recallAttemptId: "attempt-cash-remote",
      },
    }),
  );
  await assertSource(connection, "order-recall-cash-remote", {
    holdGuid: "hold-1",
    claimGuid: "claim-cash-remote",
    sourceKind: 1,
  });
  const claim = await claims.getClaim("claim-cash-remote");
  assert.equal(claim?.state, "Completed");
  assert.equal(claim?.boundOrderGuid, "order-recall-cash-remote");
  assert.equal(claim?.serverRevision, 2_147_483_653);
});

test("RemoteClaim 未知 activate（Prepared）完成：来源仍 RemoteClaim，声明原子 Superseded 防止重复恢复", async () => {
  const { connection } = await open();
  await prepareRemoteClaim(
    connection,
    "hold-1",
    "attempt-cash-prepared",
    "claim-cash-prepared",
  );
  const claims = new SqliteSharedHeldOrderClaimRepository(
    connection,
    fakeEncryptor,
  );
  const committer = new SqliteAtomicCashOrderCommitter(
    connection,
    ENCRYPTOR,
    () => SOLD_AT,
  );
  await committer.completeDurableCashOrder(
    recalledCashInput({
      intent: {
        checkoutIntentId: "intent-recall-cash-prepared",
        requestSignature: "signature-recall-cash-prepared",
        cashDueCents: 674,
        changeCents: 0,
      },
      command: {
        ...recalledCashInput().command,
        order: {
          ...recalledCashInput().command.order,
          orderGuid: "order-recall-cash-prepared",
        },
        auditEvents: [{
          ...recalledCashInput().command.auditEvents[0]!,
          orderGuid: "order-recall-cash-prepared",
        }],
        outbox: {
          ...recalledCashInput().command.outbox,
          aggregateId: "order-recall-cash-prepared",
        },
      },
      recalledHoldCompletion: {
        ...recalledCashInput().recalledHoldCompletion!,
        binding: {
          kind: "recalled",
          scope: SCOPE,
          holdId: "hold-1",
          recallAttemptId: "attempt-cash-prepared",
        },
        recallAudit: {
          ...recalledCashInput().recalledHoldCompletion!.recallAudit,
          orderGuid: "order-recall-cash-prepared",
          correlationId: "hold-1",
        },
      },
      terminalContext: {
        kind: "recalled",
        scope: SCOPE,
        holdId: "hold-1",
        recallAttemptId: "attempt-cash-prepared",
      },
    }),
  );
  await assertSource(connection, "order-recall-cash-prepared", {
    holdGuid: "hold-1",
    claimGuid: "claim-cash-prepared",
    sourceKind: 1,
  });
  const claim = await claims.getClaim("claim-cash-prepared");
  assert.equal(claim?.state, "Superseded");
  assert.equal(claim?.boundOrderGuid, null);
  assert.equal(
    claim?.supersedeIdempotencyKey,
    "completed:order-recall-cash-prepared",
  );
  assert.equal(
    (await connection.getFirst<{ n: number }>(
      "SELECT COUNT(*) AS n FROM terminal_cart_fences",
    ))?.n,
    0,
  );
});

test("RemoteClaim synthetic 挂单丢失 durable claim 时 fail closed 且订单整体回滚", async () => {
  const { connection } = await open();
  await prepareRemoteClaim(
    connection,
    "hold-1",
    "attempt-cash-corrupt",
    "claim-cash-corrupt",
  );
  await connection.run(
    "DELETE FROM shared_held_order_claim_records WHERE claim_guid = ?",
    ["claim-cash-corrupt"],
  );

  const base = recalledCashInput();
  const orderGuid = "order-recall-cash-corrupt";
  const committer = new SqliteAtomicCashOrderCommitter(
    connection,
    ENCRYPTOR,
    () => SOLD_AT,
  );
  await assert.rejects(
    committer.completeDurableCashOrder({
      ...base,
      intent: {
        ...base.intent,
        checkoutIntentId: "intent-recall-cash-corrupt",
        requestSignature: "signature-recall-cash-corrupt",
      },
      command: {
        ...base.command,
        order: { ...base.command.order, orderGuid },
        auditEvents: base.command.auditEvents.map((event) => ({
          ...event,
          orderGuid,
        })),
        outbox: { ...base.command.outbox, aggregateId: orderGuid },
      },
      recalledHoldCompletion: {
        ...base.recalledHoldCompletion!,
        binding: {
          kind: "recalled",
          scope: SCOPE,
          holdId: "hold-1",
          recallAttemptId: "attempt-cash-corrupt",
        },
        recallAudit: {
          ...base.recalledHoldCompletion!.recallAudit,
          orderGuid,
        },
      },
      terminalContext: {
        kind: "recalled",
        scope: SCOPE,
        holdId: "hold-1",
        recallAttemptId: "attempt-cash-corrupt",
      },
    }),
    /SHARED_HELD_ORDER_SOURCE_CLAIM_MISSING/,
  );

  assert.equal(
    await connection.getFirst(
      "SELECT order_guid FROM local_orders WHERE order_guid = ?",
      [orderGuid],
    ),
    null,
  );
  assert.equal(
    await connection.getFirst(
      "SELECT order_guid FROM order_held_order_sources WHERE order_guid = ?",
      [orderGuid],
    ),
    null,
  );
  assert.equal(
    (await connection.getFirst<{ n: number }>(
      "SELECT COUNT(*) AS n FROM outbox_messages WHERE aggregate_id = ?",
      [orderGuid],
    ))?.n,
    0,
  );
});

test("批准付款 RemoteClaim：来源、claim 绑定/Completed 与订单原子完成", async () => {
  const { connection } = await open();
  await prepareRemoteClaim(
    connection,
    "hold-1",
    "attempt-card-remote",
    "claim-card-remote",
  );
  const claims = new SqliteSharedHeldOrderClaimRepository(
    connection,
    fakeEncryptor,
  );
  await claims.activatePreparedClaim({
    claimGuid: "claim-card-remote",
    prepareIdempotencyKey: "prepare-claim-card-remote",
    activateIdempotencyKey: "activate-card-remote",
    serverRevision: 9,
    activatedAtIso: NOW,
  });
  await insertApprovedDraft(connection, "order-recall-card-remote", 674);
  await insertApprovedAttempt(
    connection,
    "attempt-card-remote",
    "order-recall-card-remote",
    674,
  );
  const committer = new SqliteApprovedPaymentOrderCommitter(
    connection,
    ENCRYPTOR,
    () => SOLD_AT,
  );
  const committed = await committer.completeApprovedPaymentOrder(
    approvedRecallPaymentInput(
      "order-recall-card-remote",
      "attempt-card-remote",
      "tender-card-remote",
      "hold-1",
      "attempt-card-remote",
    ),
  );
  assert.equal(committed.completed, true);
  await assertSource(connection, "order-recall-card-remote", {
    holdGuid: "hold-1",
    claimGuid: "claim-card-remote",
    sourceKind: 1,
  });
  const claim = await claims.getClaim("claim-card-remote");
  assert.equal(claim?.state, "Completed");
  assert.equal(claim?.boundOrderGuid, "order-recall-card-remote");
});

test("最终混合现金 OfflineOrigin：来源原子写入且 fence 清除", async () => {
  const { connection, records } = await open();
  await holdAndRecall(records, "attempt-mixed-offline");
  await insertApprovedDraft(connection, "order-recall-mixed-offline", 674);
  const binding = {
    kind: "recalled" as const,
    scope: SCOPE,
    holdId: "hold-1",
    recallAttemptId: "attempt-mixed-offline",
  };
  const store = new SqliteMixedPaymentTenderStore(
    connection,
    {
      createTenderGuid: () => "tender-mixed-offline",
      createAuditEventId: () => "audit-mixed-offline-tender",
    },
    () => SOLD_AT,
    mixedDependencies("order-recall-mixed-offline", binding),
  );
  const committed = await store.appendCashTenderAtomically({
    actionId: "cash-action-mixed-offline",
    orderGuid: "order-recall-mixed-offline",
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
  await assertSource(connection, "order-recall-mixed-offline", {
    holdGuid: "hold-1",
    claimGuid: null,
    sourceKind: 2,
  });
  assert.equal(
    (await connection.getFirst<{ n: number }>(
      "SELECT COUNT(*) AS n FROM terminal_cart_fences",
    ))?.n,
    0,
  );
});

test("最终混合现金 RemoteClaim：来源 RemoteClaim 且 claim 完成", async () => {
  const { connection } = await open();
  await prepareRemoteClaim(
    connection,
    "hold-1",
    "attempt-mixed-remote",
    "claim-mixed-remote",
  );
  const claims = new SqliteSharedHeldOrderClaimRepository(
    connection,
    fakeEncryptor,
  );
  await claims.activatePreparedClaim({
    claimGuid: "claim-mixed-remote",
    prepareIdempotencyKey: "prepare-claim-mixed-remote",
    activateIdempotencyKey: "activate-mixed-remote",
    serverRevision: 11,
    activatedAtIso: NOW,
  });
  await insertApprovedDraft(connection, "order-recall-mixed-remote", 674);
  const binding = {
    kind: "recalled" as const,
    scope: SCOPE,
    holdId: "hold-1",
    recallAttemptId: "attempt-mixed-remote",
  };
  const store = new SqliteMixedPaymentTenderStore(
    connection,
    {
      createTenderGuid: () => "tender-mixed-remote",
      createAuditEventId: () => "audit-mixed-remote-tender",
    },
    () => SOLD_AT,
    mixedDependencies("order-recall-mixed-remote", binding),
  );
  const committed = await store.appendCashTenderAtomically({
    actionId: "cash-action-mixed-remote",
    orderGuid: "order-recall-mixed-remote",
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
  await assertSource(connection, "order-recall-mixed-remote", {
    holdGuid: "hold-1",
    claimGuid: "claim-mixed-remote",
    sourceKind: 1,
  });
  const claim = await claims.getClaim("claim-mixed-remote");
  assert.equal(claim?.state, "Completed");
  assert.equal(claim?.boundOrderGuid, "order-recall-mixed-remote");
});

test("RemoteClaim 完成中 claim CAS 失败：订单、来源、outbox 与 intent 整体回滚，重试成功", async () => {
  const { connection } = await open();
  await prepareRemoteClaim(
    connection,
    "hold-1",
    "attempt-cash-failover",
    "claim-cash-failover",
  );
  const claims = new SqliteSharedHeldOrderClaimRepository(
    connection,
    fakeEncryptor,
  );
  await claims.activatePreparedClaim({
    claimGuid: "claim-cash-failover",
    prepareIdempotencyKey: "prepare-claim-cash-failover",
    activateIdempotencyKey: "activate-cash-failover",
    serverRevision: 21,
    activatedAtIso: NOW,
  });

  const input = recalledCashInput({
    intent: {
      checkoutIntentId: "intent-recall-cash-failover",
      requestSignature: "signature-recall-cash-failover",
      cashDueCents: 674,
      changeCents: 0,
    },
    command: {
      ...recalledCashInput().command,
      order: {
        ...recalledCashInput().command.order,
        orderGuid: "order-recall-cash-failover",
      },
      auditEvents: [{
        ...recalledCashInput().command.auditEvents[0]!,
        orderGuid: "order-recall-cash-failover",
      }],
      outbox: {
        ...recalledCashInput().command.outbox,
        aggregateId: "order-recall-cash-failover",
      },
    },
    recalledHoldCompletion: {
      ...recalledCashInput().recalledHoldCompletion!,
      binding: {
        kind: "recalled",
        scope: SCOPE,
        holdId: "hold-1",
        recallAttemptId: "attempt-cash-failover",
      },
      recallAudit: {
        ...recalledCashInput().recalledHoldCompletion!.recallAudit,
        orderGuid: "order-recall-cash-failover",
        correlationId: "hold-1",
      },
    },
    terminalContext: {
      kind: "recalled",
      scope: SCOPE,
      holdId: "hold-1",
      recallAttemptId: "attempt-cash-failover",
    },
  });
  const committer = new SqliteAtomicCashOrderCommitter(
    connection,
    ENCRYPTOR,
    () => SOLD_AT,
  );

  connection.failNextClaimComplete = true;
  await assert.rejects(
    committer.completeDurableCashOrder(input),
    /Shared hold claim changed before completion/,
  );
  // 事务整体回滚：无订单、无来源、无 outbox、无 intent。
  assert.equal(
    await connection.getFirst(
      "SELECT order_guid FROM local_orders WHERE order_guid = 'order-recall-cash-failover'",
    ),
    null,
  );
  assert.equal(
    await connection.getFirst(
      "SELECT order_guid FROM order_held_order_sources WHERE order_guid = 'order-recall-cash-failover'",
    ),
    null,
  );
  assert.equal(
    (await connection.getFirst<{ n: number }>(
      "SELECT COUNT(*) AS n FROM outbox_messages WHERE aggregate_id = 'order-recall-cash-failover'",
    ))?.n,
    0,
  );
  // claim 仍 Active 未绑定，fence 保留，held 仍 Recalling。
  const claim = await claims.getClaim("claim-cash-failover");
  assert.equal(claim?.state, "Active");
  assert.equal(claim?.boundOrderGuid, null);
  assert.equal(
    (await connection.getFirst<{ n: number }>(
      "SELECT COUNT(*) AS n FROM terminal_cart_fences",
    ))?.n,
    1,
  );
  assert.equal(
    (
      await connection.getFirst<{ status: string }>(
        "SELECT status FROM held_order_records WHERE hold_id = 'hold-1'",
      )
    )?.status,
    "Recalling",
  );

  // 同一 intent 重试成功（可崩溃重放）。
  const retried = await committer.completeDurableCashOrder(input);
  assert.equal(retried.replayed, false);
  await assertSource(connection, "order-recall-cash-failover", {
    holdGuid: "hold-1",
    claimGuid: "claim-cash-failover",
    sourceKind: 1,
  });
});

test("取单事实不匹配整体回滚：错误 binding 不产生订单与来源", async () => {
  const { connection, records } = await open();
  await holdAndRecall(records, "attempt-cash-mismatch");
  const input = recalledCashInput({
    intent: {
      checkoutIntentId: "intent-recall-cash-mismatch",
      requestSignature: "signature-recall-cash-mismatch",
      cashDueCents: 674,
      changeCents: 0,
    },
    command: {
      ...recalledCashInput().command,
      order: {
        ...recalledCashInput().command.order,
        orderGuid: "order-recall-cash-mismatch",
      },
      auditEvents: [{
        ...recalledCashInput().command.auditEvents[0]!,
        orderGuid: "order-recall-cash-mismatch",
      }],
      outbox: {
        ...recalledCashInput().command.outbox,
        aggregateId: "order-recall-cash-mismatch",
      },
    },
    recalledHoldCompletion: {
      ...recalledCashInput().recalledHoldCompletion!,
      binding: {
        kind: "recalled",
        scope: SCOPE,
        holdId: "hold-1",
        recallAttemptId: "attempt-cash-wrong",
      },
      recallAudit: {
        ...recalledCashInput().recalledHoldCompletion!.recallAudit,
        orderGuid: "order-recall-cash-mismatch",
        correlationId: "hold-1",
      },
    },
  });
  const committer = new SqliteAtomicCashOrderCommitter(
    connection,
    ENCRYPTOR,
    () => SOLD_AT,
  );
  await assert.rejects(
    committer.completeDurableCashOrder(input),
    /do not match|does not match the active recall fence/,
  );
  assert.equal(
    await connection.getFirst(
      "SELECT order_guid FROM local_orders WHERE order_guid = 'order-recall-cash-mismatch'",
    ),
    null,
  );
  assert.equal(
    await connection.getFirst(
      "SELECT order_guid FROM order_held_order_sources WHERE order_guid = 'order-recall-cash-mismatch'",
    ),
    null,
  );
});
