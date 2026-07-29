import assert from "node:assert/strict";
import test from "node:test";

import { HbposApiError, type HbposTransport, type HbposTransportRequest, type HbposTransportResponse } from "../api/hbpos-api";
import type { LocalOrder } from "../contracts/order";
import type { CardSyncEvidenceV1 } from "../contracts/payment";
import type { OrderRepositoryPort } from "../contracts/repositories";
import { OrderSyncMaterialError } from "../db/sqlite-order-sync-material";

import {
  HbposAuditBatchAdapter,
  HbposOrderSyncAdapter,
  type OrderSyncMaterialResolverPort,
} from "./hbpos-sync-adapters";

const orderGuid = "018f1b9b-47c5-7c1b-9f8e-39c5cb3b9d01";
const lineGuid = "018f1b9b-47c5-7c1b-9f8e-39c5cb3b9d02";
const tenderGuid = "018f1b9b-47c5-7c1b-9f8e-39c5cb3b9d03";
const eventGuid = "018f1b9b-47c5-7c1b-9f8e-39c5cb3b9d04";

function order(method: "cash" | "card" | "voucher" = "cash"): LocalOrder {
  return {
    orderGuid, localSequence: 7, storeCode: "1003", deviceCode: "IPAD_1", cashierId: "cashier-1", cashierName: "Alice",
    soldAtIso: "2026-07-28T00:00:00.000Z", state: "PendingSync",
    total: { cents: 1234, currency: "AUD" }, discount: { cents: 34, currency: "AUD" }, actualAmount: { cents: 1200, currency: "AUD" },
    lines: [{ lineId: lineGuid, productCode: "P1", itemNumber: "I1", lookupCode: "931234", displayName: "商品", quantity: "1.250", unitPrice: { cents: 1000, currency: "AUD" }, discount: { cents: 34, currency: "AUD" }, actualAmount: { cents: 966, currency: "AUD" }, priceSource: "catalog", syncProvenance: { referenceCode: "MULTI-123", priceSource: 3 }, kind: "sale", returnSourceKey: null, originalOrderGuid: null, originalOrderDetailGuid: null }],
    tenders: [{ tenderGuid, method, amount: { cents: 1200, currency: "AUD" }, reference: method === "cash" ? null : null, reservationToken: null }], originalOrderGuid: null,
  };
}

function cardSyncEvidence(
  overrides: Partial<CardSyncEvidenceV1> = {},
): CardSyncEvidenceV1 {
  return {
    version: 1,
    provider: "square",
    operation: "purchase",
    processor: "Square",
    txnRef: "square-payment-1",
    authCode: "AUTH01",
    cardType: "VISA",
    cardBin: 411111,
    maskedCardNumber: "411111******1111",
    merchantId: "merchant-1",
    responseCode: "00",
    responseText: "APPROVED",
    stan: "123456",
    bankDateTimeIso: "2026-07-28T00:00:00.000Z",
    amountCents: 1_200,
    refundReference: null,
    ...overrides,
  };
}

class FakeOrders implements OrderRepositoryPort {
  public constructor(private readonly value: LocalOrder | null) {}
  public async nextLocalSequence(): Promise<number> { return 1; }
  public async saveDraft(): Promise<void> {}
  public async getByGuid(guid: string): Promise<LocalOrder | null> { return guid === orderGuid ? this.value : null; }
  public async listLocal(): Promise<readonly LocalOrder[]> { return []; }
  public async transition(): Promise<boolean> { return true; }
}

class FakeTransport implements HbposTransport {
  public readonly calls: HbposTransportRequest[] = [];
  public constructor(private readonly reply: unknown | Error) {}
  public async request<T>(request: HbposTransportRequest): Promise<HbposTransportResponse<T>> {
    this.calls.push(request);
    if (this.reply instanceof Error) throw this.reply;
    return this.reply as HbposTransportResponse<T>;
  }
}

function orderAdapter(reply: unknown | Error, local = order()) {
  const transport = new FakeTransport(reply);
  return { transport, adapter: new HbposOrderSyncAdapter(transport, new FakeOrders(local)) };
}

