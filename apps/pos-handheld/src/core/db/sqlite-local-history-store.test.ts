import assert from "node:assert/strict";
import test from "node:test";

import { SqliteLocalHistoryStore } from "./sqlite-local-history-store";
import type {
  SqliteConnectionPort,
  SqlRunResult,
  SqlValue,
} from "@hb/pos-db/core/db/types";

type ReadCall = Readonly<{
  kind: "first" | "all";
  sql: string;
  parameters: readonly SqlValue[];
}>;

class RecordingConnection implements SqliteConnectionPort {
  public readonly reads: ReadCall[] = [];
  public getFirstResult: object | null = null;
  public readonly getAllResults: object[][] = [];

  public exec(): Promise<void> {
    return Promise.reject(new Error("read-only store called exec"));
  }

  public run(): Promise<SqlRunResult> {
    return Promise.reject(new Error("read-only store called run"));
  }

  public async getFirst<T extends object>(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<T | null> {
    this.reads.push({ kind: "first", sql, parameters });
    return this.getFirstResult as T | null;
  }

  public async getAll<T extends object>(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<readonly T[]> {
    this.reads.push({ kind: "all", sql, parameters });
    return (this.getAllResults.shift() ?? []) as T[];
  }

  public withExclusiveTransaction<T>(
    operation: (transaction: SqliteConnectionPort) => Promise<T>,
  ): Promise<T> {
    return operation(this);
  }

  public close(): Promise<void> {
    return Promise.resolve();
  }
}

const fromIso = "2026-07-30T14:00:00.000Z";
const toIso = "2026-07-31T13:59:59.999Z";

function summaryRow(
  sequence: number,
  overrides: Readonly<Record<string, unknown>> = {},
) {
  return {
    order_guid: `order-${sequence}`,
    local_sequence: sequence,
    sold_at_iso: `2026-07-31T0${9 - sequence}:00:00.000Z`,
    cashier_name: "Alice",
    state: "PendingSync",
    total_cents: 1_234,
    discount_cents: 34,
    actual_amount_cents: 1_200,
    line_count: 1,
    ...overrides,
  };
}

test("列表固定构造期门店/设备、状态白名单、日期和订单/商品关键字，并按 sequence 游标最多取 50", async () => {
  const connection = new RecordingConnection();
  connection.getAllResults.push(
    [summaryRow(9), summaryRow(8), summaryRow(7)],
    [
      { order_guid: "order-9", method: "cash", amount_cents: 600 },
      { order_guid: "order-9", method: "card", amount_cents: 600 },
      { order_guid: "order-8", method: "voucher", amount_cents: 1_200 },
    ],
  );
  const store = new SqliteLocalHistoryStore(connection, {
    storeCode: " S1 ",
    deviceCode: " IPAD-1 ",
  });

  const page = await store.list({
    soldFromIso: fromIso,
    soldToIso: toIso,
    keyword: "%Tea_",
    cursor: 10,
    limit: 2,
  });

  assert.deepEqual(
    page.orders.map((order) => ({
      orderGuid: order.orderGuid,
      localSequence: order.localSequence,
      paymentSummary: order.paymentSummary,
    })),
    [
      {
        orderGuid: "order-9",
        localSequence: 9,
        paymentSummary: "Cash, Card",
      },
      {
        orderGuid: "order-8",
        localSequence: 8,
        paymentSummary: "Voucher",
      },
    ],
  );
  assert.equal(page.nextCursor, 8);
  assert.equal("storeCode" in (page.orders[0] ?? {}), false);
  assert.equal("deviceCode" in (page.orders[0] ?? {}), false);

  const pageRead = connection.reads[0];
  assert.ok(pageRead);
  assert.match(pageRead.sql, /o\.store_code = \?/u);
  assert.match(pageRead.sql, /o\.device_code = \?/u);
  assert.match(
    pageRead.sql,
    /'CompletedLocal'.*'PendingSync'.*'Syncing'.*'Synced'.*'Blocked403'.*'Rejected'/su,
  );
  assert.doesNotMatch(pageRead.sql, /'Draft'|'Completing'/u);
  assert.match(pageRead.sql, /o\.local_sequence < \?/u);
  assert.match(pageRead.sql, /ORDER BY o\.local_sequence DESC/u);
  assert.match(pageRead.sql, /FROM local_order_lines search/u);
  assert.match(pageRead.sql, /search\.product_code/u);
  assert.match(pageRead.sql, /search\.item_number/u);
  assert.match(pageRead.sql, /search\.lookup_code/u);
  assert.match(pageRead.sql, /search\.display_name/u);
  assert.match(pageRead.sql, /REPLACE\(o\.order_guid, '-', ''\)/u);
  assert.ok(pageRead.parameters.includes("S1"));
  assert.ok(pageRead.parameters.includes("IPAD-1"));
  assert.ok(pageRead.parameters.includes(fromIso));
  assert.ok(pageRead.parameters.includes(toIso));
  assert.ok(pageRead.parameters.includes(10));
  assert.ok(pageRead.parameters.includes("%\\%Tea\\_%"));
  assert.equal(pageRead.parameters.at(-1), 3);
});

test("详情按同一可信 scope 和完成状态读取，仅映射安全行与付款摘要", async () => {
  const connection = new RecordingConnection();
  const longDisplayName = "T".repeat(256);
  connection.getFirstResult = summaryRow(9, {
    order_guid: "order-9",
    unsafe_header: "cashier-id",
  });
  connection.getAllResults.push(
    [
      {
        line_id: "line-1",
        product_code: "P1",
        item_number: "I1",
        lookup_code: "930001",
        display_name: longDisplayName,
        quantity: "1",
        unit_price_cents: 1_234,
        discount_cents: 34,
        actual_amount_cents: 1_200,
        line_kind: "sale",
        reference_code: "do-not-expose",
        return_source_key: "do-not-expose",
      },
    ],
    [
      {
        method: "card",
        amount_cents: 1_200,
        tender_guid: "do-not-expose",
        payment_attempt_id: "do-not-expose",
        reference: "do-not-expose",
      },
    ],
  );
  const store = new SqliteLocalHistoryStore(connection, {
    storeCode: "S1",
    deviceCode: "IPAD-1",
  });

  const details = await store.getDetails("order-9");

  assert.deepEqual(details, {
    orderGuid: "order-9",
    localSequence: 9,
    soldAtIso: "2026-07-31T00:00:00.000Z",
    cashierName: "Alice",
    state: "PendingSync",
    totalCents: 1_234,
    discountCents: 34,
    actualAmountCents: 1_200,
    lines: [
      {
        lineId: "line-1",
        productCode: "P1",
        itemNumber: "I1",
        lookupCode: "930001",
        displayName: longDisplayName,
        quantity: "1",
        unitPriceCents: 1_234,
        discountCents: 34,
        actualAmountCents: 1_200,
        kind: "sale",
      },
    ],
    tenders: [{ method: "card", amountCents: 1_200 }],
  });
  for (const read of connection.reads) {
    assert.ok(read.parameters.includes("S1"));
    assert.ok(read.parameters.includes("IPAD-1"));
    assert.match(read.sql, /local_orders/u);
  }
});

test("零付款订单使用空摘要，交由页面按当前语言显示占位", async () => {
  const connection = new RecordingConnection();
  connection.getAllResults.push([summaryRow(1)], []);
  const store = new SqliteLocalHistoryStore(connection, {
    storeCode: "S1",
    deviceCode: "IPAD-1",
  });

  const page = await store.list({
    soldFromIso: fromIso,
    soldToIso: toIso,
    keyword: null,
    cursor: null,
    limit: 50,
  });

  assert.equal(page.orders[0]?.paymentSummary, "");
});

test("越 scope 或未完成订单的详情返回 null，不继续读取行和付款", async () => {
  const connection = new RecordingConnection();
  const store = new SqliteLocalHistoryStore(connection, {
    storeCode: "S1",
    deviceCode: "IPAD-1",
  });

  assert.equal(await store.getDetails("other-order"), null);
  assert.equal(connection.reads.length, 1);
  assert.ok(connection.reads[0]?.parameters.includes("other-order"));
  assert.ok(connection.reads[0]?.parameters.includes("S1"));
  assert.ok(connection.reads[0]?.parameters.includes("IPAD-1"));
});

test("构造期拒绝空 scope，查询拒绝空订单号和超过 50 条", async () => {
  const connection = new RecordingConnection();
  assert.throws(
    () =>
      new SqliteLocalHistoryStore(connection, {
        storeCode: " ",
        deviceCode: "IPAD-1",
      }),
  );
  const store = new SqliteLocalHistoryStore(connection, {
    storeCode: "S1",
    deviceCode: "IPAD-1",
  });
  await assert.rejects(() =>
    store.list({
      soldFromIso: fromIso,
      soldToIso: toIso,
      keyword: null,
      cursor: null,
      limit: 51,
    }),
  );
  await assert.rejects(() => store.getDetails(" "));
  assert.equal(connection.reads.length, 0);
});
