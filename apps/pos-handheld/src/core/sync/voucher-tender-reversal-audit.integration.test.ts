import assert from "node:assert/strict";
import { DatabaseSync, type SQLInputValue } from "node:sqlite";
import test from "node:test";

import type {
  HbposTransport,
  HbposTransportRequest,
  HbposTransportResponse,
} from "../api/hbpos-api";
import { applyMigrations } from "../db/migrations";
import { createSqliteRepositories, type SensitivePayloadEncryptor } from "../db/sqlite-repositories";
import { SqliteVoucherProtectedTokenStore } from "../db/sqlite-voucher-protected-token-store";
import {
  SqliteVoucherTenderReversalStore,
  type VoucherTenderReversalCommand,
} from "../db/sqlite-voucher-tender-reversal-store";
import type {
  SqliteConnectionPort,
  SqlRunResult,
  SqlValue,
} from "@hb/pos-db/core/db/types";

import { HbposAuditBatchAdapter } from "@hb/pos-sync/core/sync/hbpos-sync-adapters";
import { PosSyncCoordinator } from "@hb/pos-sync/core/sync/sync-coordinator";

const T0 = "2026-07-28T00:00:00.000Z";
const T1 = "2026-07-28T00:01:00.000Z";
const T2 = "2026-07-28T00:02:00.000Z";

const ids = {
  success: {
    order: "018f1b9b-47c5-7c1b-9f8e-39c5cb3b9d01",
    action: "018f1b9b-47c5-7c1b-9f8e-39c5cb3b9d02",
    attempt: "018f1b9b-47c5-7c1b-9f8e-39c5cb3b9d03",
    tender: "018f1b9b-47c5-7c1b-9f8e-39c5cb3b9d04",
    reversalTender: "018f1b9b-47c5-7c1b-9f8e-39c5cb3b9d05",
    audit: "018f1b9b-47c5-7c1b-9f8e-39c5cb3b9d06",
  },
  blocked: {
    order: "018f1b9b-47c5-7c1b-9f8e-39c5cb3b9d07",
    action: "018f1b9b-47c5-7c1b-9f8e-39c5cb3b9d08",
    attempt: "018f1b9b-47c5-7c1b-9f8e-39c5cb3b9d09",
    tender: "018f1b9b-47c5-7c1b-9f8e-39c5cb3b9d0a",
    audit: "018f1b9b-47c5-7c1b-9f8e-39c5cb3b9d0b",
  },
  subsequentAudit: "018f1b9b-47c5-7c1b-9f8e-39c5cb3b9d0c",
  subsequentCorrelation: "018f1b9b-47c5-7c1b-9f8e-39c5cb3b9d0d",
} as const;

const encryptor: SensitivePayloadEncryptor = {
  async encrypt(plaintext) {
    return Uint8Array.from(
      new TextEncoder().encode(plaintext),
      (value) => value ^ 0xa5,
    );
  },
  async decrypt(ciphertext) {
    return new TextDecoder().decode(
      Uint8Array.from(ciphertext, (value) => value ^ 0xa5),
    );
  },
};

/** 仅记录脱敏的 HTTP 请求体，模拟后端逐事件 accepted 回执。 */
class AcceptingAuditTransport implements HbposTransport {
  public readonly calls: HbposTransportRequest[] = [];

  public async request<T>(
    request: HbposTransportRequest,
  ): Promise<HbposTransportResponse<T>> {
    this.calls.push(request);
    const events = (request.data as { events?: { eventId?: string }[] }).events;
    return {
      status: 200,
      data: {
        results: events?.map((event) => ({
          eventId: event.eventId,
          status: "accepted",
        })) ?? [],
      } as T,
    };
  }
}

