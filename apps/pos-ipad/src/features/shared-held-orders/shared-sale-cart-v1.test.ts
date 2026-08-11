import assert from "node:assert/strict";
import test from "node:test";

import {
  SharedSaleCartValidationError,
  normalizeSharedSaleCartV1,
  toSharedSaleCartV1,
  type SharedSaleCartV1,
} from "./shared-sale-cart-v1";

import type { PricingCartStateSnapshot } from "@/core/contracts";

function validCart(): SharedSaleCartV1 {
  return {
    version: 1,
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
          discountState: { mode: "manual-amount", cents: 102 },
        },
        {
          lineId: "line-2",
          productCode: "P-2",
          itemNumber: null,
          lookupCode: "200",
          displayName: "Item two",
          quantity: 1,
          unitPriceCents: 1099,
          basePriceSource: "catalog",
          syncProvenance: null,
          kind: "sale",
          returnSourceKey: null,
          originalOrderGuid: null,
          originalOrderDetailGuid: null,
          discountState: {
            mode: "promotion",
            cents: 100,
            promotionIds: ["promo-1"],
          },
        },
      ],
    },
  };
}

function snapshot(): PricingCartStateSnapshot {
  return {
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
        fixedPrice: { currency: "AUD", cents: 500 },
        maxApplicationsPerOrder: null,
        products: [{ productCode: "P-PROMO", unitWeight: 1 }],
      },
    ],
    lines: [
      {
        lineId: "line-1",
        productCode: "P-1",
        itemNumber: null,
        lookupCode: "100",
        displayName: "Item one",
        quantity: 1,
        unitPriceCents: 501,
        basePriceSource: "catalog",
        syncProvenance: { referenceCode: null, priceSource: 0 },
        kind: "sale",
        returnSourceKey: null,
        originalOrderGuid: null,
        originalOrderDetailGuid: null,
        discountState: {
          kind: "promotion",
          cents: 100,
          promotionIds: ["promo-1"],
        },
      },
    ],
  };
}

test("canonical 精确 roundtrip：JSON 往返后逐字段冻结且 cents 不丢失", () => {
  const canonical = normalizeSharedSaleCartV1(validCart());
  const roundtrip = normalizeSharedSaleCartV1(
    JSON.parse(JSON.stringify(canonical)) as unknown,
  );
  assert.deepEqual(roundtrip, canonical);
  assert.equal(canonical.pricingState.lines[0]?.unitPriceCents, 501);
  assert.deepEqual(canonical.pricingState.lines[0]?.discountState, {
    mode: "manual-amount",
    cents: 102,
  });
  assert.deepEqual(canonical.pricingState.lines[1]?.discountState, {
    mode: "promotion",
    cents: 100,
    promotionIds: ["promo-1"],
  });
  assert.deepEqual(canonical.pricingState.lines[0]?.syncProvenance, {
    referenceCode: "REF-1",
    priceSource: 1,
  });
  assert.equal(canonical.pricingState.lines[1]?.syncProvenance, null);
  assert.deepEqual(canonical.pricingState.promotions[0], {
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
  });
  // 冻结：修改抛错。
  assert.throws(() => {
    (roundtrip.pricingState as { revision: number }).revision = 99;
  }, TypeError);
});

test("JSON.stringify wire fixture：无扁平 revision/mode/fixedPrice/kind", () => {
  const canonical = normalizeSharedSaleCartV1(validCart());
  const serialized = JSON.stringify(canonical);
  const wire = JSON.parse(serialized) as Record<string, unknown>;

  // 顶层只有 version+pricingState，没有扁平 revision/mode。
  assert.deepEqual(Object.keys(wire), ["version", "pricingState"]);
  const pricing = wire.pricingState as Record<string, unknown>;
  assert.deepEqual(Object.keys(pricing), [
    "revision",
    "mode",
    "asOfIso",
    "promotions",
    "lines",
  ]);

  // 促销金额是标量分，不存在 fixedPrice Money 对象。
  const promotion = (pricing.promotions as Record<string, unknown>[])[0]!;
  assert.equal(promotion.fixedPriceCents, 500);
  assert.ok(!("fixedPrice" in promotion));
  assert.ok(!serialized.includes('"fixedPrice":{'));

  // 折扣用 mode 而非 kind。
  const lines = pricing.lines as Record<string, unknown>[];
  for (const line of lines) {
    const discount = line.discountState as Record<string, unknown>;
    assert.ok("mode" in discount);
    assert.ok(!("kind" in discount));
  }
});

