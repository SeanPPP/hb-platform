import assert from "node:assert/strict";
import { DatabaseSync, type SQLInputValue } from "node:sqlite";
import test from "node:test";

import { PosDatabase } from "./pos-database";
import { SqliteLocalHistoryStore } from "./sqlite-local-history-store";
import type {
  SqliteConnectionPort,
  SqlRunResult,
  SqlValue,
} from "./types";

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

test("生产迁移后的真实 SQLite：scope/完成状态/商品搜索/cursor 与安全详情可共同执行", async () => {
  const connection = new NodeSqliteConnection(
    new DatabaseSync(":memory:", { enableForeignKeyConstraints: true }),
  );
  const database = await PosDatabase.open({
    databaseName: "local-history-test.db",
    driver: {
      async open() {
        return connection;
      },
    },
    keyProvider: {
      async getOrCreateDatabaseKey() {
        return "ab".repeat(32);
      },
    },
    nowIso: () => "2026-07-31T00:00:00.000Z",
  });

  try {
    await seedOrder(connection, 6, "S1", "IPAD-1", "PendingSync", "Tea", "930001");
    await seedOrder(connection, 5, "S1", "IPAD-1", "Draft", "Tea Draft", "930001");
    await seedOrder(connection, 4, "S1", "IPAD-2", "Synced", "Tea Other Device", "930001");
    await seedOrder(connection, 3, "S2", "IPAD-1", "Synced", "Tea Other Store", "930001");
    await seedOrder(connection, 2, "S1", "IPAD-1", "Synced", "Coffee", "930002");
    await connection.run(
      `INSERT INTO order_tenders (
        tender_guid, order_guid, method, amount_cents,
        payment_attempt_id, created_at_iso
      ) VALUES (
        'tender-6', 'order-6', 'card', 1200,
        NULL, '2026-07-31T01:02:04.000Z'
      )`,
    );

    const store = new SqliteLocalHistoryStore(connection, {
      storeCode: "S1",
      deviceCode: "IPAD-1",
    });
    const searchPage = await store.list({
      soldFromIso: "2026-07-30T14:00:00.000Z",
      soldToIso: "2026-07-31T13:59:59.999Z",
      keyword: "930001",
      cursor: null,
      limit: 50,
    });

    assert.deepEqual(
      searchPage.orders.map((order) => order.orderGuid),
      ["order-6"],
    );
    assert.equal(searchPage.orders[0]?.paymentSummary, "Card");
    assert.equal(searchPage.nextCursor, null);

    const firstPage = await store.list({
      soldFromIso: "2026-07-30T14:00:00.000Z",
      soldToIso: "2026-07-31T13:59:59.999Z",
      keyword: null,
      cursor: null,
      limit: 1,
    });
    const secondPage = await store.list({
      soldFromIso: "2026-07-30T14:00:00.000Z",
      soldToIso: "2026-07-31T13:59:59.999Z",
      keyword: null,
      cursor: firstPage.nextCursor,
      limit: 1,
    });
    assert.deepEqual(
      [
        firstPage.orders[0]?.localSequence,
        secondPage.orders[0]?.localSequence,
      ],
      [6, 2],
    );

    const details = await store.getDetails("order-6");
    assert.equal(details?.lines[0]?.displayName, "Tea");
    assert.deepEqual(details?.tenders, [
      { method: "card", amountCents: 1_200 },
    ]);
    assert.equal(details && "storeCode" in details, false);
    assert.equal(details?.tenders[0] && "tenderGuid" in details.tenders[0], false);
  } finally {
    await database.close();
  }
});

