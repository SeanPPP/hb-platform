export interface SeasonalRange {
  startDate: string;
  endDate: string;
}

/** 进货与销售各自一个区间；两者可以不同，理论存货 = 进货区间累计进货 − 销售区间累计销量。 */
export interface SeasonalRanges {
  inbound: SeasonalRange;
  sales: SeasonalRange;
}

export interface SeasonalCandidate {
  productCode: string;
  productName: string;
  itemNumber: string | null;
  barcode: string | null;
  productImage: string | null;
  theoreticalStock: number;
}

export interface SeasonalLookupResult {
  matchMode: "barcode" | "itemNumber";
  truncated: boolean;
  ranges: SeasonalRanges;
  items: SeasonalCandidate[];
}

export interface SeasonalInboundRecord {
  id: string;
  date: string;
  documentNo: string;
  quantity: number;
}

export interface SeasonalDailySales {
  date: string;
  quantity: number;
  amount: number;
}

export interface SeasonalBranchStock {
  storeCode: string;
  storeName: string;
  inboundQuantity: number;
  salesQuantity: number;
  theoreticalStock: number;
}

export interface SeasonalProductInsight {
  generatedAt: string;
  store: { storeCode: string; storeName: string };
  product: {
    productCode: string;
    productName: string;
    itemNumber: string | null;
    barcode: string | null;
    productImage: string | null;
    sourceType: "local" | "warehouse";
  };
  ranges: SeasonalRanges;
  inbound: {
    quantity: number;
    documentCount: number;
    records: SeasonalInboundRecord[];
  };
  sales: {
    quantity: number;
    amount: number;
    daily: SeasonalDailySales[];
  };
  theoreticalStock: number;
  branches: SeasonalBranchStock[];
}

export type SeasonalTrendMode = "day" | "week";

export type SeasonalRangeError = "format" | "order" | "tooLong";

export interface DailyTrendPoint {
  /** 距销售区间起点的天数，用于在真实日期轴上定位；无记录的日期不出现。 */
  index: number;
  date: string;
  quantity: number;
}

export interface WeeklyTrendPoint {
  /** 该周在销售区间内的第一天。 */
  startDate: string;
  endDate: string;
  quantity: number;
  inboundQuantity: number;
  /** 首尾两周可能被区间截断，天数不足 7 天。 */
  partial: boolean;
}
