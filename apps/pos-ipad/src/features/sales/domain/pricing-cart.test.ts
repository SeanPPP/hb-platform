import assert from "node:assert/strict";
import test from "node:test";

import { createAud } from "../../../core/contracts";

import {
  PricingCart,
  QUICK_DISCOUNT_BASIS_POINTS,
  type AddCartItemInput,
} from "./index";

const asOfIso = "2026-06-13T12:00:00.000Z";

function item(
  lineId: string,
  overrides: Partial<AddCartItemInput> = {},
): AddCartItemInput {
  return {
    lineId,
    productCode: `SKU-${lineId}`,
    itemNumber: lineId,
    lookupCode: lineId,
    displayName: `Item ${lineId}`,
    unitPrice: createAud(1_000),
    syncProvenance: { referenceCode: null, priceSource: 0 },
    ...overrides,
  };
}

test("cart merges normalized sale lookups but keeps OPENITEM lines independent", () => {
  const cart = new PricingCart({ asOfIso });

  assert.equal(
    cart.addItem(
      item("line-a", {
        lookupCode: " abc-001 ",
        unitPrice: createAud(1_000),
      }),
    ),
    "line-a",
  );
  assert.equal(
    cart.addItem(
      item("ignored-merge-id", {
        lookupCode: "ABC-001",
        unitPrice: createAud(1_200),
      }),
    ),
    "line-a",
  );
  cart.addOpenItem({
    lineId: "open-1",
    productCode: "OPEN-SKU",
    itemNumber: null,
    displayName: "Manual item",
    unitPrice: createAud(789),
    syncProvenance: { referenceCode: "OPENITEM", priceSource: 0 },
  });
  cart.addOpenItem({
    lineId: "open-2",
    productCode: "OPEN-SKU",
    itemNumber: null,
    displayName: "Manual item",
    unitPrice: createAud(789),
    syncProvenance: { referenceCode: "OPENITEM", priceSource: 0 },
  });

  const snapshot = cart.snapshot();
  assert.equal(snapshot.lines.length, 3);
  assert.deepEqual(snapshot.lines[0], {
    lineId: "line-a",
    productCode: "SKU-line-a",
    itemNumber: "line-a",
    lookupCode: " abc-001 ",
    displayName: "Item line-a",
    quantity: "2",
    unitPrice: createAud(1_000),
    discount: createAud(0),
    actualAmount: createAud(2_000),
    priceSource: "catalog",
    syncProvenance: { referenceCode: null, priceSource: 0 },
    kind: "sale",
    returnSourceKey: null,
    originalOrderGuid: null,
    originalOrderDetailGuid: null,
  });
  assert.deepEqual(
    snapshot.lines.slice(1).map((line) => [
      line.lineId,
      line.priceSource,
      line.actualAmount.cents,
    ]),
    [
      ["open-1", "open-item", 789],
      ["open-2", "open-item", 789],
    ],
  );
});

test("quantity, unit price and remove mutations use integer cents", () => {
  const cart = new PricingCart({ asOfIso });
  cart.addItem(item("line-a"));

  assert.equal(cart.setLineQuantity("line-a", 3), true);
  assert.equal(cart.setLineUnitPrice("line-a", createAud(1_255)), true);
  assert.equal(cart.increaseLine("line-a"), true);
  assert.equal(cart.decreaseLine("line-a"), true);

  let line = cart.snapshot().lines[0]!;
  assert.equal(line.quantity, "3");
  assert.equal(line.unitPrice.cents, 1_255);
  assert.equal(line.actualAmount.cents, 3_765);
  assert.equal(line.priceSource, "manual");

  assert.equal(cart.setLineQuantity("line-a", 0), false);
  assert.equal(cart.setLineQuantity("line-a", 1.5), false);
  assert.equal(cart.removeLine("line-a"), true);
  assert.equal(cart.snapshot().lines.length, 0);
});

