import assert from "node:assert/strict";
import test from "node:test";

import {
  createAud,
  type LineSyncProvenance,
  type PricingCartStateSnapshot,
} from "../../../core/contracts";

import {
  PricingCart,
  type AddCartItemInput,
  type AddOpenItemInput,
  type PromotionDefinition,
} from "./index";

const asOfIso = "2026-07-28T00:00:00.000Z";

function saleInput(
  lineId: string,
  syncProvenance: LineSyncProvenance,
  overrides: Partial<AddCartItemInput> = {},
): AddCartItemInput & Readonly<{ syncProvenance: LineSyncProvenance }> {
  return {
    lineId,
    productCode: "SKU-001",
    itemNumber: "1001",
    lookupCode: "930000000001",
    displayName: "Tea",
    unitPrice: createAud(1_000),
    syncProvenance,
    ...overrides,
  };
}

function promotion(): PromotionDefinition {
  return {
    id: "PROMO-2-FOR-15",
    name: "2 for 15",
    effectiveStartIso: "2026-07-27T00:00:00.000Z",
    effectiveEndIso: "2026-07-29T00:00:00.000Z",
    isExclusive: false,
    priority: 10,
    applyQuantity: 2,
    fixedPrice: createAud(1_500),
    maxApplicationsPerOrder: null,
    products: [{ productCode: "SKU-001", unitWeight: 1 }],
  };
}

test("新行严格规范化并冻结服务端售卖身份，调用方后续修改不能污染购物车", () => {
  const mutableProvenance: {
    referenceCode: string | null;
    priceSource: 0 | 1 | 2 | 3 | 4;
  } = {
    referenceCode: " REF-SET-01 ",
    priceSource: 2,
  };
  const cart = new PricingCart({ asOfIso });

  cart.addItem(saleInput("line-1", mutableProvenance));
  mutableProvenance.referenceCode = "MUTATED";
  mutableProvenance.priceSource = 4;

  const line = cart.snapshot().lines[0]!;
  assert.deepEqual(line.syncProvenance, {
    referenceCode: "REF-SET-01",
    priceSource: 2,
  });
  assert.equal(Object.isFrozen(line.syncProvenance), true);
  assert.equal(
    Object.isFrozen(cart.stateSnapshot().lines[0]!.syncProvenance),
    true,
  );
});

test("同 lookup 合并要求售卖身份完全一致，冲突时 fail-closed 且不改变原行", () => {
  const cart = new PricingCart({ asOfIso });
  cart.addItem(
    saleInput("line-1", {
      referenceCode: "REF-1",
      priceSource: 1,
    }),
  );

  assert.throws(
    () =>
      cart.addItem(
        saleInput(
          "line-2",
          {
            referenceCode: "REF-2",
            priceSource: 1,
          },
          { lookupCode: " 930000000001 " },
        ),
      ),
    /sync provenance/i,
  );
  assert.equal(cart.snapshot().lines[0]!.quantity, "1");
});

test("快照恢复、手工改价和促销只改变展示价格来源，不改变补传售卖身份", () => {
  const cart = new PricingCart({
    asOfIso,
    promotions: [promotion()],
  });
  const provenance = {
    referenceCode: "REF-STORE",
    priceSource: 3 as const,
  };
  cart.addItem(saleInput("line-1", provenance));
  cart.addItem(saleInput("line-2", provenance));

  assert.equal(cart.snapshot().lines[0]!.priceSource, "promotion");
  assert.deepEqual(cart.snapshot().lines[0]!.syncProvenance, provenance);
  cart.setPromotions([], asOfIso);
  assert.equal(cart.setLineUnitPrice("line-1", createAud(900)), true);
  assert.equal(cart.snapshot().lines[0]!.priceSource, "manual");
  assert.deepEqual(cart.snapshot().lines[0]!.syncProvenance, provenance);

  const state = cart.stateSnapshot();
  const restored = PricingCart.restore(state);
  assert.deepEqual(restored.stateSnapshot(), state);
  assert.deepEqual(
    restored.snapshot().lines.map((line) => line.syncProvenance),
    [provenance],
  );
});

test("旧快照缺失售卖身份时保持 undefined，恢复过程绝不按当前目录推断", () => {
  const legacySnapshot: PricingCartStateSnapshot = {
    revision: 8,
    mode: "sale",
    asOfIso,
    promotions: [],
    lines: [
      {
        lineId: "legacy-line",
        productCode: "SKU-LEGACY",
        itemNumber: null,
        lookupCode: "LEGACY",
        displayName: "Legacy",
        quantity: 1,
        unitPriceCents: 500,
        basePriceSource: "catalog",
        kind: "sale",
        returnSourceKey: null,
        originalOrderGuid: null,
        originalOrderDetailGuid: null,
        discountState: { kind: "none" },
      },
    ],
  };

  const restored = PricingCart.restore(legacySnapshot);

  assert.equal(restored.snapshot().lines[0]!.syncProvenance, undefined);
  assert.equal(
    restored.stateSnapshot().lines[0]!.syncProvenance,
    undefined,
  );
});

test("OPENITEM 新行同样必须接收并冻结明确的目录售卖身份", () => {
  const cart = new PricingCart({ asOfIso });
  const input: AddOpenItemInput &
    Readonly<{ syncProvenance: LineSyncProvenance }> = {
    lineId: "open-1",
    productCode: "OPENITEM",
    itemNumber: null,
    lookupCode: "OPENITEM",
    displayName: "Open item",
    unitPrice: createAud(123),
    syncProvenance: {
      referenceCode: "OPEN-REF",
      priceSource: 0,
    },
  };

  cart.addOpenItem(input);

  assert.deepEqual(cart.snapshot().lines[0]!.syncProvenance, {
    referenceCode: "OPEN-REF",
    priceSource: 0,
  });
});
