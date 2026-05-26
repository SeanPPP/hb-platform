import type {
  PagedResult,
  StoreVoucher,
  StoreVoucherDetail,
  StoreVoucherFilters,
  StoreVoucherLedgerItem,
  StoreVoucherRelatedOrder,
} from "@/modules/store-vouchers/types";

const BASE_PATH = "/react/v1/store-vouchers";
const LIST_PAGE_SIZES = [20, 50, 100] as const;

async function getApiClient() {
  const { apiClient } = await import("@/shared/api/client");
  return apiClient;
}

function pick(raw: Record<string, unknown>, ...keys: string[]) {
  for (const key of keys) {
    if (raw[key] !== undefined && raw[key] !== null) {
      return raw[key];
    }
  }
  return undefined;
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return value && typeof value === "object" && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null;
}

function asString(value: unknown, fallback = ""): string {
  if (typeof value === "string") {
    return value;
  }
  if (typeof value === "number" && Number.isFinite(value)) {
    return String(value);
  }
  return fallback;
}

function asNullableNumber(value: unknown): number | null {
  if (value === null || value === undefined) {
    return null;
  }
  if (typeof value === "string" && !value.trim()) {
    return null;
  }
  const parsed = typeof value === "string" ? Number(value) : value;
  return typeof parsed === "number" && Number.isFinite(parsed) ? parsed : null;
}

function asNumber(value: unknown, fallback = 0): number {
  const parsed = asNullableNumber(value);
  return parsed == null ? fallback : parsed;
}

function asNullableInt(value: unknown): number | null {
  const parsed = asNullableNumber(value);
  return parsed == null ? null : Math.trunc(parsed);
}

function trimText(value?: string) {
  const trimmed = value?.trim();
  return trimmed ? trimmed : undefined;
}

function normalizePage(page?: number) {
  return page && Number.isFinite(page) && page > 0 ? Math.trunc(page) : 1;
}

function normalizePageSize(requested?: number) {
  return LIST_PAGE_SIZES.includes(requested as (typeof LIST_PAGE_SIZES)[number]) ? requested! : 20;
}

function getArray(raw: Record<string, unknown>, ...keys: string[]) {
  const value = pick(raw, ...keys);
  return Array.isArray(value) ? value : [];
}

function unwrapListPayload(payload: unknown): Record<string, unknown> {
  const root = asRecord(payload) ?? {};
  const data = asRecord(pick(root, "data", "Data"));
  return data ?? root;
}

function normalizeVoucherStatus(value: unknown) {
  if (typeof value === "string") {
    const trimmed = value.trim();
    if (!trimmed) {
      return null;
    }
    const numeric = Number(trimmed);
    return Number.isFinite(numeric) ? numeric : trimmed;
  }
  if (typeof value === "number" && Number.isFinite(value)) {
    return value;
  }
  return null;
}

function normalizeLedgerAction(raw: Record<string, unknown>): "issued" | "used" {
  const action = asString(pick(raw, "action", "Action", "ledgerAction", "LedgerAction")).toLowerCase();
  if (action === "issued" || action === "issue" || action === "create" || action === "created") {
    return "issued";
  }
  if (action === "used" || action === "use" || action === "consume" || action === "redeemed") {
    return "used";
  }

  const kind = asString(pick(raw, "type", "Type", "eventType", "EventType")).toLowerCase();
  if (kind.includes("issue") || kind.includes("create")) {
    return "issued";
  }
  if (kind.includes("use") || kind.includes("redeem") || kind.includes("consume")) {
    return "used";
  }

  const amount = asNullableNumber(pick(raw, "amount", "Amount", "usedAmount", "UsedAmount"));
  return amount != null && amount < 0 ? "used" : "issued";
}

export function buildStoreVoucherListPayload(query: {
  page?: number;
  pageSize?: number;
  filters?: StoreVoucherFilters;
}) {
  return {
    storeCode: trimText(query.filters?.storeCode ?? query.filters?.branchCode),
    status: query.filters?.status ?? undefined,
    startDate: trimText(query.filters?.startDate),
    endDate: trimText(query.filters?.endDate),
    pageNumber: normalizePage(query.page),
    pageSize: normalizePageSize(query.pageSize),
  };
}

export function normalizeStoreVoucher(raw: unknown): StoreVoucher {
  const item = asRecord(raw) ?? {};
  return {
    id: asString(pick(item, "id", "Id", "ID")),
    voucherCode: asString(pick(item, "voucherCode", "VoucherCode")),
    voucherType: asNullableInt(pick(item, "voucherType", "VoucherType")),
    storeCode: asString(pick(item, "storeCode", "StoreCode", "branchCode", "BranchCode")),
    storeName: asString(pick(item, "storeName", "StoreName", "branchName", "BranchName")),
    supplierCode: asString(pick(item, "supplierCode", "SupplierCode")),
    supplierName: asString(pick(item, "supplierName", "SupplierName")),
    customerCode: asString(pick(item, "customerCode", "CustomerCode")),
    customerName: asString(
      pick(item, "customerName", "CustomerName", "customer", "Customer", "userName", "UserName")
    ),
    amount: asNullableNumber(pick(item, "amount", "Amount")),
    remainingAmount: asNullableNumber(pick(item, "remainingAmount", "RemainingAmount")),
    discountRate: asNullableNumber(pick(item, "discountRate", "DiscountRate")),
    status: normalizeVoucherStatus(pick(item, "status", "Status")),
    createTime: asString(pick(item, "createTime", "CreateTime", "createdAt", "CreatedAt")),
    updateTime: asString(pick(item, "updateTime", "UpdateTime", "updatedAt", "UpdatedAt")),
    expiredDate: asString(pick(item, "expiredDate", "ExpiredDate")),
    createUser: asString(pick(item, "createUser", "CreateUser", "createdBy", "CreatedBy")),
    updateUser: asString(pick(item, "updateUser", "UpdateUser", "updatedBy", "UpdatedBy")),
    remark: asString(pick(item, "remark", "Remark", "remarks", "Remarks")),
  };
}

