import assert from "node:assert/strict";
import test from "node:test";

import { CustomerDisplayCoordinator } from "./customer-display-coordinator";
import type {
  CustomerDisplayFrame,
  CustomerDisplayPublishResult,
} from "./customer-display-publisher";

import type { CartSnapshot } from "@/core/contracts";

test("初始化和购物车变更只发布 idle/cart，且订阅与销毁均幂等", async () => {
  const cart = new MutableCart(emptyCart());
  const publisher = new RecordingPublisher();
  const coordinator = new CustomerDisplayCoordinator(cart, publisher);

  await coordinator.initialize();
  await coordinator.initialize();
  cart.set(populatedCart());
  await publisher.waitForCount(2);

  assert.deepEqual(
    publisher.frames.map((frame) => frame.mode),
    ["idle", "cart"],
  );
  assert.equal(cart.subscriptions, 1);

  coordinator.destroy();
  coordinator.destroy();
  cart.set(emptyCart());
  await Promise.resolve();
  assert.equal(publisher.frames.length, 2);
});

test("付款、找零和成功状态在订单清车后仍使用最后一个非空购物车", async () => {
  const cart = new MutableCart(populatedCart());
  const publisher = new RecordingPublisher();
  const coordinator = new CustomerDisplayCoordinator(cart, publisher);
  await coordinator.initialize();

  await coordinator.showPayment();
  cart.set(emptyCart());
  await coordinator.showChange(250);
  await coordinator.showSuccess(250);

  assert.deepEqual(
    publisher.frames.map((frame) => [
      frame.mode,
      frame.cart?.actualAmount.cents ?? null,
      frame.changeCents,
    ]),
    [
      ["cart", 1_234, 0],
      ["payment", 1_234, 0],
      ["change", 1_234, 250],
      ["success", 1_234, 250],
    ],
  );
});

test("显式付款状态不会被购物车订阅覆盖，返回购物车后才继续自动发布", async () => {
  const cart = new MutableCart(populatedCart());
  const publisher = new RecordingPublisher();
  const coordinator = new CustomerDisplayCoordinator(cart, publisher);
  await coordinator.initialize();
  await coordinator.showPayment();

  cart.set(populatedCart(2_000));
  await Promise.resolve();
  assert.deepEqual(
    publisher.frames.map((frame) => frame.mode),
    ["cart", "payment"],
  );

  await coordinator.showCart();
  assert.deepEqual(
    publisher.frames.map((frame) => [frame.mode, frame.cart?.actualAmount.cents]),
    [
      ["cart", 1_234],
      ["payment", 1_234],
      ["cart", 2_000],
    ],
  );
});

test("会话失效时立即发布不含上一笔交易和广告的 idle 快照", async () => {
  const cart = new MutableCart(populatedCart());
  const publisher = new RecordingPublisher();
  const coordinator = new CustomerDisplayCoordinator(cart, publisher);
  await coordinator.initialize();
  await coordinator.setAdvert({
    kind: "image",
    localUri: "file:///cache/customer-display/ad.jpg",
  });
  await coordinator.showSuccess(250);

  await coordinator.clearSensitiveContent();

  const cleared = publisher.frames.at(-1);
  assert.equal(cleared?.mode, "idle");
  assert.equal(cleared?.cart, null);
  assert.equal(cleared?.changeCents, 0);
  assert.equal(cleared?.advert, null);
});

class MutableCart {
  private readonly listeners = new Set<() => void>();
  public subscriptions = 0;

  public constructor(private current: CartSnapshot) {}

  public getSnapshot(): CartSnapshot {
    return this.current;
  }

  public subscribe(listener: () => void): () => void {
    this.subscriptions += 1;
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  public set(snapshot: CartSnapshot): void {
    this.current = snapshot;
    for (const listener of this.listeners) listener();
  }
}

class RecordingPublisher {
  public readonly frames: CustomerDisplayFrame[] = [];
  private readonly waiters: Readonly<{
    count: number;
    resolve(): void;
  }>[] = [];

  public async publish(
    frame: CustomerDisplayFrame,
  ): Promise<CustomerDisplayPublishResult> {
    this.frames.push(frame);
    this.resolveWaiters();
    return {
      status: "published",
      revision: this.frames.length,
    };
  }

  public waitForCount(count: number): Promise<void> {
    if (this.frames.length >= count) return Promise.resolve();
    return new Promise((resolve) => {
      this.waiters.push({ count, resolve });
    });
  }

  private resolveWaiters(): void {
    for (let index = this.waiters.length - 1; index >= 0; index -= 1) {
      const waiter = this.waiters[index];
      if (waiter && this.frames.length >= waiter.count) {
        this.waiters.splice(index, 1);
        waiter.resolve();
      }
    }
  }
}

function emptyCart(): CartSnapshot {
  return {
    revision: 1,
    mode: "sale",
    lines: [],
    subtotal: { currency: "AUD", cents: 0 },
    discount: { currency: "AUD", cents: 0 },
    actualAmount: { currency: "AUD", cents: 0 },
  };
}

function populatedCart(cents = 1_234): CartSnapshot {
  return {
    revision: cents,
    mode: "sale",
    lines: [
      {
        lineId: "line-1",
        productCode: "P-1",
        itemNumber: "I-1",
        lookupCode: "930000000001",
        displayName: "Tea",
        quantity: "2",
        unitPrice: { currency: "AUD", cents: 667 },
        discount: { currency: "AUD", cents: 100 },
        actualAmount: { currency: "AUD", cents },
        priceSource: "catalog",
        syncProvenance: { referenceCode: null, priceSource: 0 },
        kind: "sale",
        returnSourceKey: null,
        originalOrderGuid: null,
        originalOrderDetailGuid: null,
      },
    ],
    subtotal: { currency: "AUD", cents: cents + 100 },
    discount: { currency: "AUD", cents: 100 },
    actualAmount: { currency: "AUD", cents },
  };
}
