import assert from "node:assert/strict";
import test from "node:test";


import { evaluateLegacyHeldOrderPayload } from "./legacy-held-order-evaluator";
import { normalizeSharedSaleCartV1 } from "@hb/pos-domain/features/shared-held-orders/shared-sale-cart-v1";

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

test("有效 iPad payload 评估为可发布且 canonical 精确", () => {
  const result = evaluateLegacyHeldOrderPayload(validLegacyPayload());
  assert.equal(result.outcome, "publishable");
  if (result.outcome !== "publishable") return;
  assert.equal(result.cart.version, 1);
  assert.equal(result.cart.pricingState.mode, "sale");
  assert.equal(result.cart.pricingState.lines[0]?.unitPriceCents, 1002);
  assert.deepEqual(result.cart.pricingState.lines[0]?.discountState, {
    mode: "none",
  });
  assert.deepEqual(result.cart, normalizeSharedSaleCartV1(result.cart));
});

test("本地挂单含 catalog baseline 时评估为 V2 且不折叠进单价", () => {
  const legacy = validLegacyPayload();
  const result = evaluateLegacyHeldOrderPayload({
    ...legacy,
    pricingState: {
      ...legacy.pricingState,
      lines: legacy.pricingState.lines.map((line) => ({
        ...line,
        unitPriceCents: 699,
        catalogDiscountBasisPoints: 2_000,
      })),
    },
  });
  assert.equal(result.outcome, "publishable");
  if (result.outcome !== "publishable") return;
  assert.equal(result.cart.version, 2);
  if (result.cart.version !== 2) return;
  assert.equal(result.cart.pricingState.lines[0]?.unitPriceCents, 699);
  assert.equal(
    result.cart.pricingState.lines[0]?.catalogDiscountBasisPoints,
    2_000,
  );
  assert.deepEqual(result.cart.pricingState.lines[0]?.discountState, {
    mode: "none",
  });
});

function expectBlockedReason(input: unknown, reason: string): void {
  const result = evaluateLegacyHeldOrderPayload(input);
  assert.equal(result.outcome, "blocked");
  if (result.outcome !== "blocked") return;
  assert.equal(result.reason, reason);
}

test("损坏 payload 阻断并给出稳定原因", () => {
  expectBlockedReason(null, "LEGACY_PAYLOAD_CORRUPTED");
  expectBlockedReason("garbage", "LEGACY_PAYLOAD_CORRUPTED");
  expectBlockedReason({ version: 1 }, "LEGACY_PAYLOAD_CORRUPTED");
  expectBlockedReason(
    { version: 1, pricingState: "not-an-object" },
    "LEGACY_PAYLOAD_CORRUPTED",
  );
  expectBlockedReason(
    { ...validLegacyPayload(), version: 2 },
    "LEGACY_PAYLOAD_VERSION_UNSUPPORTED",
  );
});

test("非普通 sale（退货/分期/退货行/原单字段）阻断并给出稳定原因", () => {
  expectBlockedReason(
    {
      ...validLegacyPayload(),
      pricingState: { ...validLegacyPayload().pricingState, mode: "return" },
    },
    "SHARED_CART_MODE_NOT_SALE",
  );
  expectBlockedReason(
    {
      ...validLegacyPayload(),
      pricingState: {
        ...validLegacyPayload().pricingState,
        lines: [
          {
            ...validLegacyPayload().pricingState.lines[0]!,
            kind: "return",
          },
        ],
      },
    },
    "SHARED_CART_LINE_KIND_NOT_SALE",
  );
  expectBlockedReason(
    {
      ...validLegacyPayload(),
      pricingState: {
        ...validLegacyPayload().pricingState,
        lines: [
          {
            ...validLegacyPayload().pricingState.lines[0]!,
            originalOrderGuid: "order-1",
          },
        ],
      },
    },
    "SHARED_CART_RETURN_ORIGINAL_NOT_EMPTY",
  );
  expectBlockedReason(
    {
      ...validLegacyPayload(),
      pricingState: {
        ...validLegacyPayload().pricingState,
        lines: [{ ...validLegacyPayload().pricingState.lines[0]!, quantity: 0 }],
      },
    },
    "SHARED_CART_INVALID",
  );
});
