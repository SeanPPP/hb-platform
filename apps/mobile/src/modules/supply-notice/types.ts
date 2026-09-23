export type SupplyPlan = "WillRestock" | "Undecided" | "Seasonal" | "Discontinued";
export type SupplyExpectedPrecision = "Unknown" | "Day" | "Range" | "Month";

/** 仓库端录入的供货说明；日期为 YYYY-MM-DD。 */
export interface SupplyNoticeInput {
  supplyPlan: SupplyPlan;
  expectedPrecision: SupplyExpectedPrecision;
  expectedFrom?: string | null;
  expectedTo?: string | null;
  storeFacingNote?: string | null;
  internalNote?: string | null;
}

/** 分店端看到的商品供货状态；不含内部备注。 */
export interface StoreSupplyStatus {
  productCode: string;
  itemNumber: string | null;
  barcode: string | null;
  productName: string | null;
  productImage: string | null;
  /** 当前是否可订货；关注列表里为 true 表示“已恢复订货”。 */
  isOrderable: boolean;
  supplyPlan: SupplyPlan;
  hasNotice: boolean;
  expectedFrom: string | null;
  expectedTo: string | null;
  expectedPrecision: SupplyExpectedPrecision;
  isOverdue: boolean;
  storeFacingNote: string | null;
  noticeUpdatedAtUtc: string | null;
  isWatching: boolean;
}

export interface StoreSupplyWatchSummary {
  watchingCount: number;
  restockedCount: number;
}
