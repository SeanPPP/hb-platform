export interface SalesOrderRange {
  startDate: string;
  endDate: string;
}

export interface SalesOrderRangeInfo extends SalesOrderRange {
  dayCount: number;
}

/** 与后端 OrderType 枚举一致：-1 表示全部，其余对应订单 Status。 */
export type SalesOrderTypeFilter = -1 | 0 | 1 | 2 | 3 | 4;

export type SalesOrderSortDirection = "asc" | "desc";

export type SalesOrderScope = "all-stores" | "authorized-stores";

export interface SalesOrderFilters {
  range: SalesOrderRange;
  /** 空数组表示不限分店（在授权范围内）。 */
  branchCodes: string[];
  orderType: SalesOrderTypeFilter;
  sortDirection: SalesOrderSortDirection;
}

export interface SalesOrderMatchedProduct {
  productCode: string;
  /** 货号来自商品主档，主档没有对应商品时为空。 */
  itemNumber: string | null;
  productName: string | null;
  barcode: string | null;
  quantity: number;
}

export interface SalesOrderListItem {
  orderGuid: string;
  branchCode: string | null;
  branchName: string | null;
  deviceCode: string | null;
  orderTime: string | null;
  skuCount: number | null;
  /** POS 写入的 ItemCount 是明细行数。 */
  itemCount: number | null;
  /** 件数：明细数量之和，列表接口聚合返回。 */
  quantityTotal: number | null;
  totalAmount: number | null;
  discountAmount: number | null;
  actualAmount: number | null;
  status: number | null;
  matchedProducts: SalesOrderMatchedProduct[];
}

export interface SalesOrderListPage {
  items: SalesOrderListItem[];
  total: number;
  pageNumber: number;
  pageSize: number;
  scope: SalesOrderScope;
  range: SalesOrderRangeInfo;
  sortDirection: SalesOrderSortDirection;
}

export interface SalesOrderBranch {
  storeCode: string;
  storeName: string;
}

export interface SalesOrderBranchCatalog {
  scope: SalesOrderScope;
  branches: SalesOrderBranch[];
}

export interface SalesOrderLine {
  productCode: string | null;
  itemNumber: string | null;
  productName: string | null;
  productImage: string | null;
  quantity: number | null;
  unitPrice: number | null;
  discountAmount: number | null;
  actualAmount: number | null;
}

export interface SalesOrderPayment {
  paymentTime: string | null;
  paymentMethod: number | null;
  paymentMethodName: string | null;
  amount: number | null;
}

export interface SalesOrderDetail {
  order: Omit<SalesOrderListItem, "matchedProducts">;
  lines: SalesOrderLine[];
  payments: SalesOrderPayment[];
}

export interface SalesOrderQueryBody {
  startDate: string;
  endDate: string;
  branchCodes: string[];
  orderType: SalesOrderTypeFilter;
  keyword: string | null;
  sortDirection: SalesOrderSortDirection;
  pageNumber: number;
  pageSize: number;
}

export type SalesOrderRangeError = "format" | "order" | "tooLong";

export interface SalesOrderRangeValidation {
  ok: boolean;
  dayCount: number;
  reason?: SalesOrderRangeError;
  /** 超限时给出以结束日为锚点收敛后的合法起始日，供一键修复使用。 */
  clampedStartDate?: string;
}

export type SalesOrderRangePreset = 1 | 7 | 30;
