import assert from "node:assert/strict";
import { DatabaseSync, type SQLInputValue } from "node:sqlite";
import test from "node:test";

import { applyMigrations } from "./migrations";
import { SqliteVoucherBalanceMaterialStore } from "./sqlite-voucher-balance-material";
import { SqliteVoucherProtectedTokenStore } from "./sqlite-voucher-protected-token-store";
import type {
  SqliteConnectionPort,
  SqlRunResult,
  SqlValue,
} from "./types";

import type {
  VoucherProtectedAttemptState,
  VoucherProtectedAttemptStateDraft,
} from "@/features/payments/voucher";

const NOW = "2026-07-31T00:00:00.000Z";
const ORDER_GUID = "018f1b9b-47c5-7c1b-9f8e-39c5cb3b9d01";

const encryptor = {
  async encrypt(plaintext: string): Promise<Uint8Array> {
    return new TextEncoder().encode(plaintext);
  },
  async decrypt(ciphertext: Uint8Array): Promise<string> {
    return new TextDecoder().decode(ciphertext);
  },
};

test("普通订单只从有效正数 voucher tender 恢复同一券的受保护余额材料", async () => {
  await withFixture(async ({ materials }) => {
    assert.deepEqual(await materials.listForOrder(ORDER_GUID), [
      {
        attemptId: "voucher-attempt-1",
        orderGuid: ORDER_GUID,
        storeCode: "S001",
        voucherCode: "VC100",
        confirmation: null,
      },
    ]);
  });
});

test("最新余额只能在 approved 状态单调补齐一次，崩溃重放读取同一快照", async () => {
  await withFixture(async ({ materials, tokens }) => {
    const confirmation = {
      status: "confirmed" as const,
      remainingCents: 625,
      confirmedAtIso: NOW,
    };

    await materials.saveConfirmation("voucher-attempt-1", confirmation);

    assert.deepEqual(
      (await tokens.getByAttempt("voucher-attempt-1"))
        ?.latestBalanceConfirmation,
      confirmation,
    );
    assert.deepEqual(
      (await materials.listForOrder(ORDER_GUID))[0]?.confirmation,
      confirmation,
    );
    await assert.rejects(
      () =>
        materials.saveConfirmation("voucher-attempt-1", {
          ...confirmation,
          remainingCents: 624,
        }),
      /cannot be changed|already confirmed/i,
    );
  });
});

test("启动恢复只枚举已 Synced 的正余额确认，零余额和不可确认不打印", async () => {
  await withFixture(async ({ connection, materials }) => {
    await materials.saveConfirmation("voucher-attempt-1", {
      status: "confirmed",
      remainingCents: 625,
      confirmedAtIso: NOW,
    });
    assert.deepEqual(await materials.listSyncedPendingPrints(), []);

    await connection.run(
      "UPDATE local_orders SET state = 'Synced' WHERE order_guid = ?",
      [ORDER_GUID],
    );
    assert.equal(
      (await materials.listSyncedPendingPrints())[0]?.confirmation
        ?.remainingCents,
      625,
    );
  });
});

