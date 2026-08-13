import assert from "node:assert/strict";
import test from "node:test";

import {
  fromSharedSaleCart,
  fromSharedSaleCartV1,
  fromSharedSaleCartV2,
} from "./shared-held-order-cart-reverse-mapper";
import {
  normalizeSharedSaleCartV1,
  type SharedSaleCartV1,
  toSharedSaleCartV1,
} from "./shared-sale-cart-v1";
import { normalizeSharedSaleCartV2 } from "./shared-sale-cart-v2";

import type { PricingCartStateSnapshot } from "@/core/contracts";
import { PricingCart } from "@/features/sales/domain/pricing-cart";

function wireCart(overrides: Partial<SharedSaleCartV1["pricingState"]> = {}): SharedSaleCartV1 {
  return normalizeSharedSaleCartV1({
    version: 1,
    pricingState: {
      revision: 9,
      mode: "sale",
      asOfIso: "2026-07-28T08:00:00.000Z",
      promotions: [
        {
          id: "promo-1",
          name: "Special",
          effectiveStartIso: "2026-07-28T00:00:00.000Z",
          effectiveEndIso: "2026-08-01T00:00:00.000Z",
          isExclusive: true,
          priority: 10,
          applyQuantity: 2,
          fixedPriceCents: 1_500,
          maxApplicationsPerOrder: 3,
          products: [{ productCode: "P-1", unitWeight: 0.5 }],
        },
      ],
      lines: [
        {
          lineId: "line-1",
          productCode: "P-1",
          itemNumber: "I-1",
          lookupCode: "100",
          displayName: "Item one",
          quantity: 2,
          unitPriceCents: 1_000,
          basePriceSource: "catalog",
          syncProvenance: { referenceCode: "REF-1", priceSource: 0 },
          kind: "sale",
          returnSourceKey: null,
          originalOrderGuid: null,
          originalOrderDetailGuid: null,
          discountState: { mode: "manual-amount", cents: 100 },
        },
        {
          lineId: "line-2",
          productCode: "P-2",
          itemNumber: null,
          lookupCode: "200",
          displayName: "Item two",
          quantity: 1,
          unitPriceCents: 2_000,
          basePriceSource: "manual",
          syncProvenance: null,
          kind: "sale",
          returnSourceKey: null,
          originalOrderGuid: null,
          originalOrderDetailGuid: null,
          discountState: { mode: "manual-percent", basisPoints: 500 },
        },
        {
          lineId: "line-3",
          productCode: "P-3",
          itemNumber: null,
          lookupCode: "300",
          displayName: "Item three",
          quantity: 1,
          unitPriceCents: 3_000,
          basePriceSource: "catalog",
          syncProvenance: null,
          kind: "sale",
          returnSourceKey: null,
          originalOrderGuid: null,
          originalOrderDetailGuid: null,
          discountState: {
            mode: "promotion",
            cents: 300,
            promotionIds: ["promo-1"],
          },
        },
      ],
      ...overrides,
    },
  });
}

test("反向映射逐字段还原可恢复快照，promotion fixedPrice 转 Money", () => {
  const restored: PricingCartStateSnapshot = fromSharedSaleCartV1(wireCart());

  assert.equal(restored.revision, 9);
  assert.equal(restored.mode, "sale");
  assert.equal(restored.asOfIso, "2026-07-28T08:00:00.000Z");
  assert.equal(restored.promotions.length, 1);
  assert.equal(restored.promotions[0]?.fixedPrice.cents, 1_500);
  assert.equal(restored.promotions[0]?.fixedPrice.currency, "AUD");
  assert.equal(restored.promotions[0]?.maxApplicationsPerOrder, 3);
  assert.deepEqual(restored.promotions[0]?.products[0], {
    productCode: "P-1",
    unitWeight: 0.5,
  });

  const [first, second, third] = restored.lines;
  assert.equal(first?.lineId, "line-1");
  assert.equal(first?.basePriceSource, "catalog");
  assert.deepEqual(first?.syncProvenance, {
    referenceCode: "REF-1",
    priceSource: 0,
  });
  assert.deepEqual(first?.discountState, { kind: "manual-amount", cents: 100 });
  assert.equal(second?.basePriceSource, "manual");
  assert.equal(second?.syncProvenance, undefined);
  assert.deepEqual(second?.discountState, {
    kind: "manual-percent",
    basisPoints: 500,
  });
  assert.deepEqual(third?.discountState, {
    kind: "promotion",
    cents: 300,
    promotionIds: ["promo-1"],
  });
});