function trustedOrderAdapter(
  resolver: OrderSyncMaterialResolverPort,
  local: LocalOrder = order("card"),
  linklyEnvironment: string | null = "Sandbox",
) {
  const transport = new FakeTransport({
    status: 200,
    data: {
      success: true,
      data: { orderGuid, accepted: true, alreadySynced: false },
    },
  });
  return {
    transport,
    adapter: new HbposOrderSyncAdapter(
      transport,
      new FakeOrders(local),
      { resolver, linklyEnvironment },
    ),
  };
}

test("现金订单从本地账本构造分币金额和十进制数量，AlreadySynced 视为成功", async () => {
  const { transport, adapter } = orderAdapter({ status: 200, data: { success: true, data: { orderGuid, accepted: true, alreadySynced: true } } });
  const result = await adapter.sync(orderGuid, JSON.stringify({ orderGuid }));
  assert.deepEqual(result, { kind: "synced", alreadySynced: true });
  assert.deepEqual(transport.calls[0]?.data, {
    orderGuid, storeCode: "1003", deviceCode: "IPAD_1", cashierId: "cashier-1", cashierName: "Alice", soldAt: "2026-07-28T00:00:00.000Z",
    totalAmount: 12.34, discountAmount: 0.34, actualAmount: 12, lines: [{ orderLineGuid: lineGuid, productCode: "P1", referenceCode: "MULTI-123", displayName: "商品", lookupCode: "931234", quantity: 1.25, unitPrice: 10, discountAmount: 0.34, actualAmount: 9.66, priceSource: 3, itemNumber: "I1", kind: 1, returnSourceKey: null, originalOrderGuid: null, originalOrderDetailGuid: null }],
    payments: [{ paymentGuid: tenderGuid, method: 1, amount: 12, reference: null, reservationToken: null, cardTransactions: null }],
  });
});

test("缺少冻结交易行来源的旧订单不得按当前目录或 ProductBase 猜测上传", async () => {
  const legacy = order();
  const withoutProvenance: LocalOrder = {
    ...legacy,
    lines: legacy.lines.map(({ syncProvenance: _ignored, ...line }) => line),
  };
  const { transport, adapter } = orderAdapter(
    {
      status: 200,
      data: {
        success: true,
        data: { orderGuid, accepted: true, alreadySynced: false },
      },
    },
    withoutProvenance,
  );

  assert.deepEqual(
    await adapter.sync(orderGuid, JSON.stringify({ orderGuid })),
    {
      kind: "rejected",
      failure: "business-rejection",
      code: "ORDER_SYNC_LINE_PROVENANCE_MISSING",
    },
  );
  assert.equal(transport.calls.length, 0);
});

test("outbox 指针、响应订单换绑和非现金缺少安全引用全部失败关闭", async () => {
  const success = { status: 200, data: { success: true, data: { orderGuid, accepted: true } } };
  const pointer = orderAdapter(success).adapter;
  assert.equal((await pointer.sync(orderGuid, JSON.stringify({ orderGuid: lineGuid }))).kind, "rejected");
  const rebound = orderAdapter({ status: 200, data: { success: true, data: { orderGuid: lineGuid, accepted: true } } }).adapter;
  assert.deepEqual(await rebound.sync(orderGuid, JSON.stringify({ orderGuid })), { kind: "rejected", failure: "business-rejection", code: "ORDER_SYNC_RESPONSE_INVALID" });
  const card = orderAdapter(success, order("card")).adapter;
  assert.deepEqual(await card.sync(orderGuid, JSON.stringify({ orderGuid })), { kind: "rejected", failure: "business-rejection", code: "CARD_PAYMENT_REFERENCE_REQUIRED" });
});

test("HTTP 仅明确设备撤销码锁机，普通 401 与权限/门禁/scope 403 保留待重试", async () => {
  const cases: readonly [Error, string][] = [
    [new HbposApiError("auth", { kind: "http", status: 401 }), "retry"],
    [new HbposApiError("disabled", { kind: "http", status: 401, code: "DEVICE_DISABLED" }), "blocked"],
    [new HbposApiError("forbidden", { kind: "http", status: 403, code: "DEVICE_DISABLED" }), "blocked"],
    [new HbposApiError("permission", { kind: "http", status: 403 }), "retry"],
    [new HbposApiError("scope", { kind: "http", status: 403, code: "DEVICE_SCOPE_FORBIDDEN" }), "retry"],
    [new HbposApiError("gate", { kind: "http", status: 403, code: "POS_IPAD_NEW_TRANSACTIONS_DISABLED" }), "retry"],
    [new HbposApiError("server", { kind: "http", status: 503 }), "retry"],
    [new HbposApiError("rejected", { kind: "envelope", code: "ORDER_REJECTED" }), "rejected"],
  ];
  for (const [error, kind] of cases) {
    const result = await orderAdapter(error).adapter.sync(orderGuid, JSON.stringify({ orderGuid }));
    assert.equal(result.kind, kind);
  }
});