test("真实 SQLite：50 条分页无重复遗漏，LIKE 元字符与去连字符订单号按字面量搜索", async () => {
  const connection = new NodeSqliteConnection(
    new DatabaseSync(":memory:", { enableForeignKeyConstraints: true }),
  );
  const database = await PosDatabase.open({
    databaseName: "local-history-boundaries.db",
    driver: {
      async open() {
        return connection;
      },
    },
    keyProvider: {
      async getOrCreateDatabaseKey() {
        return "cd".repeat(32);
      },
    },
    nowIso: () => "2026-07-31T00:00:00.000Z",
  });

  try {
    for (let sequence = 100; sequence <= 150; sequence += 1) {
      await seedOrder(
        connection,
        sequence,
        "S-PAGE",
        "IPAD-PAGE",
        "Synced",
        `Item ${sequence}`,
        `9300${sequence}`,
      );
    }
    await seedOrder(
      connection,
      201,
      "S-SEARCH",
      "IPAD-SEARCH",
      "PendingSync",
      String.raw`Tea %_\ Special`,
      String.raw`SKU%_\42`,
    );
    await seedOrder(
      connection,
      202,
      "S-SEARCH",
      "IPAD-SEARCH",
      "PendingSync",
      "Unrelated item",
      "SKU-NORMAL",
    );

    const paged = new SqliteLocalHistoryStore(connection, {
      storeCode: "S-PAGE",
      deviceCode: "IPAD-PAGE",
    });
    const first = await paged.list({
      soldFromIso: "2026-07-30T14:00:00.000Z",
      soldToIso: "2026-07-31T13:59:59.999Z",
      keyword: null,
      cursor: null,
      limit: 50,
    });
    const second = await paged.list({
      soldFromIso: "2026-07-30T14:00:00.000Z",
      soldToIso: "2026-07-31T13:59:59.999Z",
      keyword: null,
      cursor: first.nextCursor,
      limit: 50,
    });
    assert.equal(first.orders.length, 50);
    assert.equal(first.nextCursor, 101);
    assert.deepEqual(
      second.orders.map((order) => order.localSequence),
      [100],
    );
    assert.equal(second.nextCursor, null);
    assert.equal(
      new Set([...first.orders, ...second.orders].map((order) => order.orderGuid))
        .size,
      51,
    );

    const searchable = new SqliteLocalHistoryStore(connection, {
      storeCode: "S-SEARCH",
      deviceCode: "IPAD-SEARCH",
    });
    const literal = await searchable.list({
      soldFromIso: "2026-07-30T14:00:00.000Z",
      soldToIso: "2026-07-31T13:59:59.999Z",
      keyword: "%_\\",
      cursor: null,
      limit: 50,
    });
    const orderId = await searchable.list({
      soldFromIso: "2026-07-30T14:00:00.000Z",
      soldToIso: "2026-07-31T13:59:59.999Z",
      keyword: "order201",
      cursor: null,
      limit: 50,
    });
    assert.deepEqual(
      literal.orders.map((order) => order.orderGuid),
      ["order-201"],
    );
    assert.deepEqual(
      orderId.orders.map((order) => order.orderGuid),
      ["order-201"],
    );
  } finally {
    await database.close();
  }
});

async function seedOrder(
  connection: SqliteConnectionPort,
  sequence: number,
  storeCode: string,
  deviceCode: string,
  state: string,
  displayName: string,
  lookupCode: string,
): Promise<void> {
  const orderGuid = `order-${sequence}`;
  const soldAtIso = "2026-07-31T01:00:00.000Z";
  await connection.run(
    `INSERT INTO local_orders (
      order_guid, local_sequence, store_code, device_code,
      cashier_id, cashier_name, sold_at_iso, state,
      total_cents, discount_cents, actual_amount_cents,
      original_order_guid, created_at_iso, updated_at_iso
    ) VALUES (
      ?, ?, ?, ?, 'cashier-1', 'Alice', ?, ?,
      1234, 34, 1200, NULL, ?, ?
    )`,
    [
      orderGuid,
      sequence,
      storeCode,
      deviceCode,
      soldAtIso,
      state,
      soldAtIso,
      soldAtIso,
    ],
  );
  await connection.run(
    `INSERT INTO local_order_lines (
      line_id, order_guid, line_sequence, product_code, item_number,
      lookup_code, display_name, quantity, unit_price_cents,
      discount_cents, actual_amount_cents, price_source, line_kind,
      return_source_key, original_order_guid, original_order_detail_guid,
      reference_code, sync_price_source
    ) VALUES (
      ?, ?, 1, 'P1', 'I1',
      ?, ?, '1', 1234,
      34, 1200, 'catalog', 'sale',
      NULL, NULL, NULL, 'REF-P1', 0
    )`,
    [`line-${sequence}`, orderGuid, lookupCode, displayName],
  );
}
