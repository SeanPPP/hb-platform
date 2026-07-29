import assert from "node:assert/strict";
import test from "node:test";

import {
  AtomicCashCheckoutDatabaseAdapter,
  type CashFulfilmentDraft,
} from "./atomic-cash-database-adapter";
import { CashCheckoutService } from "./cash-checkout-service";

import type { CompleteCashOrderCommand } from "@/core/contracts";

test("现金领域完成命令与预渲染履约草稿只交给一次原子 committer", async () => {
  const events: string[] = [];
  const committed: { command: CompleteCashOrderCommand | null } = {
    command: null,
  };
  const draft = fulfilment();
  const database = new AtomicCashCheckoutDatabaseAdapter(
    {
      async completeCashOrderWithFulfilment(command, planned) {
        events.push("commit");
        committed.command = command;
        assert.equal(planned, draft);
      },
    },
    {
      async createDraft(command) {
        events.push("plan");
        assert.equal(command.order.orderGuid, "id-1");
        return draft;
      },
    },
  );
  let id = 0;
  const service = new CashCheckoutService(
    database,
    { nextLocalSequence: async () => 1 },
    {
      createId: () => `id-${++id}`,
      nowIso: () => "2026-07-28T00:00:00.000Z",
      returnCapacity: async () => true,
    },
  );

  const result = await service.complete({
    checkoutIntentId: "intent-1",
    cart: cart(),
    cashTenderedCents: 500,
    storeCode: "S1",
    deviceCode: "IPAD1",
    cashierId: "C1",
    cashierName: "Cashier",
  });

  assert.deepEqual(events, ["plan", "commit"]);
  assert.equal(result.canClearCart, true);
  assert.equal(result.orderGuid, "id-1");
  assert.equal(committed.command?.order.orderGuid, result.orderGuid);
});

test("小票规划或原子提交失败时现金服务不返回 completed", async () => {
  let commitCalls = 0;
  const plannerFailure = new AtomicCashCheckoutDatabaseAdapter(
    {
      async completeCashOrderWithFulfilment() {
        commitCalls += 1;
      },
    },
    {
      async createDraft() {
        throw new Error("receipt render failed");
      },
    },
  );
  const commitFailure = new AtomicCashCheckoutDatabaseAdapter(
    {
      async completeCashOrderWithFulfilment() {
        throw new Error("disk full");
      },
    },
    {
      async createDraft() {
        return fulfilment();
      },
    },
  );

  await assert.rejects(
    () => completeWith(plannerFailure),
    /receipt render failed/,
  );
  await assert.rejects(() => completeWith(commitFailure), /disk full/);
  assert.equal(commitCalls, 0);
});

test("适配器拒绝零次或多次 completion，不能伪装事务语义", async () => {
  let commitCalls = 0;
  const database = new AtomicCashCheckoutDatabaseAdapter(
    {
      async completeCashOrderWithFulfilment() {
        commitCalls += 1;
      },
    },
    {
      async createDraft() {
        return fulfilment();
      },
    },
  );

  await assert.rejects(
    () => database.runInTransaction(async () => "no command"),
    /did not produce/,
  );
  await assert.rejects(
    () => database.runInTransaction(async (transaction) => {
      await transaction.completeCashOrder(command());
      await transaction.completeCashOrder(command());
    }),
    /more than one/,
  );
  assert.equal(commitCalls, 0);
});

async function completeWith(
  database: AtomicCashCheckoutDatabaseAdapter,
): Promise<void> {
  let id = 0;
  const service = new CashCheckoutService(
    database,
    { nextLocalSequence: async () => 1 },
    {
      createId: () => `id-${++id}`,
      nowIso: () => "2026-07-28T00:00:00.000Z",
      returnCapacity: async () => true,
    },
  );
  await service.complete({
    checkoutIntentId: "intent-1",
    cart: cart(),
    cashTenderedCents: 500,
    storeCode: "S1",
    deviceCode: "IPAD1",
    cashierId: "C1",
    cashierName: "Cashier",
  });
}

function cart() {
  return {
    revision: 1,
    mode: "sale" as const,
    subtotal: { currency: "AUD" as const, cents: 500 },
    discount: { currency: "AUD" as const, cents: 0 },
    actualAmount: { currency: "AUD" as const, cents: 500 },
    lines: [{
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
      kind: "sale" as const,
      returnSourceKey: null,
      originalOrderGuid: null,
      originalOrderDetailGuid: null,
    }],
  };
}

function fulfilment(): CashFulfilmentDraft {
  return {
    print: {
      jobId: "print-1",
      orderGuid: "id-1",
      printerId: "xprinter-1",
      receiptBytes: Uint8Array.from([0x1b, 0x40]),
      isReprint: false,
    },
    drawer: {
      eventId: "drawer-1",
      orderGuid: "id-1",
      printJobId: "print-1",
      reason: "cash-sale",
    },
  };
}

function command(): CompleteCashOrderCommand {
  return {
    order: {
      orderGuid: "order-1",
      localSequence: 1,
      storeCode: "S1",
      deviceCode: "IPAD1",
      cashierId: "C1",
      cashierName: "Cashier",
      soldAtIso: "2026-07-28T00:00:00.000Z",
      state: "PendingSync",
      total: { currency: "AUD", cents: 0 },
      discount: { currency: "AUD", cents: 0 },
      actualAmount: { currency: "AUD", cents: 0 },
      lines: [],
      tenders: [],
      originalOrderGuid: null,
    },
    auditEvents: [],
    outbox: {
      messageId: "outbox-1",
      aggregateId: "order-1",
      kind: "order-sync",
      payloadJson: "{}",
      nextAttemptAtIso: "2026-07-28T00:00:00.000Z",
    },
    requiresDrawer: false,
    printPolicy: "never",
  };
}
