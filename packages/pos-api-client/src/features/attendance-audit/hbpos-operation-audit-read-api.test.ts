import assert from "node:assert/strict";
import test from "node:test";

import { HbposOperationAuditReadApi } from "./hbpos-operation-audit-read-api";

import { HbposApiError, type HbposTransport, type HbposTransportRequest, type HbposTransportResponse } from "../../transport";

class QueueTransport implements HbposTransport {
  public readonly requests: HbposTransportRequest[] = [];

  public constructor(
    private readonly responses: (
      | HbposTransportResponse<unknown>
      | Error
    )[],
  ) {}

  public async request<T>(
    request: HbposTransportRequest,
  ): Promise<HbposTransportResponse<T>> {
    this.requests.push(request);
    const next = this.responses.shift();
    if (!next) throw new Error("Missing fake response.");
    if (next instanceof Error) throw next;
    return next as HbposTransportResponse<T>;
  }
}

const EVENT_ID = "10000000-0000-4000-8000-000000000001";
const ORDER_ID = "20000000-0000-4000-8000-000000000001";

test("list 只下传 keyword/limit，并从 direct body 建立安全白名单投影", async () => {
  const transport = new QueueTransport([
    response(200, {
      items: [
        record({
          authorizationCode: "DEVICE-SECRET",
          paymentProviderReference: "PROVIDER-SECRET",
          propertiesJson: "{\"cardNumber\":\"4111111111111111\"}",
        }),
      ],
      continuationToken: "MUST-NOT-LEAK",
    }),
  ]);
  const api = new HbposOperationAuditReadApi(
    transport,
    " S1 ",
    " IPAD-1 ",
  );

  const rows = await api.list(
    listInput({ keyword: "  cash drawer  " }),
  );

  assert.deepEqual(transport.requests, [
    {
      method: "GET",
      url: "/api/v1/operation-audits",
      params: {
        keyword: "cash drawer",
        limit: 100,
      },
    },
  ]);
  assert.deepEqual(rows, [
    {
      cashierName: "Alice",
      correlationId: "corr-1",
      deviceCode: "IPAD-1",
      eventId: EVENT_ID,
      items: [],
      occurredAtIso: "2026-07-28T01:02:03.0000000Z",
      operationType: "CASH_DRAWER_OPEN",
      orderGuid: ORDER_ID,
      outcome: "Succeeded",
      paymentAmountCents: 1_000,
      primaryProduct: "Tea",
      productCount: 1,
      receiptNumber: "R-1",
      safeMessage: "Completed",
      storeCode: "S1",
      uploadState: "uploaded",
    },
  ]);
  const serialized = JSON.stringify(rows);
  for (const forbidden of [
    "DEVICE-SECRET",
    "PROVIDER-SECRET",
    "4111111111111111",
    "MUST-NOT-LEAK",
  ]) {
    assert.equal(serialized.includes(forbidden), false);
  }
});

test("list 对 scope、UUID、UTC 时间、整数分、uploaded 状态和 items 逐项严格校验", async () => {
  const invalidRecords: readonly [string, Record<string, unknown>][] = [
    ["store scope", { storeCode: "S2" }],
    ["device scope", { deviceCode: "IPAD-2" }],
    ["event UUID", { eventId: "not-a-uuid" }],
    ["order UUID", { orderGuid: "not-a-uuid" }],
    ["UTC time", { occurredAtIso: "2026-07-28T11:02:03+10:00" }],
    ["integer cents", { paymentAmountCents: 1.2 }],
    ["safe integer cents", { paymentAmountCents: Number.MAX_VALUE }],
    ["uploaded state", { uploadState: "pending" }],
    ["item line", { items: [{ ...item(), lineIndex: -1 }] }],
    ["item cents", { items: [{ ...item(), actualAmountDeltaCents: 0.1 }] }],
    ["item quantity", { items: [{ ...item(), quantityDelta: "1e3" }] }],
  ];

  for (const [name, override] of invalidRecords) {
    const api = new HbposOperationAuditReadApi(
      new QueueTransport([
        response(200, { items: [record(override)] }),
      ]),
      "S1",
      "IPAD-1",
    );
    await assert.rejects(
      api.list(listInput()),
      TypeError,
      `应拒绝 ${name}`,
    );
  }
});

test("list 拒绝 envelope、缺失数组和重复 EventId；非 uploaded 过滤不扩大服务端 query", async () => {
  const envelopeApi = new HbposOperationAuditReadApi(
    new QueueTransport([
      response(200, { success: true, data: { items: [record()] } }),
    ]),
    "S1",
    "IPAD-1",
  );
  await assert.rejects(envelopeApi.list(listInput()));

  const duplicateApi = new HbposOperationAuditReadApi(
    new QueueTransport([
      response(200, { items: [record(), record()] }),
    ]),
    "S1",
    "IPAD-1",
  );
  await assert.rejects(duplicateApi.list(listInput()));

  const transport = new QueueTransport([
    response(200, { items: [record()] }),
  ]);
  const filtered = await new HbposOperationAuditReadApi(
    transport,
    "S1",
    "IPAD-1",
  ).list(listInput({ uploadState: "pending" }));
  assert.deepEqual(filtered, []);
  assert.deepEqual(transport.requests[0]?.params, {
    keyword: undefined,
    limit: 100,
  });
});

