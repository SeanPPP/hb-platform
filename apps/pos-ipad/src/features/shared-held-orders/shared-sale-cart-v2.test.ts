import assert from "node:assert/strict";
import test from "node:test";

import {
  normalizeSharedSaleCartV1,
  toSharedSaleCartV1,
  type SharedSaleCartV1,
} from "@hb/pos-domain/features/shared-held-orders/shared-sale-cart-v1";
import {
  SharedSaleCartValidationError,
  hasCatalogBaseline,
  normalizeSharedSaleCart,
  normalizeSharedSaleCartV2,
  toSharedSaleCartV2,
  v1ToV2,
  v2ToV1,
  type SharedSaleCartV2,
} from "./shared-sale-cart-v2";

import type { PricingCartStateSnapshot } from "@/core/contracts";

function validV2(): SharedSaleCartV2 {
  return {
    version: 2,
    pricingState: {
      revision: 7,
      mode: "sale",
      asOfIso: "2026-07-28T08:00:00.000Z",
      promotions: [
        {
          id: "promo-1",
          name: "Three for five",
          effectiveStartIso: "2026-07-01T00:00:00.000Z",
          effectiveEndIso: "2026-08-01T00:00:00.000Z",
          isExclusive: false,
          priority: 1,
          applyQuantity: 1,
          fixedPriceCents: 500,
          maxApplicationsPerOrder: null,
          products: [{ productCode: "P-PROMO", unitWeight: 1 }],
        },
      ],
      lines: [
        {
          lineId: "line-1",
          productCode: "P-1",
          itemNumber: "100",
          lookupCode: "100",
          displayName: "Item one",
          quantity: 2,
          unitPriceCents: 501,
          basePriceSource: "manual",
          syncProvenance: { referenceCode: "REF-1", priceSource: 1 },
          kind: "sale",
          returnSourceKey: null,
          originalOrderGuid: null,
          originalOrderDetailGuid: null,
          catalogDiscountBasisPoints: 1500,
          discountState: { mode: "manual-amount", cents: 102 },
        },
      ],
    },
  };
}

function withLineDiscount(
  cart: SharedSaleCartV2,
  discountState: SharedSaleCartV2["pricingState"]["lines"][number]["discountState"],
): SharedSaleCartV2 {
  return {
    ...cart,
    pricingState: {
      ...cart.pricingState,
      lines: cart.pricingState.lines.map((line, index) =>
        index === 0 ? { ...line, discountState } : line,
      ),
    },
  };
}

function withLineCatalogBasisPoints(
  cart: SharedSaleCartV2,
  catalogDiscountBasisPoints: number,
): SharedSaleCartV2 {
  return {
    ...cart,
    pricingState: {
      ...cart.pricingState,
      lines: cart.pricingState.lines.map((line, index) =>
        index === 0 ? { ...line, catalogDiscountBasisPoints } : line,
      ),
    },
  };
}

test("V2 valid cart with catalog baseline normalizes and roundtrips", () => {
  const cart = normalizeSharedSaleCartV2(validV2());
  assert.equal(cart.version, 2);
  assert.equal(cart.pricingState.lines[0]?.catalogDiscountBasisPoints, 1500);
  assert.equal(JSON.stringify(normalizeSharedSaleCartV2(cart)), JSON.stringify(cart));
});

test("V2 rejects catalog baseline coexisting with promotion discount", () => {
  const cart = withLineDiscount(validV2(), {
    mode: "promotion",
    cents: 100,
    promotionIds: ["promo-1"],
  });
  assert.throws(
    () => normalizeSharedSaleCartV2(cart),
    (error: unknown) => error instanceof SharedSaleCartValidationError,
  );
});

test("V2 accepts manual discount over catalog baseline and preserves baseline", () => {
  const cart = normalizeSharedSaleCartV2(validV2());
  assert.equal(cart.pricingState.lines[0]?.catalogDiscountBasisPoints, 1500);
  assert.deepEqual(cart.pricingState.lines[0]?.discountState, {
    mode: "manual-amount",
    cents: 102,
  });
});

test("V2 rejects catalogDiscountBasisPoints out of 0..10000", () => {
  for (const bps of [-1, 10001]) {
    const cart = withLineCatalogBasisPoints(validV2(), bps);
    assert.throws(
      () => normalizeSharedSaleCartV2(cart),
      (error: unknown) => error instanceof SharedSaleCartValidationError,
    );
  }
});

test("version dispatch normalizes V1 and V2 carts", () => {
  // 显式剥离 V2 专有的 catalogDiscountBasisPoints，构造真正的冻结 V1 wire。
  const v2Pricing = validV2().pricingState;
  const v1Pricing = {
    ...v2Pricing,
    lines: v2Pricing.lines.map(({ catalogDiscountBasisPoints: _bps, ...rest }) => rest),
  };
  const v1: SharedSaleCartV1 = normalizeSharedSaleCartV1({
    version: 1,
    pricingState: v1Pricing,
  });
  const fromV1 = normalizeSharedSaleCart(v1);
  assert.equal(fromV1.version, 1);
  const fromV2 = normalizeSharedSaleCart(validV2());
  assert.equal(fromV2.version, 2);
  assert.throws(
    () => normalizeSharedSaleCart({ version: 3, pricingState: v1Pricing }),
    (error: unknown) => error instanceof SharedSaleCartValidationError,
  );
});

test("V1<->V2 mapping: baseline drives version, downgrade rejects lossy baseline", () => {
  const v2 = normalizeSharedSaleCartV2(validV2());
  assert.equal(hasCatalogBaseline(v2), true);
  assert.throws(() => v2ToV1(v2), (error: unknown) => error instanceof SharedSaleCartValidationError);

  const noBaselineV2 = normalizeSharedSaleCartV2({
    ...validV2(),
    pricingState: {
      ...validV2().pricingState,
      lines: validV2().pricingState.lines.map((line) => ({
        ...line,
        catalogDiscountBasisPoints: 0,
      })),
    },
  });
  assert.equal(hasCatalogBaseline(noBaselineV2), false);
  const downgraded = v2ToV1(noBaselineV2);
  assert.equal(downgraded.version, 1);
  const back = v1ToV2(downgraded);
  assert.equal(back.version, 2);
  assert.equal(back.pricingState.lines[0]?.catalogDiscountBasisPoints, 0);
});

test("snapshot mapping preserves catalog baseline from snapshot lines", () => {
  const snapshot: PricingCartStateSnapshot = {
    revision: 7,
    mode: "sale",
    asOfIso: "2026-07-28T08:00:00.000Z",
    promotions: [],
    lines: [
      {
        lineId: "line-1",
        productCode: "P-1",
        itemNumber: null,
        lookupCode: "100",
        displayName: "Item one",
        quantity: 2,
        unitPriceCents: 501,
        basePriceSource: "catalog",
        kind: "sale",
        returnSourceKey: null,
        originalOrderGuid: null,
        originalOrderDetailGuid: null,
        discountState: { kind: "none" },
        catalogDiscountBasisPoints: 1500,
      },
    ],
  };
  const cart = toSharedSaleCartV2(snapshot);
  assert.equal(cart.pricingState.lines[0]?.catalogDiscountBasisPoints, 1500);
  assert.throws(
    () => toSharedSaleCartV1(snapshot),
    (error: unknown) => error instanceof SharedSaleCartValidationError,
  );
});
