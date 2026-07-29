import assert from "node:assert/strict";
import test from "node:test";

import { ActivePricingCartSession } from "./active-pricing-cart-session";
import {
  ActivePromotionSnapshotLoader,
  type ActivePromotionSnapshotPort,
  type StoredPromotionSnapshot,
} from "./promotion-snapshot-loader";

import { PricingCart } from "@/features/sales/domain";

const asOfIso = "2026-06-13T12:00:00.000Z";

test("活动快照以严格分币转换并原子重算现有购物车", async () => {
  const activeCart = session(cartWithTwoTea());
  const loader = new ActivePromotionSnapshotLoader(
    activeCart,
    new SnapshotPort(snapshot()),
  );

  const result = await loader.load({ storeCode: "S1", asOfIso });

  assert.deepEqual(result, {
    status: "loaded",
    snapshotId: "catalog-1",
    ruleCount: 1,
  });
  assert.equal(activeCart.getSnapshot().lines[0]?.discount.cents, 500);
  assert.equal(activeCart.getSnapshot().lines[0]?.actualAmount.cents, 1_500);
  assert.equal(activeCart.read().pricingState.promotions[0]?.fixedPrice.cents, 1_500);
});

test("没有活动快照或读取失败时保留当前促销与金额", async () => {
  const initial = new PricingCart({
    asOfIso,
    promotions: [parsedPromotion()],
  });
  initial.addItem(addTea("line-1"));
  initial.setLineQuantity("line-1", 2);
  const activeCart = session(initial);
  const before = activeCart.read();

  const missing = await new ActivePromotionSnapshotLoader(
    activeCart,
    new SnapshotPort(null),
  ).load({ storeCode: "S1", asOfIso });
  assert.deepEqual(missing, { status: "no-active-snapshot" });
  assert.equal(activeCart.read(), before);

  const malformed = await new ActivePromotionSnapshotLoader(
    activeCart,
    new SnapshotPort(snapshot({ fixedPrice: 15.001 })),
  ).load({ storeCode: "S1", asOfIso });
  assert.deepEqual(malformed, { status: "fallback" });
  assert.equal(activeCart.read(), before);
  assert.equal(activeCart.getSnapshot().lines[0]?.discount.cents, 500);
});

test("门店不匹配及仓储异常均 fail-closed，绝不清空已装载规则", async () => {
  const initial = new PricingCart({
    asOfIso,
    promotions: [parsedPromotion()],
  });
  initial.addItem(addTea("line-1"));
  initial.setLineQuantity("line-1", 2);
  const activeCart = session(initial);
  const before = activeCart.read();

  const mismatch = await new ActivePromotionSnapshotLoader(
    activeCart,
    new SnapshotPort({ ...snapshot(), storeCode: "S2" }),
  ).load({ storeCode: "S1", asOfIso });
  assert.deepEqual(mismatch, { status: "fallback" });
  assert.equal(activeCart.read(), before);

  const valid = snapshot();
  const duplicate = await new ActivePromotionSnapshotLoader(
    activeCart,
    new SnapshotPort({
      ...valid,
      promotions: [...valid.promotions, ...valid.promotions],
    }),
  ).load({ storeCode: "S1", asOfIso });
  assert.deepEqual(duplicate, { status: "fallback" });
  assert.equal(activeCart.read(), before);

  const unavailable = await new ActivePromotionSnapshotLoader(activeCart, {
    async loadActive() {
      throw new Error("SQLCipher unavailable");
    },
  }).load({ storeCode: "S1", asOfIso });
  assert.deepEqual(unavailable, { status: "fallback" });
  assert.equal(activeCart.read(), before);
});

class SnapshotPort implements ActivePromotionSnapshotPort {
  public constructor(private readonly value: StoredPromotionSnapshot | null) {}

  public async loadActive(): Promise<StoredPromotionSnapshot | null> {
    return this.value;
  }
}

function session(initial: PricingCart): ActivePricingCartSession {
  return new ActivePricingCartSession(initial, () => new PricingCart());
}

function cartWithTwoTea(): PricingCart {
  const cart = new PricingCart({ asOfIso });
  cart.addItem(addTea("line-1"));
  cart.setLineQuantity("line-1", 2);
  return cart;
}

function addTea(lineId: string) {
  return {
    lineId,
    productCode: "TEA",
    itemNumber: null,
    lookupCode: lineId,
    displayName: "Tea",
    unitPrice: { currency: "AUD" as const, cents: 1_000 },
    syncProvenance: { referenceCode: null, priceSource: 0 as const },
  };
}

function parsedPromotion() {
  return {
    id: "PROMO-2-FOR-15",
    name: "2 for 15",
    effectiveStartIso: "2026-06-12T00:00:00.000Z",
    effectiveEndIso: "2026-06-14T00:00:00.000Z",
    isExclusive: false,
    priority: 10,
    applyQuantity: 2,
    fixedPrice: { currency: "AUD" as const, cents: 1_500 },
    maxApplicationsPerOrder: null,
    products: [{ productCode: "TEA", unitWeight: 1 }],
  };
}

function snapshot(overrides: Partial<Record<string, unknown>> = {}): StoredPromotionSnapshot {
  return {
    snapshotId: "catalog-1",
    storeCode: "S1",
    promotions: [
      {
        promotionId: "PROMO-2-FOR-15",
        definitionJson: JSON.stringify({
          promotionId: "PROMO-2-FOR-15",
          name: "2 for 15",
          effectiveStart: "2026-06-12T00:00:00.000Z",
          effectiveEnd: "2026-06-14T00:00:00.000Z",
          isExclusive: false,
          priority: 10,
          applyQuantity: 2,
          fixedPrice: 15,
          maxApplicationsPerOrder: null,
          products: [{ productCode: "TEA", unitWeight: 1 }],
          ...overrides,
        }),
      },
    ],
  };
}
