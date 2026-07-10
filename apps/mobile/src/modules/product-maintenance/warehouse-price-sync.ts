import type {
  StorePriceEditable,
  WarehousePriceSyncRequest,
  WarehousePriceSyncResult,
  WarehousePriceSyncStatus,
} from "./types";

const WAREHOUSE_SYNC_SUPPLIER_CODE = "200";
const WAREHOUSE_SYNC_STATUSES = new Set<WarehousePriceSyncStatus>([
  "not_applicable",
  "missing_source",
  "synced",
  "confirmation_required",
]);

type WarehousePriceSyncPhase = "idle" | "previewing" | "confirmation" | "confirming";

export interface WarehousePriceSyncState {
  phase: WarehousePriceSyncPhase;
  snapshot: WarehousePriceSyncResult | null;
  errorMessage: string | null;
}

export type WarehousePriceSyncEvent =
  | { type: "preview_started" }
  | { type: "preview_succeeded"; snapshot: WarehousePriceSyncResult }
  | { type: "preview_failed"; message: string }
  | { type: "confirm_started" }
  | { type: "confirm_succeeded"; snapshot: WarehousePriceSyncResult }
  | { type: "confirm_failed"; message: string }
  | { type: "conflict_received"; snapshot: WarehousePriceSyncResult; message: string }
  | { type: "cancelled" }
  | { type: "reset" };

export type WarehousePriceLookupOrigin = "scan" | "manual" | "refresh" | "deep-link";
export type WarehousePricePrintStage =
  | "preview_succeeded"
  | "confirmation_succeeded"
  | "cancelled"
  | "failed";

export type WarehousePriceSyncApplicability =
  | "not_supplier"
  | "missing_store_price"
  | "sync";

export type WarehousePriceConfirmationFeedback =
  | "no_update"
  | "retail_updated"
  | "retail_updated_print_failed";

export interface ProductQueryInteractionGateInput {
  loading: boolean;
  lookupVisible: boolean;
  lookupSelectionOpen: boolean;
  autoPricingVisible: boolean;
  autoPricingSaving: boolean;
  warehouseLocked: boolean;
  requestInFlight: boolean;
  storeSelectionInFlight: boolean;
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return value && typeof value === "object" ? (value as Record<string, unknown>) : null;
}

function readField(record: Record<string, unknown>, camel: string, pascal: string): unknown {
  return record[camel] ?? record[pascal];
}

function unwrapWarehousePricePayload(payload: unknown): unknown {
  let current = payload;
  for (let depth = 0; depth < 3; depth += 1) {
    const record = asRecord(current);
    if (!record) {
      break;
    }

    const nestedData = record.data ?? record.Data;
    const isEnvelope =
      nestedData !== undefined &&
      ("success" in record ||
        "Success" in record ||
        "isSuccess" in record ||
        "IsSuccess" in record ||
        "message" in record ||
        "Message" in record);
    if (!isEnvelope) {
      break;
    }
    current = nestedData;
  }
  return current;
}

function toFiniteNumber(value: unknown): number | null {
  if (typeof value === "number" && Number.isFinite(value)) {
    return value;
  }
  if (typeof value === "string" && value.trim()) {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : null;
  }
  return null;
}

export function normalizeWarehouseMoney(value: unknown): number | null {
  const numeric = toFiniteNumber(value);
  if (numeric == null) {
    return null;
  }
  return Math.round((numeric + Number.EPSILON) * 100) / 100;
}

export function normalizeWarehouseDiscountRate(value: unknown): number | null {
  const numeric = toFiniteNumber(value);
  if (numeric == null || numeric < 0) {
    return null;
  }

  const ratio = numeric <= 1 ? numeric : numeric <= 100 ? numeric / 100 : null;
  return ratio == null ? null : Math.round(ratio * 10000) / 10000;
}

export function calculateDiscountedRetailPrice(
  retailPrice: unknown,
  discountRate: unknown
): number | null {
  const retail = normalizeWarehouseMoney(retailPrice);
  const discount = normalizeWarehouseDiscountRate(discountRate);
  if (retail == null) {
    return null;
  }
  return normalizeWarehouseMoney(retail * (1 - (discount ?? 0)));
}

export function formatWarehouseDiscountRate(value: unknown): string {
  const discount = normalizeWarehouseDiscountRate(value) ?? 0;
  const percent = discount * 100;
  return `${Number.isInteger(percent) ? percent : percent.toFixed(2).replace(/\.?0+$/, "")}%`;
}

