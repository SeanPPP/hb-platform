import assert from "node:assert/strict";
import test from "node:test";

import { HbposRemoteHistoryApi } from "./remote-history-api";

import type { HbposTransport, HbposTransportRequest, HbposTransportResponse } from "../../transport";

class QueueTransport implements HbposTransport {
  public readonly requests: HbposTransportRequest[] = [];

  public constructor(private readonly responses: unknown[]) {}

  public async request<T>(
    request: HbposTransportRequest,
  ): Promise<HbposTransportResponse<T>> {
    this.requests.push(request);
    if (this.responses.length === 0) throw new Error("Missing fake response.");
    return { status: 200, data: this.responses.shift() as T };
  }
}

const query = {
  storeCode: "UNTRUSTED",
  deviceCode: " IPAD-2 ",
  soldFromIso: "2026-07-27T00:00:00+10:00",
  soldToIso: "2026-07-27T23:59:59.999+10:00",
  keyword: " 930001 ",
  take: 100 as const,
};

test("list 固定可信门店和 take=100，并严格转为整数分", async () => {
  const transport = new QueueTransport([
    {
      success: true,
      data: {
        orders: [
          {
            orderGuid: "10000000-0000-4000-8000-000000000001",
            storeCode: "S1",
            deviceCode: "IPAD-2",
            cashierName: "Alice",
            soldAt: "2026-07-27T01:02:03Z",
            totalAmount: 12.34,
            discountAmount: 0.35,
            actualAmount: 11.99,
            lineCount: 2,
            paymentSummary: "Cash",
            statusLabel: "Synced",
          },
        ],
      },
    },
  ]);
  const api = new HbposRemoteHistoryApi(transport, " S1 ");

  const rows = await api.list(query);

  assert.deepEqual(transport.requests, [
    {
      method: "GET",
      url: "/api/v1/orders/history",
      params: {
        storeCode: "S1",
        deviceCode: "IPAD-2",
        soldFrom: "2026-07-26T14:00:00.000Z",
        soldTo: "2026-07-27T13:59:59.999Z",
        keyword: "930001",
        take: 100,
      },
    },
  ]);
  assert.deepEqual(rows, [
    {
      orderGuid: "10000000-0000-4000-8000-000000000001",
      storeCode: "S1",
      deviceCode: "IPAD-2",
      cashierName: "Alice",
      soldAtIso: "2026-07-27T01:02:03.000Z",
      totalCents: 1234,
      discountCents: 35,
      actualAmountCents: 1199,
      lineCount: 2,
      paymentSummary: "Cash",
      statusLabel: "Synced",
    },
  ]);
});

test("list 未指定终端时不附加有效终端筛选", async () => {
  const transport = new QueueTransport([
    {
      success: true,
      data: { orders: [] },
    },
  ]);
  const api = new HbposRemoteHistoryApi(transport, "S1");

  await api.list({
    ...query,
    deviceCode: null,
  });

  assert.equal(transport.requests[0]?.params?.storeCode, "S1");
  assert.equal(transport.requests[0]?.params?.deviceCode, undefined);
});

