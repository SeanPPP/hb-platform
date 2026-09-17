export type PosOperationOutcome = "Succeeded" | "Denied" | "Failed";

export type PosOperationDeviceSystem = "Windows" | "iPadOS" | "Unknown";

export type PosOperationRangePreset =
  | "today"
  | "yesterday"
  | "last7Days"
  | "last30Days"
  | "custom";

/** 列表顶部的快捷过滤：结果三态 + 紧急覆盖，互斥单选。 */
export type PosOperationQuickFilter =
  | "all"
  | PosOperationOutcome
  | "emergencyOverride";

export interface PosOperationLogFilters {
  preset: PosOperationRangePreset;
  /** 仅 preset=custom 时生效，本地日期 YYYY-MM-DD。 */
  startDate: string;
  endDate: string;
  storeCode: string | null;
  cashierKeyword: string;
  deviceCode: string;
  deviceSystem: PosOperationDeviceSystem | null;
  operationType: string | null;
  outcome: PosOperationOutcome | null;
  emergencyOverrideOnly: boolean;
  offlineCachedOnly: boolean;
  productKeyword: string;
  orderGuid: string;
  keyword: string;
}

export interface PosOperationLogItem {
  eventId: string;
  occurredAtUtc: string;
  receivedAtUtc: string;
  operationType: string;
  outcome: PosOperationOutcome;
  cashierId: string | null;
  userGuid: string | null;
  cashierName: string | null;
  isOfflineCached: boolean;
  isEmergencyOverride: boolean;
  storeCode: string;
  deviceCode: string;
  deviceSystem: string | null;
  appVersion: string | null;
  orderGuid: string | null;
  receiptNumber: string | null;
  correlationId: string | null;
  traceId: string | null;
  paymentMethod: string | null;
  reasonCode: string | null;
  safeMessage: string | null;
  currencyCode: string;
  paymentAmount: number | null;
  beforeGross: number | null;
  afterGross: number | null;
  beforeDiscount: number | null;
  afterDiscount: number | null;
  beforeActual: number | null;
  afterActual: number | null;
  amountDelta: number | null;
  productCount: number;
  primaryProduct: string | null;
}

export interface PosOperationLogDetailItem {
  lineIndex: number;
  productCode: string | null;
  itemNumber: string | null;
  referenceCode: string | null;
  lookupCode: string | null;
  displayName: string | null;
  lineKind: string | null;
  beforeQuantity: number | null;
  afterQuantity: number | null;
  quantityDelta: number | null;
  beforeUnitPrice: number | null;
  afterUnitPrice: number | null;
  unitPriceDelta: number | null;
  beforeDiscountAmount: number | null;
  afterDiscountAmount: number | null;
  discountAmountDelta: number | null;
  beforeActualAmount: number | null;
  afterActualAmount: number | null;
  actualAmountDelta: number | null;
}

export interface PosOperationLogDetail extends PosOperationLogItem {
  propertiesJson: string | null;
  items: PosOperationLogDetailItem[];
}

export interface PosOperationLogPage {
  items: PosOperationLogItem[];
  total: number;
  pageNumber: number;
  pageSize: number;
}

export interface PosOperationLogSummary {
  total: number;
  succeeded: number;
  denied: number;
  failed: number;
  emergencyOverride: number;
  offlineCached: number;
}

export interface PosOperationLogDaySection {
  /** 本地日期 YYYY-MM-DD，作为分组 key。 */
  dayKey: string;
  relative: "today" | "yesterday" | null;
  /** 展示用 DD/MM/YYYY。 */
  dateLabel: string;
  items: PosOperationLogItem[];
}
