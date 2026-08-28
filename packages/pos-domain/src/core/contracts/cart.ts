import type { LineSyncProvenance } from "./line-sync-provenance";
import type { Money } from "./money";

export type CartMode = "sale" | "return" | "installment";
export type PriceSource = "catalog" | "promotion" | "manual" | "open-item";
export type CartLineKind = "sale" | "return";

export type CartLine = Readonly<{
  lineId: string;
  productCode: string;
  itemNumber: string | null;
  lookupCode: string;
  displayName: string;
  quantity: string;
  unitPrice: Money;
  discount: Money;
  actualAmount: Money;
  priceSource: PriceSource;
  /**
   * M15 之前的本地记录可能没有该值；所有新交易必须在持久化前提供。
   */
  syncProvenance?: LineSyncProvenance;
  kind: CartLineKind;
  returnSourceKey: string | null;
  originalOrderGuid: string | null;
  originalOrderDetailGuid: string | null;
}>;

export type CartSnapshot = Readonly<{
  revision: number;
  mode: CartMode;
  lines: readonly CartLine[];
  subtotal: Money;
  discount: Money;
  actualAmount: Money;
}>;

export interface CartPricingPort {
  price(snapshot: CartSnapshot): Promise<CartSnapshot>;
}