export function isWarehousePriceSyncSupplier(localSupplierCode: unknown): boolean {
  return (
    typeof localSupplierCode === "string" &&
    localSupplierCode.trim() === WAREHOUSE_SYNC_SUPPLIER_CODE
  );
}

export function getWarehousePriceSyncApplicability(
  localSupplierCode: unknown,
  storePriceUuid: unknown
): WarehousePriceSyncApplicability {
  if (!isWarehousePriceSyncSupplier(localSupplierCode)) {
    return "not_supplier";
  }
  return typeof storePriceUuid === "string" && storePriceUuid.trim()
    ? "sync"
    : "missing_store_price";
}

function normalizeStatus(value: unknown): WarehousePriceSyncStatus {
  return typeof value === "string" && WAREHOUSE_SYNC_STATUSES.has(value as WarehousePriceSyncStatus)
    ? (value as WarehousePriceSyncStatus)
    : "missing_source";
}

function normalizeStorePrice(payload: unknown): StorePriceEditable | null {
  const data = asRecord(payload);
  if (!data) {
    return null;
  }

  return {
    uuid: String(readField(data, "uuid", "Uuid") ?? ""),
    storeCode: (readField(data, "storeCode", "StoreCode") ?? null) as string | null,
    storeName: (readField(data, "storeName", "StoreName") ?? null) as string | null,
    productCode: (readField(data, "productCode", "ProductCode") ?? null) as string | null,
    storeProductCode: (readField(data, "storeProductCode", "StoreProductCode") ?? null) as
      | string
      | null,
    supplierCode: (readField(data, "supplierCode", "SupplierCode") ?? null) as string | null,
    purchasePrice: normalizeWarehouseMoney(readField(data, "purchasePrice", "PurchasePrice")),
    retailPrice: normalizeWarehouseMoney(readField(data, "retailPrice", "RetailPrice")),
    discountRate: normalizeWarehouseDiscountRate(
      readField(data, "discountRate", "DiscountRate")
    ),
    isAutoPricing: Boolean(readField(data, "isAutoPricing", "IsAutoPricing")),
    isSpecialProduct: Boolean(readField(data, "isSpecialProduct", "IsSpecialProduct")),
    isActive: Boolean(readField(data, "isActive", "IsActive")),
    rate: toFiniteNumber(readField(data, "rate", "Rate")),
    strategySourceLabel: (readField(data, "strategySourceLabel", "StrategySourceLabel") ??
      null) as string | null,
    strategyRuleLabel: (readField(data, "strategyRuleLabel", "StrategyRuleLabel") ?? null) as
      | string
      | null,
  };
}

export function normalizeWarehousePriceSyncResponse(payload: unknown): WarehousePriceSyncResult {
  const data = asRecord(unwrapWarehousePricePayload(payload)) ?? {};
  return {
    status: normalizeStatus(readField(data, "status", "Status")),
    purchaseUpdated: Boolean(readField(data, "purchaseUpdated", "PurchaseUpdated")),
    retailUpdated: Boolean(readField(data, "retailUpdated", "RetailUpdated")),
    retailConfirmationRequired: Boolean(
      readField(data, "retailConfirmationRequired", "RetailConfirmationRequired")
    ),
    storePrice: normalizeStorePrice(readField(data, "storePrice", "StorePrice")),
    warehousePurchasePrice: normalizeWarehouseMoney(
      readField(data, "warehousePurchasePrice", "WarehousePurchasePrice")
    ),
    warehouseRetailPrice: normalizeWarehouseMoney(
      readField(data, "warehouseRetailPrice", "WarehouseRetailPrice")
    ),
    previousStorePurchasePrice: normalizeWarehouseMoney(
      readField(data, "previousStorePurchasePrice", "PreviousStorePurchasePrice")
    ),
    previousStoreRetailPrice: normalizeWarehouseMoney(
      readField(data, "previousStoreRetailPrice", "PreviousStoreRetailPrice")
    ),
    discountRate: normalizeWarehouseDiscountRate(readField(data, "discountRate", "DiscountRate")),
    previousDiscountedRetailPrice: normalizeWarehouseMoney(
      readField(data, "previousDiscountedRetailPrice", "PreviousDiscountedRetailPrice")
    ),
    newDiscountedRetailPrice: normalizeWarehouseMoney(
      readField(data, "newDiscountedRetailPrice", "NewDiscountedRetailPrice")
    ),
  };
}