test("受信任材料只进入本次 HTTP 载荷，仓储中的脱敏订单保持不变", async () => {
  const local = order("card");
  const resolverInputs: LocalOrder[] = [];
  let resolverEnvironment: string | null = null;
  const { transport, adapter } = trustedOrderAdapter({
    async resolveForSync(input, environment) {
      resolverInputs.push(input);
      resolverEnvironment = environment;
      return {
        order: {
          ...input,
          tenders: input.tenders.map((tender) => ({
            ...tender,
            reference: "SQ:provider-payment-id",
          })),
        },
        cardSyncEvidenceByTenderGuid: new Map([
          [tenderGuid, cardSyncEvidence()],
        ]),
      };
    },
  }, local);

  assert.deepEqual(
    await adapter.sync(orderGuid, JSON.stringify({ orderGuid })),
    { kind: "synced", alreadySynced: false },
  );
  assert.equal(resolverEnvironment, "Sandbox");
  assert.notEqual(resolverInputs[0], local);
  assert.notEqual(resolverInputs[0]?.tenders, local.tenders);
  assert.equal(local.tenders[0]?.reference, null);
  const data = transport.calls[0]?.data as {
    payments: { reference: string | null }[];
  };
  assert.equal(data.payments[0]?.reference, "SQ:provider-payment-id");
  assert.equal(local.tenders[0]?.reference, null);
});

test("卡交易只从已绑定的受保护证据构造后端 DTO，缺失或换绑证据时不发送请求", async () => {
  const approved = trustedOrderAdapter({
    async resolveForSync(input) {
      return {
        order: {
          ...input,
          tenders: input.tenders.map((tender) => ({
            ...tender,
            reference: "SQ:provider-payment-id",
          })),
        },
        cardSyncEvidenceByTenderGuid: new Map([
          [tenderGuid, cardSyncEvidence()],
        ]),
      };
    },
  });

  assert.deepEqual(
    await approved.adapter.sync(orderGuid, JSON.stringify({ orderGuid })),
    { kind: "synced", alreadySynced: false },
  );
  const approvedData = approved.transport.calls[0]?.data as {
    payments: { cardTransactions: unknown }[];
  };
  assert.deepEqual(approvedData.payments[0]?.cardTransactions, [
    {
      processor: "Square",
      txnRef: "square-payment-1",
      authCode: "AUTH01",
      cardType: "VISA",
      cardBin: 411111,
      maskedCardNumber: "411111******1111",
      merchantId: "merchant-1",
      responseCode: "00",
      responseText: "APPROVED",
      stan: "123456",
      bankDateTime: "2026-07-28T00:00:00.000Z",
      amount: 12,
      receiptText: null,
      refundReference: null,
    },
  ]);

  for (const [name, evidence] of [
    ["missing", new Map<string, CardSyncEvidenceV1>()],
    [
      "amount-mismatch",
      new Map([[tenderGuid, cardSyncEvidence({ amountCents: 1_201 })]]),
    ],
    [
      "operation-mismatch",
      new Map([
        [tenderGuid, cardSyncEvidence({ operation: "refund" })],
      ]),
    ],
  ] as const) {
    const rejected = trustedOrderAdapter({
      async resolveForSync(input) {
        return {
          order: {
            ...input,
            tenders: input.tenders.map((tender) => ({
              ...tender,
              reference: "SQ:provider-payment-id",
            })),
          },
          cardSyncEvidenceByTenderGuid: evidence,
        };
      },
    });
    assert.deepEqual(
      await rejected.adapter.sync(orderGuid, JSON.stringify({ orderGuid })),
      {
        kind: "rejected",
        failure: "business-rejection",
        code:
          name === "missing"
            ? "CARD_SYNC_EVIDENCE_REQUIRED"
            : "CARD_SYNC_EVIDENCE_MISMATCH",
      },
    );
    assert.equal(rejected.transport.calls.length, 0);
  }
});

