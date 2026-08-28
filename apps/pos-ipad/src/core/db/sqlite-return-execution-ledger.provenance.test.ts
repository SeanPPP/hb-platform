import assert from "node:assert/strict";
import { DatabaseSync, type SQLInputValue } from "node:sqlite";
import test from "node:test";

import { applyMigrations } from "./migrations";
import type {
  SensitivePayloadEncryptor,
} from "./sqlite-repositories";
import { SqliteReturnExecutionLedger } from "./sqlite-return-execution-ledger";
import type {
  SqliteConnectionPort,
  SqlRunResult,
  SqlValue,
} from "@hb/pos-db/core/db/types";

import type {
  PrepareDurableReturnAction,
} from "@hb/pos-domain/features/returns/adapters/durable-return-execution-orchestrator";

const NOW_ISO = "2026-07-28T00:00:00.000Z";

test("退货账本绑定同一交易行同步来源，数据库拒绝写后篡改", async () => {
  const connection = new NodeSqliteConnection();
  try {
    await applyMigrations(connection, () => NOW_ISO);
    const ledger = new SqliteReturnExecutionLedger(
      connection,
      plaintextEncryptor,
      {
        createTenderGuid: () => "tender-1",
        createAuditEventId: () => "audit-1",
      },
      () => NOW_ISO,
    );
    const draft = noReceiptDraft();

    const prepared = await ledger.prepareOrLoad(draft);
    assert.deepEqual(prepared.lines[0]?.syncProvenance, {
      referenceCode: "RETURN-REF",
      priceSource: 3,
    });
    assert.deepEqual(prepared.plan.lines[0]?.syncProvenance, {
      referenceCode: "RETURN-REF",
      priceSource: 3,
    });
    const persistedLine = await connection.getFirst<{
      reference_code: unknown;
      sync_price_source: unknown;
    }>(
        `SELECT reference_code, sync_price_source
         FROM local_order_lines
         WHERE order_guid = ? AND line_id = ?`,
        [draft.returnOrderGuid, draft.lines[0]!.lineId],
    );
    assert.equal(persistedLine?.reference_code, "RETURN-REF");
    assert.equal(persistedLine?.sync_price_source, 3);

    const recoverable = await ledger.listRecoverable({
      storeCode: draft.identity.storeCode,
      deviceCode: draft.identity.deviceCode,
      cashierId: draft.identity.cashierId,
      sessionEpoch: draft.identity.sessionEpoch,
    });
    assert.deepEqual(recoverable[0]?.lines[0]?.syncProvenance, {
      referenceCode: "RETURN-REF",
      priceSource: 3,
    });

    await assert.rejects(
      () =>
        connection.run(
          `UPDATE local_order_lines
           SET sync_price_source = 4
           WHERE order_guid = ? AND line_id = ?`,
          [draft.returnOrderGuid, draft.lines[0]!.lineId],
        ),
      /ORDER_LINE_SYNC_PROVENANCE_IMMUTABLE/u,
    );
    const unchangedLine = await connection.getFirst<{
      reference_code: unknown;
      sync_price_source: unknown;
    }>(
      `SELECT reference_code, sync_price_source
       FROM local_order_lines
       WHERE order_guid = ? AND line_id = ?`,
      [draft.returnOrderGuid, draft.lines[0]!.lineId],
    );
    assert.equal(unchangedLine?.reference_code, "RETURN-REF");
    assert.equal(unchangedLine?.sync_price_source, 3);

    const replayed = await ledger.load(draft.actionId);
    assert.ok(replayed);
    assert.equal(
      replayed.identity.userGuid,
      "cashier-user",
      "恢复必须使用首次持久化的 actor userGuid，而不是读取当前登录会话",
    );
    assert.deepEqual(
      replayed.lines[0]?.syncProvenance,
      draft.lines[0]?.syncProvenance,
    );
  } finally {
    await connection.close();
  }
});

