import assert from "node:assert/strict";
import test from "node:test";

import {
  OrderRepositoryReturnReceiptRenderer,
  type FrozenReturnReceiptSettings,
} from "./return-receipt-renderer";

import type { LocalOrder, OrderRepositoryPort } from "@/core/contracts";


const returnOrderGuid = "return-order-1";

test("receipt 退货使用冻结 58mm 英文设置，展示退款语义且绝不打印 provider 敏感值", async () => {
  const renderer = createRenderer(order(), settings());
  const rendered = await renderer.render(returnOrderGuid);
  const text = decode(rendered.receiptBytes);

  assert.equal(rendered.printerId, "printer-1");
  assert.match(text, /REFUND RECEIPT/u);
  assert.match(text, /Refund processed/u);
  assert.match(text, /\$-5\.00/u);
  assert.match(text, /-2/u);
  assert.equal(text.includes("$2.50"), false, "WPF 商品行只打印 lookup code、数量和行金额");
  assert.equal(text.includes("SQ:payment-secret"), false);
  assert.equal(text.includes("RFN-SECRET"), false);
  assert.equal(text.includes("reservation-secret"), false);
  assert.equal(text.includes("4111111111111111"), false);
});

test("no-receipt 退货允许 originalOrderGuid 为 null，并使用 80mm 中文标题", async () => {
  const rendered = await createRenderer(
    order({ originalOrderGuid: null }),
    settings({ paper: "80mm", locale: "zh-CN" }),
  ).render(returnOrderGuid);
  const text = decode(rendered.receiptBytes);

  assert.match(text, /退款小票/u);
  assert.match(text, /退款已处理/u);
  assert.equal(text.includes("REPRINT"), false);
});

test("账本金额不闭合、混杂 sale、Draft、正 tender 或缺失冻结设置均 fail closed", async () => {
  await assert.rejects(() => createRenderer(order({ actualAmount: money(-400) }), settings()).render(returnOrderGuid));
  await assert.rejects(() => createRenderer(order({ lines: [{ ...order().lines[0]!, kind: "sale" }] }), settings()).render(returnOrderGuid));
  await assert.rejects(() => createRenderer(order({ state: "Draft" }), settings()).render(returnOrderGuid));
  await assert.rejects(() => createRenderer(order({ tenders: [{ ...order().tenders[0]!, amount: money(500) }] }), settings()).render(returnOrderGuid));
  await assert.rejects(() => createRenderer(order(), null).render(returnOrderGuid));
});

function createRenderer(orderValue: LocalOrder, settingsValue: FrozenReturnReceiptSettings | null): OrderRepositoryReturnReceiptRenderer {
  const orders: Pick<OrderRepositoryPort, "getByGuid"> = { async getByGuid() { return orderValue; } };
  return new OrderRepositoryReturnReceiptRenderer(orders, {
    async getFrozenReturnReceiptSettings() { return settingsValue; },
  });
}

function order(overrides: Partial<LocalOrder> = {}): LocalOrder {
  return {
    orderGuid: returnOrderGuid,
    localSequence: 42,
    storeCode: "S01",
    deviceCode: "IPAD-1",
    cashierId: "cashier-1",
    cashierName: "Cashier",
    soldAtIso: "2026-07-28T00:00:00.000Z",
    state: "PendingSync",
    total: money(-500),
    discount: money(0),
    actualAmount: money(-500),
    originalOrderGuid: "original-order-1",
    lines: [{
      lineId: "return-line-1", productCode: "P1", itemNumber: "I1", lookupCode: "P1", displayName: "Product",
      quantity: "2", unitPrice: money(250), discount: money(0), actualAmount: money(-500),
      priceSource: "catalog", kind: "return", returnSourceKey: "return-source-1",
      originalOrderGuid: "original-order-1", originalOrderDetailGuid: "detail-1",
    }],
    tenders: [{
      tenderGuid: "tender-card", method: "card", amount: money(-500), reference: "SQ:payment-secret RFN-SECRET 4111111111111111", reservationToken: "reservation-secret",
    }],
    ...overrides,
  };
}

function settings(overrides: Partial<FrozenReturnReceiptSettings> = {}): FrozenReturnReceiptSettings {
  return {
    printerId: "printer-1", paper: "58mm", locale: "en",
    store: { brandName: "Hot Bargain", storeName: "Store", address: "1 Test St", phone: "123", abn: "ABN" },
    ...overrides,
  };
}

function money(cents: number) { return { currency: "AUD" as const, cents }; }
function decode(bytes: Uint8Array) { return new TextDecoder().decode(bytes); }
