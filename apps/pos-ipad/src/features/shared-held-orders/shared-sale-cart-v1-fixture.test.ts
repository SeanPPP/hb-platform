import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

import {
  SharedSaleCartValidationError,
  normalizeSharedSaleCartV1,
  type SharedSaleCartV1,
} from "@hb/pos-domain/features/shared-held-orders/shared-sale-cart-v1";

/**
 * 跨端 frozen wire fixture lane（iPad 侧）：
 * 与 WPF SharedHeldOrderCanonicalFixtureTests.cs / SharedHeldOrderFixtureContractTests.cs
 * 共用 test-fixtures/shared-held-orders/ 同一批 JSON。
 * 字节稳定判定：normalize(JSON.parse(fixture)) 再 JSON.stringify 必须逐字节还原 fixture。
 * fixture 文件带 POSIX 结尾换行，读取后统一 trimEnd()，两侧做同样的归一化。
 */

const FIXTURE_DIR = "../../../../../test-fixtures/shared-held-orders";
const FIXTURE_ROOT = resolve(
  dirname(fileURLToPath(import.meta.url)),
  FIXTURE_DIR,
);

function readFixture(name: string): string {
  return readFileSync(resolve(FIXTURE_ROOT, name), "utf8").trimEnd();
}

function wireRoundtripBytes(json: string): string {
  return JSON.stringify(normalizeSharedSaleCartV1(JSON.parse(json) as unknown));
}

function expectRejected(json: string): void {
  assert.throws(
    () => normalizeSharedSaleCartV1(JSON.parse(json) as unknown),
    (error: unknown) =>
      error instanceof SharedSaleCartValidationError &&
      error.code === "SHARED_CART_INVALID",
  );
}

test("canonical fixture：iPad strict parser/serializer 逐字节还原共享 JSON", () => {
  const fixture = readFixture("shared-sale-cart-v1.canonical.json");
  assert.equal(wireRoundtripBytes(fixture), fixture);
});

test("canonical fixture：decimal/AwayFromZero/manual/percent/promotion/provenance 语义齐备", () => {
  const cart: SharedSaleCartV1 = normalizeSharedSaleCartV1(
    JSON.parse(readFixture("shared-sale-cart-v1.canonical.json")) as unknown,
  );
  const lines = cart.pricingState.lines;

  // decimal 数量 + AwayFromZero：1.5 * 1003 = 1504.5 -> gross 1505，
  // manual-amount 1505 能通过恰好证明两端都用 half-away-from-zero 判定折扣上限。
  assert.equal(lines[0]?.quantity, 1.5);
  assert.equal(lines[0]?.unitPriceCents, 1003);
  assert.deepEqual(lines[0]?.discountState, { mode: "manual-amount", cents: 1505 });

  // catalog/manual provenance 逐字段冻结。
  assert.equal(lines[0]?.basePriceSource, "catalog");
  assert.deepEqual(lines[0]?.syncProvenance, {
    referenceCode: "REF-1",
    priceSource: 0,
  });
  assert.equal(lines[1]?.basePriceSource, "manual");
  assert.deepEqual(lines[1]?.syncProvenance, {
    referenceCode: null,
    priceSource: 1,
  });

  // manual-percent 与 promotion 折扣 union。
  assert.deepEqual(lines[1]?.discountState, {
    mode: "manual-percent",
    basisPoints: 2500,
  });
  assert.deepEqual(lines[2]?.discountState, {
    mode: "promotion",
    cents: 200,
    promotionIds: ["promo-bundle"],
  });

  // 冻结 promotion definition：标量 fixedPriceCents、null/整数 maxApplications、decimal unitWeight。
  const promotions = cart.pricingState.promotions;
  assert.equal(promotions.length, 2);
  assert.deepEqual(promotions[0], {
    id: "promo-bundle",
    name: "Buy 2 save 10",
    effectiveStartIso: "2026-07-01T00:00:00.000Z",
    effectiveEndIso: "2026-07-31T23:59:59.000Z",
    isExclusive: true,
    priority: 2,
    applyQuantity: 2,
    fixedPriceCents: 2000,
    maxApplicationsPerOrder: 1,
    products: [{ productCode: "P-BUNDLE", unitWeight: 1 }],
  });
  assert.deepEqual(promotions[1], {
    id: "promo-weight",
    name: "Weighable price",
    effectiveStartIso: "2026-07-01T00:00:00.000Z",
    effectiveEndIso: "2026-07-31T23:59:59.000Z",
    isExclusive: false,
    priority: 1,
    applyQuantity: 1,
    fixedPriceCents: 0,
    maxApplicationsPerOrder: null,
    products: [{ productCode: "P-WEIGHT", unitWeight: 0.25 }],
  });
});

test("reject fixtures：未知字段/跨店信封/summary/重复 id/越界一律 SHARED_CART_INVALID", () => {
  for (const name of [
    "shared-sale-cart-v1.reject-unknown-field.json",
    "shared-sale-cart-v1.reject-cross-store-envelope.json",
    "shared-sale-cart-v1.reject-summary-envelope.json",
    "shared-sale-cart-v1.reject-duplicate-promotion-id.json",
    "shared-sale-cart-v1.reject-duplicate-line-id.json",
    "shared-sale-cart-v1.reject-gross-overflow.json",
    "shared-sale-cart-v1.reject-unsafe-integer-cents.json",
  ]) {
    expectRejected(readFixture(name));
  }
});

test("near-max fixture：gross 恰低于 MAX_SAFE_INTEGER 被两端接受且字节稳定", () => {
  const fixture = readFixture("shared-sale-cart-v1.accept-near-max-safe-gross.json");
  const cart: SharedSaleCartV1 = normalizeSharedSaleCartV1(
    JSON.parse(fixture) as unknown,
  );
  assert.equal(cart.pricingState.lines[0]?.unitPriceCents, 9_007_199_254);
  assert.equal(cart.pricingState.lines[0]?.quantity, 1_000_000);
  // 1_000_000 * 9_007_199_254 = 9_007_199_254_000_000 < 2^53-1。
  assert.equal(wireRoundtripBytes(fixture), fixture);
});
