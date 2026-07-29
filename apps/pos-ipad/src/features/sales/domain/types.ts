import type {
  CartLineKind,
  CartMode,
  CartSnapshot,
  LineSyncProvenance,
  Money,
  PriceSource,
  PricingCartStateSnapshot,
  PromotionDefinition,
} from "../../../core/contracts";

export type {
  PricingCartLineState,
  PricingCartStateSnapshot,
  PricingDiscountState,
  PromotionDefinition,
  PromotionProduct,
} from "../../../core/contracts";

export const QUICK_DISCOUNT_BASIS_POINTS = [
  1_000,
  2_000,
  3_000,
  4_000,
  5_000,
] as const;

export type QuickDiscountBasisPoints =
  (typeof QUICK_DISCOUNT_BASIS_POINTS)[number];

export type AddCartItemInput = Readonly<{
  lineId: string;
  productCode: string;
  itemNumber: string | null;
  lookupCode: string;
  displayName: string;
  quantity?: number;
  unitPrice: Money;
  syncProvenance: LineSyncProvenance;
  priceSource?: Exclude<PriceSource, "promotion" | "open-item">;
  kind?: CartLineKind;
  returnSourceKey?: string | null;
  originalOrderGuid?: string | null;
  originalOrderDetailGuid?: string | null;
}>;

export type AddOpenItemInput = Readonly<{
  lineId: string;
  productCode: string;
  itemNumber: string | null;
  lookupCode?: string;
  displayName: string;
  quantity?: number;
  unitPrice: Money;
  syncProvenance: LineSyncProvenance;
}>;

export type PricingCartOptions = Readonly<{
  mode?: CartMode;
  asOfIso?: string;
  promotions?: readonly PromotionDefinition[];
}>;

export type PricingCartResult = Readonly<{
  state: PricingCartStateSnapshot;
  cart: CartSnapshot;
}>;

export type CashSettlement = Readonly<{
  cashDue: Money;
  normalizedCashTendered: Money;
  change: Money;
  roundingAdjustment: Money;
}>;
