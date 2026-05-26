import type {
  InstallmentOrderDetail,
  InstallmentOrderDetailLine,
  InstallmentOrderFilters,
  InstallmentOrderListItem,
  InstallmentPaymentRecord,
  PagedResult,
} from "@/modules/installment-orders/types";

const BASE_PATH = "/react/v1/posm-sales-orders";
const LIST_PAGE_SIZES = [20, 50, 100] as const;
const DEFAULT_ORDER_TYPE = 4;

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

function normalizePage(page?: number) {
  return page && Number.isFinite(page) && page > 0 ? Math.trunc(page) : 1;
}

function normalizePageSize(requested?: number) {
  return LIST_PAGE_SIZES.includes(requested as (typeof LIST_PAGE_SIZES)[number]) ? requested! : 20;
}

function trimText(value?: string) {
  const trimmed = value?.trim();
  return trimmed ? trimmed : undefined;
}

function buildKeyword(filters?: InstallmentOrderFilters) {
  const parts = [trimText(filters?.userPhone), trimText(filters?.userName)].filter(
    (value): value is string => Boolean(value)
  );
  return parts.length ? parts.join(" ") : undefined;
}

function normalizeOrderType(filters?: InstallmentOrderFilters) {
  const raw = filters?.orderType;
  return typeof raw === "number" && Number.isFinite(raw) ? Math.trunc(raw) : DEFAULT_ORDER_TYPE;
}

function normalizeStatus(filters?: InstallmentOrderFilters) {
  const raw = filters?.status;
  return typeof raw === "number" && Number.isFinite(raw) ? Math.trunc(raw) : undefined;
}

function unwrapListPayload(payload: unknown): Record<string, unknown> {
  const root = asRecord(payload) ?? {};
  const data = asRecord(pick(root, "data", "Data"));
  return data ?? root;
}

function getArray(raw: Record<string, unknown>, ...keys: string[]) {
  const value = pick(raw, ...keys);
  return Array.isArray(value) ? value : [];
}

export function buildInstallmentOrderListPayload(query: {
  page?: number;
  pageSize?: number;
  filters?: InstallmentOrderFilters;
}) {
  return {
    startDate: trimText(query.filters?.startDate),
    endDate: trimText(query.filters?.endDate),
    branchCode: trimText(query.filters?.branchCode),
    orderType: normalizeOrderType(query.filters),
    status: normalizeStatus(query.filters),
    keyword: buildKeyword(query.filters),
    pageNumber: normalizePage(query.page),
    pageSize: normalizePageSize(query.pageSize),
  };
}

export function normalizeInstallmentOrder(raw: unknown): InstallmentOrderListItem {
  const item = asRecord(raw) ?? {};
  return {
    orderGuid: asString(pick(item, "orderGuid", "OrderGuid", "orderGUID", "OrderGUID")),
    branchCode: asString(pick(item, "branchCode", "BranchCode", "storeCode", "StoreCode")),
    branchName: asString(pick(item, "branchName", "BranchName", "storeName", "StoreName")),
    abn: asString(pick(item, "abn", "ABN")),
    brandName: asString(pick(item, "brandName", "BrandName")),
    deviceCode: asString(pick(item, "deviceCode", "DeviceCode")),
    orderNo: asString(pick(item, "orderNo", "OrderNo")),
    orderTime: asString(pick(item, "orderTime", "OrderTime")),
    customerPhone: asString(
      pick(item, "customerPhone", "CustomerPhone", "userPhone", "UserPhone", "phone", "Phone")
    ),
    customerName: asString(
      pick(item, "customerName", "CustomerName", "userName", "UserName", "name", "Name")
    ),
    skuCount: asNullableInt(pick(item, "skuCount", "SkuCount")),
    itemCount: asNullableInt(pick(item, "itemCount", "ItemCount")),
    totalAmount: asNullableNumber(pick(item, "totalAmount", "TotalAmount")),
    discountAmount: asNullableNumber(pick(item, "discountAmount", "DiscountAmount")),
    actualAmount: asNullableNumber(pick(item, "actualAmount", "ActualAmount")),
    status: asNullableInt(pick(item, "status", "Status")),
  };
}

