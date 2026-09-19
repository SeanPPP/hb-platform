export type PriceUpdateTaskStatus = "Pending" | "Completed" | "Cancelled";
export type PriceUpdateTaskKind = "PriceUpdate" | "LabelOnly";
export type PriceUpdateChangedField = "retailPrice" | "discountRate";
export type PriceUpdateCompletionMode =
  | "Printed"
  | "MarkedReplaced"
  | "KeptStorePrice"
  | "PriceAligned";
export type PriceUpdateHqSyncStatus =
  | "pending"
  | "processing"
  | "retrying"
  | "succeeded"
  | "blocked"
  | "superseded";
export type PriceUpdateLabelMode = "Printed" | "MarkedReplaced";

export interface StorePriceUpdateTask {
  id: number;
  storeCode: string;
  storeName: string | null;
  productCode: string;
  storeRetailPriceUuid: string | null;
  productName: string | null;
  itemNumber: string | null;
  barcode: string | null;
  productImage: string | null;
  status: PriceUpdateTaskStatus;
  kind: PriceUpdateTaskKind;
  changedFields: PriceUpdateChangedField[];
  /** 货架标签上的旧值。 */
  shelfRetailPrice: number | null;
  shelfDiscountRate: number | null;
  /** 分店现值。 */
  storeRetailPrice: number | null;
  storeDiscountRate: number | null;
  /** 仓库目标；折扣 null = 不比较分店折扣。 */
  targetRetailPrice: number | null;
  targetDiscountRate: number | null;
  initiatorName: string;
  initiatorSource: string;
  initiatorReference: string | null;
  initiatedAtUtc: string;
  changeCount: number;
  priceAppliedBy: string | null;
  priceAppliedAtUtc: string | null;
  completionMode: PriceUpdateCompletionMode | null;
  completedBy: string | null;
  completedAtUtc: string | null;
  labelPrintCount: number;
  hqSyncOperationId: string | null;
  hqSyncStatus: PriceUpdateHqSyncStatus | null;
}

export interface StorePriceUpdateTaskPage {
  items: StorePriceUpdateTask[];
  total: number;
  page: number;
  pageSize: number;
  pendingCount: number;
  pendingPriceUpdateCount: number;
  pendingLabelOnlyCount: number;
  completedCount: number;
  hqSyncEnabled: boolean;
}

export interface PriceUpdateTaskQuery {
  storeCode: string;
  status: "Pending" | "Completed";
  kind?: PriceUpdateTaskKind;
  keyword?: string;
  hqSyncFailedOnly?: boolean;
  page: number;
  pageSize: number;
}

export type PriceUpdateBatchResultCode =
  | "ok"
  | "not_found"
  | "not_pending"
  | "target_changed"
  | "not_applicable"
  | "failed";

export interface PriceUpdateBatchResultItem {
  taskId: number;
  success: boolean;
  /** 未识别的 code 原样保留，调用方按失败处理。 */
  code: PriceUpdateBatchResultCode | string;
  message: string | null;
  task: StorePriceUpdateTask | null;
}

export interface PriceUpdateBatchResult {
  items: PriceUpdateBatchResultItem[];
  successCount: number;
  failedCount: number;
  hqSyncEnabled: boolean;
  hqSyncSubmittedCount: number;
}

export interface PriceUpdateApplyItem {
  taskId: number;
  expectedTargetRetailPrice: number | null;
  expectedTargetDiscountRate: number | null;
}

export interface SyncTargetStore {
  storeCode: string;
  storeName: string;
  hasRecord: boolean;
  retailPrice: number | null;
  discountRate: number | null;
  isSpecialProduct: boolean;
}

export interface SyncTargetsResult {
  sourceStoreCode: string;
  productCode: string;
  sourceRetailPrice: number | null;
  sourceDiscountRate: number | null;
  sourcePurchasePrice: number | null;
  targets: SyncTargetStore[];
}

export interface SyncToOtherStoresRequest {
  productCode: string;
  sourceStoreCode: string;
  targetStoreCodes: string[];
  syncRetailPrice: boolean;
  syncDiscountRate: boolean;
  syncPurchasePrice: boolean;
}

export interface PriceNotificationPreview {
  affectedStores: number;
  skippedSpecialStores: number;
}

export type SuggestedDiscountSource = "WarehouseProducts" | "MobileWarehouse" | "BatchUpdate";
