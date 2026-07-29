import type {
  CartLineKind,
  CartMode,
  PriceSource,
} from "./cart";
import type { LineSyncProvenance } from "./line-sync-provenance";
import type { Money } from "./money";

/**
 * 购物车可恢复状态是跨 feature / persistence 的冻结合同。
 * 它保留促销时点和手工百分比，不能用只面向显示的 CartSnapshot 代替。
 */
export type PromotionProduct = Readonly<{
  productCode: string;
  unitWeight: number;
}>;

export type PromotionDefinition = Readonly<{
  id: string;
  name: string;
  effectiveStartIso: string;
  effectiveEndIso: string;
  isExclusive: boolean;
  priority: number;
  applyQuantity: number;
  fixedPrice: Money;
  maxApplicationsPerOrder: number | null;
  products: readonly PromotionProduct[];
}>;

export type PricingDiscountState =
  | Readonly<{ kind: "none" }>
  | Readonly<{ kind: "manual-amount"; cents: number }>
  | Readonly<{ kind: "manual-percent"; basisPoints: number }>
  | Readonly<{
      kind: "promotion";
      cents: number;
      promotionIds: readonly string[];
    }>;

export type PricingCartLineState = Readonly<{
  lineId: string;
  productCode: string;
  itemNumber: string | null;
  lookupCode: string;
  displayName: string;
  quantity: number;
  unitPriceCents: number;
  basePriceSource: Exclude<PriceSource, "promotion">;
  /**
   * 可恢复快照必须冻结加入购物车时的服务端售卖身份。
   * 仅旧版快照允许缺失；恢复后不能自行按当前目录补值。
   */
  syncProvenance?: LineSyncProvenance;
  kind: CartLineKind;
  returnSourceKey: string | null;
  originalOrderGuid: string | null;
  originalOrderDetailGuid: string | null;
  discountState: PricingDiscountState;
}>;

export type PricingCartStateSnapshot = Readonly<{
  revision: number;
  mode: CartMode;
  asOfIso: string;
  promotions: readonly PromotionDefinition[];
  lines: readonly PricingCartLineState[];
}>;