export function normalizeStoreVoucherLedgerItem(raw: unknown): StoreVoucherLedgerItem {
  const item = asRecord(raw) ?? {};
  return {
    id: asString(pick(item, "id", "Id", "ID", "ledgerId", "LedgerId")),
    voucherCode: asString(pick(item, "voucherCode", "VoucherCode")),
    action: normalizeLedgerAction(item),
    amount: asNullableNumber(pick(item, "amount", "Amount", "usedAmount", "UsedAmount")),
    remainingAmount: asNullableNumber(pick(item, "remainingAmount", "RemainingAmount", "balance", "Balance")),
    actionTime: asString(
      pick(item, "actionTime", "ActionTime", "createTime", "CreateTime", "usedTime", "UsedTime")
    ),
    paymentMethod: asNullableInt(pick(item, "paymentMethod", "PaymentMethod")),
    paymentMethodName: asString(pick(item, "paymentMethodName", "PaymentMethodName")),
    reference: asString(pick(item, "reference", "Reference", "voucherReference", "VoucherReference")),
    orderGuid: asString(pick(item, "orderGuid", "OrderGuid", "orderGUID", "OrderGUID")),
    orderNo: asString(pick(item, "orderNo", "OrderNo")),
    operatorId: asString(pick(item, "operatorId", "OperatorId", "cashierId", "CashierId")),
    operatorName: asString(
      pick(item, "operatorName", "OperatorName", "cashierName", "CashierName", "userName", "UserName")
    ),
    remark: asString(pick(item, "remark", "Remark", "remarks", "Remarks")),
  };
}

export function normalizeStoreVoucherRelatedOrder(raw: unknown): StoreVoucherRelatedOrder {
  const item = asRecord(raw) ?? {};
  return {
    orderGuid: asString(pick(item, "orderGuid", "OrderGuid", "orderGUID", "OrderGUID")),
    orderNo: asString(pick(item, "orderNo", "OrderNo")),
    storeCode: asString(pick(item, "storeCode", "StoreCode", "branchCode", "BranchCode")),
    supplierCode: asString(pick(item, "supplierCode", "SupplierCode")),
    amount: asNullableNumber(pick(item, "amount", "Amount", "actualAmount", "ActualAmount")),
    orderTime: asString(pick(item, "orderTime", "OrderTime", "createTime", "CreateTime")),
  };
}

export function normalizeStoreVouchersResponse(payload: unknown): PagedResult<StoreVoucher> {
  const listPayload = unwrapListPayload(payload);
  return {
    items: getArray(listPayload, "items", "Items", "vouchers", "Vouchers").map(normalizeStoreVoucher),
    total: asNumber(pick(listPayload, "total", "Total", "totalCount", "TotalCount"), 0),
    pageNumber: asNumber(pick(listPayload, "pageNumber", "PageNumber", "page", "Page"), 1),
    pageSize: asNumber(pick(listPayload, "pageSize", "PageSize", "limit", "Limit"), 20),
  };
}

export function normalizeStoreVoucherDetail(payload: unknown): StoreVoucherDetail {
  const root = asRecord(payload) ?? {};
  const voucherPayload = asRecord(
    pick(root, "voucher", "Voucher", "item", "Item", "storeVoucher", "StoreVoucher")
  );
  const voucherRecord = voucherPayload ?? root;
  return {
    voucher:
      voucherPayload || pick(root, "voucher", "Voucher", "item", "Item", "storeVoucher", "StoreVoucher")
        ? normalizeStoreVoucher(voucherRecord)
        : null,
    ledger: getArray(
      root,
      "ledger",
      "Ledger",
      "ledgerItems",
      "LedgerItems",
      "histories",
      "Histories",
      "useHistory",
      "UseHistory"
    ).map(normalizeStoreVoucherLedgerItem),
    relatedOrders: getArray(
      root,
      "relatedOrders",
      "RelatedOrders",
      "orders",
      "Orders",
      "orderList",
      "OrderList"
    ).map(normalizeStoreVoucherRelatedOrder),
  };
}

export async function fetchStoreVouchers(query: {
  page?: number;
  pageSize?: number;
  filters?: StoreVoucherFilters;
}) {
  const client = await getApiClient();
  const response = await client.post(`${BASE_PATH}/list`, buildStoreVoucherListPayload(query));
  return normalizeStoreVouchersResponse(response.data);
}

export async function fetchStoreVoucherDetail(idOrCode: string) {
  const client = await getApiClient();
  const response = await client.get(`${BASE_PATH}/${encodeURIComponent(idOrCode)}`);
  return normalizeStoreVoucherDetail(response.data);
}