test("canonical 称重数量 1.25 经反向映射可被 PricingCart.restore 精确恢复并发布往返", () => {
  const wire = normalizeSharedSaleCartV1({
    version: 1,
    pricingState: {
      revision: 9,
      mode: "sale",
      asOfIso: "2026-07-28T08:00:00.000Z",
      promotions: [],
      lines: [
        {
          lineId: "line-1",
          productCode: "P-1",
          itemNumber: "I-1",
          lookupCode: "100",
          displayName: "Weighed item",
          quantity: 1.25,
          unitPriceCents: 1_000,
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

  const snapshot = fromSharedSaleCartV1(wire);
  assert.equal(snapshot.lines[0]!.quantity, 1.25);

  const restored = PricingCart.restore(snapshot);
  assert.equal(restored.stateSnapshot().lines[0]!.quantity, 1.25);
  assert.equal(restored.snapshot().lines[0]!.actualAmount.cents, 1_250);

  // 发布侧：restore 后的状态经 toSharedSaleCartV1 + normalize 与冻结 canonical 完全一致。
  const republished = normalizeSharedSaleCartV1(
    toSharedSaleCartV1(restored.stateSnapshot()),
  );
  assert.equal(republished.pricingState.lines[0]!.quantity, 1.25);
  assert.equal(JSON.stringify(republished), JSON.stringify(wire));
});

test("反向映射冻结输出并拒绝损坏输入（非 sale/缺字段由 normalize 拦截）", () => {
  const restored = fromSharedSaleCartV1(wireCart());
  assert.ok(Object.isFrozen(restored));
  assert.ok(Object.isFrozen(restored.lines[0]));

  assert.throws(
    () =>
      fromSharedSaleCartV1(
        wireCart({ mode: "return" as never }),
      ),
    /SHARED_CART_MODE_NOT_SALE|mode/i,
  );
  assert.throws(
    () =>
      fromSharedSaleCartV1(
        normalizeSharedSaleCartV1({
          version: 1,
          pricingState: {
            revision: 1,
            mode: "sale",
            asOfIso: "2026-07-28T00:00:00.000Z",
            promotions: [],
            lines: [],
          },
        }),
      ),
    /SHARED_CART_INVALID|1 to 1000/,
  );
});

test("V1 恢复 catalog baseline 为 0，V2 双向恢复 catalogDiscountBasisPoints", () => {
  const v1 = wireCart();
  const v2 = normalizeSharedSaleCartV2({
    version: 2,
    pricingState: {
      ...v1.pricingState,
      lines: v1.pricingState.lines.map((line, index) => ({
        ...line,
        catalogDiscountBasisPoints: index === 0 ? 2_000 : 0,
      })),
    },
  });

  assert.equal(fromSharedSaleCartV1(v1).lines[0]?.catalogDiscountBasisPoints, 0);
  assert.equal(fromSharedSaleCart(v1).lines[0]?.catalogDiscountBasisPoints, 0);
  assert.equal(
    fromSharedSaleCartV2(v2).lines[0]?.catalogDiscountBasisPoints,
    2_000,
  );
  assert.equal(
    fromSharedSaleCart(v2).lines[0]?.catalogDiscountBasisPoints,
    2_000,
  );
});
