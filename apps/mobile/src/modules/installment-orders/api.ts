import type {
  InstallmentOrderDetail,
  InstallmentOrderDetailOrder,
  InstallmentOrderDetailLine,
  InstallmentOrderFilters,
  InstallmentOrderListItem,
  InstallmentOrderStatus,
  InstallmentPaymentRecord,
  InstallmentPaymentStatus,
  PagedResult,
} from "@/modules/installment-orders/types";

const BASE_PATH = "/react/v1/installment-orders";
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

function normalizeStatus(filters?: InstallmentOrderFilters): InstallmentOrderStatus | undefined {
  const raw = filters?.status;
  // WPF 分期状态固定为 1 至 4，避免把旧销售单状态发送给专用接口。
  return raw === 1 || raw === 2 || raw === 3 || raw === 4 ? raw : undefined;
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

function normalizeInstallmentStatus(value: unknown): InstallmentOrderStatus {
  const status = asNullableInt(value);
  return status === 1 || status === 2 || status === 3 || status === 4 ? status : null;
}

function normalizePaymentStatus(value: unknown): InstallmentPaymentStatus {
  const status = asNullableInt(value);
  // WPF 仅定义 1=已记录、2=已作废，未知值不得伪装成有效付款。
  return status === 1 || status === 2 ? status : null;
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
    status: normalizeStatus(query.filters),
    customerPhone: trimText(query.filters?.customerPhone),
    customerName: trimText(query.filters?.customerName),
    pageNumber: normalizePage(query.page),
    pageSize: normalizePageSize(query.pageSize),
  };
}

export function normalizeInstallmentOrder(raw: unknown): InstallmentOrderListItem {
  const item = asRecord(raw) ?? {};
  return {
    installmentGuid: asString(pick(item, "installmentGuid", "InstallmentGuid")),
    installmentNumber: asString(pick(item, "installmentNumber", "InstallmentNumber")),
    storeCode: asString(pick(item, "storeCode", "StoreCode")),
    storeName: asString(pick(item, "storeName", "StoreName")),
    cashierName: asString(pick(item, "cashierName", "CashierName")),
    customerName: asString(pick(item, "customerName", "CustomerName")),
    customerPhone: asString(pick(item, "customerPhone", "CustomerPhone")),
    createdAt: asString(pick(item, "createdAt", "CreatedAt")),
    totalAmount: asNullableNumber(pick(item, "totalAmount", "TotalAmount")),
    minimumDownPayment: asNullableNumber(
      pick(item, "minimumDownPayment", "MinimumDownPayment")
    ),
    downPaymentAmount: asNullableNumber(pick(item, "downPaymentAmount", "DownPaymentAmount")),
    paidAmount: asNullableNumber(pick(item, "paidAmount", "PaidAmount")),
    balanceAmount: asNullableNumber(pick(item, "balanceAmount", "BalanceAmount")),
    status: normalizeInstallmentStatus(pick(item, "status", "Status")),
    updatedAt: asString(pick(item, "updatedAt", "UpdatedAt")),
  };
}

export function normalizeInstallmentPaymentRecord(raw: unknown): InstallmentPaymentRecord {
  const item = asRecord(raw) ?? {};
  return {
    paymentGuid: asString(pick(item, "paymentGuid", "PaymentGuid")),
    method: asNullableInt(pick(item, "method", "Method")),
    amount: asNullableNumber(pick(item, "amount", "Amount")),
    reference: asString(pick(item, "reference", "Reference")),
    status: normalizePaymentStatus(pick(item, "status", "Status")),
    recordedAt: asString(pick(item, "recordedAt", "RecordedAt")),
    cashierId: asString(pick(item, "cashierId", "CashierId")),
    deviceCode: asString(pick(item, "deviceCode", "DeviceCode")),
  };
}

function normalizeInstallmentOrderDetailOrder(
  raw: Record<string, unknown>,
  root: Record<string, unknown>
): InstallmentOrderDetailOrder {
  return {
    ...normalizeInstallmentOrder(raw),
    // 设备编码属于详情契约，列表摘要不会再读取或保留该字段。
    deviceCode: asString(pick(raw, "deviceCode", "DeviceCode")),
    cashierId: asString(pick(raw, "cashierId", "CashierId")),
    // 当前后端把备注放在详情主单中，同时兼容顶层备注以保持契约演进安全。
    note: asString(pick(raw, "note", "Note") ?? pick(root, "note", "Note")),
  };
}

export function normalizeInstallmentOrderDetailLine(raw: unknown): InstallmentOrderDetailLine {
  const item = asRecord(raw) ?? {};
  return {
    installmentLineGuid: asString(pick(item, "installmentLineGuid", "InstallmentLineGuid")),
    productCode: asString(pick(item, "productCode", "ProductCode")),
    referenceCode: asString(pick(item, "referenceCode", "ReferenceCode")),
    displayName: asString(pick(item, "displayName", "DisplayName")),
    lookupCode: asString(pick(item, "lookupCode", "LookupCode")),
    // 商品可能按重量销售，数量不能截断为整数。
    quantity: asNullableNumber(pick(item, "quantity", "Quantity")),
    unitPrice: asNullableNumber(pick(item, "unitPrice", "UnitPrice")),
    discountAmount: asNullableNumber(pick(item, "discountAmount", "DiscountAmount")),
    actualAmount: asNullableNumber(pick(item, "actualAmount", "ActualAmount")),
    itemNumber: asString(pick(item, "itemNumber", "ItemNumber")),
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
  const pickupPayload = asRecord(pick(root, "pickupInfo", "PickupInfo"));
  const cancellationPayload = asRecord(
    pick(root, "cancellationInfo", "CancellationInfo")
  );
  return {
    order: orderPayload ? normalizeInstallmentOrderDetailOrder(orderPayload, root) : null,
    lines: getArray(root, "lines", "Lines").map(normalizeInstallmentOrderDetailLine),
    payments: getArray(root, "payments", "Payments").map(normalizeInstallmentPaymentRecord),
    pickupInfo: pickupPayload
      ? {
          pickedUpAt: asString(pick(pickupPayload, "pickedUpAt", "PickedUpAt")),
          pickedUpBy: asString(pick(pickupPayload, "pickedUpBy", "PickedUpBy")),
          pickupNote: asString(pick(pickupPayload, "pickupNote", "PickupNote")),
        }
      : null,
    cancellationInfo: cancellationPayload
      ? {
          cancellationKind: (() => {
            const kind = asNullableInt(
              pick(cancellationPayload, "cancellationKind", "CancellationKind")
            );
            return kind === 1 || kind === 2 ? kind : null;
          })(),
          cancelledAt: asString(pick(cancellationPayload, "cancelledAt", "CancelledAt")),
          cancelledBy: asString(pick(cancellationPayload, "cancelledBy", "CancelledBy")),
          cancellationReason: asString(
            pick(cancellationPayload, "cancellationReason", "CancellationReason")
          ),
        }
      : null,
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

export async function fetchInstallmentOrderDetail(installmentGuid: string) {
  const client = await getApiClient();
  const response = await client.get(`${BASE_PATH}/detail/${encodeURIComponent(installmentGuid)}`);
  return normalizeInstallmentOrderDetail(response.data);
}