test("启动恢复会越过前 200 笔不可打印快照继续找到后续正余额", async () => {
  const rows = Array.from({ length: 201 }, (_, index) => {
    const suffix = String(index).padStart(3, "0");
    return {
      order_guid: ORDER_GUID,
      order_state: "Synced",
      order_store_code: "S001",
      tender_guid: `voucher-tender-${suffix}`,
      tender_amount_cents: 700,
      attempt_id: `voucher-attempt-${suffix}`,
      idempotency_key: `voucher-idem-${suffix}`,
      attempt_order_guid: ORDER_GUID,
      provider: "voucher",
      operation: "purchase",
      attempt_amount_cents: 700,
      attempt_state: "Approved",
      protected_reference: `vpr_${suffix.padEnd(16, "x")}`,
      protected_attempt_id: `voucher-attempt-${suffix}`,
      protected_idempotency_key: `voucher-idem-${suffix}`,
      protected_order_guid: ORDER_GUID,
    };
  });
  const states = new Map<string, VoucherProtectedAttemptState>(
    rows.map((row, index) => [
      String(row.attempt_id),
      {
        protectedReference: String(row.protected_reference),
        attemptId: String(row.attempt_id),
        idempotencyKey: String(row.idempotency_key),
        orderGuid: ORDER_GUID,
        operation: "purchase",
        phase: "approved",
        storeCode: "S001",
        cashierId: "cashier-1",
        voucherCode: `VC${String(index).padStart(3, "0")}`,
        reservationToken: `reservation-${index}`,
        amountCents: 700,
        expiresAtIso: "2026-08-01T00:00:00.000Z",
        latestBalanceConfirmation:
          index === 200
            ? {
                status: "confirmed",
                remainingCents: 100,
                confirmedAtIso: NOW,
              }
            : {
                status: "unavailable",
                remainingCents: null,
                confirmedAtIso: NOW,
              },
      },
    ]),
  );
  const offsets: number[] = [];
  const connection = {
    async getAll<T extends object>(
      _sql: string,
      parameters: readonly SqlValue[] = [],
    ): Promise<readonly T[]> {
      const limit = Number(parameters[parameters.length - 2]);
      const offset = Number(parameters[parameters.length - 1]);
      offsets.push(offset);
      return rows.slice(offset, offset + limit) as T[];
    },
  } as SqliteConnectionPort;
  const materials = new SqliteVoucherBalanceMaterialStore(
    connection,
    {
      async getByAttempt(attemptId) {
        return states.get(attemptId) ?? null;
      },
      async save() {
        throw new Error("枚举不应写入余额快照");
      },
    },
  );

  assert.deepEqual(await materials.listSyncedPendingPrints(1), [
    {
      attemptId: "voucher-attempt-200",
      orderGuid: ORDER_GUID,
      storeCode: "S001",
      voucherCode: "VC200",
      confirmation: {
        status: "confirmed",
        remainingCents: 100,
        confirmedAtIso: NOW,
      },
    },
  ]);
  assert.deepEqual(offsets, [0, 200]);
});

test("退款券、负 tender、已撤回或非 Approved attempt 不进入余额确认", async (t) => {
  await t.test("非 Approved attempt", async () => {
    await withFixture(async ({ connection, materials }) => {
      await connection.run(
        "UPDATE payment_attempts SET state = 'Cancelled' WHERE attempt_id = ?",
        ["voucher-attempt-1"],
      );
      assert.deepEqual(await materials.listForOrder(ORDER_GUID), []);
    });
  });

  await t.test("负 tender", async () => {
    await withFixture(async ({ connection, materials }) => {
      await connection.exec("PRAGMA foreign_keys = OFF");
      await connection.run(
        "UPDATE order_tenders SET amount_cents = -700 WHERE tender_guid = ?",
        ["voucher-tender-1"],
      );
      assert.deepEqual(await materials.listForOrder(ORDER_GUID), []);
    });
  });

  await t.test("已建立 reversal link", async () => {
    await withFixture(async ({ connection, materials }) => {
      await connection.run(
        `INSERT INTO order_tenders (
          tender_guid, order_guid, method, amount_cents,
          payment_attempt_id, created_at_iso
        ) VALUES (
          'voucher-tender-reversal', ?, 'voucher', -700, NULL, ?
        )`,
        [ORDER_GUID, NOW],
      );
      await connection.run(
        `INSERT INTO payment_tender_reversal_links (
          order_guid, action_id, source_tender_guid,
          reversal_tender_guid, created_at_iso
        ) VALUES (
          ?, 'voucher-reversal-action', 'voucher-tender-1',
          'voucher-tender-reversal', ?
        )`,
        [ORDER_GUID, NOW],
      );

      assert.deepEqual(await materials.listForOrder(ORDER_GUID), []);
    });
  });
});

