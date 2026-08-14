import assert from "node:assert/strict";
import test from "node:test";

import {
  UPDATE_TRANSITION_IN_PROGRESS,
  UpdateTransitionLeaseCoordinator,
} from "./update-transition-lease-coordinator";

import { PricingCart } from "@/features/sales/domain";
import {
  ACTIVE_PRICING_CART_UPDATE_TRANSITION,
  ActivePricingCartSession,
} from "@/features/sales/runtime/active-pricing-cart-session";

test("transition 同步封闭新 operation，并等待已在途 operation 完成后进入临界区", async () => {
  const coordinator = new UpdateTransitionLeaseCoordinator();
  const trace: string[] = [];
  const operationRelease = deferred<void>();
  const transitionRelease = deferred<void>();
  coordinator.bindTransitionBarrier(async (operation) => {
    trace.push("barrier:entered");
    try {
      return await operation();
    } finally {
      trace.push("barrier:released");
    }
  });

  const existing = coordinator.runOperation(async () => {
    trace.push("operation:started");
    await operationRelease.promise;
    trace.push("operation:finished");
  });
  const transition = coordinator.runTransition(async () => {
    trace.push("transition:started");
    await transitionRelease.promise;
    trace.push("transition:finished");
    return "done";
  });

  assert.equal(coordinator.isTransitionActive(), true);
  await assert.rejects(
    coordinator.runOperation(async () => {
      trace.push("operation:must-not-run");
    }),
    (error: unknown) =>
      error instanceof Error &&
      (error as Error & { code?: string }).code ===
        UPDATE_TRANSITION_IN_PROGRESS,
  );
  await Promise.resolve();
  assert.deepEqual(trace, [
    "operation:started",
  ]);

  operationRelease.resolve();
  await existing;
  await Promise.resolve();
  assert.deepEqual(trace, [
    "operation:started",
    "operation:finished",
    "barrier:entered",
    "transition:started",
  ]);

  transitionRelease.resolve();
  assert.equal(await transition, "done");
  assert.equal(coordinator.isTransitionActive(), false);
  assert.deepEqual(trace, [
    "operation:started",
    "operation:finished",
    "barrier:entered",
    "transition:started",
    "transition:finished",
    "barrier:released",
  ]);
});

test("transition 异常或 unavailable 结果都在 finally 释放，随后 operation 可继续", async () => {
  const coordinator = new UpdateTransitionLeaseCoordinator();
  coordinator.bindTransitionBarrier((operation) => operation());

  await assert.rejects(
    coordinator.runTransition(async () => {
      throw new Error("handoff failed");
    }),
    /handoff failed/u,
  );
  assert.equal(coordinator.isTransitionActive(), false);
  assert.equal(
    await coordinator.runOperation(async () => "after-failure"),
    "after-failure",
  );

  assert.equal(
    await coordinator.runTransition(async () => "unavailable"),
    "unavailable",
  );
  assert.equal(coordinator.isTransitionActive(), false);
  assert.equal(
    await coordinator.runOperation(async () => "after-unavailable"),
    "after-unavailable",
  );
});

test("固定锁序先等已登记 cart operation 再取 transition exclusive，不死锁且等待期间拒绝新购物车写入", async () => {
  const coordinator = new UpdateTransitionLeaseCoordinator();
  const cart = new ActivePricingCartSession(
    new PricingCart(),
    () => new PricingCart(),
    {
      canStartMutation: () => !coordinator.isTransitionActive(),
    },
  );
  coordinator.bindTransitionBarrier((operation) =>
    cart.runUpdateTransitionExclusive(() => operation()),
  );
  const ordinaryRelease = deferred<void>();
  const transitionRelease = deferred<void>();
  const ordinary = coordinator.runOperation(() =>
    cart.runExclusive(async () => ordinaryRelease.promise),
  );
  const transition = coordinator.runTransition(async () => {
    assert.equal(cart.hasPendingExclusiveOperation(), true);
    await transitionRelease.promise;
  });

  assert.throws(
    () =>
      cart.addItem({
        lineId: "must-not-add",
        productCode: "P1",
        itemNumber: null,
        lookupCode: "1",
        displayName: "Blocked",
        unitPrice: { currency: "AUD", cents: 100 },
        syncProvenance: { referenceCode: null, priceSource: 0 },
      }),
    (error: unknown) =>
      error instanceof Error &&
      (error as Error & { code?: string }).code ===
        ACTIVE_PRICING_CART_UPDATE_TRANSITION,
  );
  ordinaryRelease.resolve();
  await ordinary;
  await Promise.resolve();
  assert.equal(cart.hasPendingExclusiveOperation(), true);

  transitionRelease.resolve();
  await transition;
  assert.equal(cart.hasPendingExclusiveOperation(), false);
  assert.doesNotThrow(() =>
    cart.addItem({
      lineId: "after-transition",
      productCode: "P1",
      itemNumber: null,
      lookupCode: "1",
      displayName: "Allowed",
      unitPrice: { currency: "AUD", cents: 100 },
      syncProvenance: { referenceCode: null, priceSource: 0 },
    }),
  );
});

function deferred<T>(): Readonly<{
  promise: Promise<T>;
  resolve(value: T | PromiseLike<T>): void;
}> {
  let resolve!: (value: T | PromiseLike<T>) => void;
  const promise = new Promise<T>((accept) => {
    resolve = accept;
  });
  return { promise, resolve };
}