export function buildWarehousePriceSyncRequest(
  snapshot: WarehousePriceSyncResult,
  confirmRetailPrice: boolean
): WarehousePriceSyncRequest {
  return {
    confirmRetailPrice,
    expectedWarehousePurchasePrice: snapshot.warehousePurchasePrice,
    expectedWarehouseRetailPrice: snapshot.warehouseRetailPrice,
    // 首轮调用可能已自动更新进货价，确认请求必须使用响应中的最新分店快照。
    expectedStorePurchasePrice: snapshot.storePrice
      ? snapshot.storePrice.purchasePrice
      : snapshot.previousStorePurchasePrice,
    expectedStoreRetailPrice: snapshot.storePrice
      ? snapshot.storePrice.retailPrice
      : snapshot.previousStoreRetailPrice,
    expectedDiscountRate: snapshot.storePrice
      ? snapshot.storePrice.discountRate
      : snapshot.discountRate,
  };
}

export function isWarehousePriceConflictSnapshotComplete(
  snapshot: WarehousePriceSyncResult
): boolean {
  return (
    Boolean(snapshot.storePrice?.uuid) &&
    snapshot.warehouseRetailPrice != null
  );
}

export function extractWarehousePriceSyncConflict(error: unknown): WarehousePriceSyncResult | null {
  const errorRecord = asRecord(error);
  const response = asRecord(errorRecord?.response);
  if (!response || Number(response.status) !== 409) {
    return null;
  }

  const payload = asRecord(response.data ?? response.Data);
  if (!payload) {
    return null;
  }

  const code = payload.errorCode ?? payload.ErrorCode ?? payload.code ?? payload.Code;
  if (code !== "PRICE_VERSION_CONFLICT") {
    return null;
  }

  const snapshot = payload.data ?? payload.Data;
  return asRecord(snapshot) ? normalizeWarehousePriceSyncResponse(snapshot) : null;
}

export function createWarehousePriceSyncState(): WarehousePriceSyncState {
  return { phase: "idle", snapshot: null, errorMessage: null };
}

export function reduceWarehousePriceSyncState(
  state: WarehousePriceSyncState,
  event: WarehousePriceSyncEvent
): WarehousePriceSyncState {
  switch (event.type) {
    case "preview_started":
      return { phase: "previewing", snapshot: null, errorMessage: null };
    case "preview_succeeded":
      return event.snapshot.retailConfirmationRequired
        ? { phase: "confirmation", snapshot: event.snapshot, errorMessage: null }
        : { phase: "idle", snapshot: event.snapshot, errorMessage: null };
    case "preview_failed":
      return { phase: "idle", snapshot: null, errorMessage: event.message };
    case "confirm_started":
      return { ...state, phase: "confirming", errorMessage: null };
    case "confirm_succeeded":
      return { phase: "idle", snapshot: event.snapshot, errorMessage: null };
    case "confirm_failed":
      return { ...state, phase: "confirmation", errorMessage: event.message };
    case "conflict_received":
      return {
        phase: "confirmation",
        snapshot: event.snapshot,
        errorMessage: event.message,
      };
    case "cancelled":
    case "reset":
      return createWarehousePriceSyncState();
  }
}

export function isWarehousePriceInteractionLocked(state: WarehousePriceSyncState): boolean {
  return state.phase !== "idle";
}

export function isProductQueryInteractionBlocked(
  input: ProductQueryInteractionGateInput
): boolean {
  return (
    input.loading ||
    input.lookupVisible ||
    input.lookupSelectionOpen ||
    input.autoPricingVisible ||
    input.autoPricingSaving ||
    input.warehouseLocked ||
    input.requestInFlight ||
    input.storeSelectionInFlight
  );
}

export function shouldAutoPrintWarehousePrice(input: {
  lookupOrigin: WarehousePriceLookupOrigin;
  stage: WarehousePricePrintStage;
  snapshot: WarehousePriceSyncResult | null;
  alreadyPrinted: boolean;
}): boolean {
  if (input.lookupOrigin !== "scan" || input.alreadyPrinted || !input.snapshot) {
    return false;
  }

  if (input.snapshot.status !== "synced" || input.snapshot.retailConfirmationRequired) {
    return false;
  }

  if (input.stage === "preview_succeeded") {
    return true;
  }

  if (input.stage === "confirmation_succeeded") {
    return input.snapshot.retailUpdated;
  }

  return false;
}

export function resolveWarehousePriceConfirmationFeedback(input: {
  retailUpdated: boolean;
  printAttempted: boolean;
  labelPrinted: boolean;
}): WarehousePriceConfirmationFeedback {
  if (!input.retailUpdated) {
    return "no_update";
  }
  if (input.printAttempted && !input.labelPrinted) {
    return "retail_updated_print_failed";
  }
  return "retail_updated";
}