export function normalizeInstallmentPaymentRecord(raw: unknown): InstallmentPaymentRecord {
  const item = asRecord(raw) ?? {};
  return {
    paymentGuid: asString(pick(item, "paymentGuid", "PaymentGuid", "PaymentGUID")),
    orderGuid: asString(pick(item, "orderGuid", "OrderGuid", "OrderGUID")),
    paymentTime: asString(
      pick(item, "paymentTime", "PaymentTime", "createdTime", "CreatedTime")
    ),
    paymentMethod: asNullableInt(pick(item, "paymentMethod", "PaymentMethod")),
    paymentMethodName: asString(pick(item, "paymentMethodName", "PaymentMethodName")),
    amount: asNullableNumber(pick(item, "amount", "Amount")),
    reference: asString(pick(item, "reference", "Reference")),
    cashierId: asString(pick(item, "cashierId", "CashierId")),
    cashierName: asString(pick(item, "cashierName", "CashierName")),
    createdBy: asString(pick(item, "createdBy", "CreatedBy")),
    updatedBy: asString(pick(item, "updatedBy", "UpdatedBy")),
  };
}

export function normalizeInstallmentOrderDetailLine(raw: unknown): InstallmentOrderDetailLine {
  const item = asRecord(raw) ?? {};
  return {
    productImage: asString(pick(item, "productImage", "ProductImage")),
    productCode: asString(pick(item, "productCode", "ProductCode")),
    productName: asString(pick(item, "productName", "ProductName")),
    quantity: asNullableInt(pick(item, "quantity", "Quantity")),
    unitPrice: asNullableNumber(pick(item, "unitPrice", "UnitPrice", "price", "Price")),
    discountAmount: asNullableNumber(pick(item, "discountAmount", "DiscountAmount")),
    actualAmount: asNullableNumber(pick(item, "actualAmount", "ActualAmount")),
  };
}

export function normalizeInstallmentOrdersResponse(
  payload: unknown
): PagedResult<InstallmentOrderListItem> {
  const listPayload = unwrapListPayload(payload);
  return {
    items: getArray(listPayload, "items", "Items").map(normalizeInstallmentOrder),
    total: asNumber(pick(listPayload, "total", "Total", "totalCount", "TotalCount"), 0),
    pageNumber: asNumber(pick(listPayload, "pageNumber", "PageNumber", "page", "Page"), 1),
    pageSize: asNumber(pick(listPayload, "pageSize", "PageSize", "limit", "Limit"), 20),
  };
}

export function normalizeInstallmentOrderDetail(payload: unknown): InstallmentOrderDetail {
  const root = asRecord(payload) ?? {};
  const orderPayload = asRecord(pick(root, "order", "Order"));
  const orderRecord = orderPayload ?? root;
  return {
    order: orderPayload || pick(root, "order", "Order") ? normalizeInstallmentOrder(orderRecord) : null,
    orderDetails: getArray(root, "orderDetails", "OrderDetails", "details", "Details").map(
      normalizeInstallmentOrderDetailLine
    ),
    paymentDetails: getArray(root, "paymentDetails", "PaymentDetails", "payments", "Payments").map(
      normalizeInstallmentPaymentRecord
    ),
  };
}

export async function fetchInstallmentOrders(query: {
  page?: number;
  pageSize?: number;
  filters?: InstallmentOrderFilters;
}) {
  const client = await getApiClient();
  const response = await client.post(`${BASE_PATH}/list`, buildInstallmentOrderListPayload(query));
  return normalizeInstallmentOrdersResponse(response.data);
}

export async function fetchInstallmentOrderDetail(orderGuid: string) {
  const client = await getApiClient();
  const response = await client.get(`${BASE_PATH}/detail/${encodeURIComponent(orderGuid)}`);
  return normalizeInstallmentOrderDetail(response.data);
}