async function withFixture(
  operation: (fixture: Readonly<{
    connection: SqliteConnectionPort;
    tokens: SqliteVoucherProtectedTokenStore;
    materials: SqliteVoucherBalanceMaterialStore;
  }>) => Promise<void>,
): Promise<void> {
  const connection = new NodeSqliteConnection();
  try {
    await applyMigrations(connection, () => NOW);
    await seedSale(connection);
    const tokens = new SqliteVoucherProtectedTokenStore(
      connection,
      encryptor,
      () => "vpr_abcdefghijklmnop",
      () => NOW,
    );
    await tokens.save(protectedState());
    await operation({
      connection,
      tokens,
      materials: new SqliteVoucherBalanceMaterialStore(
        connection,
        tokens,
      ),
    });
  } finally {
    await connection.close();
  }
}

async function seedSale(connection: SqliteConnectionPort): Promise<void> {
  await connection.run(
    `INSERT INTO local_orders (
      order_guid, local_sequence, store_code, device_code,
      cashier_id, cashier_name, sold_at_iso, state,
      total_cents, discount_cents, actual_amount_cents,
      original_order_guid, created_at_iso, updated_at_iso
    ) VALUES (?, 1, 'S001', 'IPAD-1', 'cashier-1', 'Cashier',
      ?, 'PendingSync', 700, 0, 700, NULL, ?, ?)`,
    [ORDER_GUID, NOW, NOW, NOW],
  );
  await connection.run(
    `INSERT INTO local_order_lines (
      line_id, order_guid, line_sequence, product_code, item_number,
      lookup_code, display_name, quantity, unit_price_cents,
      discount_cents, actual_amount_cents, price_source, line_kind,
      return_source_key, original_order_guid, original_order_detail_guid,
      reference_code, sync_price_source
    ) VALUES (
      'sale-line-1', ?, 1, 'P1', NULL, 'P1', 'Product', '1',
      700, 0, 700, 'catalog', 'sale', NULL, NULL, NULL, 'P1', 1
    )`,
    [ORDER_GUID],
  );
  await connection.run(
    `INSERT INTO payment_attempts (
      attempt_id, idempotency_key, order_guid, provider, operation,
      amount_cents, state, checkout_id, payment_id, session_id,
      txn_ref, rfn, provider_payload_ciphertext,
      provider_receipt_ciphertext, provider_response_code,
      created_at_iso, updated_at_iso, last_error_code
    ) VALUES (
      'voucher-attempt-1', 'voucher-idem-1', ?, 'voucher',
      'purchase', 700, 'Approved', NULL, NULL, NULL, NULL, NULL,
      NULL, NULL, NULL, ?, ?, NULL
    )`,
    [ORDER_GUID, NOW, NOW],
  );
  await connection.run(
    `INSERT INTO order_tenders (
      tender_guid, order_guid, method, amount_cents,
      payment_attempt_id, created_at_iso
    ) VALUES (
      'voucher-tender-1', ?, 'voucher', 700, 'voucher-attempt-1', ?
    )`,
    [ORDER_GUID, NOW],
  );
}

function protectedState(): VoucherProtectedAttemptStateDraft {
  return {
    attemptId: "voucher-attempt-1",
    idempotencyKey: "voucher-idem-1",
    orderGuid: ORDER_GUID,
    operation: "purchase",
    phase: "approved",
    storeCode: "S001",
    cashierId: "cashier-1",
    voucherCode: "VC100",
    reservationToken: "reservation-1",
    amountCents: 700,
    expiresAtIso: "2026-08-01T00:00:00.000Z",
    reason: null,
  };
}

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
      .run(...parameters.map(toSqlInput));
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
      (this.database
        .prepare(sql)
        .get(...parameters.map(toSqlInput)) as T | undefined) ?? null
    );
  }

  public async getAll<T extends object>(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<readonly T[]> {
    return this.database
      .prepare(sql)
      .all(...parameters.map(toSqlInput)) as T[];
  }

  public async withExclusiveTransaction<T>(
    operation: (transaction: SqliteConnectionPort) => Promise<T>,
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

function toSqlInput(value: SqlValue): SQLInputValue {
  return value instanceof Uint8Array ? Buffer.from(value) : value;
}