test("fixed and percent line discounts match WPF recomputation behavior", () => {
  const cart = new PricingCart({ asOfIso });
  cart.addItem(item("line-a"));
  cart.setLineQuantity("line-a", 2);

  assert.equal(cart.setLineDiscountAmount("line-a", createAud(300)), true);
  assert.equal(cart.snapshot().lines[0]!.actualAmount.cents, 1_700);
  assert.equal(
    cart.setLineDiscountAmount("line-a", createAud(2_001)),
    false,
  );

  assert.equal(cart.setLineDiscountPercentBps("line-a", 850), true);
  assert.equal(cart.snapshot().lines[0]!.discount.cents, 170);
  assert.equal(cart.setLineQuantity("line-a", 3), true);
  assert.equal(cart.snapshot().lines[0]!.discount.cents, 255);
  assert.equal(cart.snapshot().lines[0]!.actualAmount.cents, 2_745);

  assert.deepEqual(QUICK_DISCOUNT_BASIS_POINTS, [
    1_000,
    2_000,
    3_000,
    4_000,
    5_000,
  ]);
  assert.equal(cart.applyQuickLineDiscount("line-a", 2_000), true);
  assert.equal(cart.snapshot().lines[0]!.discount.cents, 600);
});

test("order discounts allocate rounded remainders deterministically", () => {
  const proportional = new PricingCart({ asOfIso });
  proportional.addItem(
    item("low", { productCode: "SKU-SAME", unitPrice: createAud(400) }),
  );
  proportional.addItem(
    item("high", { productCode: "SKU-SAME", unitPrice: createAud(1_000) }),
  );

  assert.equal(
    proportional.setOrderDiscountAmount(createAud(400)),
    true,
  );
  assert.deepEqual(
    proportional.snapshot().lines.map((line) => line.discount.cents),
    [114, 286],
  );

  const cents = new PricingCart({ asOfIso });
  cents.addItem(item("cent-a", { unitPrice: createAud(1) }));
  cents.addItem(item("cent-b", { unitPrice: createAud(1) }));
  cents.addItem(item("cent-c", { unitPrice: createAud(1) }));

  assert.equal(cents.setOrderDiscountPercentBps(5_000), true);
  assert.deepEqual(
    cents.snapshot().lines.map((line) => line.discount.cents),
    [1, 1, 0],
  );
  assert.equal(cents.snapshot().discount.cents, 2);
  assert.equal(cents.snapshot().actualAmount.cents, 1);
});

test("feature-private state restores percent behavior while frozen snapshots stay stable", () => {
  const first = new PricingCart({ asOfIso });
  first.addItem(item("line-a", { unitPrice: createAud(1_000) }));
  first.setLineQuantity("line-a", 2);
  first.setLineDiscountPercentBps("line-a", 850);

  const state = first.stateSnapshot();
  const restored = PricingCart.restore(state);

  assert.deepEqual(restored.snapshot(), first.snapshot());
  assert.deepEqual(restored.stateSnapshot(), state);
  assert.equal(restored.setLineQuantity("line-a", 3), true);
  assert.equal(restored.snapshot().lines[0]!.discount.cents, 255);
  assert.equal(
    restored.stateSnapshot().lines[0]!.discountState.kind,
    "manual-percent",
  );
  assert.deepEqual(restored.snapshot(), restored.snapshot());
});

test("zero-price lines remain representable without floating point coercion", () => {
  const cart = new PricingCart({ asOfIso });
  cart.addOpenItem({
    lineId: "open-zero",
    productCode: "OPENITEM",
    itemNumber: null,
    displayName: "Open item",
    unitPrice: createAud(0),
    syncProvenance: { referenceCode: "OPENITEM", priceSource: 0 },
  });

  assert.equal(cart.snapshot().subtotal.cents, 0);
  assert.equal(cart.snapshot().actualAmount.cents, 0);
  assert.equal(cart.snapshot().lines[0]!.priceSource, "open-item");
});