test("M16 撤券终态审计映射后上传并不阻塞后续审计", async () => {
  await withDatabase(async (connection) => {
    await applyMigrations(connection, () => T0);
    const repositories = createSqliteRepositories(connection, {
      nowIso: () => T2,
      createLeaseId: () => "lease-unused-for-audit-only-drain",
      encryptor,
      auditScope: { storeCode: "STORE-1", deviceCode: "DEVICE-1" },
    });

    await seedApprovedVoucherPurchase(connection, ids.success, 500);
    await seedApprovedVoucherPurchase(connection, ids.blocked, 700);

    const reversalStore = new SqliteVoucherTenderReversalStore(
      connection,
      encryptor,
      {
        createReversalTenderGuid: () => ids.success.reversalTender,
        createAuditEventId: (() => {
          const auditIds = [ids.success.audit, ids.blocked.audit];
          let index = 0;
          return () => auditIds[index++]!;
        })(),
      },
      () => T1,
    );

    const preparedSuccess = await reversalStore.prepareOrLoad(command(ids.success));
    const submittedSuccess = await reversalStore.markSubmitted(preparedSuccess);
    await saveProtectedState(connection, ids.success, "released");
    assert.equal(
      (await reversalStore.commitReleased(submittedSuccess, {
        state: "Cancelled",
        responseCode: "VOUCHER_RELEASED",
      })).state,
      "Reversed",
    );

    const preparedBlocked = await reversalStore.prepareOrLoad(command(ids.blocked));
    assert.equal(
      (await reversalStore.markBlocked(preparedBlocked, "VOUCHER_RELEASE_DENIED"))
        .state,
      "Blocked",
    );

    await repositories.audit.append([
      {
        eventId: ids.subsequentAudit,
        eventType: "SALE_COMPLETE",
        occurredAtIso: T2,
        orderGuid: ids.success.order,
        correlationId: ids.subsequentCorrelation,
        payload: { source: "cash" },
      },
    ]);

    const transport = new AcceptingAuditTransport();
    const coordinator = new PosSyncCoordinator({
      outbox: repositories.outbox,
      auditRepository: repositories.audit,
      auditDelivery: repositories.auditDelivery,
      orderSync: { async sync() { throw new Error("No order outbox is expected."); } },
      auditUploader: new HbposAuditBatchAdapter(
        transport,
        repositories.orders,
        {
          storeCode: "STORE-1",
          deviceCode: "DEVICE-1",
          appVersion: "0.1.0",
          instanceId: "ipad-installation",
        },
      ),
      security: { async lockDevice() {} },
      now: () => new Date(T2),
      random: () => 0.5,
    });

    const report = await coordinator.requestDrain();
    assert.deepEqual(report, {
      leased: 0,
      orderSucceeded: 0,
      orderRetried: 0,
      orderBlocked: 0,
      orderRejected: 0,
      auditUploaded: 3,
    });
    assert.equal(await pendingAuditCount(connection), 0);
    const uploadedAt = await connection.getAll<{ uploaded_at_iso: string }>(
      "SELECT uploaded_at_iso FROM audit_events ORDER BY event_id",
    );
    assert.deepEqual(uploadedAt.map((row) => row.uploaded_at_iso), [T2, T2, T2]);

    const sent = transport.calls[0]?.data as {
      events: readonly {
        eventId: string;
        outcome: string;
        properties: Record<string, string> | null;
      }[];
    };
    assert.deepEqual(
      sent.events.map((event) => [event.eventId, event.outcome]),
      [
        [ids.success.audit, "Succeeded"],
        [ids.blocked.audit, "Denied"],
        [ids.subsequentAudit, "Succeeded"],
      ],
    );
    assert.deepEqual(sent.events[0]?.properties, {
      action: "payment-tender-remove",
      reason: "SALE",
      requestingCashierId: "cashier-1",
      requestingCashierName: "Cashier One",
      requestingUserGuid: "user-guid-1",
    });
    assert.deepEqual(sent.events[1]?.properties, {
      action: "payment-tender-remove",
      reason: "SALE",
      requestingCashierId: "cashier-1",
      requestingCashierName: "Cashier One",
      requestingUserGuid: "user-guid-1",
    });
    // 审计上传体中既不携带券码/预留 token，也不暴露本地 payment attempt 身份。
    assert.doesNotMatch(JSON.stringify(sent), /voucher-|reservation|attempt/i);
  });
});

type ReversalSeed = Readonly<{
  order: string;
  action: string;
  attempt: string;
  tender: string;
}>;

function command(seed: ReversalSeed): VoucherTenderReversalCommand {
  return {
    actionId: seed.action,
    orderGuid: seed.order,
    sourceTenderGuid: seed.tender,
    reason: "SALE",
    actor: {
      cashierId: "cashier-1",
      cashierName: "Cashier One",
      userGuid: "user-guid-1",
    },
  };
}