test("detail 仅接受同一 EventId/scope，并把 item 映射为固定白名单", async () => {
  const transport = new QueueTransport([
    response(
      200,
      record({
        items: [
          {
            ...item(),
            authCode: "AUTH-SECRET",
            rawPayload: "RAW-SECRET",
          },
        ],
      }),
    ),
  ]);
  const api = new HbposOperationAuditReadApi(
    transport,
    "S1",
    "IPAD-1",
  );

  const detail = await api.get(detailInput());

  assert.deepEqual(transport.requests, [
    {
      acceptedStatuses: [404],
      method: "GET",
      url: `/api/v1/operation-audits/${EVENT_ID}`,
    },
  ]);
  assert.deepEqual(detail?.items, [
    {
      actualAmountDeltaCents: -125,
      displayName: "Tea",
      lineIndex: 0,
      productCode: "P1",
      quantityDelta: "-1.25",
    },
  ]);
  assert.equal(JSON.stringify(detail).includes("AUTH-SECRET"), false);
  assert.equal(JSON.stringify(detail).includes("RAW-SECRET"), false);

  const mismatchApi = new HbposOperationAuditReadApi(
    new QueueTransport([
      response(
        200,
        record({
          eventId: "30000000-0000-4000-8000-000000000001",
        }),
      ),
    ]),
    "S1",
    "IPAD-1",
  );
  await assert.rejects(mismatchApi.get(detailInput()));
});

test("detail 的 404 映射 null，401/403 原样交回既有传输全局行为", async () => {
  const fromResponse = await new HbposOperationAuditReadApi(
    new QueueTransport([response(404, { code: "AUDIT_NOT_FOUND" })]),
    "S1",
    "IPAD-1",
  ).get(detailInput());
  assert.equal(fromResponse, null);

  const notFound = new HbposApiError("not found", {
    kind: "http",
    status: 404,
    code: "AUDIT_NOT_FOUND",
  });
  const fromError = await new HbposOperationAuditReadApi(
    new QueueTransport([notFound]),
    "S1",
    "IPAD-1",
  ).get(detailInput());
  assert.equal(fromError, null);

  for (const status of [401, 403]) {
    const authError = new HbposApiError("auth", {
      kind: "http",
      status,
    });
    const api = new HbposOperationAuditReadApi(
      new QueueTransport([authError]),
      "S1",
      "IPAD-1",
    );
    await assert.rejects(
      api.get(detailInput()),
      (error: unknown) => error === authError,
    );
  }
});

test("adapter 固定 remote 与可信 scope，拒绝调用方扩大范围或非法 query", async () => {
  const api = new HbposOperationAuditReadApi(
    new QueueTransport([]),
    "S1",
    "IPAD-1",
  );

  await assert.rejects(
    api.list(listInput({ source: "local" })),
  );
  await assert.rejects(
    api.list(listInput({ storeCode: "S2" })),
  );
  await assert.rejects(
    api.list(listInput({ deviceCode: "IPAD-2" })),
  );
  await assert.rejects(
    api.list(listInput({ keyword: "x".repeat(121) })),
  );
  await assert.rejects(
    api.get(detailInput({ eventId: "not-a-uuid" })),
  );
});

function response(
  status: number,
  data: unknown,
): HbposTransportResponse<unknown> {
  return { status, data };
}

function listInput(
  override: Record<string, unknown> = {},
): Parameters<HbposOperationAuditReadApi["list"]>[0] {
  return {
    deviceCode: "IPAD-1",
    keyword: null,
    limit: 100,
    source: "remote",
    storeCode: "S1",
    uploadState: null,
    ...override,
  } as Parameters<HbposOperationAuditReadApi["list"]>[0];
}

function detailInput(
  override: Record<string, unknown> = {},
): Parameters<HbposOperationAuditReadApi["get"]>[0] {
  return {
    deviceCode: "IPAD-1",
    eventId: EVENT_ID,
    source: "remote",
    storeCode: "S1",
    ...override,
  } as Parameters<HbposOperationAuditReadApi["get"]>[0];
}

function record(
  override: Record<string, unknown> = {},
): Record<string, unknown> {
  return {
    cashierName: "Alice",
    correlationId: "corr-1",
    deviceCode: "IPAD-1",
    eventId: EVENT_ID,
    items: [],
    occurredAtIso: "2026-07-28T01:02:03.0000000Z",
    operationType: "CASH_DRAWER_OPEN",
    orderGuid: ORDER_ID,
    outcome: "Succeeded",
    paymentAmountCents: 1_000,
    primaryProduct: "Tea",
    productCount: 1,
    receiptNumber: "R-1",
    safeMessage: "Completed",
    storeCode: "S1",
    uploadState: "uploaded",
    ...override,
  };
}

function item(): Record<string, unknown> {
  return {
    actualAmountDeltaCents: -125,
    displayName: "Tea",
    lineIndex: 0,
    productCode: "P1",
    quantityDelta: "-1.25",
  };
}
