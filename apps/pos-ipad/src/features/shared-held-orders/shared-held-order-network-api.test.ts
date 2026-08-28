import assert from "node:assert/strict";
import test from "node:test";

import {
  SharedHeldOrderApiError,
  SharedHeldOrderNetworkApi,
  type SharedHeldOrderNetworkApiPort,
} from "./shared-held-order-network-api";
import { normalizeSharedSaleCartV1, type SharedSaleCartV1 } from "@hb/pos-domain/features/shared-held-orders/shared-sale-cart-v1";

import {
  HbposApiError,
  type HbposEnvelope,
  type HbposTransport,
  type HbposTransportRequest,
  type HbposTransportResponse,
} from "@/core/api/hbpos-api";

class FakeTransport implements HbposTransport {
  public calls: readonly HbposTransportRequest[] = [];
  public queue: (() => Promise<HbposTransportResponse<unknown>>)[] = [];
  public failWith: unknown = null;

  public async request<T>(
    request: HbposTransportRequest,
  ): Promise<HbposTransportResponse<T>> {
    this.calls = [...this.calls, request];
    if (this.failWith !== null) {
      throw this.failWith;
    }
    const next = this.queue.shift();
    if (!next) {
      throw new Error("No fake transport response queued.");
    }
    return next() as Promise<HbposTransportResponse<T>>;
  }

  public enqueue<T>(response: HbposTransportResponse<T>): void {
    this.queue.push(() => Promise.resolve(response));
  }
}

function envelope<T>(data: T): HbposEnvelope<T> {
  return { success: true, data };
}

function wireCart(): SharedSaleCartV1 {
  return normalizeSharedSaleCartV1({
    version: 1,
    pricingState: {
      revision: 3,
      mode: "sale",
      asOfIso: "2026-07-28T00:00:00.000Z",
      promotions: [],
      lines: [
        {
          lineId: "line-1",
          productCode: "P-1",
          itemNumber: null,
          lookupCode: "100",
          displayName: "Item",
          quantity: 1,
          unitPriceCents: 100,
          basePriceSource: "catalog",
          syncProvenance: null,
          kind: "sale",
          returnSourceKey: null,
          originalOrderGuid: null,
          originalOrderDetailGuid: null,
          discountState: { mode: "none" },
        },
      ],
    },
  });
}

function makeApi(transport: FakeTransport): SharedHeldOrderNetworkApiPort {
  return new SharedHeldOrderNetworkApi(transport);
}

test("capabilities/list/prepare/claims-mine 严格解析为本地域类型", async () => {
  const transport = new FakeTransport();
  transport.enqueue({
    status: 200,
    data: envelope({
      enabled: true,
      payloadVersion: 1,
      preparedTtlSeconds: 900,
      forceReleaseSupported: true,
    }),
  });
  transport.enqueue({
    status: 200,
    data: envelope([
      {
        holdGuid: "hold-1",
        storeCode: "S1",
        deviceCode: "IPAD-1",
        heldByCashierId: "cashier-1",
        heldByCashierName: "Cashier",
        heldAtUtc: "2026-07-28T00:00:00.000Z",
        updatedAtUtc: "2026-07-28T00:05:00.000Z",
        lineCount: 2,
        totalCents: 200,
        discountCents: 10,
        actualCents: 190,
        revision: 4,
      },
    ]),
  });
  transport.enqueue({
    status: 200,
    data: envelope({
      holdGuid: "hold-1",
      claimGuid: "claim-1",
      status: 1,
      payload: wireCart(),
      claimantDeviceCode: "IPAD-2",
      claimantCashierId: "cashier-2",
      claimantCashierName: "Other",
      createdAtUtc: "2026-07-28T00:06:00.000Z",
      expiresAtUtc: "2026-07-28T00:21:00.000Z",
      revision: 5,
      alreadyExists: false,
    }),
  });
  transport.enqueue({
    status: 200,
    data: envelope([
      {
        holdGuid: "hold-1",
        claimGuid: "claim-1",
        status: 2,
        storeCode: "S1",
        claimantDeviceCode: "IPAD-2",
        claimantCashierId: "cashier-2",
        claimantCashierName: "Other",
        payload: wireCart(),
        createdAtUtc: "2026-07-28T00:06:00.000Z",
        updatedAtUtc: "2026-07-28T00:07:00.000Z",
        expiresAtUtc: null,
        activatedAtUtc: "2026-07-28T00:07:00.000Z",
        revision: 5,
      },
    ]),
  });

  const api = makeApi(transport);
  const capabilities = await api.getCapabilities();
  assert.deepEqual(capabilities, {
    enabled: true,
    payloadVersion: 1,
    supportedPayloadVersions: [1],
    preferredPayloadVersion: 1,
    preparedTtlSeconds: 900,
    forceReleaseSupported: true,
  });

  const pending = await api.listPending();
  assert.equal(pending.length, 1);
  assert.equal(pending[0]?.holdGuid, "hold-1");
  assert.equal(pending[0]?.actualCents, 190);

  const prepared = await api.prepare({
    holdGuid: "hold-1",
    claimGuid: "claim-1",
    idempotencyKey: "prepare-key",
  });
  assert.equal(prepared.status, "Prepared");
  assert.equal(prepared.payload.pricingState.revision, 3);
  assert.equal(prepared.expiresAtIso, "2026-07-28T00:21:00.000Z");

  const mine = await api.claimsMine();
  assert.equal(mine.length, 1);
  assert.equal(mine[0]?.status, "Active");
  assert.equal(mine[0]?.activatedAtIso, "2026-07-28T00:07:00.000Z");

  const urls = transport.calls.map((call) => call.url);
  assert.deepEqual(urls, [
    "/api/v1/held-orders/capabilities",
    "/api/v1/held-orders?supportedPayloadVersions=1&supportedPayloadVersions=2",
    "/api/v1/held-orders/hold-1/claims/prepare?supportedPayloadVersions=1&supportedPayloadVersions=2",
    "/api/v1/held-orders/claims/mine?supportedPayloadVersions=1&supportedPayloadVersions=2",
  ]);
});

