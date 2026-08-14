import assert from "node:assert/strict";
import test from "node:test";

import {
  CashFulfilmentPlanner,
  type ReceiptFulfilmentSettings,
  type ReceiptFulfilmentSettingsProvider,
} from "./cash-fulfilment-planner";

import { createAud, type CompleteCashOrderCommand } from "@/core/contracts";

function settings(overrides: Partial<ReceiptFulfilmentSettings> = {}): ReceiptFulfilmentSettings {
  return {
    printEnabled: true,
    drawerEnabled: true,
    cashDrawerPermissionAllowed: true,
    printerId: "xp-q200",
    paper: "58mm",
    locale: "en",
    store: {
      brandName: "Hot Bargain",
      storeName: "Brisbane",
      address: "1 Queen St",
      phone: "0712345678",
      abn: "12 345 678 901",
      returnPolicy: "",
    },
    ...overrides,
  };
}

function provider(value: ReceiptFulfilmentSettings | null): ReceiptFulfilmentSettingsProvider {
  return { getSettings: async () => value };
}

function ids(...values: string[]): () => string {
  let index = 0;
  return () => values[index++] ?? "unexpected-id";
}

function command(overrides: Partial<CompleteCashOrderCommand> = {}): CompleteCashOrderCommand {
  const orderGuid = "order-100";
  return {
    order: {
      orderGuid,
      localSequence: 100,
      storeCode: "BNE",
      deviceCode: "IPAD-1",
      cashierId: "cashier-1",
      cashierName: "Alice",
      soldAtIso: "2026-07-28T10:11:12.000Z",
      state: "PendingSync",
      total: createAud(782),
      discount: createAud(0),
      actualAmount: createAud(782),
      lines: [{
        lineId: "line-1",
        productCode: "P-1",
        itemNumber: "SKU-1",
        lookupCode: "123456",
        displayName: "Spring water",
        quantity: "1",
        unitPrice: createAud(782),
        discount: createAud(0),
        actualAmount: createAud(782),
        priceSource: "catalog",
        kind: "sale",
        returnSourceKey: null,
        originalOrderGuid: null,
        originalOrderDetailGuid: null,
      }],
      tenders: [{
        tenderGuid: "tender-1",
        method: "cash",
        amount: createAud(782),
        reference: null,
        reservationToken: null,
      }],
      originalOrderGuid: null,
    },
    auditEvents: [{
      eventId: "audit-1",
      eventType: "SALE_COMPLETE",
      occurredAtIso: "2026-07-28T10:11:12.000Z",
      orderGuid,
      correlationId: orderGuid,
      payload: { checkoutIntentId: "cash-intent-1", localSequence: 100, cashDueCents: 780, changeCents: 220 },
    }],
    outbox: {
      messageId: "outbox-1",
      aggregateId: orderGuid,
      kind: "order-sync",
      payloadJson: JSON.stringify({ orderGuid }),
      nextAttemptAtIso: "2026-07-28T10:11:12.000Z",
    },
    requiresDrawer: true,
    printPolicy: "automatic",
    ...overrides,
  };
}

test("WPF 等价：现金销售完成不自动打印，只在显式权限允许时创建冻结 printerId 的钱箱事件", async () => {
  const planner = new CashFulfilmentPlanner(provider(settings()), ids("drawer-1"));

  const plan = await planner.createDraft(command());

  assert.equal(plan.drawerDisposition, "queued");
  assert.equal(plan.draft.print, null);
  assert.equal(plan.draft.drawer?.eventId, "drawer-1");
  assert.equal(plan.draft.drawer?.printerId, "xp-q200");
  assert.equal(plan.draft.drawer?.printJobId, null);
  assert.equal(plan.draft.drawer?.reason, "cash-sale");
});

