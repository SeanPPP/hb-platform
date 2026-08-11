import assert from "node:assert/strict";
import { DatabaseSync, type SQLInputValue } from "node:sqlite";
import test from "node:test";

import type { LocalOrder } from "../contracts/order";

import { applyMigrations } from "./migrations";
import { SqliteOrderSyncMaterialResolver } from "./sqlite-order-sync-material";
import type {
  SqliteConnectionPort,
  SqlRunResult,
  SqlValue,
} from "./types";

const NOW = "2026-07-28T00:00:00.000Z";

class TrackingConnection implements SqliteConnectionPort {
  public sourceSelectQueries = 0;
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

async function open(): Promise<TrackingConnection> {
  const connection = new TrackingConnection(
    new DatabaseSync(":memory:", { enableForeignKeyConstraints: true }),
  );
  await applyMigrations(connection, () => NOW);
  return connection;
}

async function seedCashOrder(
  connection: TrackingConnection,
  orderGuid: string,
): Promise<void> {
  await connection.run(
    `INSERT INTO local_orders (
      order_guid, local_sequence, store_code, device_code,
      cashier_id, cashier_name, sold_at_iso, state,
      total_cents, discount_cents, actual_amount_cents,
      original_order_guid, created_at_iso, updated_at_iso
    ) VALUES (?, 1, 'S1', 'IPAD-1', 'cashier-1', 'Cashier', ?,
      'PendingSync', 100, 0, 100, NULL, ?, ?)`,
    [orderGuid, NOW, NOW, NOW],
  );
  await connection.run(
    `INSERT INTO local_order_lines (
      line_id, order_guid, line_sequence, product_code, item_number,
      lookup_code, display_name, quantity, unit_price_cents,
      discount_cents, actual_amount_cents, price_source, line_kind,
      return_source_key, original_order_guid, original_order_detail_guid,
      reference_code, sync_price_source
    ) VALUES (?, ?, 1, 'P1', NULL, 'P1', 'Product', '1', 100,
      0, 100, 'catalog', 'sale', NULL, NULL, NULL, NULL, 0)`,
    [`line-${orderGuid}`, orderGuid],
  );
  await connection.run(
    `INSERT INTO order_tenders (
      tender_guid, order_guid, method, amount_cents,
      payment_attempt_id, created_at_iso
    ) VALUES (?, ?, 'cash', 100, NULL, ?)`,
    [`tender-${orderGuid}`, orderGuid, NOW],
  );
}

async function seedClaim(
  connection: TrackingConnection,
  claimGuid: string,
  holdGuid: string,
  recallAttemptId: string,
): Promise<void> {
  await connection.run(
    `INSERT INTO shared_held_order_claim_records (
      claim_guid, hold_guid, recall_attempt_id, store_code, device_code,
      source, state, prepare_idempotency_key, payload_version,
      payload_ciphertext, server_revision, activate_idempotency_key,
      release_idempotency_key, prepared_expires_at_iso, held_at_iso,
      held_by_cashier_id, held_by_cashier_name, bound_order_guid,
      created_at_iso, updated_at_iso
    ) VALUES (?, ?, ?, 'S1', 'IPAD-1', 'RemoteClaim', 'Completed',
      ?, 1, X'01', 1, 'activate-remote-sync', 'release-remote-sync',
      ?, ?, 'c-1', 'Cashier', 'order-remote-sync', ?, ?)`,
    [
      claimGuid,
      holdGuid,
      recallAttemptId,
      `prepare-${claimGuid}`,
      NOW,
      NOW,
      NOW,
      NOW,
    ],
  );
}

async function seedHeldSource(
  connection: TrackingConnection,
  orderGuid: string,
  holdGuid: string,
  claimGuid: string | null,
  sourceKind: 1 | 2,
): Promise<void> {
  await connection.run(
    `INSERT INTO order_held_order_sources (
      order_guid, hold_guid, claim_guid, source_kind, created_at_iso
    ) VALUES (?, ?, ?, ?, ?)`,
    [orderGuid, holdGuid, claimGuid, sourceKind, NOW],
  );
  await connection.run(
    `UPDATE local_orders SET is_shared_held_origin = 1
     WHERE order_guid = ?`,
    [orderGuid],
  );
}

function cashOrder(orderGuid: string): LocalOrder {
  return {
    orderGuid,
    localSequence: 1,
    storeCode: "S1",
    deviceCode: "IPAD-1",
    cashierId: "cashier-1",
    cashierName: "Cashier",
    soldAtIso: NOW,
    state: "PendingSync",
    total: { currency: "AUD", cents: 100 },
    discount: { currency: "AUD", cents: 0 },
    actualAmount: { currency: "AUD", cents: 100 },
    lines: [{
      lineId: `line-${orderGuid}`,
      productCode: "P1",
      itemNumber: null,
      lookupCode: "P1",
      displayName: "Product",
      quantity: "1",
      unitPrice: { currency: "AUD", cents: 100 },
      discount: { currency: "AUD", cents: 0 },
      actualAmount: { currency: "AUD", cents: 100 },
      priceSource: "catalog",
      syncProvenance: { referenceCode: null, priceSource: 0 },
      kind: "sale",
      returnSourceKey: null,
      originalOrderGuid: null,
      originalOrderDetailGuid: null,
    }],
    tenders: [{
      tenderGuid: `tender-${orderGuid}`,
      method: "cash",
      amount: { currency: "AUD", cents: 100 },
      reference: null,
      reservationToken: null,
    }],
    originalOrderGuid: null,
  };
}

function resolver(connection: TrackingConnection): SqliteOrderSyncMaterialResolver {
  return new SqliteOrderSyncMaterialResolver(connection, {
    returnCapacityVault: {
      async resolveProtectedContext(): Promise<never> {
        throw new Error("unused");
      },
    },
    voucherProtectedTokens: {
      async getByAttempt(): Promise<never> {
        throw new Error("unused");
      },
    },
  });
}

test("同步材料从数据库解析 RemoteClaim 来源（不依赖调用方内存对象）", async () => {
  const connection = await open();
  await seedCashOrder(connection, "order-remote-sync");
  await seedClaim(
    connection,
    "claim-remote-sync",
    "hold-remote-sync",
    "attempt-remote-sync",
  );
  await seedHeldSource(
    connection,
    "order-remote-sync",
    "hold-remote-sync",
    "claim-remote-sync",
    1,
  );
  const before = connection.sourceSelectQueries;
  const resolved = await resolver(connection).resolveForSync(
    cashOrder("order-remote-sync"),
    null,
  );
  assert.deepEqual(resolved.heldOrderSource, {
    holdGuid: "hold-remote-sync",
    claimGuid: "claim-remote-sync",
    sourceKind: 1,
  });
  assert.equal(connection.sourceSelectQueries - before, 1);
});

test("同步材料解析 OfflineOrigin 来源（claimGuid 为 null）", async () => {
  const connection = await open();
  await seedCashOrder(connection, "order-offline-sync");
  await seedHeldSource(
    connection,
    "order-offline-sync",
    "hold-offline-sync",
    null,
    2,
  );
  const resolved = await resolver(connection).resolveForSync(
    cashOrder("order-offline-sync"),
    null,
  );
  assert.deepEqual(resolved.heldOrderSource, {
    holdGuid: "hold-offline-sync",
    claimGuid: null,
    sourceKind: 2,
  });
});

test("普通订单同步解析零来源查询，heldOrderSource 为 null", async () => {
  const connection = await open();
  await seedCashOrder(connection, "order-ordinary-sync");
  const before = connection.sourceSelectQueries;
  const resolved = await resolver(connection).resolveForSync(
    cashOrder("order-ordinary-sync"),
    null,
  );
  assert.equal(resolved.heldOrderSource, null);
  assert.equal(connection.sourceSelectQueries - before, 0);
});

test("来源表不可变：数据库拒绝篡改，解析器稳定失败关闭", async () => {
  const connection = await open();
  await seedCashOrder(connection, "order-tamper-sync");
  await seedHeldSource(
    connection,
    "order-tamper-sync",
    "hold-tamper-sync",
    null,
    2,
  );
  await assert.rejects(
    connection.run(
      `UPDATE order_held_order_sources
       SET source_kind = 1 WHERE order_guid = 'order-tamper-sync'`,
    ),
    /ORDER_HELD_ORDER_SOURCE_IMMUTABLE/,
  );
  const resolved = await resolver(connection).resolveForSync(
    cashOrder("order-tamper-sync"),
    null,
  );
  assert.deepEqual(resolved.heldOrderSource, {
    holdGuid: "hold-tamper-sync",
    claimGuid: null,
    sourceKind: 2,
  });
});