test("未配置 Linkly 环境仍原样交给受信任解析器，由实际 tender 决定是否拒绝", async () => {
  let resolverEnvironment: string | null | undefined;
  const { adapter } = trustedOrderAdapter(
    {
      async resolveForSync(input, environment) {
        resolverEnvironment = environment;
        return {
          order: {
            ...input,
            tenders: input.tenders.map((tender) => ({
              ...tender,
              reference: "SQ:provider-payment-id",
            })),
          },
          cardSyncEvidenceByTenderGuid: new Map([
            [tenderGuid, cardSyncEvidence()],
          ]),
        };
      },
    },
    order("card"),
    null,
  );

  assert.deepEqual(
    await adapter.sync(orderGuid, JSON.stringify({ orderGuid })),
    { kind: "synced", alreadySynced: false },
  );
  assert.equal(resolverEnvironment, null);
});

test("稳定材料错配返回明确业务拒绝且不发送请求", async () => {
  const { transport, adapter } = trustedOrderAdapter({
    async resolveForSync() {
      throw new OrderSyncMaterialError("ORDER_SYNC_TENDER_MISMATCH");
    },
  });

  assert.deepEqual(
    await adapter.sync(orderGuid, JSON.stringify({ orderGuid })),
    {
      kind: "rejected",
      failure: "business-rejection",
      code: "ORDER_SYNC_TENDER_MISMATCH",
    },
  );
  assert.equal(transport.calls.length, 0);
});

test("意外数据库或 IO 错误向 outbox 抛出且不发送请求", async () => {
  const ioError = new Error("temporary database read failure");
  const { transport, adapter } = trustedOrderAdapter({
    async resolveForSync() {
      throw ioError;
    },
  });

  await assert.rejects(
    adapter.sync(orderGuid, JSON.stringify({ orderGuid })),
    (error: unknown) => error === ioError,
  );
  assert.equal(transport.calls.length, 0);
});

test("券退款允许已签发券码且不得携带购买 reservation token", async () => {
  const local: LocalOrder = {
    ...order("voucher"),
    total: { cents: -1200, currency: "AUD" },
    discount: { cents: 0, currency: "AUD" },
    actualAmount: { cents: -1200, currency: "AUD" },
    tenders: [{
      tenderGuid,
      method: "voucher",
      amount: { cents: -1200, currency: "AUD" },
      reference: null,
      reservationToken: null,
    }],
    originalOrderGuid: "018f1b9b-47c5-7c1b-9f8e-39c5cb3b9d09",
  };
  const { transport, adapter } = trustedOrderAdapter({
    async resolveForSync(input) {
      return {
        order: {
          ...input,
          tenders: input.tenders.map((tender) => ({
            ...tender,
            reference: "issued-voucher-code",
            reservationToken: null,
          })),
        },
        cardSyncEvidenceByTenderGuid: new Map(),
      };
    },
  }, local);

  assert.equal(
    (await adapter.sync(orderGuid, JSON.stringify({ orderGuid }))).kind,
    "synced",
  );
  const data = transport.calls[0]?.data as {
    payments: { reference: string; reservationToken: string | null }[];
  };
  assert.deepEqual(data.payments[0], {
    paymentGuid: tenderGuid,
    method: 3,
    amount: -12,
    reference: "issued-voucher-code",
    reservationToken: null,
    cardTransactions: null,
  });
});

test("审计从原订单加载身份；duplicate 成功、rejected 失败，并从载荷剔除 token/PAN", async () => {
  const transport = new FakeTransport({ status: 200, data: { success: true, data: { results: [{ eventId: eventGuid, status: "duplicate" }] } } });
  const adapter = new HbposAuditBatchAdapter(transport, new FakeOrders(order()), { storeCode: "bad", deviceCode: "bad", appVersion: "1.0.0", instanceId: "ipad-install" });
  const event = { eventId: eventGuid, eventType: "SALE_COMPLETE", occurredAtIso: "2026-07-28T00:00:00.000Z", orderGuid, correlationId: orderGuid, payload: { source: "cash", authorizationToken: "secret", pan: "4111111111111111" } } as const;
  assert.deepEqual(await adapter.upload([event]), { kind: "uploaded" });
  const body = transport.calls[0]?.data as { events: { storeCode: string; deviceCode: string; properties: Record<string, string> | null }[] };
  assert.deepEqual(body.events[0]?.properties, { source: "cash" });
  assert.equal(body.events[0]?.storeCode, "1003");
  assert.equal(body.events[0]?.deviceCode, "IPAD_1");
  assert.doesNotMatch(JSON.stringify(body), /secret|4111111111111111/);

  const rejected = new HbposAuditBatchAdapter(new FakeTransport({ status: 200, data: { success: true, data: { results: [{ eventId: eventGuid, status: "rejected", errorCode: "INVALID_EVENT" }] } } }), new FakeOrders(order()), { storeCode: "1003", deviceCode: "IPAD_1", appVersion: "1", instanceId: "i" });
  assert.deepEqual(await rejected.upload([event]), { kind: "rejected", code: "INVALID_EVENT" });
});