test("旧 capabilities 默认只支持 V1，但所有读取/prepare 请求声明客户端 V1/V2 能力", async () => {
  const transport = new FakeTransport();
  transport.enqueue({
    status: 200,
    data: envelope({
      enabled: true,
      payloadVersion: 1,
      preparedTtlSeconds: 900,
      forceReleaseSupported: true,
    }),
  });
  transport.enqueue({ status: 200, data: envelope([]) });
  transport.enqueue({
    status: 200,
    data: envelope({
      holdGuid: "hold-1",
      claimGuid: "claim-1",
      status: 1,
      payload: wireCart(),
      claimantDeviceCode: "IPAD-2",
      claimantCashierId: "cashier-2",
      claimantCashierName: "Other",
      createdAtUtc: "2026-07-28T00:06:00.000Z",
      expiresAtUtc: null,
      revision: 5,
      alreadyExists: false,
    }),
  });
  transport.enqueue({ status: 200, data: envelope([]) });

  const api = makeApi(transport);
  assert.deepEqual(await api.getCapabilities(), {
    enabled: true,
    payloadVersion: 1,
    supportedPayloadVersions: [1],
    preferredPayloadVersion: 1,
    preparedTtlSeconds: 900,
    forceReleaseSupported: true,
  });
  await api.listPending();
  await api.prepare({
    holdGuid: "hold-1",
    claimGuid: "claim-1",
    idempotencyKey: "prepare-key",
  });
  await api.claimsMine();

  assert.deepEqual(
    transport.calls.slice(1).map((call) =>
      [...new URL(call.url, "https://hbpos.example").searchParams.values()],
    ),
    [
      ["1", "2"],
      ["1", "2"],
      ["1", "2"],
    ],
  );
});

test("错误分类：网络/5xx/BUSY 可重试，409/MISMATCH 冲突，403 禁止，disabled/invalid 稳定", async () => {
  const cases: readonly {
    error: unknown;
    expected: string;
  }[] = [
    {
      error: new HbposApiError("network", {
        kind: "transport",
        code: "NO_HTTP_RESPONSE",
        networkCode: "ERR_NETWORK",
      }),
      expected: "Retryable",
    },
    {
      error: new HbposApiError("busy", { kind: "http", status: 503 }),
      expected: "Retryable",
    },
    {
      error: new HbposApiError("busy", {
        kind: "http",
        status: 409,
        code: "SHARED_HELD_ORDER_BUSY",
      }),
      expected: "Retryable",
    },
    {
      error: new HbposApiError("conflict", {
        kind: "http",
        status: 409,
        code: "SHARED_HELD_ORDER_MISMATCH",
      }),
      expected: "Conflict",
    },
    {
      error: new HbposApiError("forbidden", {
        kind: "http",
        status: 403,
        code: "DEVICE_SCOPE_FORBIDDEN",
      }),
      expected: "Forbidden",
    },
    {
      error: new HbposApiError("disabled", {
        kind: "http",
        status: 400,
        code: "SHARED_HELD_ORDER_DISABLED",
      }),
      expected: "Disabled",
    },
    {
      error: new HbposApiError("not found", {
        kind: "http",
        status: 404,
        code: "SHARED_HELD_ORDER_NOT_FOUND",
      }),
      expected: "Invalid",
    },
    {
      error: new HbposApiError("envelope", {
        kind: "envelope",
        code: "SHARED_HELD_ORDER_DISABLED",
      }),
      expected: "Disabled",
    },
  ];

  for (const { error, expected } of cases) {
    const transport = new FakeTransport();
    transport.failWith = error;
    const api = makeApi(transport);
    await assert.rejects(
      api.getCapabilities(),
      (caught: unknown) => {
        assert.ok(caught instanceof SharedHeldOrderApiError);
        assert.equal(caught.kind, expected);
        return true;
      },
    );
  }
});

