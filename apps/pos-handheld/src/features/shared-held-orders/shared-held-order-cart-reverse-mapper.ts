import {
  normalizeSharedSaleCartV1,
  type SharedSaleCartV1,
} from "@hb/pos-domain/features/shared-held-orders/shared-sale-cart-v1";
import {
  normalizeSharedSaleCart,
  normalizeSharedSaleCartV2,
  type SharedSaleCartPayload,
  type SharedSaleCartV2,
} from "./shared-sale-cart-v2";

import type {
  PricingCartLineState,
  PricingCartStateSnapshot,
  PricingDiscountState,
  PromotionDefinition,
  LineSyncProvenance,
} from "@/core/contracts";

/**
 * 冻结 wire SharedSaleCartV1 -> 可恢复 PricingCartStateSnapshot 的显式反向映射。
 * 输入必须已通过 normalizeSharedSaleCartV1（网络/仓库层负责），因此只做
 * 字段/结构转换；任何缺失或越界字段都抛 TypeError，绝不静默有损恢复。
 * 不计算金额：PricingCart.restore 会独立校验并重建金额。
 */
export function fromSharedSaleCartV1(
  input: SharedSaleCartV1,
): PricingCartStateSnapshot {
  const cart = normalizeSharedSaleCartV1(input);
  return fromNormalizedSharedSaleCart(cart, 0);
}

export function fromSharedSaleCartV2(
  input: SharedSaleCartV2,
): PricingCartStateSnapshot {
  const cart = normalizeSharedSaleCartV2(input);
  return fromNormalizedSharedSaleCart(
    cart,
    cart.pricingState.lines.map((line) => line.catalogDiscountBasisPoints),
  );
}

export function fromSharedSaleCart(
  input: SharedSaleCartPayload,
): PricingCartStateSnapshot {
  const normalized = normalizeSharedSaleCart(input);
  return normalized.version === 1
    ? fromSharedSaleCartV1(normalized)
    : fromSharedSaleCartV2(normalized);
}

function fromNormalizedSharedSaleCart(
  cart: SharedSaleCartPayload,
  catalogDiscountBasisPoints: number | readonly number[],
): PricingCartStateSnapshot {
  const pricing = cart.pricingState;
  return Object.freeze({
    revision: pricing.revision,
    mode: pricing.mode,
    asOfIso: pricing.asOfIso,
    promotions: pricing.promotions.map(toPromotion),
    lines: pricing.lines.map((line, index) =>
      toLine(
        line,
        typeof catalogDiscountBasisPoints === "number"
          ? catalogDiscountBasisPoints
          : catalogDiscountBasisPoints[index] ?? 0,
      ),
    ),
  });
}

function toPromotion(
  promotion: SharedSaleCartV1["pricingState"]["promotions"][number],
): PromotionDefinition {
  return Object.freeze({
    id: promotion.id,
    name: promotion.name,
    effectiveStartIso: promotion.effectiveStartIso,
    effectiveEndIso: promotion.effectiveEndIso,
    isExclusive: promotion.isExclusive,
    priority: promotion.priority,
    applyQuantity: promotion.applyQuantity,
    fixedPrice: Object.freeze({
      currency: "AUD" as const,
      cents: promotion.fixedPriceCents,
    }),
    maxApplicationsPerOrder: promotion.maxApplicationsPerOrder,
    products: promotion.products.map((product) =>
      Object.freeze({
        productCode: product.productCode,
        unitWeight: product.unitWeight,
      }),
    ),
  });
}

function toLine(
  line: SharedSaleCartPayload["pricingState"]["lines"][number],
  catalogDiscountBasisPoints: number,
): PricingCartLineState {
  return Object.freeze({
    lineId: line.lineId,
    productCode: line.productCode,
    itemNumber: line.itemNumber,
    lookupCode: line.lookupCode,
    displayName: line.displayName,
    quantity: line.quantity,
    unitPriceCents: line.unitPriceCents,
    basePriceSource: line.basePriceSource,
    catalogDiscountBasisPoints,
    ...(line.syncProvenance
      ? { syncProvenance: toSyncProvenance(line.syncProvenance) }
      : {}),
    kind: line.kind,
    returnSourceKey: line.returnSourceKey,
    originalOrderGuid: line.originalOrderGuid,
    originalOrderDetailGuid: line.originalOrderDetailGuid,
    discountState: toDiscountState(line.discountState),
  });
}

function toSyncProvenance(
  provenance: NonNullable<SharedSaleCartV1["pricingState"]["lines"][number]["syncProvenance"]>,
): LineSyncProvenance {
  return Object.freeze({
    referenceCode: provenance.referenceCode,
    priceSource: provenance.priceSource,
  });
}

function toDiscountState(
  discount: SharedSaleCartV1["pricingState"]["lines"][number]["discountState"],
): PricingDiscountState {
  switch (discount.mode) {
    case "none":
      return Object.freeze({ kind: "none" });
    case "manual-amount":
      return Object.freeze({ kind: "manual-amount", cents: discount.cents });
    case "manual-percent":
      return Object.freeze({
        kind: "manual-percent",
        basisPoints: discount.basisPoints,
      });
    case "promotion":
      return Object.freeze({
        kind: "promotion",
        cents: discount.cents,
        promotionIds: discount.promotionIds,
      });
  }
}