test("购物车审计将快照明细、分币金额和授权上下文映射为后端合同", async () => {
  const transport = new FakeTransport({
    status: 200,
    data: { success: true, data: { results: [{ eventId: eventGuid, status: "accepted" }] } },
  });
  const adapter = new HbposAuditBatchAdapter(transport, new FakeOrders(null), {
    storeCode: "1003",
    deviceCode: "IPAD_1",
    appVersion: "1.0.0",
    instanceId: "ipad-install",
  });
  const event = {
    eventId: eventGuid,
    eventType: "CART_ITEM_PRICE_CHANGE",
    occurredAtIso: "2026-07-28T00:00:00.000Z",
    orderGuid: null,
    correlationId: orderGuid,
    payload: {
      outcome: "Succeeded",
      action: "cart-item-price-change",
      screen: "pos-terminal",
      permissionCode: "POS.CART.PRICE_CHANGE",
      authorizationMode: "supervisor",
      requestingCashierId: "cashier-1",
      authorizingCashierId: "manager-2",
      reason: "supervisor-approved",
      itemCount: 1,
      beforeSubtotalCents: 1234,
      afterSubtotalCents: 1334,
      beforeDiscountCents: 34,
      afterDiscountCents: 54,
      beforeActualCents: 1200,
      afterActualCents: 1280,
      amountDeltaCents: 80,
      items: [{
        productCode: "P1",
        itemNumber: "I1",
        referenceCode: "MULTI-123",
        lookupCode: "931234",
        displayName: "商品",
        lineKind: "sale",
        beforeQuantity: 1,
        afterQuantity: 1,
        quantityDelta: 0,
        beforeUnitPriceCents: 1000,
        afterUnitPriceCents: 1100,
        unitPriceDeltaCents: 100,
        beforeDiscountCents: 34,
        afterDiscountCents: 54,
        discountDeltaCents: 20,
        beforeGrossCents: 1000,
        afterGrossCents: 1100,
        grossDeltaCents: 100,
        beforeActualCents: 966,
        afterActualCents: 1046,
        actualDeltaCents: 80,
      }],
      authorizationToken: "secret",
    },
  } as const;

  assert.deepEqual(await adapter.upload([event]), { kind: "uploaded" });
  const body = transport.calls[0]?.data as { events: Record<string, unknown>[] };
  assert.deepEqual(body.events[0], {
    eventId: eventGuid,
    schemaVersion: 1,
    occurredAtUtc: "2026-07-28T00:00:00.000Z",
    operationType: "CART_ITEM_PRICE_CHANGE",
    outcome: "Succeeded",
    cashierId: null,
    cashierName: null,
    isOfflineCached: true,
    isEmergencyOverride: false,
    storeCode: "1003",
    deviceCode: "IPAD_1",
    appVersion: "1.0.0",
    instanceId: "ipad-install",
    orderGuid: null,
    correlationId: orderGuid,
    currencyCode: "AUD",
    beforeGross: 12.34,
    afterGross: 13.34,
    beforeDiscount: 0.34,
    afterDiscount: 0.54,
    beforeActual: 12,
    afterActual: 12.8,
    amountDelta: 0.8,
    properties: {
      action: "cart-item-price-change",
      screen: "pos-terminal",
      reason: "supervisor-approved",
      itemCount: "1",
      requestingCashierId: "cashier-1",
      authorizingCashierId: "manager-2",
      permissionCode: "POS.CART.PRICE_CHANGE",
      authorizationMode: "supervisor",
    },
    items: [{
      productCode: "P1",
      itemNumber: "I1",
      referenceCode: "MULTI-123",
      lookupCode: "931234",
      displayName: "商品",
      lineKind: "sale",
      beforeQuantity: 1,
      afterQuantity: 1,
      quantityDelta: 0,
      beforeUnitPrice: 10,
      afterUnitPrice: 11,
      unitPriceDelta: 1,
      beforeDiscountAmount: 0.34,
      afterDiscountAmount: 0.54,
      discountAmountDelta: 0.2,
      beforeGrossAmount: 10,
      afterGrossAmount: 11,
      grossAmountDelta: 1,
      beforeActualAmount: 9.66,
      afterActualAmount: 10.46,
      actualAmountDelta: 0.8,
    }],
  });
  assert.doesNotMatch(JSON.stringify(body), /secret/);

  assert.deepEqual(
    await adapter.upload([{
      ...event,
      payload: { ...event.payload, afterActualCents: Number.MAX_SAFE_INTEGER + 1 },
    }]),
    { kind: "rejected", code: "AUDIT_AMOUNT_INVALID" },
  );
  assert.equal(transport.calls.length, 1);
});