test("成功 envelope 缺 data 或 payload 损坏时按 Invalid 拒绝，不泄露 payload", async () => {
  const missingData = new FakeTransport();
  missingData.enqueue({ status: 200, data: { success: true, data: undefined } });
  const api = makeApi(missingData);
  await assert.rejects(
    api.getCapabilities(),
    (caught: unknown) =>
      caught instanceof SharedHeldOrderApiError &&
      caught.kind === "Invalid",
  );

  const badPayload = new FakeTransport();
  badPayload.enqueue({
    status: 200,
    data: envelope({
      holdGuid: "hold-1",
      claimGuid: "claim-1",
      status: 1,
      payload: { version: 2, pricingState: null },
      claimantDeviceCode: "IPAD-2",
      claimantCashierId: "cashier-2",
      claimantCashierName: "Other",
      createdAtUtc: "2026-07-28T00:06:00.000Z",
      expiresAtUtc: null,
      revision: 1,
      alreadyExists: false,
    }),
  });
  const api2 = makeApi(badPayload);
  await assert.rejects(
    api2.prepare({
      holdGuid: "hold-1",
      claimGuid: "claim-1",
      idempotencyKey: "key",
    }),
    (caught: unknown) => {
      assert.ok(caught instanceof Error);
      assert.ok(!JSON.stringify(caught.message).includes("pricingState"));
      return true;
    },
  );
});

test("publish 上传 holdGuid 幂等键并解析 revision", async () => {
  const transport = new FakeTransport();
  transport.enqueue({
    status: 200,
    data: envelope({
      holdGuid: "hold-1",
      status: 1,
      revision: 12,
      createdAtUtc: "2026-07-28T00:00:00.000Z",
      alreadyExists: false,
    }),
  });
  const api = makeApi(transport);
  const result = await api.publish({
    holdGuid: "hold-1",
    storeCode: "S1",
    deviceCode: "IPAD-1",
    cart: wireCart(),
    idempotencyKey: "hold-1",
  });
  assert.equal(result.status, "Pending");
  assert.equal(result.revision, 12);
  const call = transport.calls[0];
  assert.equal(call?.method, "POST");
  assert.equal((call?.data as { idempotencyKey?: unknown })?.idempotencyKey, "hold-1");
});

test("cancel 以原设备身份取消挂单并严格解析 Cancelled 终态", async () => {
  const transport = new FakeTransport();
  transport.enqueue({
    status: 200,
    data: envelope({
      holdGuid: "hold-1",
      status: 4,
      revision: 13,
      updatedAtUtc: "2026-07-28T00:03:00.000Z",
      alreadyCancelled: false,
    }),
  });
  const api = makeApi(transport);

  const result = await api.cancel("hold-1");

  assert.deepEqual(result, {
    holdGuid: "hold-1",
    status: "Cancelled",
    revision: 13,
    updatedAtIso: "2026-07-28T00:03:00.000Z",
    alreadyCancelled: false,
  });
  assert.equal(transport.calls[0]?.method, "POST");
  assert.equal(transport.calls[0]?.url, "/api/v1/held-orders/hold-1/cancel");
  assert.equal(transport.calls[0]?.data, undefined);
});

test("cancel 响应 holdGuid 与请求不一致时 fail-closed", async () => {
  const transport = new FakeTransport();
  transport.enqueue({
    status: 200,
    data: envelope({
      holdGuid: "hold-other",
      status: 4,
      revision: 13,
      updatedAtUtc: "2026-07-28T00:03:00.000Z",
      alreadyCancelled: false,
    }),
  });
  const api = makeApi(transport);

  await assert.rejects(
    api.cancel("hold-1"),
    /Cancelled held order response holdGuid is invalid/,
  );
});