async function seedApprovedVoucherPurchase(
  connection: SqliteConnectionPort,
  seed: ReversalSeed,
  amountCents: number,
): Promise<void> {
  await connection.run(
    `INSERT INTO local_orders (
      order_guid, local_sequence, store_code, device_code,
      cashier_id, cashier_name, sold_at_iso, state,
      total_cents, discount_cents, actual_amount_cents,
      original_order_guid, created_at_iso, updated_at_iso
    ) VALUES (?, ?, 'STORE-1', 'DEVICE-1', 'cashier-1', 'Cashier',
      ?, 'Completing', ?, 0, ?, NULL, ?, ?)`,
    [seed.order, sequence(seed.order), T0, amountCents, amountCents, T0, T0],
  );
  await connection.run(
    `INSERT INTO local_order_lines (
      line_id, order_guid, line_sequence, product_code, item_number,
      lookup_code, display_name, quantity, unit_price_cents,
      discount_cents, actual_amount_cents, price_source, line_kind,
      return_source_key, original_order_guid, original_order_detail_guid,
      reference_code, sync_price_source
    ) VALUES (?, ?, 1, 'P', NULL, 'P', 'Product', '1', ?, 0, ?,
      'catalog', 'sale', NULL, NULL, NULL, NULL, 0)`,
    [`line-${seed.order}`, seed.order, amountCents, amountCents],
  );
  await connection.run(
    `INSERT INTO payment_attempts (
      attempt_id, idempotency_key, order_guid, provider, operation,
      amount_cents, state, checkout_id, payment_id, session_id,
      txn_ref, rfn, provider_payload_ciphertext, provider_receipt_ciphertext,
      provider_response_code, created_at_iso, updated_at_iso, last_error_code
    ) VALUES (?, ?, ?, 'voucher', 'purchase', ?, 'Approved',
      NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'APPROVED', ?, ?, NULL)`,
    [seed.attempt, `idem-${seed.attempt}`, seed.order, amountCents, T0, T0],
  );
  await connection.run(
    `INSERT INTO order_tenders (
      tender_guid, order_guid, method, amount_cents,
      payment_attempt_id, created_at_iso
    ) VALUES (?, ?, 'voucher', ?, ?, ?)`,
    [seed.tender, seed.order, amountCents, seed.attempt, T0],
  );
  await saveProtectedState(connection, seed, "approved");
}

async function saveProtectedState(
  connection: SqliteConnectionPort,
  seed: ReversalSeed,
  phase: "approved" | "released",
): Promise<void> {
  const store = new SqliteVoucherProtectedTokenStore(
    connection,
    encryptor,
    () => `vpr_${seed.attempt.replaceAll("-", "_")}`,
    () => T1,
  );
  const base = {
    attemptId: seed.attempt,
    idempotencyKey: `idem-${seed.attempt}`,
    orderGuid: seed.order,
    operation: "purchase" as const,
    storeCode: "STORE-1",
    cashierId: "cashier-1",
    voucherCode: "test-voucher-code-not-emitted",
    reservationToken: "test-reservation-not-emitted",
    amountCents: seed === ids.success ? 500 : 700,
    expiresAtIso: "2026-07-29T00:00:00.000Z",
    reason: null,
  };
  await store.save({ ...base, phase: "approved" });
  if (phase === "released") {
    await store.save({ ...base, phase: "release-submitted" });
    await store.save({ ...base, phase: "released" });
  }
}

function sequence(orderGuid: string): number {
  return orderGuid.endsWith("01") ? 1 : 2;
}

async function pendingAuditCount(connection: SqliteConnectionPort): Promise<number> {
  return Number(
    (await connection.getFirst<{ count: unknown }>(
      "SELECT COUNT(*) AS count FROM audit_events WHERE uploaded_at_iso IS NULL",
    ))?.count,
  );
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
    const result = this.database.prepare(sql).run(
      ...parameters.map(toSqlInputValue),
    );
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
      this.database.prepare(sql).get(
        ...parameters.map(toSqlInputValue),
      ) as T | undefined
    ) ?? null;
  }

  public async getAll<T extends object>(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<readonly T[]> {
    return this.database.prepare(sql).all(
      ...parameters.map(toSqlInputValue),
    ) as unknown as readonly T[];
  }

  public async withExclusiveTransaction<T>(
    operation: (transaction: SqliteConnectionPort) => Promise<T>,
  ): Promise<T> {
    this.database.exec("BEGIN IMMEDIATE;");
    try {
      const result = await operation(new TransactionConnection(this.database));
      this.database.exec("COMMIT;");
      return result;
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
    return Promise.reject(new Error("Nested test transaction."));
  }

  public override close(): Promise<void> {
    return Promise.reject(new Error("Transaction cannot close database."));
  }
}

async function withDatabase(
  operation: (connection: SystemSqliteConnection) => Promise<void>,
): Promise<void> {
  const connection = new SystemSqliteConnection(new DatabaseSync(":memory:"));
  try {
    await operation(connection);
  } finally {
    await connection.close();
  }
}

function toSqlInputValue(value: SqlValue): SQLInputValue {
  return value as SQLInputValue;
}
