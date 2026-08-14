import assert from "node:assert/strict";
import test from "node:test";

import { evaluateLegacyHeldOrderPayload } from "./legacy-held-order-evaluator";
import { normalizeSharedSaleCartV1 } from "./shared-sale-cart-v1";

import type { HeldOrderPayloadV1 } from "@/core/contracts";

function validLegacyPayload(): HeldOrderPayloadV1 {
  return {
    version: 1,
    pricingState: {
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
          quantity: 1,
          unitPriceCents: 1002,
          basePriceSource: "catalog",
          syncProvenance: { referenceCode: null, priceSource: 0 },
          kind: "sale",
          returnSourceKey: null,
          originalOrderGuid: null,
          originalOrderDetailGuid: null,
          discountState: { kind: "none" },
        },
      ],
    },
  };
}

test("有效 legacy payload 评估为可发布且 canonical 精确", () => {
  const result = evaluateLegacyHeldOrderPayload(validLegacyPayload());
  assert.equal(result.outcome, "publishable");
  if (result.outcome !== "publishable") return;
  assert.equal(result.cart.version, 1);
  assert.equal(result.cart.pricingState.lines[0]?.unitPriceCents, 1002);
  assert.deepEqual(result.cart.pricingState.lines[0]?.discountState, {
    mode: "none",
  });
  assert.deepEqual(result.cart, normalizeSharedSaleCartV1(result.cart));
});

test("catalog baseline 会选择 V2 且不折叠进单价", () => {
  const result = evaluateLegacyHeldOrderPayload({
    ...validLegacyPayload(),
    pricingState: {
      ...validLegacyPayload().pricingState,
      lines: validLegacyPayload().pricingState.lines.map((line) => ({
        ...line,
        unitPriceCents: 699,
        catalogDiscountBasisPoints: 2_000,
      })),
    },
  });
  assert.equal(result.outcome, "publishable");
  if (result.outcome !== "publishable" || result.cart.version !== 2) return;
  assert.equal(result.cart.pricingState.lines[0]?.unitPriceCents, 699);
  assert.equal(
    result.cart.pricingState.lines[0]?.catalogDiscountBasisPoints,
    2_000,
  );
});

test("损坏或非销售 payload fail-closed", () => {
  const corrupted = evaluateLegacyHeldOrderPayload(null);
  assert.equal(corrupted.outcome, "blocked");
  if (corrupted.outcome === "blocked") {
    assert.equal(corrupted.reason, "LEGACY_PAYLOAD_CORRUPTED");
  }

  const returnCart = evaluateLegacyHeldOrderPayload({
    ...validLegacyPayload(),
    pricingState: {
      ...validLegacyPayload().pricingState,
      mode: "return",
    },
  });
  assert.equal(returnCart.outcome, "blocked");
  if (returnCart.outcome === "blocked") {
    assert.equal(returnCart.reason, "SHARED_CART_MODE_NOT_SALE");
  }
});