function expectCode(
  input: unknown,
  code: string,
): SharedSaleCartValidationError {
  try {
    normalizeSharedSaleCartV1(input);
  } catch (error) {
    if (error instanceof SharedSaleCartValidationError) {
      assert.equal(error.code, code);
      return error;
    }
    throw error;
  }
  assert.fail("expected validation error");
}

test("canonical 只接受 pricingState.mode=sale、每条 kind=sale、return/original 为空", () => {
  expectCode(
    {
      ...validCart(),
      pricingState: { ...validCart().pricingState, mode: "return" },
    },
    "SHARED_CART_MODE_NOT_SALE",
  );
  expectCode(
    {
      ...validCart(),
      pricingState: { ...validCart().pricingState, mode: "installment" },
    },
    "SHARED_CART_MODE_NOT_SALE",
  );

  const returnLine = {
    ...validCart(),
    pricingState: {
      ...validCart().pricingState,
      lines: [{ ...validCart().pricingState.lines[0]!, kind: "return" }],
    },
  };
  expectCode(returnLine, "SHARED_CART_LINE_KIND_NOT_SALE");

  const nonEmptyReturn = {
    ...validCart(),
    pricingState: {
      ...validCart().pricingState,
      lines: [
        { ...validCart().pricingState.lines[0]!, returnSourceKey: "return-1" },
      ],
    },
  };
  expectCode(nonEmptyReturn, "SHARED_CART_RETURN_ORIGINAL_NOT_EMPTY");

  const nonEmptyOriginal = {
    ...validCart(),
    pricingState: {
      ...validCart().pricingState,
      lines: [
        {
          ...validCart().pricingState.lines[0]!,
          originalOrderGuid: "order-1",
        },
      ],
    },
  };
  expectCode(nonEmptyOriginal, "SHARED_CART_RETURN_ORIGINAL_NOT_EMPTY");
});

test("basePriceSource 仅 catalog/manual，拒绝 open-item/promotion", () => {
  for (const basePriceSource of ["open-item", "promotion"]) {
    expectCode(
      {
        ...validCart(),
        pricingState: {
          ...validCart().pricingState,
          lines: [
            {
              ...validCart().pricingState.lines[0]!,
              basePriceSource,
            },
          ],
        },
      },
      "SHARED_CART_INVALID",
    );
  }
});

test("discountState 使用 mode；未知 mode/旧 kind/越界 basisPoints 被拒绝", () => {
  const line0 = () => validCart().pricingState.lines[0]!;
  expectCode(
    {
      ...validCart(),
      pricingState: {
        ...validCart().pricingState,
        lines: [{ ...line0(), discountState: { kind: "none" } }],
      },
    },
    "SHARED_CART_INVALID",
  );
  expectCode(
    {
      ...validCart(),
      pricingState: {
        ...validCart().pricingState,
        lines: [{ ...line0(), discountState: { mode: "unknown" } }],
      },
    },
    "SHARED_CART_INVALID",
  );
  expectCode(
    {
      ...validCart(),
      pricingState: {
        ...validCart().pricingState,
        lines: [{ ...line0(), discountState: { mode: "manual-percent", basisPoints: 0 } }],
      },
    },
    "SHARED_CART_INVALID",
  );
});

test("promotion discount 必须引用冻结促销且 id 不重复", () => {
  const line2 = () => validCart().pricingState.lines[1]!;
  expectCode(
    {
      ...validCart(),
      pricingState: {
        ...validCart().pricingState,
        lines: [
          {
            ...line2(),
            discountState: {
              mode: "promotion",
              cents: 100,
              promotionIds: ["promo-missing"],
            },
          },
        ],
      },
    },
    "SHARED_CART_INVALID",
  );
  expectCode(
    {
      ...validCart(),
      pricingState: {
        ...validCart().pricingState,
        lines: [
          {
            ...line2(),
            discountState: {
              mode: "promotion",
              cents: 100,
              promotionIds: ["promo-1", "promo-1"],
            },
          },
        ],
      },
    },
    "SHARED_CART_INVALID",
  );
});

