export interface WarehouseInsightRange {
  startDate: string;
  endDate: string;
}

export interface WarehouseInsightRangeInfo extends WarehouseInsightRange {
  dayCount: number;
}

export interface WarehouseInsightProduct {
  productCode: string;
  productName: string;
  itemNumber: string | null;
  barcode: string | null;
  productImage: string | null;
  supplierCode: string | null;
  supplierName: string | null;
  locationCode: string | null;
  stockQuantity: number | null;
}

export interface WarehouseInsightTotals {
  inboundQuantity: number;
  containerCount: number;
  inTransitQuantity: number;
  inTransitContainerCount: number;
  orderedQuantity: number;
  orderedStoreCount: number;
  orderDocumentCount: number;
  shippedQuantity: number;
  shippedStoreCount: number;
  shipmentDocumentCount: number;
  pendingQuantity: number;
  pendingStoreCount: number;
  salesQuantity: number;
  salesAmount: number;
  salesStoreCount: number;
}

export interface WarehouseInsightBranch {
  storeCode: string;
  storeName: string;
  orderedQuantity: number;
  shippedQuantity: number;
  pendingQuantity: number;
  salesQuantity: number;
  salesAmount: number;
  /** 售罄率 = 销售 / 发货；未发货时为 null，不能当作 0 展示。 */
  sellThroughRate: number | null;
}

export interface WarehouseInsightContainer {
  containerNumber: string;
  arrivalDate: string;
  isEstimatedArrival: boolean;
  quantity: number;
  pieces: number;
  status: string;
}

export interface WarehouseInsightMovement {
  documentNo: string;
  storeCode: string;
  storeName: string;
  date: string;
  quantity: number;
}

export interface WarehouseInsightDailySales {
  date: string;
  quantity: number;
  amount: number;
}

export interface WarehouseProductInsight {
  range: WarehouseInsightRangeInfo;
  /** 货柜进货的独立区间：固定最近一年，不跟随 range。 */
  inboundRange: WarehouseInsightRangeInfo;
  generatedAt: string;
  salesStatisticLastUpdatedAt: string | null;
  scope: "all-stores" | "authorized-stores";
  product: WarehouseInsightProduct;
  totals: WarehouseInsightTotals;
  branches: WarehouseInsightBranch[];
  containers: WarehouseInsightContainer[];
  orders: WarehouseInsightMovement[];
  shipments: WarehouseInsightMovement[];
  dailySales: WarehouseInsightDailySales[];
}

export type WarehouseInsightRangePreset = 7 | 30 | 90 | 180 | 365;

export type WarehouseInsightRangeError = "format" | "order" | "tooLong";

export interface WarehouseInsightRangeValidation {
  ok: boolean;
  dayCount: number;
  reason?: WarehouseInsightRangeError;
  /** 超限时给出以结束日为锚点收敛后的合法起始日，供一键修复使用。 */
  clampedStartDate?: string;
}

export type WarehouseInsightTab =
  | "containers"
  | "orders"
  | "shipments"
  | "sales";