test("硬件失败审计保留 Failed outcome，非法 outcome 在请求前失败关闭", async () => {
  const transport = new FakeTransport({ status: 200, data: { success: true, data: { results: [{ eventId: eventGuid, status: "accepted" }] } } });
  const adapter = new HbposAuditBatchAdapter(transport, new FakeOrders(order()), {
    storeCode: "1003",
    deviceCode: "IPAD_1",
    appVersion: "1.0.0",
    instanceId: "ipad-install",
  });
  const event = {
    eventId: eventGuid,
    eventType: "CASH_DRAWER_OPEN",
    occurredAtIso: "2026-07-28T00:00:00.000Z",
    orderGuid,
    correlationId: orderGuid,
    payload: { outcome: "Failed", reason: "cash-sale", status: "Failed" },
  } as const;

  assert.deepEqual(await adapter.upload([event]), { kind: "uploaded" });
  const body = transport.calls[0]?.data as {
    events: { outcome: string; properties: Record<string, string> | null }[];
  };
  assert.equal(body.events[0]?.outcome, "Failed");
  assert.deepEqual(body.events[0]?.properties, {
    status: "Failed",
    reason: "cash-sale",
  });

  assert.deepEqual(
    await adapter.upload([{ ...event, payload: { outcome: "maybe" } }]),
    { kind: "rejected", code: "AUDIT_OUTCOME_INVALID" },
  );
  assert.equal(transport.calls.length, 1);
});

test("M16 礼券撤销的本地终态事实映射为后端审计枚举，避免堵塞上传队列", async () => {
  const blockedEventGuid = "018f1b9b-47c5-7c1b-9f8e-39c5cb3b9d05";
  const transport = new FakeTransport({
    status: 200,
    data: {
      success: true,
      data: {
        results: [
          { eventId: eventGuid, status: "accepted" },
          { eventId: blockedEventGuid, status: "accepted" },
        ],
      },
    },
  });
  const adapter = new HbposAuditBatchAdapter(
    transport,
    new FakeOrders(order("voucher")),
    {
      storeCode: "1003",
      deviceCode: "IPAD_1",
      appVersion: "1.0.0",
      instanceId: "ipad-install",
    },
  );
  const terminalFacts = [
    {
      eventId: eventGuid,
      eventType: "PAYMENT_TENDER_REMOVE",
      occurredAtIso: "2026-07-28T00:00:00.000Z",
      orderGuid,
      correlationId: eventGuid,
      payload: {
        action: "payment-tender-remove",
        outcome: "success",
        reason: "SALE",
      },
    },
    {
      eventId: blockedEventGuid,
      eventType: "PAYMENT_TENDER_REMOVE",
      occurredAtIso: "2026-07-28T00:00:01.000Z",
      orderGuid,
      correlationId: blockedEventGuid,
      payload: {
        action: "payment-tender-remove",
        outcome: "blocked",
        reason: "SALE",
      },
    },
  ] as const;

  assert.deepEqual(await adapter.upload(terminalFacts), { kind: "uploaded" });
  const body = transport.calls[0]?.data as {
    events: { outcome: string; properties: Record<string, string> | null }[];
  };
  assert.deepEqual(
    body.events.map((event) => event.outcome),
    ["Succeeded", "Denied"],
  );
  assert.deepEqual(body.events[0]?.properties, {
    action: "payment-tender-remove",
    reason: "SALE",
  });
});