test("冻结 promotions 定义的 id 必须唯一", () => {
  const promotion = validCart().pricingState.promotions[0]!;
  expectCode(
    {
      ...validCart(),
      pricingState: {
        ...validCart().pricingState,
        promotions: [
          promotion,
          { ...promotion, name: "Duplicate promotion definition" },
        ],
      },
    },
    "SHARED_CART_INVALID",
  );
});

test("canonical 拒绝版本、未知字段与损坏金额", () => {
  expectCode({ ...validCart(), version: 2 }, "SHARED_CART_VERSION_UNSUPPORTED");
  expectCode(
    { ...validCart(), unexpectedField: true },
    "SHARED_CART_INVALID",
  );
  expectCode(
    {
      ...validCart(),
      pricingState: {
        ...validCart().pricingState,
        unexpectedField: true,
      },
    },
    "SHARED_CART_INVALID",
  );
  expectCode(
    {
      ...validCart(),
      pricingState: {
        ...validCart().pricingState,
        lines: [
          {
            ...validCart().pricingState.lines[0]!,
            unitPriceCents: 1.5,
          },
        ],
      },
    },
    "SHARED_CART_INVALID",
  );
  expectCode(
    {
      ...validCart(),
      pricingState: {
        ...validCart().pricingState,
        promotions: [
          {
            ...validCart().pricingState.promotions[0]!,
            fixedPriceCents: 1.5,
          },
        ],
      },
    },
    "SHARED_CART_INVALID",
  );
  // 旧 wire：促销仍带 fixedPrice Money 对象 -> 拒绝。
  expectCode(
    {
      ...validCart(),
      pricingState: {
        ...validCart().pricingState,
        promotions: [
          {
            ...validCart().pricingState.promotions[0]!,
            fixedPrice: { currency: "AUD", cents: 500 },
          },
        ],
      },
    },
    "SHARED_CART_INVALID",
  );
  expectCode(null, "SHARED_CART_INVALID");
});

test("快照映射器显式转换 kind/fixedPrice 到 mode/fixedPriceCents", () => {
  const canonical = normalizeSharedSaleCartV1(toSharedSaleCartV1(snapshot()));
  assert.equal(canonical.version, 1);
  assert.deepEqual(Object.keys(canonical), ["version", "pricingState"]);
  assert.equal(canonical.pricingState.promotions[0]?.fixedPriceCents, 500);
  assert.ok(!("fixedPrice" in canonical.pricingState.promotions[0]!));
  assert.deepEqual(canonical.pricingState.lines[0]?.discountState, {
    mode: "promotion",
    cents: 100,
    promotionIds: ["promo-1"],
  });
});

test("快照映射器不掩盖非法快照：open-item/退货行/非 sale mode 由 normalize 拒绝", () => {
  expectCode(
    toSharedSaleCartV1({
      ...snapshot(),
      lines: [{ ...snapshot().lines[0]!, basePriceSource: "open-item" }],
    }),
    "SHARED_CART_INVALID",
  );
  expectCode(
    toSharedSaleCartV1({
      ...snapshot(),
      lines: [{ ...snapshot().lines[0]!, kind: "return" }],
    }),
    "SHARED_CART_LINE_KIND_NOT_SALE",
  );
  expectCode(
    toSharedSaleCartV1({
      ...snapshot(),
      mode: "return",
    }),
    "SHARED_CART_MODE_NOT_SALE",
  );
});