test("details 将数量转为十进制字符串，并只保留付款脱敏白名单", async () => {
  const transport = new QueueTransport([
    {
      success: true,
      data: {
        orderGuid: "10000000-0000-4000-8000-000000000001",
        storeCode: "S1",
        deviceCode: "IPAD-2",
        cashierName: "Alice",
        soldAt: "2026-07-27T01:02:03Z",
        totalAmount: 12.34,
        discountAmount: 0,
        actualAmount: 12.34,
        lines: [
          {
            orderLineGuid: "20000000-0000-4000-8000-000000000001",
            productCode: "P1",
            referenceCode: "PUBLIC-PRODUCT-REF",
            displayName: "Tea",
            lookupCode: "930001",
            itemNumber: "I1",
            quantity: 1.25e-7,
            unitPrice: 12.34,
            discountAmount: 0,
            actualAmount: 12.34,
            kind: 1,
          },
        ],
        payments: [
          {
            paymentGuid: "30000000-0000-4000-8000-000000000001",
            method: 2,
            amount: 12.34,
            reference: "provider-checkout-secret",
            cardTransactions: [
              {
                processor: "provider-id",
                txnRef: "txn-secret",
                authCode: "AUTH-SECRET",
                cardType: "VISA",
                cardBin: 411111,
                maskedCardNumber: "**** **** **** 1234",
                merchantId: "MERCHANT-SECRET",
                responseCode: "00",
                responseText: "APPROVED",
                stan: "123456",
                receiptText: "RAW RECEIPT SECRET",
                refundReference: "REFUND-SECRET",
              },
            ],
          },
        ],
      },
    },
  ]);
  const api = new HbposRemoteHistoryApi(transport, "S1");

  const details = await api.getDetails(
    "10000000-0000-4000-8000-000000000001",
  );

  assert.equal(details?.lines[0]?.quantity, "0.000000125");
  assert.deepEqual(details?.payments, [
    {
      paymentGuid: "30000000-0000-4000-8000-000000000001",
      method: "card",
      amountCents: 1234,
      displayReference: null,
      cardType: "VISA",
      maskedCardNumber: "**** **** **** 1234",
    },
  ]);
  const serialized = JSON.stringify(details);
  for (const secret of [
    "provider-checkout-secret",
    "provider-id",
    "txn-secret",
    "AUTH-SECRET",
    "411111",
    "MERCHANT-SECRET",
    "123456",
    "RAW RECEIPT SECRET",
    "REFUND-SECRET",
    "APPROVED",
  ]) {
    assert.equal(serialized.includes(secret), false);
  }
});

test("完整 PAN 即使来自 masked 字段也被丢弃", async () => {
  const transport = new QueueTransport([
    {
      success: true,
      data: {
        orderGuid: "10000000-0000-4000-8000-000000000001",
        storeCode: "S1",
        deviceCode: "IPAD-2",
        cashierName: "Alice",
        soldAt: "2026-07-27T01:02:03Z",
        totalAmount: 1,
        discountAmount: 0,
        actualAmount: 1,
        lines: [],
        payments: [
          {
            paymentGuid: "30000000-0000-4000-8000-000000000001",
            method: 2,
            amount: 1,
            reference: "ignore-me",
            cardTransactions: [
              {
                maskedCardNumber: "4111111111111111",
              },
            ],
          },
        ],
      },
    },
  ]);

  const details = await new HbposRemoteHistoryApi(
    transport,
    "S1",
  ).getDetails("10000000-0000-4000-8000-000000000001");

  assert.equal(details?.payments[0]?.maskedCardNumber, null);
});

test("details 的 200/null 保持 null", async () => {
  const transport = new QueueTransport([{ success: true, data: null }]);

  const details = await new HbposRemoteHistoryApi(
    transport,
    "S1",
  ).getDetails("10000000-0000-4000-8000-000000000001");

  assert.equal(details, null);
});

test("金额超过两位小数或响应门店越权时严格拒绝", async () => {
  const invalidMoney = new HbposRemoteHistoryApi(
    new QueueTransport([
      {
        success: true,
        data: {
          orders: [
            {
              orderGuid: "10000000-0000-4000-8000-000000000001",
              storeCode: "S1",
              deviceCode: "IPAD-2",
              cashierName: "Alice",
              soldAt: "2026-07-27T01:02:03Z",
              totalAmount: 1.001,
              discountAmount: 0,
              actualAmount: 1.001,
              lineCount: 1,
            },
          ],
        },
      },
    ]),
    "S1",
  );
  await assert.rejects(() => invalidMoney.list(query), /money/i);

  const wrongStore = new HbposRemoteHistoryApi(
    new QueueTransport([
      {
        success: true,
        data: {
          orders: [
            {
              orderGuid: "10000000-0000-4000-8000-000000000001",
              storeCode: "S2",
              deviceCode: "IPAD-2",
              cashierName: "Alice",
              soldAt: "2026-07-27T01:02:03Z",
              totalAmount: 1,
              discountAmount: 0,
              actualAmount: 1,
              lineCount: 1,
            },
          ],
        },
      },
    ]),
    "S1",
  );
  await assert.rejects(() => wrongStore.list(query), /store/i);
});
