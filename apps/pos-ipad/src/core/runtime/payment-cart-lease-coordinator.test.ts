import assert from "node:assert/strict";
import test from "node:test";

import {
  ActivePricingCartPaymentLeaseCoordinator,
  type PaymentCartRecoveryMaterial,
} from "./payment-cart-lease-coordinator";

import { PricingCart } from "@/features/sales/domain";
import { ActivePricingCartSession } from "@/features/sales/runtime";

test("支付 lease 跨异步生命周期独占购物车，安全取消后保留原定价车", async () => {
  const cart = cartWithDiscount();
  const active = session(cart);
  const leaseCoordinator = createCoordinator(active, null);
  const expected = active.read();

  const first = await leaseCoordinator.acquireExact({
    checkoutIntentId: "checkout-1",
    expectedRevision: expected.cart.revision,
  });
  const replay = await leaseCoordinator.acquireExact({
    checkoutIntentId: "checkout-1",
    expectedRevision: expected.cart.revision,
  });
  assert.equal(replay, first);
  await assert.rejects(
    () =>
      leaseCoordinator.acquireExact({
        checkoutIntentId: "checkout-2",
        expectedRevision: expected.cart.revision,
      }),
    hasCode("PAYMENT_CART_LEASE_CONFLICT"),
  );
  assert.throws(
    () =>
      active.addItem({
        lineId: "late-line",
        productCode: "P2",
        itemNumber: null,
        lookupCode: "2",
        displayName: "Late",
        unitPrice: { currency: "AUD", cents: 100 },
        syncProvenance: { referenceCode: null, priceSource: 0 },
      }),
    hasCode("ACTIVE_PRICING_CART_BUSY"),
  );

  await leaseCoordinator.releaseAfterSafeCancel(first, "order-1");
  assert.deepEqual(active.read().pricingState, expected.pricingState);
  assert.deepEqual(active.read().cart, expected.cart);
  active.increaseLine("line-1");
  assert.equal(active.read().cart.lines[0]?.quantity, "2");
  await assert.rejects(
    () => leaseCoordinator.readExact(first),
    hasCode("PAYMENT_CART_LEASE_CONFLICT"),
  );
});

test("订单确认后才清空购物车并释放支付 lease", async () => {
  const active = session(cartWithDiscount());
  const leaseCoordinator = createCoordinator(active, null);
  const lease = await leaseCoordinator.acquireExact({
    checkoutIntentId: "checkout-complete",
    expectedRevision: active.read().cart.revision,
  });

  await leaseCoordinator.clearAfterCompleted(lease, "order-complete");
  assert.equal(active.read().cart.lines.length, 0);
  const afterCompletion = active.read();
  assert.equal(
    active.clearAfterCommittedOrder("order-complete"),
    afterCompletion,
    "同一 OrderGuid 的重复完成只命中 session tombstone",
  );
  active.addItem({
    lineId: "next-line",
    productCode: "P-NEXT",
    itemNumber: null,
    lookupCode: "NEXT",
    displayName: "Next",
    unitPrice: { currency: "AUD", cents: 300 },
    syncProvenance: { referenceCode: null, priceSource: 0 },
  });
  assert.equal(active.read().cart.lines.length, 1);
});

test("冷启动先恢复 promotion/asOf/手工折扣状态并立即持有写锁", async () => {
  const recoveredCart = cartWithPromotionAndManualDiscount();
  const recovered = recoveredCart.snapshot();
  const pricingState = recoveredCart.stateSnapshot();
  const active = session();
  const material: PaymentCartRecoveryMaterial = {
    checkoutIntentId: "checkout-recovery",
    cart: recovered,
    pricingState,
  };
  const leaseCoordinator = createCoordinator(active, material);

  const initialized = await leaseCoordinator.initializeRecovery();
  assert.ok(initialized);
  assert.equal(initialized.checkoutIntentId, "checkout-recovery");
  assert.deepEqual(initialized.pricingState, pricingState);
  assert.deepEqual(initialized.cart, recovered);
  assert.throws(
    () => active.increaseLine("line-promo"),
    hasCode("ACTIVE_PRICING_CART_BUSY"),
  );

  const replay = await leaseCoordinator.initializeRecovery();
  assert.equal(replay, initialized);
  await leaseCoordinator.releaseAfterSafeCancel(initialized, "order-recovery");
  assert.deepEqual(active.read().pricingState, pricingState);
  assert.deepEqual(active.read().cart, recovered);
});