test("decimal quantity/unitWeight 的 frozen wire roundtrip（0.5 / 0.25）", () => {
  const cart = normalizeSharedSaleCartV1({
    version: 1,
    pricingState: {
      revision: 8,
      mode: "sale",
      asOfIso: "2026-07-28T08:00:00.000Z",
      promotions: [
        {
          id: "promo-w",
          name: "Weighable",
          effectiveStartIso: "2026-07-01T00:00:00.000Z",
          effectiveEndIso: "2026-08-01T00:00:00.000Z",
          isExclusive: false,
          priority: 0,
          applyQuantity: 1,
          fixedPriceCents: 0,
          maxApplicationsPerOrder: null,
          products: [{ productCode: "P-W", unitWeight: 0.25 }],
        },
      ],
      lines: [
        {
          lineId: "line-w",
          productCode: "P-W",
          itemNumber: null,
          lookupCode: "W1",
          displayName: "Weighed item",
          quantity: 0.5,
          unitPriceCents: 501,
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
  const roundtrip = normalizeSharedSaleCartV1(
    JSON.parse(JSON.stringify(cart)) as unknown,
  );
  assert.deepEqual(roundtrip, cart);
  assert.equal(cart.pricingState.lines[0]?.quantity, 0.5);
  assert.equal(cart.pricingState.promotions[0]?.products[0]?.unitWeight, 0.25);
});

test("decimal quantity gross 按 half-away-from-zero 取整且以整数分校验折扣", () => {
  const weighedLine = (discountState: unknown) => ({
    ...validCart(),
    pricingState: {
      ...validCart().pricingState,
      lines: [
        {
          ...validCart().pricingState.lines[0]!,
          lineId: "line-w",
          productCode: "P-W",
          lookupCode: "W1",
          displayName: "Weighed item",
          quantity: 0.5,
          unitPriceCents: 501,
          discountState,
        },
      ],
    },
  });

  // 0.5 * 501 = 250.5 cents -> AwayFromZero 取整为 251；等额折扣可过，252 超 gross 拒绝。
  const exact = normalizeSharedSaleCartV1(
    weighedLine({ mode: "manual-amount", cents: 251 }),
  );
  assert.deepEqual(exact.pricingState.lines[0]?.discountState, {
    mode: "manual-amount",
    cents: 251,
  });
  expectCode(
    weighedLine({ mode: "manual-amount", cents: 252 }),
    "SHARED_CART_INVALID",
  );

  // 促销折扣同样以取整后的 gross 校验，且引用冻结促销。
  normalizeSharedSaleCartV1(
    weighedLine({
      mode: "promotion",
      cents: 251,
      promotionIds: ["promo-1"],
    }),
  );
  expectCode(
    weighedLine({
      mode: "promotion",
      cents: 252,
      promotionIds: ["promo-1"],
    }),
    "SHARED_CART_INVALID",
  );
});

test("decimal quantity gross 修复 JS double：0.29 × 50 必须为 15（C# decimal）", () => {
  // 0.29 * 50 在 JS double 中是 14.499999999999998，Math.round 会错给 14；
  // C# decimal 精确为 14.5，AwayFromZero 给 15。15 等额折扣必须可过，16 拒绝。
  const weighedLine = (discountState: unknown) => ({
    ...validCart(),
    pricingState: {
      ...validCart().pricingState,
      lines: [
        {
          ...validCart().pricingState.lines[0]!,
          lineId: "line-w-29",
          productCode: "P-W29",
          lookupCode: "W29",
          displayName: "Weighed 0.29",
          quantity: 0.29,
          unitPriceCents: 50,
          discountState,
        },
      ],
    },
  });

  normalizeSharedSaleCartV1(
    weighedLine({ mode: "manual-amount", cents: 15 }),
  );
  expectCode(
    weighedLine({ mode: "manual-amount", cents: 16 }),
    "SHARED_CART_INVALID",
  );
});

test("quantity/unitWeight 拒绝 NaN/Infinity 及越界值", () => {
  for (const quantity of [NaN, Infinity, -Infinity]) {
    expectCode(
      {
        ...validCart(),
        pricingState: {
          ...validCart().pricingState,
          lines: [{ ...validCart().pricingState.lines[0]!, quantity }],
        },
      },
      "SHARED_CART_INVALID",
    );
  }
  for (const unitWeight of [NaN, Infinity, -Infinity, -0.01]) {
    expectCode(
      {
        ...validCart(),
        pricingState: {
          ...validCart().pricingState,
          promotions: [
            {
              ...validCart().pricingState.promotions[0]!,
              products: [{ productCode: "P-PROMO", unitWeight }],
            },
          ],
        },
      },
      "SHARED_CART_INVALID",
    );
  }

  // 边界：quantity/unitWeight 恰为 1_000_000 接受，超界拒绝；0/负数量拒绝。
  normalizeSharedSaleCartV1({
    ...validCart(),
    pricingState: {
      ...validCart().pricingState,
      lines: [{ ...validCart().pricingState.lines[0]!, quantity: 1_000_000 }],
    },
  });
  normalizeSharedSaleCartV1({
    ...validCart(),
    pricingState: {
      ...validCart().pricingState,
      promotions: [
        {
          ...validCart().pricingState.promotions[0]!,
          products: [{ productCode: "P-PROMO", unitWeight: 1_000_000 }],
        },
      ],
    },
  });
  expectCode(
    {
      ...validCart(),
      pricingState: {
        ...validCart().pricingState,
        lines: [
          { ...validCart().pricingState.lines[0]!, quantity: 1_000_001 },
        ],
      },
    },
    "SHARED_CART_INVALID",
  );
  expectCode(
    {
      ...validCart(),
      pricingState: {
        ...validCart().pricingState,
        promotions: [
          {
            ...validCart().pricingState.promotions[0]!,
            products: [{ productCode: "P-PROMO", unitWeight: 1_000_001 }],
          },
        ],
      },
    },
    "SHARED_CART_INVALID",
  );
  for (const quantity of [0, -1]) {
    expectCode(
      {
        ...validCart(),
        pricingState: {
          ...validCart().pricingState,
          lines: [{ ...validCart().pricingState.lines[0]!, quantity }],
        },
      },
      "SHARED_CART_INVALID",
    );
  }
});

test("cents 上限：1e12 接受、1e12+1 拒绝（unitPrice/fixedPrice/折扣）", () => {
  const maxCents = 1_000_000_000_000;
  const line = (unitPriceCents: number, discountState: unknown) => ({
    ...validCart(),
    pricingState: {
      ...validCart().pricingState,
      promotions: [
        {
          ...validCart().pricingState.promotions[0]!,
          fixedPriceCents: unitPriceCents,
        },
      ],
      lines: [
        {
          ...validCart().pricingState.lines[0]!,
          unitPriceCents,
          quantity: 1,
          discountState,
        },
      ],
    },
  });

  normalizeSharedSaleCartV1(
    line(maxCents, { mode: "manual-amount", cents: maxCents }),
  );
  expectCode(line(maxCents + 1, { mode: "none" }), "SHARED_CART_INVALID");
  expectCode(
    line(maxCents, { mode: "manual-amount", cents: maxCents + 1 }),
    "SHARED_CART_INVALID",
  );
  // 促销折扣同样受 cents 上限约束，且引用冻结促销。
  normalizeSharedSaleCartV1(
    line(maxCents, {
      mode: "promotion",
      cents: maxCents,
      promotionIds: ["promo-1"],
    }),
  );
  expectCode(
    line(maxCents, {
      mode: "promotion",
      cents: maxCents + 1,
      promotionIds: ["promo-1"],
    }),
    "SHARED_CART_INVALID",
  );
});

test("单行 rounded gross 上限：MAX_SAFE_INTEGER 边界可接受、超限拒绝", () => {
  // 69_431 * 129_728_784_761 = 9_007_199_254_740_991 = Number.MAX_SAFE_INTEGER。
  const boundary = {
    ...validCart().pricingState.lines[0]!,
    lineId: "line-max-gross",
    quantity: 69_431,
    unitPriceCents: 129_728_784_761,
  };
  normalizeSharedSaleCartV1({
    ...validCart(),
    pricingState: { ...validCart().pricingState, lines: [boundary] },
  });

  // 单行超限：quantity 加 1 后 gross > MAX_SAFE_INTEGER，直接拒绝。
  expectCode(
    {
      ...validCart(),
      pricingState: {
        ...validCart().pricingState,
        lines: [{ ...boundary, quantity: 69_432 }],
      },
    },
    "SHARED_CART_INVALID",
  );
});

test("每行都安全但多行 rounded gross 合计超限拒绝", () => {
  // 每行 65_536 * 68_719_476_736 = 2^52（均 <= MAX_SAFE_INTEGER），
  // 两行合计 = 2^53 > MAX_SAFE_INTEGER；累计必须按行校验后安全相加。
  const safeLine = {
    ...validCart().pricingState.lines[0]!,
    lineId: "line-safe-gross",
    quantity: 65_536,
    unitPriceCents: 68_719_476_736,
  };
  expectCode(
    {
      ...validCart(),
      pricingState: {
        ...validCart().pricingState,
        lines: [
          { ...safeLine, lineId: "line-safe-gross-1" },
          { ...safeLine, lineId: "line-safe-gross-2" },
        ],
      },
    },
    "SHARED_CART_INVALID",
  );
});

test("集合上限：lines<=1000、promotions<=100、promotion.products<=100", () => {
  const manyLines = Array.from({ length: 1_000 }, (_, index) => ({
    ...validCart().pricingState.lines[0]!,
    lineId: `line-${index + 1}`,
  }));
  const manyPromotions = Array.from({ length: 100 }, (_, index) => ({
    ...validCart().pricingState.promotions[0]!,
    id: `promo-${index + 1}`,
  }));
  const manyProducts = Array.from({ length: 100 }, (_, index) => ({
    productCode: `P-${index + 1}`,
    unitWeight: 1,
  }));

  normalizeSharedSaleCartV1({
    ...validCart(),
    pricingState: {
      ...validCart().pricingState,
      promotions: manyPromotions,
      lines: manyLines,
    },
  });
  normalizeSharedSaleCartV1({
    ...validCart(),
    pricingState: {
      ...validCart().pricingState,
      promotions: [
        {
          ...validCart().pricingState.promotions[0]!,
          products: manyProducts,
        },
      ],
    },
  });

  expectCode(
    {
      ...validCart(),
      pricingState: {
        ...validCart().pricingState,
        lines: [...manyLines, { ...manyLines[0]!, lineId: "line-1001" }],
      },
    },
    "SHARED_CART_INVALID",
  );
  expectCode(
    {
      ...validCart(),
      pricingState: {
        ...validCart().pricingState,
        promotions: [
          ...manyPromotions,
          { ...manyPromotions[0]!, id: "promo-101" },
        ],
      },
    },
    "SHARED_CART_INVALID",
  );
  expectCode(
    {
      ...validCart(),
      pricingState: {
        ...validCart().pricingState,
        promotions: [
          {
            ...validCart().pricingState.promotions[0]!,
            products: [...manyProducts, { productCode: "P-101", unitWeight: 1 }],
          },
        ],
      },
    },
    "SHARED_CART_INVALID",
  );
});

test("字符串上限：code<=64、name<=200、reference<=128", () => {
  const longCode = "x".repeat(65);
  const longName = "x".repeat(201);
  const longReference = "x".repeat(129);
  const line0 = () => validCart().pricingState.lines[0]!;
  const promo0 = () => validCart().pricingState.promotions[0]!;

  for (const line of [
    { ...line0(), lineId: longCode },
    { ...line0(), productCode: longCode },
    { ...line0(), lookupCode: longCode },
    { ...line0(), itemNumber: longCode },
    { ...line0(), displayName: longName },
    { ...line0(), syncProvenance: { referenceCode: longReference, priceSource: 0 } },
  ]) {
    expectCode(
      {
        ...validCart(),
        pricingState: { ...validCart().pricingState, lines: [line] },
      },
      "SHARED_CART_INVALID",
    );
  }
  for (const promotion of [
    { ...promo0(), id: longCode },
    { ...promo0(), name: longName },
    { ...promo0(), products: [{ productCode: longCode, unitWeight: 1 }] },
  ]) {
    expectCode(
      {
        ...validCart(),
        pricingState: {
          ...validCart().pricingState,
          promotions: [promotion],
        },
      },
      "SHARED_CART_INVALID",
    );
  }
  expectCode(
    {
      ...validCart(),
      pricingState: {
        ...validCart().pricingState,
        lines: [
          {
            ...line0(),
            discountState: {
              mode: "promotion",
              cents: 1,
              promotionIds: [longCode],
            },
          },
        ],
      },
    },
    "SHARED_CART_INVALID",
  );
});

test("revision/priority/applyQuantity/maxApplications 上限 1_000_000", () => {
  const atLimit = 1_000_000;
  const overLimit = 1_000_001;
  const promo = () => validCart().pricingState.promotions[0]!;

  normalizeSharedSaleCartV1({
    ...validCart(),
    pricingState: {
      ...validCart().pricingState,
      revision: atLimit,
      promotions: [
        {
          ...promo(),
          priority: atLimit,
          applyQuantity: atLimit,
          maxApplicationsPerOrder: atLimit,
        },
      ],
    },
  });

  expectCode(
    {
      ...validCart(),
      pricingState: { ...validCart().pricingState, revision: overLimit },
    },
    "SHARED_CART_INVALID",
  );
  for (const promotion of [
    { ...promo(), priority: overLimit },
    { ...promo(), applyQuantity: overLimit },
    { ...promo(), maxApplicationsPerOrder: overLimit },
  ]) {
    expectCode(
      {
        ...validCart(),
        pricingState: {
          ...validCart().pricingState,
          promotions: [promotion],
        },
      },
      "SHARED_CART_INVALID",
    );
  }
});