function noReceiptDraft(): PrepareDurableReturnAction {
  const syncProvenance = Object.freeze({
    referenceCode: "RETURN-REF",
    priceSource: 3 as const,
  });
  return {
    actionId: "action-provenance-1",
    requestFingerprint: "fingerprint-provenance-1",
    returnOrderGuid: "return-order-provenance-1",
    actionRecoveryToken: "recovery-token-provenance-1",
    identity: {
      storeCode: "S01",
      deviceCode: "IPAD-1",
      cashierId: "CASHIER-1",
      cashierName: "Cashier",
      userGuid: "cashier-user",
      sessionEpoch: "epoch-1",
    },
    plan: {
      sourceKind: "no-receipt",
      totalRefundCents: 500,
      lines: [
        {
          sourceKind: "no-receipt-product",
          returnSourceKey: "return-source-1",
          originalOrderGuid: null,
          originalOrderDetailGuid: null,
          productCode: "P1",
          quantity: 1,
          signedAmountCents: -500,
          syncProvenance,
        },
      ],
      allocations: [
        {
          method: "cash",
          signedAmountCents: -500,
          originalCapacityId: null,
          originalOrderGuid: null,
          offlineCashProof: null,
        },
      ],
      online: true,
    },
    supervisorGrantKey: "supervisor-grant-provenance-1",
    createdAtIso: NOW_ISO,
    lines: [
      {
        lineId: "return-line-provenance-1",
        selectionKey: "selection-provenance-1",
        sourceKind: "no-receipt-product",
        returnSourceKey: "return-source-1",
        originalOrderGuid: null,
        originalOrderDetailGuid: null,
        productCode: "P1",
        itemNumber: "ITEM-1",
        lookupCode: "LOOKUP-1",
        displayName: "Product",
        quantity: 1,
        unitRefundCents: 500,
        signedAmountCents: -500,
        availableQuantity: null,
        remainingAmountCents: null,
        syncProvenance,
      },
    ],
    allocations: [
      {
        allocationId: "allocation-provenance-1",
        index: 0,
        executionKind: "online-refund",
        method: "cash",
        signedAmountCents: -500,
        capacityId: null,
        originalOrderGuid: null,
        offlineCashProof: null,
        externalAttemptId: "external-attempt-provenance-1",
        externalAttemptKind: null,
        externalActionId: null,
        durableAttemptId: null,
        status: "created",
        protectedRecoveryKey: null,
      },
    ],
  };
}

const plaintextEncryptor: SensitivePayloadEncryptor = {
  async encrypt(plaintext) {
    return new TextEncoder().encode(plaintext);
  },
  async decrypt(ciphertext) {
    return new TextDecoder().decode(ciphertext);
  },
};

class NodeSqliteConnection implements SqliteConnectionPort {
  private readonly database = new DatabaseSync(":memory:");

  public constructor() {
    this.database.exec("PRAGMA foreign_keys = ON");
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
      .run(...parameters.map(toSqlInputValue));
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
      this.database
        .prepare(sql)
        .get(...parameters.map(toSqlInputValue)) as T | undefined
    ) ?? null;
  }

  public async getAll<T extends object>(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<readonly T[]> {
    return this.database
      .prepare(sql)
      .all(...parameters.map(toSqlInputValue)) as unknown as readonly T[];
  }

  public async withExclusiveTransaction<T>(
    operation: (
      transaction: SqliteConnectionPort,
    ) => Promise<T>,
  ): Promise<T> {
    this.database.exec("BEGIN IMMEDIATE");
    try {
      const result = await operation(this);
      this.database.exec("COMMIT");
      return result;
    } catch (error) {
      this.database.exec("ROLLBACK");
      throw error;
    }
  }

  public async close(): Promise<void> {
    this.database.close();
  }
}

function toSqlInputValue(value: SqlValue): SQLInputValue {
  return value as SQLInputValue;
}