test("恢复材料不能覆盖 RecallActive 或已有普通购物车", async () => {
  const source = cartWithDiscount();
  const material: PaymentCartRecoveryMaterial = {
    checkoutIntentId: "checkout-conflict",
    cart: source.snapshot(),
    pricingState: source.stateSnapshot(),
  };
  const nonEmpty = session(cartWithDiscount());
  await assert.rejects(
    () => createCoordinator(nonEmpty, material).initializeRecovery(),
    hasCode("ACTIVE_PRICING_CART_BUSY"),
  );

  const recalled = session();
  recalled.blockForRecallRecovery({
    kind: "recalled",
    scope: { storeCode: "S1", deviceCode: "D1" },
    holdId: "hold-1",
    recallAttemptId: "recall-1",
  });
  await assert.rejects(
    () => createCoordinator(recalled, material).initializeRecovery(),
    hasCode("ACTIVE_PRICING_CART_TERMINAL_RECOVERY_REQUIRED"),
  );
});

function createCoordinator(
  active: ActivePricingCartSession,
  material: PaymentCartRecoveryMaterial | null,
): ActivePricingCartPaymentLeaseCoordinator {
  let lease = 0;
  return new ActivePricingCartPaymentLeaseCoordinator(
    active,
    {
      async findBlockingCart() {
        return material;
      },
    },
    () => `payment-lease-${++lease}`,
  );
}

function session(cart = new PricingCart()): ActivePricingCartSession {
  return new ActivePricingCartSession(cart, () => new PricingCart());
}

function cartWithDiscount(): PricingCart {
  const cart = new PricingCart({
    asOfIso: "2026-07-28T01:00:00.000Z",
  });
  cart.addItem({
    lineId: "line-1",
    productCode: "P1",
    itemNumber: "1001",
    lookupCode: "930000000001",
    displayName: "Tea",
    unitPrice: { currency: "AUD", cents: 1_000 },
    syncProvenance: { referenceCode: null, priceSource: 0 },
  });
  cart.setLineDiscountPercentBps("line-1", 2_000);
  return cart;
}

function cartWithPromotionAndManualDiscount(): PricingCart {
  const asOfIso = "2026-07-28T02:00:00.000Z";
  const cart = new PricingCart({
    asOfIso,
    promotions: [
      {
        id: "promo-1",
        name: "Tea pair",
        effectiveStartIso: "2026-07-28T00:00:00.000Z",
        effectiveEndIso: "2026-07-29T00:00:00.000Z",
        isExclusive: false,
        priority: 1,
        applyQuantity: 2,
        fixedPrice: { currency: "AUD", cents: 1_500 },
        maxApplicationsPerOrder: null,
        products: [{ productCode: "P-PROMO", unitWeight: 1 }],
      },
    ],
  });
  cart.addItem({
    lineId: "line-promo",
    productCode: "P-PROMO",
    itemNumber: "2001",
    lookupCode: "930000000002",
    displayName: "Promo tea",
    quantity: 2,
    unitPrice: { currency: "AUD", cents: 1_000 },
    syncProvenance: { referenceCode: null, priceSource: 0 },
  });
  cart.setOrderDiscountAmount({ currency: "AUD", cents: 100 });
  return cart;
}

function hasCode(code: string): (error: unknown) => boolean {
  return (error) =>
    error instanceof Error &&
    (error as Error & { code?: string }).code === code;
}
