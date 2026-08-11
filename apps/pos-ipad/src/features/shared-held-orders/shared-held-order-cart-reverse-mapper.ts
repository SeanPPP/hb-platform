import { normalizeSharedSaleCartV1, type SharedSaleCartV1 } from "./shared-sale-cart-v1";

import type {
  PricingCartStateSnapshot,
  PromotionDefinition,
  PricingCartLineState,
  PricingDiscountState,
 LineSyncProvenance } from "@/core/contracts";

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
  const pricing = cart.pricingState;
  return Object.freeze({
    revision: pricing.revision,
    mode: pricing.mode,
    asOfIso: pricing.asOfIso,
    promotions: pricing.promotions.map(toPromotion),
    lines: pricing.lines.map(toLine),
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
  line: SharedSaleCartV1["pricingState"]["lines"][number],
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
