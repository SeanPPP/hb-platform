import type {
  ActivePreordersResult,
  PreorderActivationDetail,
  PreorderActivationItem,
  PreorderActivationSummary,
  PreorderSubmitResult,
} from "./types";

type ApiRecord = Record<string, unknown>;

function asRecord(value: unknown): ApiRecord {
  return value && typeof value === "object" && !Array.isArray(value)
    ? value as ApiRecord
    : {};
}

function asString(value: unknown, fallback = "") {
  return typeof value === "string" ? value : fallback;
}

function asOptionalString(value: unknown) {
  const normalized = asString(value).trim();
  return normalized || undefined;
}

function asNumber(value: unknown, fallback = 0) {
  if (typeof value === "number" && Number.isFinite(value)) return value;
  if (typeof value === "string" && value.trim()) {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : fallback;
  }
  return fallback;
}

function asBoolean(value: unknown, fallback = false) {
  if (typeof value === "boolean") return value;
  if (typeof value === "string") {
    if (value.toLowerCase() === "true") return true;
    if (value.toLowerCase() === "false") return false;
  }
  return fallback;
}

export function normalizeActivationSummary(value: unknown): PreorderActivationSummary {
  const raw = asRecord(value);
  return {
    activationGuid: asString(raw.activationGuid),
    templateGuid: asString(raw.templateGuid),
    templateName: asString(raw.templateName),
    periodNumber: Math.max(0, Math.trunc(asNumber(raw.periodNumber))),
    activationCode: asString(raw.activationCode),
    startAtUtc: asString(raw.startAtUtc),
    endAtUtc: asString(raw.endAtUtc),
    status: asString(raw.status, "Active"),
  };
}

export function normalizeActivePreorders(value: unknown): ActivePreordersResult {
  const raw = asRecord(value);
  const activations = Array.isArray(raw.activations)
    ? raw.activations.map(normalizeActivationSummary).filter((item) => item.activationGuid)
    : [];
  return {
    storeCode: asString(raw.storeCode),
    // 服务端必须明确返回 false 才能解除普通订货；响应缺字段或结构异常时保持 fail-closed。
    normalOrderBlocked: raw.normalOrderBlocked === false ? false : true,
    activations,
  };
}

function normalizeActivationItem(value: unknown): PreorderActivationItem {
  const raw = asRecord(value);
  const minimumOrderQuantity = Math.max(1, Math.trunc(asNumber(raw.minimumOrderQuantity, 1)));
  const packCount = Math.max(0, Math.trunc(asNumber(raw.packCount)));
  return {
    activationItemGuid: asString(raw.activationItemGuid),
    productCode: asString(raw.productCode),
    itemNumber: asString(raw.itemNumber),
    productName: asString(raw.productName),
    productImage: asOptionalString(raw.productImage),
    importPrice: asNumber(raw.importPrice),
    retailPrice: asNumber(raw.retailPrice),
    minimumOrderQuantity,
    packCount,
    orderedQuantity: Math.max(0, Math.trunc(asNumber(raw.orderedQuantity, packCount * minimumOrderQuantity))),
  };
}

export function normalizeActivationDetail(value: unknown): PreorderActivationDetail {
  const raw = asRecord(value);
  const activation = asRecord(raw.activation);
  const summary = normalizeActivationSummary(Object.keys(activation).length ? activation : raw);
  const order = asRecord(raw.order);
  return {
    ...summary,
    storeCode: asString(raw.storeCode),
    draftRevision: Math.max(0, Math.trunc(asNumber(raw.draftRevision ?? order.draftRevision))),
    orderGuid: asOptionalString(raw.orderGuid ?? order.orderGuid),
    orderNo: asOptionalString(raw.orderNo ?? order.orderNo),
    orderStatus: asOptionalString(raw.orderStatus ?? order.status),
    warehouseNotes: asOptionalString(raw.warehouseNotes ?? order.warehouseNotes),
    items: Array.isArray(raw.items)
      ? raw.items.map(normalizeActivationItem).filter((item) => item.activationItemGuid)
      : [],
  };
}

export function normalizeSubmitResult(value: unknown): PreorderSubmitResult {
  const raw = asRecord(value);
  return {
    orderGuid: asString(raw.orderGuid),
    orderNo: asString(raw.orderNo),
    status: asString(raw.status),
    draftRevision: Math.max(0, Math.trunc(asNumber(raw.draftRevision))),
    submittedAt: asOptionalString(raw.submittedAt),
    totalPackCount: Math.max(0, Math.trunc(asNumber(raw.totalPackCount))),
    totalQuantity: Math.max(0, Math.trunc(asNumber(raw.totalQuantity))),
    totalImportAmount: asNumber(raw.totalImportAmount),
    totalRetailAmount: asNumber(raw.totalRetailAmount),
  };
}

export function readPreorderErrorCode(error: unknown): string | undefined {
  const raw = asRecord(error);
  const response = asRecord(raw.response);
  const data = asRecord(response.data);
  const nestedData = asRecord(data.data);
  // 业务错误码必须优先于 Axios 的 ERR_BAD_REQUEST，否则 409 门禁与版本冲突无法识别。
  const code = data.errorCode ?? data.code ?? nestedData.errorCode ?? nestedData.code ?? raw.code;
  return asOptionalString(code)?.toUpperCase();
}

export function isPreorderRequiredError(error: unknown) {
  return readPreorderErrorCode(error) === "PREORDER_REQUIRED";
}
