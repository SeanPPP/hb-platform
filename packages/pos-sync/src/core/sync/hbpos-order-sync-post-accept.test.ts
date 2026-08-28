import assert from "node:assert/strict";
import test from "node:test";

import { HbposApiError, type HbposTransport } from "@hb/pos-api-client/transport";
import type { LocalOrder } from "@hb/pos-domain/core/contracts/order";
import type { OrderRepositoryPort } from "@hb/pos-domain/core/contracts/repositories";

import {
  HbposOrderSyncAdapter,
  type OrderSyncPostAcceptPort,
} from "./hbpos-sync-adapters";

const orderGuid = "018f1b9b-47c5-7c1b-9f8e-39c5cb3b9d01";
const lineGuid = "018f1b9b-47c5-7c1b-9f8e-39c5cb3b9d02";
const tenderGuid = "018f1b9b-47c5-7c1b-9f8e-39c5cb3b9d03";

test("服务端订单先同步成功，再执行礼券最新余额确认", async () => {
  const events: string[] = [];
  const adapter = createAdapter(
    false,
    {
      async afterOrderAccepted(guid) {
        events.push(`balance:${guid}`);
      },
    },
    events,
  );

  const result = await adapter.sync(
    orderGuid,
    JSON.stringify({ orderGuid }),
  );

  assert.deepEqual(result, { kind: "synced", alreadySynced: false });
  assert.deepEqual(events, [
    `sync:${orderGuid}`,
    `balance:${orderGuid}`,
  ]);
});

test("余额确认网络失败时保留 outbox 重试，AlreadySynced 重放仍会再次确认", async () => {
  let attempts = 0;
  const postAccept: OrderSyncPostAcceptPort = {
    async afterOrderAccepted() {
      attempts += 1;
      if (attempts === 1) throw new Error("offline");
    },
  };

  const first = await createAdapter(false, postAccept).sync(
    orderGuid,
    JSON.stringify({ orderGuid }),
  );
  const replay = await createAdapter(true, postAccept).sync(
    orderGuid,
    JSON.stringify({ orderGuid }),
  );

  assert.deepEqual(first, { kind: "retry", failure: "network" });
  assert.deepEqual(replay, { kind: "synced", alreadySynced: true });
  assert.equal(attempts, 2);
});

test("订单已被服务端接受后，余额查询 400 只保留重试且绝不改写为 Rejected", async () => {
  const result = await createAdapter(false, {
    async afterOrderAccepted() {
      throw new HbposApiError("post-accept query rejected", {
        kind: "http",
        status: 400,
      });
    },
  }).sync(orderGuid, JSON.stringify({ orderGuid }));

  assert.deepEqual(result, {
    kind: "retry",
    failure: "server",
    code: "HTTP_400",
  });
});

function createAdapter(
  alreadySynced: boolean,
  postAccept: OrderSyncPostAcceptPort,
  events: string[] = [],
): HbposOrderSyncAdapter {
  const transport: HbposTransport = {
    async request<T>() {
      events.push(`sync:${orderGuid}`);
      return {
        status: 200,
        data: {
          success: true,
          data: {
            orderGuid,
            accepted: true,
            alreadySynced,
          },
        },
      } as T extends never ? never : {
        status: number;
        data: T;
      };
    },
  };
  return new HbposOrderSyncAdapter(
    transport,
    new Orders(localOrder()),
    null,
    postAccept,
  );
}

class Orders implements OrderRepositoryPort {
  public constructor(private readonly order: LocalOrder) {}
  public nextLocalSequence(): Promise<number> {
    return Promise.resolve(1);
  }
  public saveDraft(): Promise<void> {
    return Promise.resolve();
  }
  public getByGuid(guid: string): Promise<LocalOrder | null> {
    return Promise.resolve(guid === orderGuid ? this.order : null);
  }
  public listLocal(): Promise<readonly LocalOrder[]> {
    return Promise.resolve([]);
  }
  public transition(): Promise<boolean> {
    return Promise.resolve(true);
  }
}

function localOrder(): LocalOrder {
  return {
    orderGuid,
    localSequence: 1,
    storeCode: "S001",
    deviceCode: "IPAD-1",
    cashierId: "cashier-1",
    cashierName: "Cashier",
    soldAtIso: "2026-07-31T00:00:00.000Z",
    state: "PendingSync",
    total: { currency: "AUD", cents: 700 },
    discount: { currency: "AUD", cents: 0 },
    actualAmount: { currency: "AUD", cents: 700 },
    originalOrderGuid: null,
    lines: [
      {
        lineId: lineGuid,
        productCode: "P1",
        itemNumber: null,
        lookupCode: "P1",
        displayName: "Item",
        quantity: "1",
        unitPrice: { currency: "AUD", cents: 700 },
        discount: { currency: "AUD", cents: 0 },
        actualAmount: { currency: "AUD", cents: 700 },
        priceSource: "catalog",
        syncProvenance: {
          referenceCode: null,
          priceSource: 1,
        },
        kind: "sale",
        returnSourceKey: null,
        originalOrderGuid: null,
        originalOrderDetailGuid: null,
      },
    ],
    tenders: [
      {
        tenderGuid,
        method: "cash",
        amount: { currency: "AUD", cents: 700 },
        reference: null,
        reservationToken: null,
      },
    ],
  };
}