test("现金退款同样不自动打印，只持久化退款钱箱原因与原 printerId", async () => {
  const refund = command({
    order: {
      ...command().order,
      actualAmount: createAud(-782),
      total: createAud(-782),
      lines: [{ ...command().order.lines[0]!, quantity: "1", unitPrice: createAud(782), actualAmount: createAud(-782), kind: "return", returnSourceKey: "source-1", originalOrderGuid: "original-1" }],
      tenders: [{ ...command().order.tenders[0]!, amount: createAud(-782) }],
      originalOrderGuid: "original-1",
    },
    auditEvents: [{ ...command().auditEvents[0]!, eventType: "RETURN_REFUND_COMPLETE", payload: { checkoutIntentId: "cash-intent-1", localSequence: 100, cashDueCents: -780, changeCents: 0 } }],
  });
  const planner = new CashFulfilmentPlanner(provider(settings({ paper: "80mm", locale: "zh-CN" })), ids("drawer-2"));

  const plan = await planner.createDraft(refund);

  assert.equal(plan.drawerDisposition, "queued");
  assert.equal(plan.draft.print, null);
  assert.equal(plan.draft.drawer?.printerId, "xp-q200");
  assert.equal(plan.draft.drawer?.reason, "cash-refund");
});

test("打印关闭但钱箱开启时不虚构打印任务，钱箱冻结规范化外设标识且不绑定打印任务", async () => {
  const planner = new CashFulfilmentPlanner(provider(settings({ printEnabled: false, drawerEnabled: true, printerId: " xp-q200 " })), ids("drawer-only"));

  const plan = await planner.createDraft(command());

  assert.equal(plan.drawerDisposition, "queued");
  assert.equal(plan.draft.print, null);
  assert.deepEqual(plan.draft.drawer, {
    eventId: "drawer-only",
    orderGuid: "order-100",
    printerId: "xp-q200",
    printJobId: null,
    reason: "cash-sale",
  });
});

test("禁用、缺少或无效打印机配置时不排入必失败的打印或钱箱任务", async () => {
  let calls = 0;
  const createId = () => { calls += 1; return "must-not-be-used"; };

  const disabled = await new CashFulfilmentPlanner(provider(settings({ printEnabled: false, drawerEnabled: false })), createId).createDraft(command());
  const permissionDenied = await new CashFulfilmentPlanner(provider(settings({ cashDrawerPermissionAllowed: false })), createId).createDraft(command());
  const missingPermission = await new CashFulfilmentPlanner(
    provider({ ...settings(), cashDrawerPermissionAllowed: undefined } as unknown as ReceiptFulfilmentSettings),
    createId,
  ).createDraft(command());
  const missing = await new CashFulfilmentPlanner(provider(null), createId).createDraft(command());
  const invalid = await new CashFulfilmentPlanner(provider(settings({ printerId: "  " })), createId).createDraft(command());

  const emptyDraft = { print: null, drawer: null };
  assert.deepEqual(disabled, {
    draft: emptyDraft,
    drawerDisposition: "disabled",
  });
  assert.deepEqual(permissionDenied, {
    draft: emptyDraft,
    drawerDisposition: "permission-denied",
  });
  assert.deepEqual(missingPermission, {
    draft: emptyDraft,
    drawerDisposition: "unavailable",
  });
  assert.deepEqual(missing, {
    draft: emptyDraft,
    drawerDisposition: "unavailable",
  });
  assert.deepEqual(invalid, {
    draft: emptyDraft,
    drawerDisposition: "unavailable",
  });
  assert.equal(calls, 0);
});

test("命令标识、审计和金额不一致时拒绝生成外设副作用", async () => {
  let calls = 0;
  const planner = new CashFulfilmentPlanner(provider(settings()), () => { calls += 1; return "must-not-be-used"; });
  const invalid = command({
    outbox: { ...command().outbox, aggregateId: "another-order" },
    auditEvents: [{ ...command().auditEvents[0]!, orderGuid: "another-order" }],
  });

  await assert.rejects(() => planner.createDraft(invalid), /order guid mismatch/i);
  assert.equal(calls, 0);
});
