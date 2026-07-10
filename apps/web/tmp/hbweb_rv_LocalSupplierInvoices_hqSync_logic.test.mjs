// <define:import.meta.env>
var define_import_meta_env_default = {};

// src/pages/PosAdmin/LocalSupplierInvoices/LocalSupplierInvoices.hqSync.logic.test.ts
import { readFileSync } from "node:fs";
import path from "node:path";

// src/utils/detailLoadState.ts
function shouldSkipDetailAutoReload({
  requestedDetailId,
  loadedDetailId,
  visibleDetailId,
  requestedDetailQueryKey,
  loadedDetailQueryKey
}) {
  if (!requestedDetailId) {
    return false;
  }
  const isSameDetail = loadedDetailId === requestedDetailId && visibleDetailId === requestedDetailId;
  if (!isSameDetail) {
    return false;
  }
  if (requestedDetailQueryKey !== void 0 || loadedDetailQueryKey !== void 0) {
    return requestedDetailQueryKey === loadedDetailQueryKey;
  }
  return true;
}

// src/utils/clientPublicIp.ts
var CLIENT_PUBLIC_IP_HEADER = "X-Client-Public-IP";
var CACHE_KEY = "hbweb:client-public-ipv4";
var CACHE_TTL_MS = 5 * 60 * 1e3;
var PUBLIC_IP_ENDPOINTS = [
  "https://api.ipify.org?format=json",
  "https://checkip.amazonaws.com"
];
function isPublicIpv4(value) {
  if (!value) {
    return false;
  }
  const parts = value.trim().split(".").map((part) => Number(part));
  if (parts.length !== 4 || parts.some((part) => !Number.isInteger(part) || part < 0 || part > 255)) {
    return false;
  }
  const [first, second] = parts;
  return !(first === 10 || first === 127 || first === 0 || first >= 224 || first === 169 && second === 254 || first === 172 && second >= 16 && second <= 31 || first === 192 && second === 168 || first === 192 && second === 0 && (parts[2] === 0 || parts[2] === 2) || first === 192 && second === 88 && parts[2] === 99 || first === 198 && (second === 18 || second === 19) || first === 198 && second === 51 && parts[2] === 100 || first === 203 && second === 0 && parts[2] === 113 || first === 100 && second >= 64 && second <= 127);
}
function readCachedPublicIp() {
  try {
    const cached = window.sessionStorage.getItem(CACHE_KEY);
    if (!cached) {
      return void 0;
    }
    const parsed = JSON.parse(cached);
    if (parsed.expiresAt > Date.now() && isPublicIpv4(parsed.ip)) {
      return parsed.ip;
    }
  } catch {
    return void 0;
  }
  return void 0;
}
function writeCachedPublicIp(ip) {
  try {
    window.sessionStorage.setItem(
      CACHE_KEY,
      JSON.stringify({ ip, expiresAt: Date.now() + CACHE_TTL_MS })
    );
  } catch {
  }
}
async function fetchWithTimeout(url) {
  const controller = new AbortController();
  const timeoutId = window.setTimeout(() => controller.abort(), 1500);
  try {
    return await fetch(url, {
      cache: "no-store",
      signal: controller.signal
    });
  } finally {
    window.clearTimeout(timeoutId);
  }
}
async function resolveClientPublicIpv4() {
  if (typeof window === "undefined") {
    return void 0;
  }
  const cachedIp = readCachedPublicIp();
  if (cachedIp) {
    return cachedIp;
  }
  for (const endpoint of PUBLIC_IP_ENDPOINTS) {
    try {
      const response = await fetchWithTimeout(endpoint);
      if (!response.ok) {
        continue;
      }
      const text = await response.text();
      const parsedIp = text.trim().startsWith("{") ? JSON.parse(text).ip : text.trim();
      if (typeof parsedIp === "string" && isPublicIpv4(parsedIp)) {
        writeCachedPublicIp(parsedIp);
        return parsedIp;
      }
    } catch {
    }
  }
  return void 0;
}
async function getClientPublicIpHeaders() {
  const ip = await resolveClientPublicIpv4();
  return ip ? { [CLIENT_PUBLIC_IP_HEADER]: ip } : {};
}

// src/utils/centerLogClient.ts
var importMetaEnv = define_import_meta_env_default ?? {};
var API_BASE_URL = (importMetaEnv.VITE_API_BASE_URL || "").trim();
var CENTER_LOG_INGEST_PATH = "/api/system/logs/ingest";
var CENTER_LOG_PROJECT = (importMetaEnv.VITE_CENTER_LOG_PROJECT || "hbweb_rv").trim();
var CENTER_LOG_KEY = (importMetaEnv.VITE_CENTER_LOG_KEY || "").trim();
var CENTER_LOG_ENVIRONMENT = (importMetaEnv.VITE_CENTER_LOG_ENVIRONMENT || importMetaEnv.MODE || "development").trim();
var CENTER_LOG_SERVICE_NAME = (importMetaEnv.VITE_CENTER_LOG_SERVICE_NAME || "hbweb_rv-web").trim();
var CENTER_LOG_SOURCE_TYPE = "Web";
var MAX_MESSAGE_LENGTH = 2e3;
var MAX_STACK_LENGTH = 12e3;
var MAX_PROPERTY_LENGTH = 1e3;
function trimText(value, maxLength) {
  if (!value) {
    return void 0;
  }
  const normalized = value.trim();
  if (!normalized) {
    return void 0;
  }
  return normalized.length > maxLength ? `${normalized.slice(0, maxLength - 3)}...` : normalized;
}
function buildApiUrl(path2) {
  return `${API_BASE_URL}${path2}`.replace(/([^:]\/)\/+/g, "$1");
}
function getRequestPath(url, options) {
  if (!url) {
    return void 0;
  }
  try {
    const resolved = new URL(url, typeof window !== "undefined" ? window.location.origin : "http://localhost");
    return options?.stripQuery ? resolved.pathname : `${resolved.pathname}${resolved.search}`;
  } catch {
    return options?.stripQuery ? url.split("?")[0] : url;
  }
}
function sanitizeProperties(properties) {
  if (!properties) {
    return void 0;
  }
  const sanitizedEntries = [];
  Object.entries(properties).forEach(([key, value]) => {
    if (value === void 0 || value === null || value === "") {
      return;
    }
    if (typeof value === "string") {
      const trimmedValue = trimText(value, MAX_PROPERTY_LENGTH);
      if (trimmedValue) {
        sanitizedEntries.push([key, trimmedValue]);
      }
      return;
    }
    sanitizedEntries.push([key, value]);
  });
  return sanitizedEntries.length ? Object.fromEntries(sanitizedEntries) : void 0;
}
function summarizeResponsePayloadForLog(payload) {
  if (payload === void 0 || payload === null || payload === "") {
    return void 0;
  }
  if (typeof payload === "string") {
    return { message: trimText(payload, MAX_PROPERTY_LENGTH) };
  }
  if (typeof payload !== "object") {
    return { message: trimText(String(payload), MAX_PROPERTY_LENGTH) };
  }
  const raw = payload;
  const summary = {};
  ["success", "isSuccess", "message", "code", "errorCode"].forEach((key) => {
    const value = raw[key];
    if (typeof value === "boolean" || typeof value === "number") {
      summary[key] = value;
      return;
    }
    if (typeof value === "string") {
      const trimmed = trimText(value, MAX_PROPERTY_LENGTH);
      if (trimmed) {
        summary[key] = trimmed;
      }
    }
  });
  return Object.keys(summary).length ? summary : void 0;
}
function isCenterLogIngestRequest(url) {
  const requestPath = getRequestPath(url) || "";
  return requestPath.startsWith(CENTER_LOG_INGEST_PATH);
}
function isCenterLogConfigured() {
  return Boolean(CENTER_LOG_KEY);
}
function sendCenterLog(payload) {
  if (!isCenterLogConfigured()) {
    return;
  }
  const item = {
    ...payload,
    message: trimText(payload.message, MAX_MESSAGE_LENGTH) || "\u672A\u77E5\u9519\u8BEF",
    timestampUtc: payload.timestampUtc || (/* @__PURE__ */ new Date()).toISOString(),
    projectCode: CENTER_LOG_PROJECT,
    environment: CENTER_LOG_ENVIRONMENT,
    sourceType: CENTER_LOG_SOURCE_TYPE,
    serviceName: CENTER_LOG_SERVICE_NAME || void 0,
    exceptionMessage: trimText(payload.exceptionMessage, MAX_MESSAGE_LENGTH),
    stackTrace: trimText(payload.stackTrace, MAX_STACK_LENGTH),
    requestPath: trimText(payload.requestPath, MAX_PROPERTY_LENGTH),
    traceId: trimText(payload.traceId, MAX_PROPERTY_LENGTH),
    category: trimText(payload.category || payload.sourceType, MAX_PROPERTY_LENGTH),
    userId: trimText(payload.userId, MAX_PROPERTY_LENGTH),
    userName: trimText(payload.userName, MAX_PROPERTY_LENGTH),
    properties: sanitizeProperties(payload.properties)
  };
  void fetch(buildApiUrl(CENTER_LOG_INGEST_PATH), {
    method: "POST",
    credentials: "include",
    keepalive: true,
    headers: {
      "Content-Type": "application/json",
      "X-Log-Project": CENTER_LOG_PROJECT,
      "X-Log-Key": CENTER_LOG_KEY
    },
    body: JSON.stringify({ logs: [item] })
  }).catch(() => {
  });
}
function normalizeUnknownError(error) {
  if (error instanceof Error) {
    return {
      message: error.message,
      exceptionType: error.name,
      stackTrace: error.stack
    };
  }
  return {
    message: typeof error === "string" ? error : "\u672A\u77E5\u5F02\u5E38",
    exceptionType: typeof error,
    stackTrace: void 0
  };
}
function isAbortOrCanceledError(error) {
  if (typeof DOMException !== "undefined" && error instanceof DOMException && error.name === "AbortError") {
    return true;
  }
  if (error instanceof Error) {
    return error.name === "AbortError" || error.name === "CanceledError";
  }
  return false;
}
function reportRequestError(input) {
  if (isAbortOrCanceledError(input.error)) {
    return;
  }
  if (isCenterLogIngestRequest(input.url)) {
    return;
  }
  const normalizedError = normalizeUnknownError(input.error);
  sendCenterLog({
    level: input.statusCode && input.statusCode < 500 ? "Warning" : "Error",
    sourceType: "frontend-request",
    message: normalizedError.message,
    exceptionType: normalizedError.exceptionType,
    exceptionMessage: normalizedError.message,
    stackTrace: normalizedError.stackTrace,
    requestPath: getRequestPath(input.url),
    requestMethod: input.method,
    statusCode: input.statusCode,
    traceId: input.traceId,
    properties: {
      // 只记录失败摘要，避免把后端响应里的客户资料、token 等敏感字段写进前端日志。
      responsePayload: summarizeResponsePayloadForLog(input.responsePayload)
    }
  });
}

// src/utils/request.ts
var RequestError = class extends Error {
  status;
  payload;
  constructor(message, status, payload) {
    super(message);
    this.name = "RequestError";
    this.status = status;
    this.payload = payload;
  }
};
function buildQueryString(params) {
  if (!params) {
    return "";
  }
  const searchParams = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value === void 0 || value === null || value === "") {
      return;
    }
    if (Array.isArray(value)) {
      value.forEach((item) => {
        if (item !== void 0 && item !== null && item !== "") {
          searchParams.append(key, String(item));
        }
      });
      return;
    }
    searchParams.append(key, String(value));
  });
  const query = searchParams.toString();
  return query ? `?${query}` : "";
}
var API_BASE_URL2 = (define_import_meta_env_default?.VITE_API_BASE_URL || "").trim();
var LOGIN_PATH = "/login";
var AUTH_EXPIRED_EVENT = "hbweb:auth-expired";
var AUTH_WHITELIST = /* @__PURE__ */ new Set([
  "/api/Auth/session/login",
  "/api/Auth/session/logout",
  "/api/Auth/session/refresh"
]);
var authRedirecting = false;
var refreshPromise = null;
function buildRequestUrl(url, params) {
  const requestPath = url.startsWith("http://") || url.startsWith("https://") ? url : `${API_BASE_URL2}${url}`.replace(/([^:]\/)\/+/g, "$1");
  return `${requestPath}${buildQueryString(params)}`;
}
async function tryRefreshToken() {
  if (refreshPromise) {
    return refreshPromise;
  }
  refreshPromise = (async () => {
    try {
      const refreshUrl = buildRequestUrl("/api/Auth/session/refresh");
      const response = await fetch(refreshUrl, {
        method: "POST",
        credentials: "include",
        headers: {
          "Content-Type": "application/json",
          ...await getClientPublicIpHeaders()
        },
        body: JSON.stringify({})
      });
      if (!response.ok) {
        return false;
      }
      const payload = await response.json();
      return !!(payload?.success ?? payload?.data);
    } catch {
      return false;
    } finally {
      refreshPromise = null;
    }
  })();
  return refreshPromise;
}
function handleUnauthorized(requestUrl) {
  if (typeof window === "undefined" || authRedirecting) {
    return;
  }
  const currentPath = `${window.location.pathname}${window.location.search}`;
  const normalizedUrl = requestUrl.replace(API_BASE_URL2, "");
  if (window.location.pathname === LOGIN_PATH || AUTH_WHITELIST.has(normalizedUrl)) {
    return;
  }
  authRedirecting = true;
  window.dispatchEvent(new Event(AUTH_EXPIRED_EVENT));
  window.location.replace(`${LOGIN_PATH}?redirect=${encodeURIComponent(currentPath)}`);
}
async function parseResponse(response) {
  const contentType = response.headers.get("content-type") || "";
  if (contentType.includes("application/json")) {
    return await response.json();
  }
  return await response.text();
}
async function rawFetch(url, options = {}) {
  const { method = "GET", params, data, headers, signal } = options;
  const requestUrl = buildRequestUrl(url, params);
  const isFormDataBody = typeof FormData !== "undefined" && data instanceof FormData;
  const response = await fetch(requestUrl, {
    method,
    credentials: "include",
    headers: {
      // FormData 必须交给浏览器/运行时自动补 multipart boundary，不能手动写 JSON 头。
      ...data && !isFormDataBody ? { "Content-Type": "application/json" } : {},
      ...headers
    },
    body: data ? isFormDataBody ? data : JSON.stringify(data) : void 0,
    signal
  });
  const payload = await parseResponse(response);
  return { response, payload };
}
async function request(url, options = {}) {
  const { skipAuthRedirect = false } = options;
  const normalizedUrl = url.replace(API_BASE_URL2, "");
  let response;
  let payload;
  try {
    const result = await rawFetch(url, options);
    response = result.response;
    payload = result.payload;
  } catch (error) {
    reportRequestError({
      url,
      method: options.method ?? "GET",
      error
    });
    throw error;
  }
  if (!response.ok) {
    if (response.status === 401 && !skipAuthRedirect && !AUTH_WHITELIST.has(normalizedUrl)) {
      const refreshed = await tryRefreshToken();
      if (refreshed) {
        const retryResult = await rawFetch(url, options);
        if (retryResult.response.ok) {
          return retryResult.payload;
        }
      }
      handleUnauthorized(url);
    }
    const message = typeof payload === "object" && payload !== null && "message" in payload && typeof payload.message === "string" ? payload.message : `\u8BF7\u6C42\u5931\u8D25 (${response.status})`;
    if (!isCenterLogIngestRequest(url)) {
      reportRequestError({
        url,
        method: options.method ?? "GET",
        statusCode: response.status,
        error: new RequestError(message, response.status, payload),
        responsePayload: payload,
        traceId: response.headers.get("x-trace-id") ?? response.headers.get("trace-id") ?? void 0
      });
    }
    throw new RequestError(message, response.status, payload);
  }
  return payload;
}
function unwrapApiData(payload) {
  if (payload && typeof payload === "object") {
    const response = payload;
    const success = response.success ?? response.isSuccess;
    if (success === false) {
      const code = response.code ?? response.errorCode;
      const message = response.message || "\u8BF7\u6C42\u5931\u8D25";
      throw new RequestError(code ? `${code}: ${message}` : message, 200, payload);
    }
    if ("data" in payload) {
      return response.data;
    }
  }
  return payload;
}
request.get = (url, options) => request(url, { ...options, method: "GET" });
request.post = (url, data, options) => request(url, { ...options, method: "POST", data });
request.put = (url, data, options) => request(url, { ...options, method: "PUT", data });
request.patch = (url, data, options) => request(url, { ...options, method: "PATCH", data });
request.delete = (url, options) => request(url, { ...options, method: "DELETE" });
var request_default = request;

// src/services/localSupplierInvoiceService.ts
var API_BASE = "/api/react/v1/local-supplier-invoices";
var PURCHASE_SALES_ANALYSIS_API_BASE = `${API_BASE}/purchase-sales-analysis`;
function assertApiSuccess(response, fallbackMessage) {
  if (response.success === false || response.isSuccess === false) {
    throw new RequestError(response.message || fallbackMessage, 200, response);
  }
}
async function batchUpsertDetails(invoiceGuid, items) {
  const response = await request_default.post(
    `${API_BASE}/${invoiceGuid}/details/batch-upsert`,
    items
  );
  assertApiSuccess(response, "\u4FDD\u5B58\u660E\u7EC6\u5931\u8D25");
  return unwrapApiData(response);
}
async function startCheckProductsJob(data) {
  const response = await request_default.post(`${API_BASE}/check-products/jobs`, data);
  assertApiSuccess(response, "\u521B\u5EFA\u5546\u54C1\u68C0\u6D4B\u4EFB\u52A1\u5931\u8D25");
  return unwrapApiData(response);
}
async function getCheckProductsJob(invoiceGuid, jobId) {
  const response = await request_default.get(
    `${API_BASE}/${invoiceGuid}/check-products/jobs/${encodeURIComponent(jobId)}`
  );
  assertApiSuccess(response, "\u67E5\u8BE2\u5546\u54C1\u68C0\u6D4B\u4EFB\u52A1\u5931\u8D25");
  return unwrapApiData(response);
}
async function startPasteDetailsJob(data) {
  const response = await request_default.post(`${API_BASE}/${data.invoiceGuid}/details/paste/jobs`, {
    mode: data.mode,
    items: data.items
  });
  assertApiSuccess(response, "\u521B\u5EFA\u7C98\u8D34\u660E\u7EC6\u4EFB\u52A1\u5931\u8D25");
  return unwrapApiData(response);
}
async function getPasteDetailsJob(invoiceGuid, jobId) {
  const response = await request_default.get(
    `${API_BASE}/${invoiceGuid}/details/paste/jobs/${encodeURIComponent(jobId)}`
  );
  assertApiSuccess(response, "\u67E5\u8BE2\u7C98\u8D34\u660E\u7EC6\u4EFB\u52A1\u5931\u8D25");
  return unwrapApiData(response);
}
async function batchUpdateDetailAction(invoiceGuid, detailGuids, action) {
  const response = await request_default.put(`${API_BASE}/${invoiceGuid}/details/batch-action`, { detailGuids, action });
  assertApiSuccess(response, "\u6279\u91CF\u8BBE\u7F6E\u64CD\u4F5C\u7C7B\u578B\u5931\u8D25");
}
async function updateDetailAction(invoiceGuid, detailGuid, action) {
  const response = await request_default.put(`${API_BASE}/${invoiceGuid}/details/${detailGuid}/action`, { action });
  assertApiSuccess(response, "\u66F4\u65B0\u64CD\u4F5C\u7C7B\u578B\u5931\u8D25");
}
async function updateToStorePrices(data) {
  const response = await request_default.post(`${API_BASE}/update-to-store-prices`, data);
  assertApiSuccess(response, "\u66F4\u65B0\u5230\u5206\u5E97\u4EF7\u683C\u5931\u8D25");
  return unwrapApiData(response);
}
async function ensureHqProducts(invoiceGuid, data) {
  const response = await request_default.post(
    `${API_BASE}/${invoiceGuid}/details/ensure-hq-products`,
    data
  );
  assertApiSuccess(response, "\u540C\u6B65\u5546\u54C1\u5230HQ\u5931\u8D25");
  return unwrapApiData(response);
}
async function updateHqProducts(invoiceGuid, data) {
  const response = await request_default.post(
    `${API_BASE}/${invoiceGuid}/details/update-hq-products`,
    data
  );
  assertApiSuccess(response, "\u66F4\u65B0HQ\u5546\u54C1\u5931\u8D25");
  return unwrapApiData(response);
}
async function batchUpdateDetails(invoiceGuid, items, editFields) {
  const response = await request_default.post(`${API_BASE}/${invoiceGuid}/details/batch-update`, {
    items,
    editFields
  });
  assertApiSuccess(response, "\u6279\u91CF\u7F16\u8F91\u660E\u7EC6\u5931\u8D25");
  return unwrapApiData(response);
}
async function batchExecuteActions(data) {
  if (!data.detailGuids.length) {
    throw new Error("\u8BF7\u9009\u62E9\u8981\u6267\u884C\u7684\u660E\u7EC6");
  }
  if (!data.expectedActions.length || data.confirmedCreateProductCount == null || !data.confirmedAt) {
    throw new Error("\u8BF7\u5148\u786E\u8BA4\u6279\u91CF\u6267\u884C\u64CD\u4F5C");
  }
  const response = await request_default.post(`${API_BASE}/${data.invoiceGuid}/details/batch-execute`, {
    detailGuids: data.detailGuids,
    expectedActions: data.expectedActions,
    confirmedCreateProductCount: data.confirmedCreateProductCount,
    confirmedAt: data.confirmedAt,
    newProductProductTypeSelections: data.newProductProductTypeSelections ?? []
  });
  assertApiSuccess(response, "\u6279\u91CF\u6267\u884C\u64CD\u4F5C\u5931\u8D25");
  return unwrapApiData(response);
}
async function syncInvoicesFromHq(data) {
  let response;
  try {
    response = await request_default.post(`${API_BASE}/sync-from-hq`, data);
  } catch (error) {
    if (error instanceof RequestError) {
      const payload = error.payload;
      const syncResult = payload?.data ?? payload?.details;
      if (syncResult) {
        throw new RequestError(payload?.message || error.message, error.status, {
          ...payload,
          data: syncResult
        });
      }
    }
    throw error;
  }
  assertApiSuccess(response, "\u4ECEHQ\u540C\u6B65\u5931\u8D25");
  return unwrapApiData(response);
}

// src/pages/PosAdmin/LocalSupplierInvoices/InvoiceEdit/statusFilters.ts
var actionTypeFilters = [
  0 /* None */,
  1 /* CreateProduct */,
  2 /* UpdatePurchasePrice */,
  3 /* WaitForOperation */,
  4 /* UpdateItemNumber */,
  5 /* AddMultiCode */
];
function getProductStatusFilter(detail) {
  const count = detail.existingProductCount;
  if (count === void 0 || count === null) return "notDetected";
  if (count > 0) return "exists";
  return "notExists";
}
function getBarcodeStatusFilter(detail) {
  const status = detail.barcodeStatus;
  const count = detail.barcodeMatchCount ?? 0;
  if (status === void 0 || status === null || status === 0) return "notDetected";
  if (status === 1) return "normal";
  if (count === 0) return "noMatch";
  return "multiMatch";
}
function getActionTypeFilter(detail, rowActions = {}) {
  const action = rowActions[detail.detailGUID] ?? detail.activityType ?? 0 /* None */;
  return isActionTypeFilter(action) ? action : 0 /* None */;
}
function getDetailStatusStats(details, rowActions = {}) {
  const stats = {
    product: {
      notDetected: 0,
      exists: 0,
      notExists: 0
    },
    barcode: {
      notDetected: 0,
      normal: 0,
      noMatch: 0,
      multiMatch: 0
    },
    action: {
      [0 /* None */]: 0,
      [1 /* CreateProduct */]: 0,
      [2 /* UpdatePurchasePrice */]: 0,
      [3 /* WaitForOperation */]: 0,
      [4 /* UpdateItemNumber */]: 0,
      [5 /* AddMultiCode */]: 0
    }
  };
  details.forEach((item) => {
    stats.product[getProductStatusFilter(item)] += 1;
    stats.barcode[getBarcodeStatusFilter(item)] += 1;
    stats.action[getActionTypeFilter(item, rowActions)] += 1;
  });
  return stats;
}
function toggleStatusFilter(currentFilter, nextFilter) {
  return currentFilter === nextFilter ? "all" : nextFilter;
}
function filterInvoiceDetails(details, filters) {
  let result = details;
  const keyword = filters.searchText.trim().toLowerCase();
  if (keyword) {
    result = result.filter(
      (item) => item.productCode?.toLowerCase().includes(keyword) || item.itemNumber?.toLowerCase().includes(keyword) || item.barcode?.toLowerCase().includes(keyword) || item.productName?.toLowerCase().includes(keyword) || item.storeProductCode?.toLowerCase().includes(keyword)
    );
  }
  if (filters.priceFilter === "up") {
    result = result.filter((item) => hasPurchasePriceChanged(item, "up"));
  } else if (filters.priceFilter === "down") {
    result = result.filter((item) => hasPurchasePriceChanged(item, "down"));
  }
  if (filters.productStatusFilter !== "all") {
    result = result.filter((item) => getProductStatusFilter(item) === filters.productStatusFilter);
  }
  if (filters.barcodeStatusFilter !== "all") {
    result = result.filter((item) => getBarcodeStatusFilter(item) === filters.barcodeStatusFilter);
  }
  if (filters.actionTypeFilter !== void 0 && filters.actionTypeFilter !== "all") {
    result = result.filter((item) => getActionTypeFilter(item, filters.rowActions) === filters.actionTypeFilter);
  }
  return result;
}
function isActionTypeFilter(value) {
  return actionTypeFilters.includes(value);
}
function hasPurchasePriceChanged(item, direction) {
  if (item.lastPurchasePrice === void 0 || item.lastPurchasePrice === null || item.lastPurchasePrice <= 0 || item.purchasePrice === void 0 || item.purchasePrice === null) {
    return false;
  }
  return direction === "up" ? item.purchasePrice > item.lastPurchasePrice : item.purchasePrice < item.lastPurchasePrice;
}

// src/pages/PosAdmin/LocalSupplierInvoices/InvoiceEdit/tableColumnFilters.ts
function isEmptyValue(value) {
  return value === void 0 || value === null || value === "";
}
function parseJsonObject(value) {
  if (typeof value !== "string") return null;
  try {
    const parsed = JSON.parse(value);
    return parsed && typeof parsed === "object" && !Array.isArray(parsed) ? parsed : null;
  } catch {
    return null;
  }
}
function parseNumber(value) {
  if (value === void 0 || value === null || value === "") return void 0;
  const numeric = Number(value);
  return Number.isFinite(numeric) ? numeric : void 0;
}
function getNumberColumnValue(record, field) {
  const value = record[field];
  if (value === void 0 || value === null) return void 0;
  return field === "discountRate" ? value * 100 : value;
}
function serializeTextColumnFilter(model) {
  return JSON.stringify(model);
}
function parseTextColumnFilter(value) {
  const parsed = parseJsonObject(value);
  if (!parsed) {
    return { mode: "contains", value: String(value ?? "") };
  }
  const mode = typeof parsed.mode === "string" ? parsed.mode : "contains";
  const normalizedMode = ["contains", "equals", "startsWith", "endsWith", "empty", "notEmpty"].includes(mode) ? mode : "contains";
  return {
    mode: normalizedMode,
    value: typeof parsed.value === "string" ? parsed.value : ""
  };
}
function serializeNumberColumnFilter(model) {
  return JSON.stringify(model);
}
function parseNumberColumnFilter(value) {
  const parsed = parseJsonObject(value);
  if (!parsed) {
    return { mode: "equals", value: parseNumber(value) };
  }
  const mode = typeof parsed.mode === "string" ? parsed.mode : "equals";
  const normalizedMode = ["equals", "gt", "gte", "lt", "lte", "between", "empty", "notEmpty"].includes(mode) ? mode : "equals";
  return {
    mode: normalizedMode,
    value: parseNumber(parsed.value),
    min: parseNumber(parsed.min),
    max: parseNumber(parsed.max)
  };
}
function compareNullableNumbers(left, right) {
  const leftEmpty = isEmptyValue(left);
  const rightEmpty = isEmptyValue(right);
  if (leftEmpty && rightEmpty) return 0;
  if (leftEmpty) return 1;
  if (rightEmpty) return -1;
  return Number(left) - Number(right);
}
function compareNullableText(left, right) {
  const leftEmpty = isEmptyValue(left);
  const rightEmpty = isEmptyValue(right);
  if (leftEmpty && rightEmpty) return 0;
  if (leftEmpty) return 1;
  if (rightEmpty) return -1;
  return String(left).localeCompare(String(right), void 0, {
    sensitivity: "base",
    numeric: true
  });
}
function matchesTextColumnFilter(record, field, value) {
  const filter = parseTextColumnFilter(value);
  const actual = String(record[field] ?? "");
  const normalizedActual = actual.trim().toLowerCase();
  if (filter.mode === "empty") return !normalizedActual;
  if (filter.mode === "notEmpty") return Boolean(normalizedActual);
  const keyword = String(filter.value ?? "").trim().toLowerCase();
  if (!keyword) return true;
  if (filter.mode === "equals") return normalizedActual === keyword;
  if (filter.mode === "startsWith") return normalizedActual.startsWith(keyword);
  if (filter.mode === "endsWith") return normalizedActual.endsWith(keyword);
  return normalizedActual.includes(keyword);
}
function matchesNumberColumnFilter(record, field, value) {
  const filter = parseNumberColumnFilter(value);
  const actual = getNumberColumnValue(record, field);
  if (filter.mode === "empty") return actual === void 0 || actual === null;
  if (filter.mode === "notEmpty") return actual !== void 0 && actual !== null;
  if (actual === void 0 || actual === null) return false;
  if (filter.mode === "between") {
    const hasMin = filter.min !== void 0;
    const hasMax = filter.max !== void 0;
    if (!hasMin && !hasMax) return true;
    return (!hasMin || actual >= filter.min) && (!hasMax || actual <= filter.max);
  }
  if (filter.value === void 0) return true;
  if (filter.mode === "gt") return actual > filter.value;
  if (filter.mode === "gte") return actual >= filter.value;
  if (filter.mode === "lt") return actual < filter.value;
  if (filter.mode === "lte") return actual <= filter.value;
  return actual === filter.value;
}
function filterBooleanColumn(actual, value) {
  if (actual === void 0 || actual === null) return false;
  return actual === String(value).toLowerCase().includes("true");
}
function filterProductStatusColumn(record, value) {
  return getProductStatusFilter(record) === String(value);
}
function filterBarcodeStatusColumn(record, value) {
  return getBarcodeStatusFilter(record) === String(value);
}
function matchesActionTypeColumnFilter(record, value, rowActions = {}) {
  return getActionTypeFilter(record, rowActions) === Number(value);
}

// src/pages/PosAdmin/LocalSupplierInvoices/InvoiceEdit/pasteDetails.ts
var defaultPasteFieldOrder = [
  "itemNumber",
  "barcode",
  "productName",
  "quantity",
  "purchasePrice",
  "newAutoRetailPrice",
  "retailPrice"
];
function normalizeCellLineBreaks(value) {
  return value.replace(/\r\n/g, "\n").replace(/\r/g, "\n");
}
function splitCellLines(value) {
  return normalizeCellLineBreaks(value).split("\n").map((line) => line.trim());
}
function mergeCellText(value) {
  if (value === void 0) return void 0;
  return normalizeCellLineBreaks(value).replace(/\n+/g, " ").replace(/\s+/g, " ").trim();
}
function normalizeHeaderCell(value) {
  return mergeCellText(value)?.toLowerCase().replace(/[^a-z0-9\u4e00-\u9fa5]/g, "");
}
function isHeaderCell(field, value) {
  const normalized = normalizeHeaderCell(value);
  if (!normalized) return false;
  const headers = {
    itemNumber: ["itemno", "itemnumber", "item", "\u8D27\u53F7"],
    barcode: ["barcode", "\u6761\u7801"],
    productName: ["description", "desc", "productname", "\u5546\u54C1\u540D\u79F0"],
    quantity: ["invoiceqty", "qty", "quantity", "\u6570\u91CF"],
    purchasePrice: ["priceexgst", "price", "purchaseprice", "\u672C\u6B21\u8FDB\u8D27\u4EF7", "\u8FDB\u8D27\u4EF7"],
    newAutoRetailPrice: ["newautoretailprice", "\u65B0\u81EA\u52A8\u96F6\u552E\u4EF7"],
    retailPrice: ["retailprice", "\u96F6\u552E\u4EF7"]
  };
  return field !== "skip" && headers[field].includes(normalized);
}
function isPasteHeaderRow(cols, fieldOrder) {
  let mappedCells = 0;
  let headerCells = 0;
  fieldOrder.forEach((field, index) => {
    if (field === "skip" || !cols[index]?.trim()) return;
    mappedCells += 1;
    if (isHeaderCell(field, cols[index])) {
      headerCells += 1;
    }
  });
  return mappedCells > 0 && mappedCells === headerCells && headerCells >= 2;
}
function parsePasteCells(text) {
  if (!text.trim()) return [];
  const normalized = normalizeCellLineBreaks(text);
  const rows = [];
  let row = [];
  let cell = "";
  let inQuotedCell = false;
  for (let index = 0; index < normalized.length; index += 1) {
    const char = normalized[index];
    const nextChar = normalized[index + 1];
    if (char === '"') {
      if (inQuotedCell && nextChar === '"') {
        cell += '"';
        index += 1;
        continue;
      }
      if (inQuotedCell || cell.length === 0) {
        inQuotedCell = !inQuotedCell;
        continue;
      }
    }
    if (char === "	" && !inQuotedCell) {
      row.push(cell);
      cell = "";
      continue;
    }
    if (char === "\n" && !inQuotedCell) {
      row.push(cell);
      rows.push(row);
      row = [];
      cell = "";
      continue;
    }
    cell += char;
  }
  row.push(cell);
  rows.push(row);
  return rows.filter((currentRow) => currentRow.some((currentCell) => currentCell.trim()));
}
function parsePastedNumber(value) {
  if (!value?.trim()) return void 0;
  const normalized = value.trim().replace(/,/g, "").replace(/\s+/g, "").replace(/[^\d.-]/g, "");
  if (!normalized || normalized === "-" || normalized === "." || normalized === "-.") {
    return void 0;
  }
  const parsed = Number(normalized);
  return Number.isNaN(parsed) ? void 0 : parsed;
}
function parsePastedBarcode(value) {
  if (!value?.trim()) return { additionalBarcodes: [] };
  const normalized = value.trim().replace(/^'+/, "").replace(/条码|barcode|bar\s*code|ean|upc/gi, " ").replace(/[\s:：]+/g, "");
  const barcodes = [];
  const seen = /* @__PURE__ */ new Set();
  normalized.split(/[，,;；、]+/).map((barcode2) => barcode2.trim()).filter(Boolean).forEach((barcode2) => {
    const key = barcode2.toUpperCase();
    if (seen.has(key)) return;
    seen.add(key);
    barcodes.push(barcode2);
  });
  const [barcode, ...additionalBarcodes] = barcodes;
  return { barcode, additionalBarcodes };
}
function parsePastedItemNumber(value) {
  if (!value?.trim()) return void 0;
  const normalized = value.trim().replace(/^'+/, "");
  return normalized || void 0;
}
function getSmartSplitPlan(cols, fieldOrder) {
  const businessCells = fieldOrder.map((field, index) => ({ field, value: cols[index] })).filter(({ field, value }) => field !== "skip" && Boolean(value?.trim()));
  const businessLineCounts = businessCells.map(({ value }) => splitCellLines(value ?? "").length);
  const hasBusinessMultiline = businessLineCounts.some((count) => count > 1);
  const splitCount = businessLineCounts[0] ?? 0;
  const canSplit = businessCells.length > 1 && splitCount > 1 && businessLineCounts.every((count) => count === splitCount);
  return {
    canSplit,
    splitCount: canSplit ? splitCount : 1,
    hasBusinessMultiline
  };
}
function createSmartSplitCols(cols, fieldOrder, rowIndex) {
  return cols.map((value, index) => {
    const field = fieldOrder[index];
    if (field === "skip" || !value?.trim()) return value;
    return splitCellLines(value)[rowIndex] ?? value;
  });
}
function parsePasteColumns(cols, fieldOrder, options) {
  const row = {};
  fieldOrder.forEach((field, index) => {
    if (field === "skip") return;
    const value = mergeCellText(cols[index]);
    if (field === "quantity" || field === "purchasePrice" || field === "newAutoRetailPrice" || field === "retailPrice") {
      const parsedNumber = parsePastedNumber(value);
      row[field] = field === "retailPrice" && options.normalizeRetailPrice && parsedNumber !== void 0 ? normalizePastedRetailPrice(parsedNumber) : parsedNumber;
      return;
    }
    if (field === "barcode") {
      const parsedBarcode = parsePastedBarcode(value);
      row[field] = parsedBarcode.barcode;
      row.additionalBarcodes = parsedBarcode.additionalBarcodes.length ? parsedBarcode.additionalBarcodes : void 0;
      return;
    }
    if (field === "itemNumber") {
      row[field] = parsePastedItemNumber(value);
      return;
    }
    row[field] = value || void 0;
  });
  return row;
}
function normalizePastedRetailPrice(price) {
  if (!Number.isFinite(price) || price < 3) return price;
  const cents = Math.round(price * 100);
  const integerCents = Math.floor(cents / 100) * 100;
  const decimalCents = cents - integerCents;
  if (decimalCents === 0) {
    return Number(((integerCents - 1) / 100).toFixed(2));
  }
  if (decimalCents <= 50) {
    return Number(((integerCents + 50) / 100).toFixed(2));
  }
  return Number(((integerCents + 99) / 100).toFixed(2));
}
function parsePasteText(text, fieldOrder = defaultPasteFieldOrder, options = {}) {
  if (!text.trim()) return [];
  const rows = parsePasteCells(text);
  const multilineCellMode = options.multilineCellMode ?? "merge";
  return rows.flatMap((cols) => {
    if (isPasteHeaderRow(cols, fieldOrder)) {
      return [];
    }
    if (multilineCellMode === "smartSplit") {
      const plan = getSmartSplitPlan(cols, fieldOrder);
      if (plan.canSplit) {
        return Array.from({ length: plan.splitCount }, (_, rowIndex) => parsePasteColumns(createSmartSplitCols(cols, fieldOrder, rowIndex), fieldOrder, options));
      }
    }
    return [parsePasteColumns(cols, fieldOrder, options)];
  });
}

// src/pages/PosAdmin/LocalSupplierInvoices/InvoiceEdit/batchExecuteConfirm.ts
function getCurrentDetailAction(detail, rowActions) {
  return rowActions[detail.detailGUID] ?? detail.activityType ?? 0 /* None */;
}
function renderTemplate(template, values) {
  return Object.entries(values).reduce(
    (result, [key, value]) => result.replace(new RegExp(`{{${key}}}`, "g"), String(value)),
    template
  );
}
function countSelectedBatchExecuteActions(selectedRowKeys, details, rowActions) {
  const selectedKeys = new Set(selectedRowKeys.map(String));
  const selectedDetails = details.filter((item) => selectedKeys.has(item.detailGUID));
  const createDetails = selectedDetails.filter((item) => {
    const currentAction = getCurrentDetailAction(item, rowActions);
    return currentAction === 1 /* CreateProduct */;
  });
  return {
    selectedCount: selectedKeys.size,
    createProductCount: createDetails.length,
    createProductWithAdditionalBarcodesCount: createDetails.filter((item) => (item.additionalBarcodes?.length ?? 0) > 0).length
  };
}
function constrainSelectedRowKeysToVisibleDetails(selectedRowKeys, visibleDetails) {
  const visibleKeys = new Set(visibleDetails.map((item) => item.detailGUID));
  const nextSelectedRowKeys = selectedRowKeys.filter((key) => visibleKeys.has(String(key)));
  if (nextSelectedRowKeys.length === selectedRowKeys.length) {
    return selectedRowKeys;
  }
  return nextSelectedRowKeys;
}
function buildBatchExecuteConfirmText({
  selectedCount,
  createProductCount,
  labels
}) {
  const lines = [
    renderTemplate(labels.content, { count: selectedCount })
  ];
  if (createProductCount > 0) {
    lines.push(renderTemplate(labels.createProductNotice, { count: createProductCount }));
  }
  return {
    title: labels.title,
    content: lines.join("\n"),
    okText: labels.okText,
    cancelText: labels.cancelText
  };
}

// src/pages/PosAdmin/LocalSupplierInvoices/LocalSupplierInvoices.hqSync.logic.test.ts
function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}
function assertEqual(actual, expected, message) {
  if (actual !== expected) {
    throw new Error(`${message}\u3002Expected: ${String(expected)}, received: ${String(actual)}`);
  }
}
function assertDeepEqual(actual, expected, message) {
  const actualJson = JSON.stringify(actual);
  const expectedJson = JSON.stringify(expected);
  if (actualJson !== expectedJson) {
    throw new Error(`${message}\u3002Expected: ${expectedJson}, received: ${actualJson}`);
  }
}
async function assertRejects(execute, expectedMessage, message) {
  try {
    await execute();
  } catch (error) {
    const actualMessage = error instanceof Error ? error.message : String(error);
    assertEqual(actualMessage, expectedMessage, message);
    return;
  }
  throw new Error(`${message}\u3002Expected promise to reject`);
}
async function assertRequestErrorPayload(execute, expectedMessage, message) {
  try {
    await execute();
  } catch (error) {
    const actualMessage = error instanceof Error ? error.message : String(error);
    assertEqual(actualMessage, expectedMessage, message);
    assertEqual(error.payload?.data?.invoiceAddedCount, 1, "400 \u5931\u8D25\u65F6\u5E94\u4FDD\u7559\u540E\u7AEF data payload");
    assertEqual(error.payload?.data?.errors?.[0], "\u6D4B\u8BD5\u9875\u5931\u8D25", "400 \u5931\u8D25\u65F6\u5E94\u4FDD\u7559\u9519\u8BEF\u5217\u8868");
    return;
  }
  throw new Error(`${message}\u3002Expected promise to reject`);
}
async function runTest(name, execute) {
  try {
    await execute();
    console.log(`ok - ${name}`);
    return null;
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error);
    console.error(`not ok - ${name}`);
    console.error(reason);
    return `${name}: ${reason}`;
  }
}
var pageFile = path.resolve(process.cwd(), "src/pages/PosAdmin/LocalSupplierInvoices/index.tsx");
var editPageFile = path.resolve(process.cwd(), "src/pages/PosAdmin/LocalSupplierInvoices/InvoiceEdit/index.tsx");
var editCellsFile = path.resolve(process.cwd(), "src/pages/PosAdmin/LocalSupplierInvoices/InvoiceEdit/EditableCells.tsx");
var detailPageFile = path.resolve(process.cwd(), "src/pages/PosAdmin/LocalSupplierInvoiceDetailPage/index.tsx");
var serviceFile = path.resolve(process.cwd(), "src/services/localSupplierInvoiceService.ts");
var typeFile = path.resolve(process.cwd(), "src/types/localSupplierInvoice.ts");
var globalStyleFile = path.resolve(process.cwd(), "src/styles/global.css");
var pageSource = readFileSync(pageFile, "utf8");
var editPageSource = readFileSync(editPageFile, "utf8");
var editCellsSource = readFileSync(editCellsFile, "utf8");
var detailPageSource = readFileSync(detailPageFile, "utf8");
var serviceSource = readFileSync(serviceFile, "utf8");
var typeSource = readFileSync(typeFile, "utf8");
var globalStyleSource = readFileSync(globalStyleFile, "utf8");
async function main() {
  const failures = [];
  const typeFailure = await runTest("\u540C\u6B65\u8BF7\u6C42\u548C\u7ED3\u679C\u7C7B\u578B\u5E94\u58F0\u660E\u9875\u9762\u5951\u7EA6\u5B57\u6BB5", () => {
    assert(typeSource.includes("LocalSupplierInvoiceHqSyncRequest"), "\u5E94\u58F0\u660E\u540C\u6B65\u8BF7\u6C42\u7C7B\u578B");
    assert(typeSource.includes("selectedStoreCodes?: string[]"), "\u8BF7\u6C42\u5E94\u652F\u6301 selectedStoreCodes");
    assert(typeSource.includes("startDate?: string"), "\u8BF7\u6C42\u5E94\u652F\u6301 startDate");
    assert(typeSource.includes("endDate?: string"), "\u8BF7\u6C42\u5E94\u652F\u6301 endDate");
    assert(typeSource.includes("invoiceAddedCount: number"), "\u7ED3\u679C\u5E94\u652F\u6301\u4E3B\u8868\u65B0\u589E\u8BA1\u6570");
    assert(typeSource.includes("detailUpdatedCount: number"), "\u7ED3\u679C\u5E94\u652F\u6301\u660E\u7EC6\u66F4\u65B0\u8BA1\u6570");
  });
  if (typeFailure) failures.push(typeFailure);
  const ensureHqTypeFailure = await runTest("\u7F16\u8F91\u9875\u5546\u54C1\u540C\u6B65\u5230HQ\u5E94\u58F0\u660E\u4E13\u7528\u5951\u7EA6\u5B57\u6BB5", () => {
    assert(typeSource.includes("EnsureHqProductsRequest"), "\u5E94\u58F0\u660E EnsureHqProductsRequest");
    assert(typeSource.includes("detailGuids: string[]"), "\u5546\u54C1\u540C\u6B65\u8BF7\u6C42\u5E94\u5F3A\u5236\u4F20 detailGuids");
    assert(typeSource.includes("targetStoreCodes: string[]"), "\u5546\u54C1\u540C\u6B65\u8BF7\u6C42\u5E94\u5F3A\u5236\u4F20 targetStoreCodes\uFF0C\u907F\u514D\u540E\u7AEF\u6269\u5927\u5199\u5165\u8303\u56F4");
    assert(typeSource.includes("idempotencyKey?: string"), "\u5546\u54C1\u540C\u6B65\u8BF7\u6C42\u5E94\u652F\u6301 idempotencyKey");
    assert(typeSource.includes("EnsureHqProductsResult"), "\u5E94\u58F0\u660E EnsureHqProductsResult");
    assert(typeSource.includes("hqPurchasePricesUpdated: number"), "\u5546\u54C1\u540C\u6B65\u7ED3\u679C\u5E94\u5305\u542B HQ \u5206\u5E97\u8FDB\u8D27\u4EF7\u66F4\u65B0\u8BA1\u6570");
    assert(typeSource.includes("errors: EnsureHqProductError[]"), "\u5546\u54C1\u540C\u6B65\u7ED3\u679C\u5E94\u5305\u542B\u9010\u884C\u9519\u8BEF\u5217\u8868");
    assert(!typeSource.includes("updateHqProduct?: boolean"), "\u66F4\u65B0\u5230\u5206\u5E97\u8BF7\u6C42\u4E0D\u5E94\u518D\u643A\u5E26\u540C\u6B65 HQ \u5F00\u5173");
    assert(typeSource.includes("UpdateHqProductsRequest"), "\u5E94\u58F0\u660E\u5B57\u6BB5\u7EA7\u66F4\u65B0 HQ \u8BF7\u6C42\u7C7B\u578B");
    assert(typeSource.includes("updateFields: UpdateToStorePricesFields"), "\u5B57\u6BB5\u7EA7\u66F4\u65B0 HQ \u8BF7\u6C42\u5E94\u590D\u7528\u5B57\u6BB5\u9009\u62E9\u5951\u7EA6");
    assert(typeSource.includes("UpdateHqProductsResult"), "\u5E94\u58F0\u660E\u5B57\u6BB5\u7EA7\u66F4\u65B0 HQ \u7ED3\u679C\u7C7B\u578B");
    assert(typeSource.includes("hqRetailPricesUpdated?: number"), "\u5B57\u6BB5\u7EA7\u66F4\u65B0 HQ \u7ED3\u679C\u5E94\u5305\u542B\u96F6\u552E\u4EF7\u66F4\u65B0\u8BA1\u6570");
    assert(typeSource.includes("hqDiscountRatesUpdated?: number"), "\u5B57\u6BB5\u7EA7\u66F4\u65B0 HQ \u7ED3\u679C\u5E94\u5305\u542B\u6298\u6263\u7387\u66F4\u65B0\u8BA1\u6570");
    assert(typeSource.includes("hqProductSetCodesCreated?: number"), "\u5B57\u6BB5\u7EA7\u66F4\u65B0 HQ \u7ED3\u679C\u5E94\u5305\u542B HQ \u4E00\u54C1\u591A\u7801\u65B0\u589E\u8BA1\u6570");
    assert(typeSource.includes("hqProductSetCodesUpdated?: number"), "\u5B57\u6BB5\u7EA7\u66F4\u65B0 HQ \u7ED3\u679C\u5E94\u5305\u542B HQ \u4E00\u54C1\u591A\u7801\u66F4\u65B0\u8BA1\u6570");
    assert(typeSource.includes("hqStoreMultiCodesCreated?: number"), "\u5B57\u6BB5\u7EA7\u66F4\u65B0 HQ \u7ED3\u679C\u5E94\u5305\u542B HQ \u5206\u5E97\u4E00\u54C1\u591A\u7801\u65B0\u589E\u8BA1\u6570");
    assert(typeSource.includes("hqStoreMultiCodesUpdated?: number"), "\u5B57\u6BB5\u7EA7\u66F4\u65B0 HQ \u7ED3\u679C\u5E94\u5305\u542B HQ \u5206\u5E97\u4E00\u54C1\u591A\u7801\u66F4\u65B0\u8BA1\u6570");
    assert(typeSource.includes("LocalSupplierInvoiceBatchJobStatus"), "\u672C\u5730\u8FDB\u8D27\u5355\u6279\u91CF\u540E\u53F0\u4EFB\u52A1\u5E94\u58F0\u660E\u72B6\u6001\u7C7B\u578B");
    assert(typeSource.includes("UpdateToStorePricesJobResult"), "\u66F4\u65B0\u5230\u5206\u5E97\u5E94\u58F0\u660E\u540E\u53F0\u4EFB\u52A1\u7ED3\u679C\u7C7B\u578B");
    assert(typeSource.includes("UpdateHqProductsJobResult"), "\u66F4\u65B0HQ\u5546\u54C1\u5E94\u58F0\u660E\u540E\u53F0\u4EFB\u52A1\u7ED3\u679C\u7C7B\u578B");
    assert(typeSource.includes("isDuplicateRequest?: boolean"), "\u540E\u53F0\u4EFB\u52A1\u7ED3\u679C\u5E94\u652F\u6301\u91CD\u590D\u63D0\u4EA4\u6807\u8BB0");
    assert(typeSource.includes("PasteDetailsJobResult"), "\u7C98\u8D34\u660E\u7EC6\u5E94\u58F0\u660E\u540E\u53F0\u4EFB\u52A1\u7ED3\u679C\u7C7B\u578B");
    assert(typeSource.includes("result?: BatchResultDto"), "\u7C98\u8D34\u660E\u7EC6\u540E\u53F0\u4EFB\u52A1 result \u5E94\u590D\u7528 BatchResultDto");
    assert(typeSource.includes("CheckProductsJobResult"), "\u5546\u54C1\u68C0\u6D4B\u5E94\u58F0\u660E\u540E\u53F0\u4EFB\u52A1\u7ED3\u679C\u7C7B\u578B");
    assert(typeSource.includes("result?: CheckProductsResponse"), "\u5546\u54C1\u68C0\u6D4B\u540E\u53F0\u4EFB\u52A1 result \u5E94\u590D\u7528 CheckProductsResponse");
  });
  if (ensureHqTypeFailure) failures.push(ensureHqTypeFailure);
  const updateHqMultiCodeResultFailure = await runTest("\u7F16\u8F91\u9875\u66F4\u65B0HQ\u5546\u54C1\u7ED3\u679C\u5E94\u5C55\u793A\u591A\u7801\u540C\u6B65\u7EDF\u8BA1", () => {
    assert(editPageSource.includes("result.hqProductSetCodesCreated"), "\u7ED3\u679C\u5F39\u7A97\u5E94\u5C55\u793A HQ \u4E00\u54C1\u591A\u7801\u65B0\u589E\u6570\u91CF");
    assert(editPageSource.includes("result.hqProductSetCodesUpdated"), "\u7ED3\u679C\u5F39\u7A97\u5E94\u5C55\u793A HQ \u4E00\u54C1\u591A\u7801\u66F4\u65B0\u6570\u91CF");
    assert(editPageSource.includes("result.hqStoreMultiCodesCreated"), "\u7ED3\u679C\u5F39\u7A97\u5E94\u5C55\u793A HQ \u5206\u5E97\u4E00\u54C1\u591A\u7801\u65B0\u589E\u6570\u91CF");
    assert(editPageSource.includes("result.hqStoreMultiCodesUpdated"), "\u7ED3\u679C\u5F39\u7A97\u5E94\u5C55\u793A HQ \u5206\u5E97\u4E00\u54C1\u591A\u7801\u66F4\u65B0\u6570\u91CF");
  });
  if (updateHqMultiCodeResultFailure) failures.push(updateHqMultiCodeResultFailure);
  const invoiceDetailKeepAliveFailure = await runTest("\u5206\u5E97\u8FDB\u8D27\u5355\u8BE6\u60C5 Tab \u5207\u56DE\u5DF2\u6709\u8FDB\u8D27\u5355\u65F6\u5E94\u8DF3\u8FC7\u81EA\u52A8\u5237\u65B0", () => {
    for (const [pageName, source] of [
      ["\u7F16\u8F91\u9875", editPageSource],
      ["\u53EA\u8BFB\u8BE6\u60C5\u9875", detailPageSource]
    ]) {
      assert(
        source.includes("loadedInvoiceGuidRef") && source.includes("useKeepAliveContext") && source.includes("const { active } = useKeepAliveContext()") && source.includes("if (!active) return") && source.includes("visibleInvoiceGuidRef") && source.includes("lastLoadedManagedStoreCodeKeyRef") && source.includes("shouldSkipDetailAutoReload({") && source.includes("requestedDetailQueryKey: managedStoreCodeKey") && source.includes("loadedDetailQueryKey: lastLoadedManagedStoreCodeKeyRef.current") && source.includes("shouldShowDetailInitialLoading({") && source.includes("active,") && source.includes("return"),
        `${pageName} \u7F3A\u5C11 KeepAlive active \u5B88\u536B\uFF0C\u9690\u85CF Tab \u4F1A\u8DDF\u968F\u5168\u5C40\u8DEF\u7531\u53D8\u5316\u91CD\u65B0\u8BF7\u6C42`
      );
      assert(
        source.includes("const loadDetails") && source.includes("showLoading = true") && source.includes("if (showLoading)") && source.includes("setDetailLoading(true)") && source.includes("setDetailLoading(false)") && source.includes("await loadDetails(showLoading)"),
        `${pageName} \u660E\u7EC6\u52A0\u8F7D\u5E94\u4FDD\u7559 showLoading \u53C2\u6570\uFF1B\u624B\u52A8\u6216\u4E1A\u52A1\u663E\u5F0F\u5237\u65B0\u4ECD\u5E94\u53EF\u663E\u793A loading`
      );
      assert(
        source.includes("invoiceSnapshotRef") && source.includes("detailsSnapshotRef") && source.includes("areLocalSupplierInvoicesEqual(invoiceSnapshotRef.current, data)") && source.includes("areLocalSupplierInvoiceDetailsEqual(detailsSnapshotRef.current, data)"),
        `${pageName} \u540E\u53F0\u8FD4\u56DE\u76F8\u540C\u8BA2\u5355\u5934\u548C\u660E\u7EC6\u65F6\u5E94\u8DF3\u8FC7 setFieldsValue/setDetails\uFF0C\u907F\u514D\u76F8\u540C\u6570\u636E\u91CD\u7ED8\u4E00\u95EA`
      );
    }
    assert(
      editPageSource.includes("additionalBarcodes: item.additionalBarcodes"),
      "\u7F16\u8F91\u9875\u660E\u7EC6\u5FEB\u7167\u5E94\u5305\u542B additionalBarcodes\uFF0C\u526F\u7801\u53D8\u5316\u540E\u7C98\u8D34\u5B8C\u6210 reload \u624D\u4F1A\u5237\u65B0\u8868\u683C\u63D0\u793A"
    );
    assert(
      shouldSkipDetailAutoReload({
        requestedDetailId: "invoice-1",
        loadedDetailId: "invoice-1",
        visibleDetailId: "invoice-1",
        requestedDetailQueryKey: "1012",
        loadedDetailQueryKey: "1012"
      }) && !shouldSkipDetailAutoReload({
        requestedDetailId: "invoice-2",
        loadedDetailId: "invoice-1",
        visibleDetailId: "invoice-1"
      }) && !shouldSkipDetailAutoReload({
        requestedDetailId: "invoice-1",
        loadedDetailId: "invoice-1",
        visibleDetailId: "invoice-1",
        requestedDetailQueryKey: "1012",
        loadedDetailQueryKey: "1033"
      }),
      "\u540C\u8FDB\u8D27\u5355\u4E14\u6743\u9650\u8303\u56F4\u4E00\u81F4\u65F6\u5E94\u8DF3\u8FC7\u81EA\u52A8\u5237\u65B0\uFF0C\u5207\u6362\u65B0\u8FDB\u8D27\u5355\u6216\u6743\u9650\u8303\u56F4\u53D8\u5316\u65F6\u4E0D\u5E94\u8DF3\u8FC7"
    );
  });
  if (invoiceDetailKeepAliveFailure) failures.push(invoiceDetailKeepAliveFailure);
  const jobTypeFailure = await runTest("\u66F4\u65B0\u5230\u5206\u5E97\u548C\u66F4\u65B0HQ\u5546\u54C1\u5E94\u58F0\u660E\u540E\u53F0 Job \u5951\u7EA6\u5B57\u6BB5", () => {
    assert(typeSource.includes("export interface LocalSupplierInvoiceJobBase"), "\u5E94\u58F0\u660E\u672C\u5730\u8FDB\u8D27\u5355\u540E\u53F0 Job \u57FA\u7840\u7C7B\u578B");
    assert(typeSource.includes("jobId: string"), "\u540E\u53F0 Job \u5E94\u58F0\u660E jobId");
    assert(typeSource.includes("targetStoreCodes?: string[]"), "\u540E\u53F0 Job \u5E94\u58F0\u660E\u76EE\u6807\u5206\u5E97\u7F16\u7801\u7528\u4E8E\u6743\u9650\u590D\u9A8C");
    assert(typeSource.includes("operationId: string"), "\u540E\u53F0 Job \u5E94\u58F0\u660E operationId");
    assert(typeSource.includes("status:"), "\u540E\u53F0 Job \u5E94\u58F0\u660E status");
    assert(typeSource.includes("isDuplicateRequest?: boolean"), "\u540E\u53F0 Job \u5E94\u58F0\u660E isDuplicateRequest");
    assert(typeSource.includes("createdAt?: string"), "\u540E\u53F0 Job \u5E94\u58F0\u660E createdAt");
    assert(typeSource.includes("completedAt?: string"), "\u540E\u53F0 Job \u5E94\u58F0\u660E completedAt");
    assert(typeSource.includes("expiresAt?: string"), "\u540E\u53F0 Job \u5E94\u58F0\u660E expiresAt");
    assert(typeSource.includes("message?: string"), "\u540E\u53F0 Job \u5E94\u58F0\u660E message");
    assert(typeSource.includes("export interface UpdateToStorePricesJobDto"), "\u5E94\u58F0\u660E\u66F4\u65B0\u5230\u5206\u5E97 Job DTO");
    assert(typeSource.includes("result?: UpdateToStorePricesResult"), "\u66F4\u65B0\u5230\u5206\u5E97 Job result \u5E94\u590D\u7528 UpdateToStorePricesResult");
    assert(typeSource.includes("export interface UpdateHqProductsJobDto"), "\u5E94\u58F0\u660E\u66F4\u65B0HQ\u5546\u54C1 Job DTO");
    assert(typeSource.includes("result?: UpdateHqProductsResult"), "\u66F4\u65B0HQ\u5546\u54C1 Job result \u5E94\u590D\u7528 UpdateHqProductsResult");
  });
  if (jobTypeFailure) failures.push(jobTypeFailure);
  const pageButtonFailure = await runTest("\u9875\u9762\u5E94\u7ED9\u7BA1\u7406\u5458\u663E\u793A\u4ECEHQ\u540C\u6B65\u6309\u94AE\u5E76\u6253\u5F00\u5F39\u7A97", () => {
    assert(pageSource.includes("CloudSyncOutlined"), "\u9875\u9762\u5E94\u4F7F\u7528\u540C\u6B65\u56FE\u6807");
    assert(pageSource.includes("t('posAdmin.invoices.syncFromHQ'"), "\u9875\u9762\u5E94\u5B58\u5728\u4ECEHQ\u540C\u6B65\u6309\u94AE\u6587\u6848");
    assert(pageSource.includes("isAdmin &&") && pageSource.includes("setHqSyncModalOpen(true)"), "\u6309\u94AE\u5E94\u4EC5\u7BA1\u7406\u5458\u53EF\u89C1\u5E76\u6253\u5F00\u540C\u6B65\u5F39\u7A97");
  });
  if (pageButtonFailure) failures.push(pageButtonFailure);
  const pagePayloadFailure = await runTest("\u9875\u9762\u5E94\u4ECE\u5F39\u7A97\u63D0\u4EA4\u5206\u5E97\u548C\u65E5\u671F\u8303\u56F4", () => {
    assert(pageSource.includes("hqSyncForm.validateFields()"), "\u540C\u6B65\u524D\u5E94\u6821\u9A8C\u5F39\u7A97\u8868\u5355");
    assert(pageSource.includes("dto.startDate = values.dateRange[0].format('YYYY-MM-DD')"), "\u9875\u9762\u5E94\u4F20 startDate");
    assert(pageSource.includes("dto.endDate = values.dateRange[1].format('YYYY-MM-DD')"), "\u9875\u9762\u5E94\u4F20 endDate");
    assert(pageSource.includes("dto.selectedStoreCodes = values.selectedStoreCodes"), "\u9875\u9762\u5E94\u4F20 selectedStoreCodes");
  });
  if (pagePayloadFailure) failures.push(pagePayloadFailure);
  const listPaginationLayoutFailure = await runTest("\u5217\u8868\u9875\u8868\u683C\u6EDA\u52A8\u533A\u57DF\u4E0D\u5E94\u8986\u76D6\u5916\u7F6E\u5206\u9875", () => {
    assert(pageSource.includes("const tableRegionRef = useRef<HTMLDivElement>(null)"), "\u5217\u8868\u9875\u5E94\u58F0\u660E\u8868\u683C\u533A\u57DF ref");
    assert(pageSource.includes("region.querySelector('.ant-table-thead')"), "\u8868\u4F53\u9AD8\u5EA6\u8BA1\u7B97\u5FC5\u987B\u6263\u9664 AntD \u8868\u5934");
    assert(pageSource.includes("horizontalScrollbarHeight"), "\u8868\u4F53\u9AD8\u5EA6\u8BA1\u7B97\u5FC5\u987B\u6263\u9664\u6A2A\u5411\u6EDA\u52A8\u6761\u9AD8\u5EA6");
    assert(
      pageSource.includes("region.clientHeight - tableHeaderHeight - horizontalScrollbarHeight - 8"),
      "\u8868\u4F53\u9AD8\u5EA6\u5FC5\u987B\u6309\u8868\u683C\u533A\u57DF\u6263\u9664\u8868\u5934\u548C\u6A2A\u5411\u6EDA\u52A8\u6761\u8BA1\u7B97"
    );
    assert(
      pageSource.includes("window.requestAnimationFrame") && pageSource.includes("ResizeObserver"),
      "\u8868\u683C\u9AD8\u5EA6\u5E94\u5728\u5E03\u5C40\u53D8\u5316\u540E\u91CD\u65B0\u6D4B\u91CF"
    );
    assert(
      pageSource.includes("ref={tableRegionRef}") && pageSource.includes("flex: 1") && pageSource.includes("minHeight: 0") && pageSource.includes("overflow: 'hidden'"),
      "\u8868\u683C\u533A\u57DF\u5FC5\u987B\u88C1\u526A\u6EA2\u51FA\uFF0C\u907F\u514D\u56FA\u5B9A\u5217\u753B\u5230\u5206\u9875\u680F"
    );
    assert(
      pageSource.includes("position: 'relative'") && pageSource.includes("zIndex: 3") && pageSource.includes("flexShrink: 0"),
      "\u5206\u9875\u680F\u5E94\u4FDD\u6301\u72EC\u7ACB\u5C42\u7EA7\u548C\u56FA\u5B9A\u5E95\u90E8\u7A7A\u95F4"
    );
  });
  if (listPaginationLayoutFailure) failures.push(listPaginationLayoutFailure);
  const editPageButtonFailure = await runTest("\u7F16\u8F91\u9875\u5E94\u4F7F\u7528\u4E13\u7528\u6743\u9650\u663E\u793A\u66F4\u65B0HQ\u5546\u54C1\u6309\u94AE", () => {
    assert(editPageSource.includes("canWriteLocalPurchaseToHq"), "\u7F16\u8F91\u9875\u5E94\u4F7F\u7528\u53EF\u7F16\u8F91\u672C\u5730\u8FDB\u8D27 + PushToHq \u7684\u7EC4\u5408\u6743\u9650\u63A7\u5236\u5199 HQ \u5165\u53E3");
    assert(editPageSource.includes("t('posAdmin.invoiceDetail.updateHqProductsBtn', '\u66F4\u65B0HQ\u5546\u54C1')"), "\u7F16\u8F91\u9875\u5E94\u663E\u793A\u66F4\u65B0HQ\u5546\u54C1\u6309\u94AE\u6587\u6848");
    assert(editPageSource.includes("setHqUpdateVisible(true)"), "\u66F4\u65B0HQ\u5546\u54C1\u6309\u94AE\u5E94\u6253\u5F00\u72EC\u7ACB\u5F39\u7A97");
    assert(editPageSource.includes("disabled={hqUpdateLoading || !selectedRowKeys.length}"), "\u66F4\u65B0HQ\u5546\u54C1\u6309\u94AE\u5FC5\u987B\u8981\u6C42\u5DF2\u9009\u62E9\u660E\u7EC6");
    assert(editPageSource.includes("handleUpdateHqProducts"), "\u7F16\u8F91\u9875\u5E94\u5B9E\u73B0\u5B57\u6BB5\u7EA7\u66F4\u65B0HQ\u5904\u7406\u51FD\u6570");
    assert(editPageSource.includes("startUpdateHqProductsJob(invoiceGuid"), "\u7F16\u8F91\u9875\u5E94\u63D0\u4EA4\u5B57\u6BB5\u7EA7\u66F4\u65B0 HQ \u540E\u53F0\u4EFB\u52A1");
    assert(editPageSource.includes("getUpdateHqProductsJob(invoiceGuid"), "\u7F16\u8F91\u9875\u5E94\u8F6E\u8BE2\u5B57\u6BB5\u7EA7\u66F4\u65B0 HQ \u540E\u53F0\u4EFB\u52A1");
    assert(editPageSource.includes("completedJob.status === 'Failed'"), "\u66F4\u65B0HQ\u5546\u54C1\u8F6E\u8BE2\u5230 Failed Job \u65F6\u5E94\u5C55\u793A\u5931\u8D25\u800C\u4E0D\u662F\u5B8C\u6210");
    assert(!editPageSource.includes("const result = await updateHqProducts(invoiceGuid"), "\u7F16\u8F91\u9875\u4E0D\u5E94\u518D\u76F4\u63A5\u7B49\u5F85\u5B57\u6BB5\u7EA7\u66F4\u65B0 HQ \u957F\u8BF7\u6C42");
    assert(editPageSource.includes("if (hqUpdateLoading) return"), "\u66F4\u65B0HQ\u5546\u54C1\u5E94\u963B\u6B62\u91CD\u590D\u8BF7\u6C42");
    assert(editPageSource.includes("hqUpdateIdempotencyKeyRef.current"), "\u66F4\u65B0HQ\u5546\u54C1\u5E94\u5728\u8BF7\u6C42\u5468\u671F\u5185\u590D\u7528\u7A33\u5B9A idempotencyKey");
  });
  if (editPageButtonFailure) failures.push(editPageButtonFailure);
  const editPageImportFailure = await runTest("\u7F16\u8F91\u9875\u4E0D\u5E94\u52A8\u6001\u5BFC\u5165\u5DF2\u9759\u6001\u4F7F\u7528\u7684\u672C\u5730\u4F9B\u5E94\u5546\u8FDB\u8D27\u5355\u670D\u52A1", () => {
    assert(
      !editPageSource.includes("await import('../../../../services/localSupplierInvoiceService')"),
      "\u7F16\u8F91\u9875\u5DF2\u9759\u6001\u5BFC\u5165\u8BE5\u670D\u52A1\uFF0C\u4E0D\u80FD\u518D\u52A8\u6001\u5BFC\u5165\u540C\u4E00\u6A21\u5757\uFF0C\u5426\u5219 Vite \u4F1A\u63D0\u793A\u52A8\u6001\u5BFC\u5165\u65E0\u6CD5\u62C6\u5206 chunk"
    );
    assert(editPageSource.includes("updateDetailAction,"), "\u7F16\u8F91\u9875\u5E94\u9759\u6001\u5BFC\u5165\u66F4\u65B0\u884C\u64CD\u4F5C\u63A5\u53E3");
  });
  if (editPageImportFailure) failures.push(editPageImportFailure);
  const editPageDynamicTabTitleFailure = await runTest("\u7F16\u8F91\u9875 Tab \u6807\u9898\u5E94\u5728\u53D1\u7968\u52A0\u8F7D\u540E\u663E\u793A\u4E1A\u52A1\u6807\u9898", () => {
    assert(
      editPageSource.includes("import { useDynamicTabTitle } from '../../../../hooks/useDynamicTabTitle'"),
      "\u7F16\u8F91\u9875\u5FC5\u987B import useDynamicTabTitle"
    );
    assert(editPageSource.includes("useDynamicTabTitle(invoiceTabTitle)"), "\u7F16\u8F91\u9875\u5FC5\u987B\u5728\u7EC4\u4EF6\u9876\u5C42\u8C03\u7528 useDynamicTabTitle");
    assert(editPageSource.includes("function buildInvoiceTabTitle("), "\u7F16\u8F91\u9875\u5E94\u63D0\u4F9B\u672C\u9875\u4E13\u7528\u6807\u9898\u683C\u5F0F\u51FD\u6570");
    assert(editPageSource.includes("invoice.storeName?.trim()"), "\u6807\u9898\u51FD\u6570\u5FC5\u987B\u4F18\u5148\u8BFB\u53D6 storeName");
    assert(editPageSource.includes("invoice.storeCode?.trim()"), "\u5206\u5E97\u540D\u79F0\u7F3A\u5931\u65F6\u5FC5\u987B\u56DE\u9000 storeCode");
    assert(editPageSource.includes("invoice.supplierName?.trim().slice(0, 4).toUpperCase()"), "\u4F9B\u5E94\u5546\u540D\u79F0\u5FC5\u987B trim \u540E\u53D6\u524D 4 \u4F4D\u5E76\u8F6C\u5927\u5199");
    assert(editPageSource.includes("invoice.supplierCode?.trim().slice(0, 4).toUpperCase()"), "\u4F9B\u5E94\u5546\u540D\u79F0\u7F3A\u5931\u65F6\u5FC5\u987B\u56DE\u9000 supplierCode \u524D 4 \u4F4D");
    assert(editPageSource.includes("invoice.invoiceNo?.trim()"), "\u6807\u9898\u51FD\u6570\u5FC5\u987B\u8BFB\u53D6 invoiceNo");
    assert(editPageSource.includes("[storeSegment, supplierSegment, invoiceNoSegment].filter(Boolean).join"), "\u6807\u9898\u51FD\u6570\u5E94\u8FC7\u6EE4\u7F3A\u5931\u5B57\u6BB5\u540E\u7528\u7A7A\u683C\u62FC\u63A5");
    assert(
      editPageSource.includes("t('menu.editInvoice', '\u7F16\u8F91\u8FDB\u8D27\u5355')"),
      "\u672A\u52A0\u8F7D\u53D1\u7968\u524D\u5FC5\u987B\u4FDD\u7559\u7F16\u8F91\u8FDB\u8D27\u5355 fallback\uFF0C\u907F\u514D Tab \u7A7A\u767D"
    );
    assert(
      editPageSource.includes("\u8FD9\u91CC\u53EA\u66F4\u65B0\u5F53\u524D\u7F16\u8F91\u9875\u7684 KeepAlive Tab \u6807\u9898\uFF0C\u4E0D\u6539\u53D8\u8DEF\u7531\u6807\u9898\u6216\u9762\u5305\u5C51\u3002"),
      "\u5E94\u7528\u4E2D\u6587\u6CE8\u91CA\u8BF4\u660E\u52A8\u6001\u6807\u9898\u53EA\u5F71\u54CD\u5F53\u524D\u7F16\u8F91\u9875 Tab"
    );
  });
  if (editPageDynamicTabTitleFailure) failures.push(editPageDynamicTabTitleFailure);
  const batchEditPersistFailure = await runTest("\u7F16\u8F91\u9875\u6279\u91CF\u7F16\u8F91\u5E94\u901A\u8FC7\u6279\u91CF\u66F4\u65B0\u63A5\u53E3\u6301\u4E45\u5316 editFields", () => {
    assert(editPageSource.includes("batchUpdateDetails,"), "\u7F16\u8F91\u9875\u5E94\u9759\u6001\u5BFC\u5165\u6279\u91CF\u66F4\u65B0\u660E\u7EC6\u63A5\u53E3");
    assert(
      editPageSource.includes("await batchUpdateDetails(submittedInvoiceGuid, items, editFields)"),
      "\u6279\u91CF\u7F16\u8F91\u5E94\u628A editFields \u53D1\u9001\u5230\u540E\u7AEF\uFF0C\u907F\u514D\u81EA\u52A8\u5B9A\u4EF7\u53EA\u5728\u672C\u5730\u4E34\u65F6\u53D8\u5316"
    );
    assert(editPageSource.includes("applyInvoiceDetailBatchEdit(prev, submittedDetailGuids, editFields)"), "\u6279\u91CF\u7F16\u8F91\u786E\u8BA4\u540E\u5E94\u5148\u4E50\u89C2\u66F4\u65B0\u524D\u7AEF\u5F53\u524D\u660E\u7EC6");
    const batchEditStart = editPageSource.indexOf("const handleBatchEdit = async () => {");
    const storePriceStart = editPageSource.indexOf("const openStorePriceModal", batchEditStart);
    const batchEditSource = editPageSource.slice(batchEditStart, storePriceStart);
    assert(batchEditSource.includes("setBatchEditVisible(false)") && batchEditSource.indexOf("setBatchEditVisible(false)") < batchEditSource.indexOf("await batchUpdateDetails(submittedInvoiceGuid, items, editFields)"), "\u6279\u91CF\u7F16\u8F91\u5E94\u5148\u5173\u95ED\u5F39\u7A97\uFF0C\u518D\u540E\u53F0\u63D0\u4EA4\u540E\u7AEF");
    assert(batchEditSource.includes("setSelectedRowKeys([])") && batchEditSource.indexOf("setSelectedRowKeys([])") < batchEditSource.indexOf("await batchUpdateDetails(submittedInvoiceGuid, items, editFields)"), "\u6279\u91CF\u7F16\u8F91\u5E94\u5148\u6E05\u7A7A\u9009\u62E9\uFF0C\u518D\u540E\u53F0\u63D0\u4EA4\u540E\u7AEF");
    assert(batchEditSource.includes("t('posAdmin.invoiceDetail.batchUpdateSubmitted', '\u6279\u91CF\u66F4\u65B0\u5DF2\u63D0\u4EA4')"), "\u6279\u91CF\u7F16\u8F91\u5E94\u63D0\u793A\u540E\u53F0\u66F4\u65B0\u5DF2\u63D0\u4EA4");
    assert(!batchEditSource.includes("message.success(t('posAdmin.invoiceDetail.batchUpdateSuccess', '\u6279\u91CF\u66F4\u65B0\u6210\u529F'))\n        await loadDetails()"), "\u6279\u91CF\u7F16\u8F91\u6210\u529F\u8DEF\u5F84\u4E0D\u5E94\u7B49\u5F85\u5168\u91CF\u5237\u65B0");
    assert(batchEditSource.includes("if (canApplyInvoiceJobResult(currentInvoiceGuidRef.current, submittedInvoiceGuid))"), "\u6279\u91CF\u7F16\u8F91\u5931\u8D25\u540E\u5E94\u53EA\u5237\u65B0\u5F53\u524D\u8FDB\u8D27\u5355");
    assert(batchEditSource.includes("await loadDetails()"), "\u6279\u91CF\u7F16\u8F91\u540E\u53F0\u5931\u8D25\u65F6\u5E94\u5237\u65B0\u670D\u52A1\u7AEF\u660E\u7EC6");
    assert(
      !editPageSource.includes("await batchUpsertDetails(invoiceGuid, items)\n      // \u4F7F\u7528 batchUpdateDetails \u7684\u66FF\u4EE3\u65B9\u6848"),
      "\u6279\u91CF\u7F16\u8F91\u4E0D\u80FD\u7528\u7A7A items \u7684 batchUpsertDetails \u66FF\u4EE3\u6279\u91CF\u66F4\u65B0"
    );
  });
  if (batchEditPersistFailure) failures.push(batchEditPersistFailure);
  const updateToStoreHqFailure = await runTest("\u66F4\u65B0\u5230\u5206\u5E97\u5E94\u79FB\u9664\u540C\u6B65HQ\u8026\u5408\u5E76\u4FDD\u7559\u72EC\u7ACBHQ\u5F39\u7A97", () => {
    const storeModalStart = editPageSource.indexOf("{/* \u66F4\u65B0\u5230\u5206\u5E97\u4EF7\u683C Modal");
    const hqModalStart = editPageSource.indexOf("{/* \u66F4\u65B0 HQ \u5546\u54C1 Modal");
    const storeModalSource = editPageSource.slice(storeModalStart, hqModalStart);
    assert(!editPageSource.includes('name="updateHqProduct"'), "\u66F4\u65B0\u5230\u5206\u5E97\u5F39\u7A97\u4E0D\u5E94\u518D\u5305\u542B updateHqProduct \u590D\u9009\u6846");
    assert(!editPageSource.includes("confirmUpdateToStorePrices"), "\u66F4\u65B0\u5230\u5206\u5E97\u4E0D\u5E94\u518D\u5305\u542B\u540C\u65F6\u66F4\u65B0 HQ \u7684\u4E8C\u6B21\u786E\u8BA4");
    assert(!editPageSource.includes("showUpdateToStoreHqResult"), "\u66F4\u65B0\u5230\u5206\u5E97\u4E0D\u5E94\u518D\u5C55\u793A HQ \u6DF7\u5408\u7ED3\u679C");
    assert(!editPageSource.includes("updateHqProduct:"), "\u66F4\u65B0\u5230\u5206\u5E97\u8BF7\u6C42\u4E0D\u5E94\u518D\u4F20\u9012 updateHqProduct");
    assert(editPageSource.includes("hqUpdateForm.validateFields()"), "\u66F4\u65B0HQ\u5546\u54C1\u5E94\u6821\u9A8C\u72EC\u7ACB\u5F39\u7A97\u8868\u5355");
    assert(editPageSource.includes("startUpdateToStorePricesJob(request)"), "\u66F4\u65B0\u5230\u5206\u5E97\u5E94\u63D0\u4EA4\u540E\u53F0\u4EFB\u52A1");
    assert(editPageSource.includes("getUpdateToStorePricesJob(jobId)"), "\u66F4\u65B0\u5230\u5206\u5E97\u5E94\u8F6E\u8BE2\u540E\u53F0\u4EFB\u52A1");
    assert(storeModalSource.includes("initialValues={{ updatePurchasePrice: true }}"), "\u66F4\u65B0\u5230\u5206\u5E97\u5F39\u7A97\u9ED8\u8BA4\u5E94\u52FE\u9009\u66F4\u65B0\u8FDB\u8D27\u4EF7");
    assert(storeModalSource.includes('name="updatePurchasePrice"'), "\u66F4\u65B0\u5230\u5206\u5E97\u5F39\u7A97\u5E94\u4FDD\u7559\u66F4\u65B0\u8FDB\u8D27\u4EF7\u5B57\u6BB5\u5F00\u5173");
    assert(storeModalSource.includes('name="updateRetailPrice"'), "\u66F4\u65B0\u5230\u5206\u5E97\u5F39\u7A97\u5E94\u4FDD\u7559\u66F4\u65B0\u96F6\u552E\u4EF7\u5B57\u6BB5\u5F00\u5173");
    assert(storeModalSource.includes('name="updateIsAutoPricing"'), "\u66F4\u65B0\u5230\u5206\u5E97\u5F39\u7A97\u5E94\u4FDD\u7559\u66F4\u65B0\u81EA\u52A8\u5B9A\u4EF7\u5B57\u6BB5\u5F00\u5173");
    assert(storeModalSource.includes('name="updateIsSpecialProduct"'), "\u66F4\u65B0\u5230\u5206\u5E97\u5F39\u7A97\u5E94\u4FDD\u7559\u66F4\u65B0\u7279\u6B8A\u5546\u54C1\u5B57\u6BB5\u5F00\u5173");
    assert(storeModalSource.includes('name="updateDiscountRate"'), "\u66F4\u65B0\u5230\u5206\u5E97\u5F39\u7A97\u5E94\u4FDD\u7559\u66F4\u65B0\u6298\u6263\u7387\u5B57\u6BB5\u5F00\u5173");
    assert(!storeModalSource.includes('name="purchasePrice"'), "\u66F4\u65B0\u5230\u5206\u5E97\u5F39\u7A97\u4E0D\u5E94\u63D0\u4F9B\u8FDB\u8D27\u4EF7\u8986\u76D6\u503C\uFF0C\u5E94\u6309\u524D\u7AEF\u660E\u7EC6\u884C\u5199\u5165");
    assert(!storeModalSource.includes('name="retailPrice"'), "\u66F4\u65B0\u5230\u5206\u5E97\u5F39\u7A97\u4E0D\u5E94\u63D0\u4F9B\u96F6\u552E\u4EF7\u8986\u76D6\u503C\uFF0C\u5E94\u6309\u524D\u7AEF\u660E\u7EC6\u884C\u5199\u5165");
    assert(!storeModalSource.includes('name="isAutoPricing"'), "\u66F4\u65B0\u5230\u5206\u5E97\u5F39\u7A97\u4E0D\u5E94\u63D0\u4F9B\u81EA\u52A8\u5B9A\u4EF7\u8986\u76D6\u503C\uFF0C\u5E94\u6309\u524D\u7AEF\u660E\u7EC6\u884C\u5199\u5165");
    assert(!storeModalSource.includes('name="isSpecialProduct"'), "\u66F4\u65B0\u5230\u5206\u5E97\u5F39\u7A97\u4E0D\u5E94\u63D0\u4F9B\u7279\u6B8A\u5546\u54C1\u8986\u76D6\u503C\uFF0C\u5E94\u6309\u524D\u7AEF\u660E\u7EC6\u884C\u5199\u5165");
    assert(!storeModalSource.includes('name="discountRate"'), "\u66F4\u65B0\u5230\u5206\u5E97\u5F39\u7A97\u4E0D\u5E94\u63D0\u4F9B\u6298\u6263\u7387\u8986\u76D6\u503C\uFF0C\u5E94\u6309\u524D\u7AEF\u660E\u7EC6\u884C\u5199\u5165");
    assert(editPageSource.includes("const storePriceDetailSet = new Set(request.detailGuids)"), "\u66F4\u65B0\u5230\u5206\u5E97\u5E94\u6309\u9009\u4E2D\u7684\u524D\u7AEF\u660E\u7EC6\u884C\u6784\u5EFA\u4FDD\u5B58\u8303\u56F4");
    assert(editPageSource.includes("await batchUpsertDetails(invoiceGuid, buildInvoiceDetailSaveItems(storePriceDetails))"), "\u66F4\u65B0\u5230\u5206\u5E97\u63D0\u4EA4\u524D\u5E94\u5148\u4FDD\u5B58\u524D\u7AEF\u5F53\u524D\u9009\u4E2D\u660E\u7EC6\u884C");
    assert(
      editPageSource.indexOf("await batchUpsertDetails(invoiceGuid, buildInvoiceDetailSaveItems(storePriceDetails))") < editPageSource.indexOf("const job = await startUpdateToStorePricesJob(request)"),
      "\u66F4\u65B0\u5230\u5206\u5E97\u5E94\u5148\u4FDD\u5B58\u524D\u7AEF\u660E\u7EC6\u884C\uFF0C\u518D\u63D0\u4EA4\u540E\u7AEF\u5206\u5E97\u66F4\u65B0\u4EFB\u52A1"
    );
    assert(editPageSource.includes("completedJob.status === 'Failed'"), "\u66F4\u65B0\u5230\u5206\u5E97\u8F6E\u8BE2\u5230 Failed Job \u65F6\u5E94\u5C55\u793A\u5931\u8D25\u800C\u4E0D\u662F\u5B8C\u6210");
    assert(!editPageSource.includes("const result = await updateToStorePrices(request)"), "\u66F4\u65B0\u5230\u5206\u5E97\u4E0D\u5E94\u518D\u76F4\u63A5\u7B49\u5F85\u957F\u8BF7\u6C42");
    assert(editPageSource.includes("localSupplierInvoiceBatchJobTimeoutTitle"), "\u6279\u91CF\u540E\u53F0\u4EFB\u52A1\u8F6E\u8BE2\u8D85\u65F6\u5E94\u5C55\u793A\u540E\u53F0\u4ECD\u5728\u6267\u884C\u63D0\u793A");
    assert(editPageSource.includes('name="targetStoreCodes"'), "\u66F4\u65B0HQ\u5546\u54C1\u5F39\u7A97\u5E94\u9009\u62E9\u76EE\u6807\u5206\u5E97");
    assert(editPageSource.includes("allHqUpdateStoresSelected"), "\u66F4\u65B0HQ\u5546\u54C1\u76EE\u6807\u5206\u5E97\u5E94\u652F\u6301\u5168\u9009\u9009\u4E2D\u72B6\u6001");
    assert(editPageSource.includes("hasPartialHqUpdateStoreSelection"), "\u66F4\u65B0HQ\u5546\u54C1\u76EE\u6807\u5206\u5E97\u5E94\u652F\u6301\u534A\u9009\u72B6\u6001");
    assert(editPageSource.includes("hqUpdateForm.setFieldValue('targetStoreCodes', event.target.checked ? allStoreCodes : [])"), "\u66F4\u65B0HQ\u5546\u54C1\u76EE\u6807\u5206\u5E97\u5168\u9009\u5E94\u5199\u5165\u5F53\u524D\u53EF\u9009\u5206\u5E97\u7F16\u7801");
    assert(editPageSource.includes("t('posAdmin.invoiceDetail.updateHqProductsTitle'"), "\u66F4\u65B0HQ\u5546\u54C1\u5E94\u6709\u72EC\u7ACB\u5F39\u7A97\u6807\u9898");
    assert(editPageSource.includes("t('posAdmin.invoiceDetail.updateHqProductsResultTitle'"), "\u66F4\u65B0HQ\u5546\u54C1\u5E94\u6709\u72EC\u7ACB\u7ED3\u679C\u5F39\u7A97");
  });
  if (updateToStoreHqFailure) failures.push(updateToStoreHqFailure);
  const updateHqAutoPricingValueFailure = await runTest("\u66F4\u65B0HQ\u5546\u54C1\u81EA\u52A8\u5B9A\u4EF7\u5E94\u6309\u660E\u7EC6\u884C\u503C\u540C\u6B65", () => {
    const hqModalStart = editPageSource.indexOf("{/* \u66F4\u65B0 HQ \u5546\u54C1 Modal");
    const hqModalSource = editPageSource.slice(hqModalStart);
    assert(hqModalSource.includes('name="updateIsAutoPricing"'), "\u66F4\u65B0HQ\u5546\u54C1\u5F39\u7A97\u5E94\u4FDD\u7559\u66F4\u65B0\u81EA\u52A8\u5B9A\u4EF7\u5B57\u6BB5\u5F00\u5173");
    assert(!hqModalSource.includes('name="isAutoPricing"'), "\u66F4\u65B0HQ\u5546\u54C1\u5F39\u7A97\u4E0D\u5E94\u63D0\u4F9B\u81EA\u52A8\u5B9A\u4EF7\u662F/\u5426\u4E0B\u62C9\uFF0C\u5E94\u7531\u540E\u7AEF\u6309\u660E\u7EC6\u884C\u503C\u5199\u5165");
    assert(editPageSource.includes("const selectedDetailSet = new Set(detailGuids)"), "\u66F4\u65B0HQ\u5546\u54C1\u5E94\u6309\u9009\u4E2D\u7684\u524D\u7AEF\u660E\u7EC6\u884C\u6784\u5EFA\u4FDD\u5B58\u8303\u56F4");
    assert(editPageSource.includes("await batchUpsertDetails(invoiceGuid, buildInvoiceDetailSaveItems(selectedDetails))"), "\u66F4\u65B0HQ\u5546\u54C1\u63D0\u4EA4\u524D\u5E94\u5148\u4FDD\u5B58\u524D\u7AEF\u5F53\u524D\u9009\u4E2D\u660E\u7EC6\u884C");
    assert(
      editPageSource.indexOf("await batchUpsertDetails(invoiceGuid, buildInvoiceDetailSaveItems(selectedDetails))") < editPageSource.indexOf("const job = await startUpdateHqProductsJob(invoiceGuid"),
      "\u66F4\u65B0HQ\u5546\u54C1\u5E94\u5148\u4FDD\u5B58\u524D\u7AEF\u660E\u7EC6\u884C\uFF0C\u518D\u63D0\u4EA4\u540E\u7AEFHQ\u66F4\u65B0\u4EFB\u52A1"
    );
  });
  if (updateHqAutoPricingValueFailure) failures.push(updateHqAutoPricingValueFailure);
  const pasteAndCheckJobPageFailure = await runTest("\u7F16\u8F91\u9875\u7C98\u8D34\u548C\u5546\u54C1\u68C0\u6D4B\u5E94\u63D0\u4EA4\u540E\u53F0 Job \u5E76\u8F6E\u8BE2\u5B8C\u6210\u901A\u77E5", () => {
    assert(editPageSource.includes("startPasteDetailsJob,"), "\u7F16\u8F91\u9875\u5E94\u9759\u6001\u5BFC\u5165\u7C98\u8D34\u540E\u53F0\u4EFB\u52A1\u521B\u5EFA\u63A5\u53E3");
    assert(editPageSource.includes("getPasteDetailsJob,"), "\u7F16\u8F91\u9875\u5E94\u9759\u6001\u5BFC\u5165\u7C98\u8D34\u540E\u53F0\u4EFB\u52A1\u67E5\u8BE2\u63A5\u53E3");
    assert(editPageSource.includes("startCheckProductsJob,"), "\u7F16\u8F91\u9875\u5E94\u9759\u6001\u5BFC\u5165\u5546\u54C1\u68C0\u6D4B\u540E\u53F0\u4EFB\u52A1\u521B\u5EFA\u63A5\u53E3");
    assert(editPageSource.includes("getCheckProductsJob,"), "\u7F16\u8F91\u9875\u5E94\u9759\u6001\u5BFC\u5165\u5546\u54C1\u68C0\u6D4B\u540E\u53F0\u4EFB\u52A1\u67E5\u8BE2\u63A5\u53E3");
    assert(editPageSource.includes("pollPasteDetailsJob"), "\u7F16\u8F91\u9875\u5E94\u4E3A\u7C98\u8D34\u660E\u7EC6\u63D0\u4F9B\u540E\u53F0\u4EFB\u52A1\u8F6E\u8BE2 helper");
    assert(editPageSource.includes("pollCheckProductsJob"), "\u7F16\u8F91\u9875\u5E94\u4E3A\u5546\u54C1\u68C0\u6D4B\u63D0\u4F9B\u540E\u53F0\u4EFB\u52A1\u8F6E\u8BE2 helper");
    assert(editPageSource.includes("createHqSyncJobPoller<PasteDetailsJobResult>"), "\u7C98\u8D34\u660E\u7EC6\u5E94\u590D\u7528\u540E\u53F0 Job \u8F6E\u8BE2\u5668");
    assert(editPageSource.includes("createHqSyncJobPoller<CheckProductsJobResult>"), "\u5546\u54C1\u68C0\u6D4B\u5E94\u590D\u7528\u540E\u53F0 Job \u8F6E\u8BE2\u5668");
    assert(editPageSource.includes("startPasteDetailsJob({"), "\u7C98\u8D34\u786E\u8BA4\u5E94\u521B\u5EFA\u540E\u53F0\u4EFB\u52A1");
    assert(editPageSource.includes("startCheckProductsJob({"), "\u5546\u54C1\u68C0\u6D4B\u5E94\u521B\u5EFA\u540E\u53F0\u4EFB\u52A1");
    assert(editPageSource.includes("getPasteDetailsJob(") && editPageSource.includes("getPasteDetailsJob(submittedInvoiceGuid, jobId)"), "\u7C98\u8D34\u4EFB\u52A1\u5E94\u6309\u63D0\u4EA4\u65F6\u7684\u53D1\u7968 id \u548C jobId \u67E5\u8BE2\u6700\u7EC8\u72B6\u6001");
    assert(editPageSource.includes("getCheckProductsJob(") && editPageSource.includes("getCheckProductsJob(submittedInvoiceGuid, jobId)"), "\u5546\u54C1\u68C0\u6D4B\u4EFB\u52A1\u5E94\u6309\u63D0\u4EA4\u65F6\u7684\u53D1\u7968 id \u548C jobId \u67E5\u8BE2\u6700\u7EC8\u72B6\u6001");
    assert(editPageSource.includes("isMissingBackgroundJobEndpoint(error)"), "\u540E\u7AEF job \u63A5\u53E3\u672A\u53D1\u5E03\u8FD4\u56DE 404 \u65F6\u5E94\u8BC6\u522B\u4E3A\u53EF\u517C\u5BB9\u573A\u666F");
    assert(editPageSource.includes("await pasteDetails({"), "\u7C98\u8D34 job \u521B\u5EFA\u63A5\u53E3 404 \u65F6\u5E94\u56DE\u9000\u65E7\u540C\u6B65\u63A5\u53E3\uFF0C\u907F\u514D\u5F39\u7A97\u64CD\u4F5C\u76F4\u63A5\u5931\u8D25");
    assert(editPageSource.includes("await checkProducts({"), "\u5546\u54C1\u68C0\u6D4B job \u521B\u5EFA\u63A5\u53E3 404 \u65F6\u5E94\u56DE\u9000\u65E7\u540C\u6B65\u63A5\u53E3\uFF0C\u907F\u514D\u5546\u54C1\u68C0\u6D4B\u76F4\u63A5\u5931\u8D25");
    assert(
      editPageSource.indexOf("await pasteDetails({") > editPageSource.indexOf("isMissingBackgroundJobEndpoint(error)"),
      "\u7C98\u8D34\u65E7\u540C\u6B65\u63A5\u53E3\u53EA\u80FD\u4F5C\u4E3A job \u521B\u5EFA 404 \u7684\u517C\u5BB9\u56DE\u9000"
    );
    assert(
      editPageSource.indexOf("await checkProducts({") > editPageSource.indexOf("isMissingBackgroundJobEndpoint(error)"),
      "\u5546\u54C1\u68C0\u6D4B\u65E7\u540C\u6B65\u63A5\u53E3\u53EA\u80FD\u4F5C\u4E3A job \u521B\u5EFA 404 \u7684\u517C\u5BB9\u56DE\u9000"
    );
    assert(editPageSource.includes("t('posAdmin.invoiceDetail.pasteJobSubmitted'"), "\u7C98\u8D34\u4EFB\u52A1\u63D0\u4EA4\u540E\u5E94\u63D0\u793A\u540E\u53F0\u6267\u884C");
    assert(editPageSource.includes("t('posAdmin.invoiceDetail.checkProductsJobSubmitted'"), "\u5546\u54C1\u68C0\u6D4B\u4EFB\u52A1\u63D0\u4EA4\u540E\u5E94\u63D0\u793A\u540E\u53F0\u6267\u884C");
    assert(editPageSource.includes("canApplyInvoiceJobResult(currentInvoiceGuidRef.current, submittedInvoiceGuid)"), "\u7C98\u8D34\u4EFB\u52A1\u5B8C\u6210\u540E\u5E94\u786E\u8BA4\u4ECD\u5728\u540C\u4E00\u5F20\u8FDB\u8D27\u5355\u518D\u5237\u65B0");
    assert(editPageSource.includes("canApplyCheckProductsJobResult({"), "\u5546\u54C1\u68C0\u6D4B\u4EFB\u52A1\u5B8C\u6210\u540E\u5E94\u901A\u8FC7 guard \u5224\u65AD\u662F\u5426\u53EF\u5199\u56DE");
    assert(editPageSource.includes("currentInvoiceGuidRef.current = invoiceGuid"), "\u5F53\u524D\u53D1\u7968 ref \u5E94\u5728 render \u9636\u6BB5\u540C\u6B65\u66F4\u65B0\uFF0C\u907F\u514D useEffect \u524D\u7684\u7A84\u7A97\u53E3\u7ADE\u6001");
    assert(editPageSource.includes("activePasteJobIdRef.current = null") && editPageSource.includes("activeCheckProductsJobIdRef.current = null"), "\u5207\u6362\u8FDB\u8D27\u5355\u65F6\u5E94\u6E05\u7A7A\u65E7\u540E\u53F0 job\uFF0C\u907F\u514D\u65E7\u4EFB\u52A1\u5199\u56DE\u65B0\u9875\u9762");
    assert(editPageSource.includes("completedJob.status === 'Failed'") && editPageSource.indexOf("completedJob.status === 'Failed'") < editPageSource.indexOf("applyCheckProductsResponse(result)"), "\u5546\u54C1\u68C0\u6D4B\u5931\u8D25\u4EFB\u52A1\u4E0D\u5E94\u5148\u5408\u5E76 result");
    assert(editPageSource.includes("if (checking) return"), "\u5546\u54C1\u68C0\u6D4B\u8FD0\u884C\u4E2D\u5E94\u963B\u6B62\u91CD\u590D\u63D0\u4EA4");
    assert(editPageSource.includes("disabled={checking}"), "\u5546\u54C1\u68C0\u6D4B\u6309\u94AE\u5728\u540E\u53F0\u4EFB\u52A1\u8FD0\u884C\u4E2D\u5E94\u7981\u7528");
    assert(editPageSource.includes("setPasteVisible(false)") && editPageSource.includes("setPasteText('')"), "\u7C98\u8D34\u4EFB\u52A1\u63D0\u4EA4\u6210\u529F\u540E\u5E94\u5173\u95ED\u5F39\u7A97\u5E76\u6E05\u7A7A\u8F93\u5165");
    assert(editPageSource.includes("applyCheckProductsResponse(result)"), "\u5546\u54C1\u68C0\u6D4B\u540E\u53F0\u5B8C\u6210\u540E\u5E94\u590D\u7528\u7ED3\u679C\u5408\u5E76\u903B\u8F91\u66F4\u65B0\u8868\u683C");
    assert(editPageSource.includes("await loadDetails()"), "\u540E\u53F0\u4EFB\u52A1\u5B8C\u6210\u540E\u5E94\u5237\u65B0\u660E\u7EC6");
  });
  if (pasteAndCheckJobPageFailure) failures.push(pasteAndCheckJobPageFailure);
  const batchExecuteConfirmFailure = await runTest("\u6279\u91CF\u6267\u884C\u64CD\u4F5C\u5E94\u5148\u5C55\u793A\u4E8C\u6B21\u786E\u8BA4\u5E76\u7A81\u51FA\u65B0\u5EFA\u5546\u54C1\u6570\u91CF", () => {
    assert(editPageSource.includes("Modal.confirm({"), "\u6279\u91CF\u6267\u884C\u64CD\u4F5C\u5E94\u4F7F\u7528\u786E\u8BA4\u6846");
    assert(editPageSource.includes("batchExecuteConfirmTitle"), "\u786E\u8BA4\u6846\u5E94\u6709\u4E13\u7528\u6807\u9898\u6587\u6848");
    assert(editPageSource.includes("batchExecuteCreateProductNotice"), "\u786E\u8BA4\u6846\u5E94\u5305\u542B\u65B0\u5EFA\u5546\u54C1\u98CE\u9669\u63D0\u793A\u6587\u6848");
    assert(editPageSource.includes("canRunGlobalLocalPurchaseBatchActions"), "\u6279\u91CF\u6267\u884C\u5165\u53E3\u5E94\u4F7F\u7528\u4E0E\u540E\u7AEF\u5168\u5E97\u8BBF\u95EE\u4E00\u81F4\u7684\u6743\u9650\u6761\u4EF6");
    assert(editPageSource.includes("if (!isAdmin)") && editPageSource.includes("actionConfig[currentAction] || actionConfig[0]"), "\u884C\u5185\u64CD\u4F5C\u7C7B\u578B\u8BBE\u7F6E\u5E94\u8981\u6C42\u7BA1\u7406\u5458\u6743\u9650\uFF0C\u4F46\u975E\u7BA1\u7406\u5458\u4ECD\u53EF\u67E5\u770B\u5F53\u524D\u72B6\u6001");
    assert(
      editPageSource.includes("onClick: ({ key }) => void handleRowActionChange(record.detailGUID, key)") && editPageSource.includes("isAdmin ? ("),
      "\u884C\u5185\u64CD\u4F5C\u7C7B\u578B\u4E0B\u62C9\u53EA\u5E94\u5BF9\u7BA1\u7406\u5458\u53EF\u4EA4\u4E92"
    );
    assert(editPageSource.includes("okButtonProps: { danger: previewSnapshot.confirmedCreateProductCount > 0 }"), "\u5B58\u5728\u65B0\u5EFA\u5546\u54C1\u65F6\u786E\u8BA4\u6309\u94AE\u5E94\u4F7F\u7528\u786E\u8BA4\u9884\u89C8\u5FEB\u7167\u7684\u5371\u9669\u6001");
    assert(editPageSource.includes("constrainSelectedRowKeysToVisibleDetails(selectedRowKeys, filteredDetails)"), "\u6279\u91CF\u6267\u884C\u524D\u5E94\u6536\u655B\u5230\u5F53\u524D\u53EF\u89C1\u9009\u4E2D\u660E\u7EC6");
    assertEqual((editPageSource.match(/await batchExecuteActions\(/g) ?? []).length, 1, "\u6279\u91CF\u6267\u884C\u524D\u7AEF\u5E94\u53EA\u53D1\u8D77\u4E00\u6B21 batchExecuteActions \u8BF7\u6C42");
    assert(editPageSource.includes("detailGuids: snapshot.detailGuids"), "\u6279\u91CF\u6267\u884C\u5E94\u628A\u9009\u4E2D\u660E\u7EC6\u6574\u4F53\u4EA4\u7ED9\u540E\u7AEF\u6279\u91CF\u5904\u7406");
    assert(!editPageSource.includes("for (const detailGuid of snapshot.detailGuids)"), "\u6279\u91CF\u6267\u884C\u524D\u7AEF\u4E0D\u80FD\u6309 detailGuid \u5FAA\u73AF\u62C6\u5206\u8BF7\u6C42");
    assert(!editPageSource.includes("snapshot.detailGuids.map(async"), "\u6279\u91CF\u6267\u884C\u524D\u7AEF\u4E0D\u80FD\u7528 map(async) \u62C6\u5206\u8BF7\u6C42");
    assert(
      editPageSource.indexOf("await updateDetailAction(invoiceGuid, detailGuid, action)") < editPageSource.indexOf("setRowActions((prev) => ({ ...prev, [detailGuid]: action }))"),
      "\u884C\u64CD\u4F5C\u7C7B\u578B\u5E94\u5728\u670D\u52A1\u7AEF\u66F4\u65B0\u6210\u529F\u540E\u518D\u66F4\u65B0\u672C\u5730\u72B6\u6001"
    );
    const counts = countSelectedBatchExecuteActions(
      ["d1", "d2", "d3"],
      [
        { detailGUID: "d1", activityType: 1 /* CreateProduct */ },
        { detailGUID: "d2", activityType: 2 /* UpdatePurchasePrice */ },
        { detailGUID: "d3", activityType: 3 /* WaitForOperation */ }
      ],
      { d2: 1 /* CreateProduct */ }
    );
    assertEqual(counts.selectedCount, 3, "\u786E\u8BA4\u7EDF\u8BA1\u5E94\u5305\u542B\u9009\u4E2D\u6761\u6570");
    assertEqual(counts.createProductCount, 2, "\u786E\u8BA4\u7EDF\u8BA1\u5E94\u4EE5 rowActions \u8986\u76D6\u540E\u7684\u64CD\u4F5C\u7C7B\u578B\u8BA1\u7B97\u65B0\u5EFA\u5546\u54C1\u6570\u91CF");
    const confirmText = buildBatchExecuteConfirmText({
      ...counts,
      labels: {
        title: "\u786E\u8BA4\u6267\u884C\u6279\u91CF\u64CD\u4F5C\uFF1F",
        content: "\u5C06\u5BF9 {{count}} \u6761\u660E\u7EC6\u6267\u884C\u5DF2\u8BBE\u7F6E\u7684\u64CD\u4F5C\u3002",
        createProductNotice: "\u5176\u4E2D {{count}} \u6761\u4F1A\u65B0\u5EFA\u5546\u54C1\uFF0C\u8BF7\u786E\u8BA4\u8D27\u53F7\u3001\u6761\u7801\u548C\u540D\u79F0\u65E0\u8BEF\u3002",
        okText: "\u786E\u8BA4\u6267\u884C",
        cancelText: "\u53D6\u6D88"
      }
    });
    assert(confirmText.content.includes("3 \u6761\u660E\u7EC6"), "\u786E\u8BA4\u6587\u6848\u5E94\u5C55\u793A\u6267\u884C\u6761\u6570");
    assert(confirmText.content.includes("2 \u6761\u4F1A\u65B0\u5EFA\u5546\u54C1"), "\u786E\u8BA4\u6587\u6848\u5E94\u5C55\u793A\u65B0\u5EFA\u5546\u54C1\u6570\u91CF");
    const visibleKeys = constrainSelectedRowKeysToVisibleDetails(
      ["d1", "d2", "hidden"],
      [
        { detailGUID: "d1" },
        { detailGUID: "d2" }
      ]
    );
    assertDeepEqual(visibleKeys.map(String), ["d1", "d2"], "\u7B5B\u9009\u540E\u9690\u85CF\u660E\u7EC6\u4E0D\u5E94\u7EE7\u7EED\u53C2\u4E0E\u6279\u91CF\u6267\u884C");
  });
  if (batchExecuteConfirmFailure) failures.push(batchExecuteConfirmFailure);
  const editPageStatsFailure = await runTest("\u7F16\u8F91\u9875\u5E94\u63D0\u4F9B\u72B6\u6001\u7EDF\u8BA1\u680F\u5E76\u652F\u6301\u70B9\u51FB\u53E0\u52A0\u8FC7\u6EE4", () => {
    assert(editPageSource.includes("useState<StatusFilterValue<ProductStatusFilter>>('all')"), "\u7F16\u8F91\u9875\u5E94\u7EF4\u62A4\u5546\u54C1\u72B6\u6001\u8FC7\u6EE4\u72B6\u6001");
    assert(editPageSource.includes("useState<StatusFilterValue<BarcodeStatusFilter>>('all')"), "\u7F16\u8F91\u9875\u5E94\u7EF4\u62A4\u6761\u7801\u72B6\u6001\u8FC7\u6EE4\u72B6\u6001");
    assert(editPageSource.includes("useState<ActionTypeFilterValue>('all')"), "\u7F16\u8F91\u9875\u5E94\u7EF4\u62A4\u64CD\u4F5C\u7C7B\u578B\u8FC7\u6EE4\u72B6\u6001");
    assert(editPageSource.includes("getDetailStatusStats(details, rowActions)"), "\u7F16\u8F91\u9875\u5E94\u57FA\u4E8E\u5168\u90E8 details \u548C\u5F53\u524D\u64CD\u4F5C\u7C7B\u578B\u8BA1\u7B97\u72B6\u6001\u7EDF\u8BA1");
    assert(editPageSource.includes("filterInvoiceDetails(details"), "\u7F16\u8F91\u9875\u8FC7\u6EE4\u94FE\u5E94\u59D4\u6258\u884C\u4E3A\u7EA7\u7EAF\u51FD\u6570");
    assert(editPageSource.includes("[details, searchText, priceFilter, productStatusFilter, barcodeStatusFilter, actionTypeFilter, rowActions]"), "\u8FC7\u6EE4\u7ED3\u679C\u5E94\u4F9D\u8D56\u641C\u7D22\u3001\u6DA8\u8DCC\u3001\u72B6\u6001\u548C\u64CD\u4F5C\u7C7B\u578B\u8FC7\u6EE4\uFF0C\u6309 AND \u53E0\u52A0");
    assert(editPageSource.includes("toggleStatusFilter(productStatusFilter, 'exists')"), "\u518D\u6B21\u70B9\u51FB\u540C\u4E00\u5546\u54C1\u72B6\u6001\u6807\u7B7E\u5E94\u53D6\u6D88\u8FC7\u6EE4");
    assert(editPageSource.includes("toggleStatusFilter(barcodeStatusFilter, 'normal')"), "\u518D\u6B21\u70B9\u51FB\u540C\u4E00\u6761\u7801\u72B6\u6001\u6807\u7B7E\u5E94\u53D6\u6D88\u8FC7\u6EE4");
    assert(editPageSource.includes("toggleStatusFilter(actionTypeFilter, actionType)"), "\u518D\u6B21\u70B9\u51FB\u540C\u4E00\u64CD\u4F5C\u7C7B\u578B\u6807\u7B7E\u5E94\u53D6\u6D88\u8FC7\u6EE4");
    assert(editPageSource.includes("t('posAdmin.invoiceDetail.statusStatsTitle', '\u72B6\u6001\u7EDF\u8BA1')"), "\u9875\u9762\u5E94\u663E\u793A\u72B6\u6001\u7EDF\u8BA1\u680F\u6807\u9898");
    assert(editPageSource.includes("t('posAdmin.invoiceDetail.statusStatsAll', '\u5168\u90E8 {{count}}'"), "\u9875\u9762\u5E94\u63D0\u4F9B\u5168\u90E8\u72B6\u6001\u6807\u7B7E\u4EE5\u6E05\u9664\u72B6\u6001\u8FC7\u6EE4");
    assert(editPageSource.includes("t('posAdmin.invoiceDetail.productStatusLabel', '\u5546\u54C1\u72B6\u6001')"), "\u9875\u9762\u5E94\u663E\u793A\u5546\u54C1\u72B6\u6001\u5206\u7EC4\u6807\u9898");
    assert(editPageSource.includes("t('posAdmin.invoiceDetail.actionTypeLabel', '\u64CD\u4F5C\u7C7B\u578B')"), "\u9875\u9762\u5E94\u663E\u793A\u64CD\u4F5C\u7C7B\u578B\u5206\u7EC4\u6807\u9898");
    assert(editPageSource.includes("t('posAdmin.invoiceDetail.barcodeStatusLabel', '\u6761\u7801\u72B6\u6001')"), "\u9875\u9762\u5E94\u663E\u793A\u6761\u7801\u72B6\u6001\u5206\u7EC4\u6807\u9898");
    assert(editPageSource.includes("t('posAdmin.invoiceDetail.activeFiltersTitle', '\u5F53\u524D\u8FC7\u6EE4')"), "\u9875\u9762\u5E94\u5355\u72EC\u663E\u793A\u5F53\u524D\u8FC7\u6EE4\u680F\u6807\u9898");
    assert(editPageSource.includes("activeFilterTags"), "\u9875\u9762\u5E94\u628A\u5DF2\u542F\u7528\u7684\u641C\u7D22\u3001\u6DA8\u8DCC\u3001\u72B6\u6001\u8FC7\u6EE4\u5355\u72EC\u5217\u51FA");
    assert(editPageSource.includes("handleClearAllOuterFilters"), "\u9875\u9762\u5E94\u63D0\u4F9B\u6E05\u7A7A\u5916\u5C42\u8FC7\u6EE4\u6761\u4EF6\u5165\u53E3");
    assert(editPageSource.includes("closable"), "\u5F53\u524D\u8FC7\u6EE4\u6807\u7B7E\u5E94\u53EF\u5355\u72EC\u5173\u95ED\u6E05\u9664");
    assert(editPageSource.includes("setSearchText('')"), "\u6E05\u7A7A\u8FC7\u6EE4\u5E94\u91CD\u7F6E\u641C\u7D22\u5173\u952E\u8BCD");
    assert(editPageSource.includes("setPriceFilter('all')"), "\u6E05\u7A7A\u8FC7\u6EE4\u5E94\u91CD\u7F6E\u6DA8\u8DCC\u8FC7\u6EE4");
    assert(editPageSource.includes("setProductStatusFilter('all')"), "\u6E05\u7A7A\u8FC7\u6EE4\u5E94\u91CD\u7F6E\u5546\u54C1\u72B6\u6001\u8FC7\u6EE4");
    assert(editPageSource.includes("setBarcodeStatusFilter('all')"), "\u6E05\u7A7A\u8FC7\u6EE4\u5E94\u91CD\u7F6E\u6761\u7801\u72B6\u6001\u8FC7\u6EE4");
    assert(editPageSource.includes("statusStatsTagColors"), "\u72B6\u6001\u7EDF\u8BA1\u6807\u7B7E\u5E94\u4F7F\u7528\u663E\u5F0F\u8BED\u4E49\u8272\u914D\u7F6E");
    assert(editPageSource.includes("product: { all: 'blue', notDetected: 'purple', exists: 'green', notExists: 'red' }"), "\u5546\u54C1\u72B6\u6001\u6807\u7B7E\u5E94\u4F7F\u7528\u4E0D\u540C\u989C\u8272");
    assert(editPageSource.includes("barcode: { all: 'geekblue', notDetected: 'purple', normal: 'cyan', noMatch: 'volcano', multiMatch: 'orange' }"), "\u6761\u7801\u72B6\u6001\u6807\u7B7E\u5E94\u4F7F\u7528\u4E0D\u540C\u989C\u8272");
    assert(editPageSource.includes("getStatusStatsTagStyle("), "\u72B6\u6001\u7EDF\u8BA1\u6807\u7B7E\u5E94\u4F7F\u7528\u72EC\u7ACB\u9009\u4E2D\u6001\u6837\u5F0F");
    assert(editPageSource.includes("/* \u72B6\u6001\u7EDF\u8BA1\u680F\uFF1A\u6570\u91CF\u6309\u5168\u90E8\u660E\u7EC6\u8BA1\u7B97\uFF0C\u70B9\u51FB\u540E\u4E0E\u641C\u7D22\u548C\u6DA8\u8DCC\u7B5B\u9009\u53E0\u52A0\u3002 */"), "\u72B6\u6001\u7EDF\u8BA1\u680F\u5E94\u4F4D\u4E8E\u660E\u7EC6\u5361\u7247\u5185\u5BB9\u533A\u3001\u5DE5\u5177\u680F\u6309\u94AE\u4E0A\u65B9");
    assert(editPageSource.includes("productNameCellStyle"), "\u5546\u54C1\u540D\u79F0\u5217\u5E94\u4F7F\u7528\u4E13\u7528\u6362\u884C\u6837\u5F0F");
    assert(editPageSource.includes("WebkitLineClamp: 2"), "\u5546\u54C1\u540D\u79F0\u5217\u5E94\u6700\u591A\u81EA\u52A8\u6362\u884C 2 \u884C");
  });
  if (editPageStatsFailure) failures.push(editPageStatsFailure);
  const barcodeMatchModalFailure = await runTest("\u7F16\u8F91\u9875\u6761\u7801\u72B6\u6001\u5E94\u53EF\u70B9\u51FB\u67E5\u770B\u5339\u914D\u5546\u54C1", () => {
    const matchedProductColumnsStart = editPageSource.indexOf("const matchedProductColumns");
    const matchedProductColumnsEnd = editPageSource.indexOf("modal.update({", matchedProductColumnsStart);
    assert(matchedProductColumnsStart >= 0 && matchedProductColumnsEnd > matchedProductColumnsStart, "\u5E94\u80FD\u5B9A\u4F4D\u5F39\u7A97\u5339\u914D\u5546\u54C1\u8868\u683C\u5217\u5B9A\u4E49");
    const matchedProductColumnsSource = editPageSource.slice(matchedProductColumnsStart, matchedProductColumnsEnd);
    assert(editPageSource.includes("getProductsByBarcode"), "\u7F16\u8F91\u9875\u5E94\u590D\u7528\u6309\u6761\u7801\u67E5\u8BE2\u5339\u914D\u5546\u54C1\u63A5\u53E3");
    assert(editPageSource.includes("getProductById"), "\u66F4\u6362\u5339\u914D\u5546\u54C1\u4E3B\u6863\u524D\u5E94\u5148\u8BFB\u53D6\u5B8C\u6574\u5546\u54C1\u8BE6\u60C5");
    assert(editPageSource.includes("updateProduct"), "\u66F4\u6362\u5339\u914D\u5546\u54C1\u4E3B\u6863\u5E94\u590D\u7528\u5546\u54C1\u4E3B\u6863\u66F4\u65B0\u63A5\u53E3");
    assert(editPageSource.includes("canManagePosProducts"), "\u66F4\u6362\u5339\u914D\u5546\u54C1\u4E3B\u6863\u5E94\u4F7F\u7528\u5546\u54C1\u7BA1\u7406\u6743\u9650\u63A7\u5236");
    assert(editPageSource.includes("buildMatchedProductMasterUpdatePayload"), "\u66F4\u6362\u5339\u914D\u5546\u54C1\u4E3B\u6863\u5E94\u901A\u8FC7\u7EAF\u51FD\u6570\u6784\u5EFA\u5B8C\u6574\u66F4\u65B0 payload");
    assert(editPageSource.includes("showBarcodeMatchedProducts"), "\u7F16\u8F91\u9875\u5E94\u63D0\u4F9B\u6761\u7801\u5339\u914D\u5546\u54C1\u5F39\u7A97\u5165\u53E3");
    assert(editPageSource.includes("width: 920"), "\u5339\u914D\u5546\u54C1\u5F39\u7A97\u5E94\u52A0\u5BBD\uFF0C\u907F\u514D\u65B0\u589E\u64CD\u4F5C\u5217\u540E\u6324\u538B\u5546\u54C1\u540D\u79F0");
    assert(editPageSource.includes("matchedProductTableScrollX"), "\u5339\u914D\u5546\u54C1\u8868\u683C\u5E94\u542F\u7528\u6A2A\u5411\u6EDA\u52A8\u5BBD\u5EA6\uFF0C\u907F\u514D\u7A84\u5C4F\u5217\u5185\u5BB9\u7AD6\u6392");
    assert(editPageSource.includes('tableLayout="fixed"'), "\u5339\u914D\u5546\u54C1\u8868\u683C\u5E94\u4F7F\u7528\u56FA\u5B9A\u5E03\u5C40\u7A33\u5B9A\u5217\u5BBD");
    assert(editPageSource.includes("matchedProductNameCellStyle"), "\u5339\u914D\u5546\u54C1\u540D\u79F0\u5217\u5E94\u4F7F\u7528\u5F39\u7A97\u4E13\u7528\u5BBD\u5EA6\u548C\u6362\u884C\u6837\u5F0F");
    assert(editPageSource.includes("handleReplaceMatchedProductMaster"), "\u5F39\u7A97\u5E94\u63D0\u4F9B\u66F4\u6362\u6240\u9009\u5339\u914D\u5546\u54C1\u4E3B\u6863\u7684\u5904\u7406\u51FD\u6570");
    assert(editPageSource.includes("t('posAdmin.invoiceDetail.barcodeMatchedProductsTitle'"), "\u5F39\u7A97\u6807\u9898\u5E94\u5C55\u793A\u5F53\u524D\u6761\u7801");
    assert(matchedProductColumnsSource.includes("t('posAdmin.invoiceDetail.matchSource', '\u6765\u6E90')"), "\u5F39\u7A97\u8868\u683C\u5E94\u663E\u793A\u5339\u914D\u6765\u6E90");
    assert(matchedProductColumnsSource.includes("dataIndex: 'supplierName'"), "\u5F39\u7A97\u8868\u683C\u5E94\u663E\u793A\u4F9B\u5E94\u5546\u540D\u79F0\u5217");
    assert(matchedProductColumnsSource.includes("t('posAdmin.invoiceDetail.replaceProductMaster', '\u66F4\u6362\u8D27\u53F7\u548C\u4F9B\u5E94\u5546')"), "\u5F39\u7A97\u8868\u683C\u5E94\u63D0\u4F9B\u66F4\u6362\u8D27\u53F7\u548C\u4F9B\u5E94\u5546\u64CD\u4F5C");
    assert(matchedProductColumnsSource.includes("t('posAdmin.invoiceDetail.replaceProductMasterShort', '\u66F4\u6362')"), "\u5F39\u7A97\u64CD\u4F5C\u5217\u5E94\u4F7F\u7528\u77ED\u6309\u94AE\u6587\u6848\u907F\u514D\u6324\u538B");
    assert(matchedProductColumnsSource.includes("canManagePosProducts"), "\u5F39\u7A97\u64CD\u4F5C\u5217\u5E94\u53D7\u5546\u54C1\u7BA1\u7406\u6743\u9650\u63A7\u5236");
    assert(!matchedProductColumnsSource.includes("dataIndex: 'productCode'"), "\u5F39\u7A97\u8868\u683C\u4E0D\u5E94\u663E\u793A\u5546\u54C1\u7F16\u7801\u5217");
    assert(editPageSource.includes("t('posAdmin.invoiceDetail.replaceProductMasterConfirmTitle', '\u786E\u8BA4\u66F4\u6362\u5339\u914D\u5546\u54C1\u4E3B\u6863\uFF1F')"), "\u66F4\u6362\u524D\u5E94\u5C55\u793A\u786E\u8BA4\u6807\u9898");
    assert(editPageSource.includes("t('posAdmin.invoiceDetail.replaceProductMasterSourceLine'"), "\u786E\u8BA4\u5185\u5BB9\u5E94\u5C55\u793A\u6240\u9009\u5339\u914D\u5546\u54C1\u5F53\u524D\u8D27\u53F7\u548C\u4F9B\u5E94\u5546");
    assert(editPageSource.includes("t('posAdmin.invoiceDetail.replaceProductMasterTargetLine'"), "\u786E\u8BA4\u5185\u5BB9\u5E94\u5C55\u793A\u5F53\u524D\u660E\u7EC6\u5C06\u5199\u5165\u7684\u8D27\u53F7\u548C\u4F9B\u5E94\u5546");
    assert(editPageSource.includes("onClick={openMatchedProducts}"), "\u6761\u7801\u72B6\u6001\u6807\u7B7E\u5E94\u7ED1\u5B9A\u70B9\u51FB\u4E8B\u4EF6");
  });
  if (barcodeMatchModalFailure) failures.push(barcodeMatchModalFailure);
  const editPageStatsBehaviorFailure = await runTest("\u72B6\u6001\u7EDF\u8BA1\u548C\u8FC7\u6EE4\u5E94\u6309\u771F\u5B9E\u660E\u7EC6\u884C\u4E3A\u8BA1\u7B97", () => {
    const details = [
      {
        detailGUID: "not-detected",
        itemNumber: "CARD-001",
        barcode: "111",
        productName: "Birthday Card",
        lastPurchasePrice: 1,
        purchasePrice: 2
      },
      {
        detailGUID: "exists-normal",
        itemNumber: "BAG-002",
        barcode: "222",
        productName: "Gift Bag",
        storeProductCode: "STORE-002",
        existingProductCount: 2,
        barcodeStatus: 1,
        barcodeMatchCount: 1,
        lastPurchasePrice: 5,
        purchasePrice: 4,
        activityType: 3 /* WaitForOperation */
      },
      {
        detailGUID: "not-exists-no-match",
        itemNumber: "WRAP-003",
        barcode: "333",
        productName: "Wrap",
        existingProductCount: 0,
        barcodeStatus: 2,
        barcodeMatchCount: 0,
        lastPurchasePrice: 1,
        purchasePrice: 1.5,
        activityType: 2 /* UpdatePurchasePrice */
      },
      {
        detailGUID: "exists-multi-match",
        itemNumber: "CARD-004",
        barcode: "444",
        productName: "Birthday Card Multi",
        existingProductCount: 1,
        barcodeStatus: 2,
        barcodeMatchCount: 3,
        lastPurchasePrice: 2,
        purchasePrice: 3,
        activityType: 3 /* WaitForOperation */
      }
    ];
    const rowActions = {
      "exists-multi-match": 1 /* CreateProduct */
    };
    assertEqual(getProductStatusFilter(details[0]), "notDetected", "\u672A\u68C0\u6D4B\u5546\u54C1\u72B6\u6001\u5E94\u6765\u81EA\u7A7A existingProductCount");
    assertEqual(getProductStatusFilter(details[1]), "exists", "\u5DF2\u5B58\u5728\u5546\u54C1\u72B6\u6001\u5E94\u6765\u81EA existingProductCount > 0");
    assertEqual(getProductStatusFilter(details[2]), "notExists", "\u4E0D\u5B58\u5728\u5546\u54C1\u72B6\u6001\u5E94\u6765\u81EA existingProductCount = 0");
    assertEqual(getBarcodeStatusFilter(details[0]), "notDetected", "\u672A\u68C0\u6D4B\u6761\u7801\u72B6\u6001\u5E94\u6765\u81EA\u7A7A barcodeStatus");
    assertEqual(getBarcodeStatusFilter(details[1]), "normal", "\u6B63\u5E38\u6761\u7801\u72B6\u6001\u5E94\u6765\u81EA barcodeStatus = 1");
    assertEqual(getBarcodeStatusFilter(details[2]), "noMatch", "\u65E0\u5339\u914D\u6761\u7801\u72B6\u6001\u5E94\u6765\u81EA\u5F02\u5E38\u4E14\u5339\u914D\u6570\u4E3A 0");
    assertEqual(getBarcodeStatusFilter(details[3]), "multiMatch", "\u591A\u5339\u914D\u6761\u7801\u72B6\u6001\u5E94\u6765\u81EA\u5F02\u5E38\u4E14\u5339\u914D\u6570\u5927\u4E8E 0");
    assertEqual(getActionTypeFilter(details[0], rowActions), 0 /* None */, "\u7A7A\u64CD\u4F5C\u7C7B\u578B\u5E94\u6309\u65E0\u7EDF\u8BA1");
    assertEqual(getActionTypeFilter(details[3], rowActions), 1 /* CreateProduct */, "rowActions \u5E94\u8986\u76D6\u539F\u59CB\u64CD\u4F5C\u7C7B\u578B");
    assertDeepEqual(
      getDetailStatusStats(details, rowActions),
      {
        product: { notDetected: 1, exists: 2, notExists: 1 },
        barcode: { notDetected: 1, normal: 1, noMatch: 1, multiMatch: 1 },
        action: {
          [0 /* None */]: 1,
          [1 /* CreateProduct */]: 1,
          [2 /* UpdatePurchasePrice */]: 1,
          [3 /* WaitForOperation */]: 1,
          [4 /* UpdateItemNumber */]: 0,
          [5 /* AddMultiCode */]: 0
        }
      },
      "\u72B6\u6001\u7EDF\u8BA1\u5E94\u57FA\u4E8E\u5168\u90E8\u660E\u7EC6\u8BA1\u7B97"
    );
    const filtered = filterInvoiceDetails(details, {
      searchText: "card",
      priceFilter: "up",
      productStatusFilter: "exists",
      barcodeStatusFilter: "multiMatch",
      actionTypeFilter: 1 /* CreateProduct */,
      rowActions
    });
    assertDeepEqual(filtered.map((item) => item.detailGUID), ["exists-multi-match"], "\u641C\u7D22\u3001\u6DA8\u4EF7\u3001\u5546\u54C1\u72B6\u6001\u3001\u6761\u7801\u72B6\u6001\u3001\u64CD\u4F5C\u7C7B\u578B\u5E94\u6309 AND \u53E0\u52A0");
    assertEqual(getDetailStatusStats(filtered, rowActions).product.exists, 1, "\u8FC7\u6EE4\u7ED3\u679C\u53EF\u5355\u72EC\u7EDF\u8BA1\uFF0C\u4F46\u9875\u9762\u5168\u91CF\u7EDF\u8BA1\u4E0D\u5E94\u4F9D\u8D56\u8FC7\u6EE4\u7ED3\u679C");
    assertEqual(toggleStatusFilter("exists", "exists"), "all", "\u518D\u6B21\u70B9\u51FB\u540C\u4E00\u5546\u54C1\u72B6\u6001\u5E94\u53D6\u6D88\u8FC7\u6EE4");
    assertEqual(toggleStatusFilter("all", "normal"), "normal", "\u4ECE\u5168\u90E8\u70B9\u51FB\u67D0\u4E2A\u6761\u7801\u72B6\u6001\u5E94\u542F\u7528\u8FC7\u6EE4");
    assertEqual(toggleStatusFilter(1 /* CreateProduct */, 1 /* CreateProduct */), "all", "\u518D\u6B21\u70B9\u51FB\u540C\u4E00\u64CD\u4F5C\u7C7B\u578B\u5E94\u53D6\u6D88\u8FC7\u6EE4");
  });
  if (editPageStatsBehaviorFailure) failures.push(editPageStatsBehaviorFailure);
  const tableColumnFilterBehaviorFailure = await runTest("\u660E\u7EC6\u8868\u5217\u5934\u6392\u5E8F\u548C\u8FC7\u6EE4\u5E94\u53EA\u5728\u524D\u7AEF\u5F53\u524D\u6570\u636E\u5185\u751F\u6548", () => {
    const details = [
      {
        detailGUID: "empty-price",
        itemNumber: "cap-003",
        barcode: "333",
        productName: "Cap Birthday",
        purchasePrice: void 0,
        amount: void 0,
        autoPricing: void 0,
        isSpecialProduct: void 0,
        existingProductCount: void 0,
        barcodeStatus: void 0
      },
      {
        detailGUID: "cheap-card",
        itemNumber: "BINE1001",
        barcode: "111",
        productName: "Birthday Card",
        purchasePrice: 1.2,
        amount: 7.2,
        discountRate: 0.1,
        autoPricing: true,
        isSpecialProduct: false,
        existingProductCount: 1,
        barcodeStatus: 1,
        barcodeMatchCount: 1
      },
      {
        detailGUID: "gift-bag",
        itemNumber: "E11988",
        barcode: "222",
        productName: "Gift Bag",
        purchasePrice: 1.91,
        amount: 22.92,
        autoPricing: false,
        isSpecialProduct: true,
        existingProductCount: 0,
        barcodeStatus: 2,
        barcodeMatchCount: 0,
        activityType: 2 /* UpdatePurchasePrice */
      }
    ];
    assertDeepEqual(
      [...details].sort((a, b) => compareNullableNumbers(a.purchasePrice, b.purchasePrice)).map((item) => item.detailGUID),
      ["cheap-card", "gift-bag", "empty-price"],
      "\u6570\u503C\u5217\u5347\u5E8F\u5E94\u6309\u6570\u5B57\u6392\u5E8F\uFF0C\u5E76\u628A\u7A7A\u503C\u6392\u5728\u6700\u540E"
    );
    assertDeepEqual(
      [...details].sort((a, b) => compareNullableText(a.itemNumber, b.itemNumber)).map((item) => item.detailGUID),
      ["cheap-card", "empty-price", "gift-bag"],
      "\u6587\u672C\u5217\u6392\u5E8F\u5E94\u5927\u5C0F\u5199\u4E0D\u654F\u611F"
    );
    assertDeepEqual(
      details.filter((item) => matchesTextColumnFilter(item, "productName", "birthday")).map((item) => item.detailGUID),
      ["empty-price", "cheap-card"],
      "\u5217\u5934\u6587\u672C\u8FC7\u6EE4\u5E94\u53EA\u5339\u914D\u6307\u5B9A\u5217"
    );
    assertDeepEqual(
      details.filter((item) => matchesTextColumnFilter(item, "itemNumber", serializeTextColumnFilter({ mode: "equals", value: "BINE1001" }))).map((item) => item.detailGUID),
      ["cheap-card"],
      "\u6587\u672C\u5217\u5E94\u652F\u6301\u7B49\u4E8E\u5339\u914D"
    );
    assertDeepEqual(
      details.filter((item) => matchesTextColumnFilter(item, "itemNumber", serializeTextColumnFilter({ mode: "startsWith", value: "cap" }))).map((item) => item.detailGUID),
      ["empty-price"],
      "\u6587\u672C\u5217\u5E94\u652F\u6301\u5F00\u5934\u5339\u914D"
    );
    assertDeepEqual(
      details.filter((item) => matchesTextColumnFilter(item, "productName", serializeTextColumnFilter({ mode: "endsWith", value: "bag" }))).map((item) => item.detailGUID),
      ["gift-bag"],
      "\u6587\u672C\u5217\u5E94\u652F\u6301\u7ED3\u5C3E\u5339\u914D"
    );
    assertDeepEqual(
      details.filter((item) => matchesTextColumnFilter(item, "barcode", serializeTextColumnFilter({ mode: "empty" }))).map((item) => item.detailGUID),
      [],
      "\u6587\u672C\u5217\u5E94\u652F\u6301\u4E3A\u7A7A\u5339\u914D"
    );
    assertDeepEqual(
      details.filter((item) => matchesTextColumnFilter(item, "barcode", serializeTextColumnFilter({ mode: "notEmpty" }))).map((item) => item.detailGUID),
      ["empty-price", "cheap-card", "gift-bag"],
      "\u6587\u672C\u5217\u5E94\u652F\u6301\u975E\u7A7A\u5339\u914D"
    );
    assertDeepEqual(
      details.filter((item) => matchesNumberColumnFilter(item, "purchasePrice", serializeNumberColumnFilter({ mode: "gt", value: 1.5 }))).map((item) => item.detailGUID),
      ["gift-bag"],
      "\u6570\u5B57\u5217\u5E94\u652F\u6301\u5927\u4E8E\u5339\u914D"
    );
    assertDeepEqual(
      details.filter((item) => matchesNumberColumnFilter(item, "purchasePrice", serializeNumberColumnFilter({ mode: "lt", value: 1.5 }))).map((item) => item.detailGUID),
      ["cheap-card"],
      "\u6570\u5B57\u5217\u5E94\u652F\u6301\u5C0F\u4E8E\u5339\u914D"
    );
    assertDeepEqual(
      details.filter((item) => matchesNumberColumnFilter(item, "purchasePrice", serializeNumberColumnFilter({ mode: "between", min: 1, max: 1.5 }))).map((item) => item.detailGUID),
      ["cheap-card"],
      "\u6570\u5B57\u5217\u5E94\u652F\u6301\u533A\u95F4\u5339\u914D"
    );
    assertDeepEqual(
      details.filter((item) => matchesNumberColumnFilter(item, "purchasePrice", serializeNumberColumnFilter({ mode: "empty" }))).map((item) => item.detailGUID),
      ["empty-price"],
      "\u6570\u5B57\u5217\u5E94\u652F\u6301\u4E3A\u7A7A\u5339\u914D"
    );
    assertDeepEqual(
      details.filter((item) => matchesNumberColumnFilter(item, "amount", serializeNumberColumnFilter({ mode: "notEmpty" }))).map((item) => item.detailGUID),
      ["cheap-card", "gift-bag"],
      "\u6570\u5B57\u5217\u5E94\u652F\u6301\u975E\u7A7A\u5339\u914D"
    );
    assertDeepEqual(
      details.filter((item) => matchesNumberColumnFilter(item, "discountRate", serializeNumberColumnFilter({ mode: "equals", value: 10 }))).map((item) => item.detailGUID),
      ["cheap-card"],
      "\u6298\u6263\u7387\u5217\u8FC7\u6EE4\u6309\u9875\u9762\u5C55\u793A\u767E\u5206\u6BD4\u5339\u914D"
    );
    assertDeepEqual(
      details.filter((item) => filterBooleanColumn(item.autoPricing, true)).map((item) => item.detailGUID),
      ["cheap-card"],
      "\u81EA\u52A8\u5B9A\u4EF7\u5217\u5E94\u652F\u6301\u5E03\u5C14\u8FC7\u6EE4"
    );
    assertDeepEqual(
      details.filter((item) => filterBooleanColumn(item.autoPricing, false)).map((item) => item.detailGUID),
      ["gift-bag"],
      "\u81EA\u52A8\u5B9A\u4EF7 false \u8FC7\u6EE4\u4E0D\u5E94\u5305\u542B\u672A\u68C0\u6D4B\u7A7A\u503C"
    );
    assertDeepEqual(
      details.filter((item) => filterBooleanColumn(item.isSpecialProduct, true)).map((item) => item.detailGUID),
      ["gift-bag"],
      "\u7279\u6B8A\u5546\u54C1\u5217\u5E94\u652F\u6301\u5E03\u5C14\u8FC7\u6EE4"
    );
    assertDeepEqual(
      details.filter((item) => filterBooleanColumn(item.isSpecialProduct, false)).map((item) => item.detailGUID),
      ["cheap-card"],
      "\u7279\u6B8A\u5546\u54C1 false \u8FC7\u6EE4\u4E0D\u5E94\u5305\u542B\u672A\u68C0\u6D4B\u7A7A\u503C"
    );
    assertDeepEqual(
      details.filter((item) => filterProductStatusColumn(item, "notExists")).map((item) => item.detailGUID),
      ["gift-bag"],
      "\u5546\u54C1\u72B6\u6001\u5217\u8FC7\u6EE4\u5E94\u590D\u7528\u5546\u54C1\u72B6\u6001\u89C4\u5219"
    );
    assertDeepEqual(
      details.filter((item) => filterBarcodeStatusColumn(item, "noMatch")).map((item) => item.detailGUID),
      ["gift-bag"],
      "\u6761\u7801\u72B6\u6001\u5217\u8FC7\u6EE4\u5E94\u590D\u7528\u6761\u7801\u72B6\u6001\u89C4\u5219"
    );
    assertDeepEqual(
      details.filter((item) => matchesActionTypeColumnFilter(item, 2 /* UpdatePurchasePrice */)).map((item) => item.detailGUID),
      ["gift-bag"],
      "\u64CD\u4F5C\u7C7B\u578B\u5217\u8FC7\u6EE4\u5E94\u6309\u5F53\u524D\u660E\u7EC6\u64CD\u4F5C\u7C7B\u578B\u5339\u914D"
    );
    const topFiltered = filterInvoiceDetails(details, {
      searchText: "gift",
      priceFilter: "all",
      productStatusFilter: "notExists",
      barcodeStatusFilter: "all"
    });
    assertDeepEqual(
      topFiltered.filter((item) => filterBooleanColumn(item.isSpecialProduct, true)).map((item) => item.detailGUID),
      ["gift-bag"],
      "\u5217\u5934\u8FC7\u6EE4\u5E94\u80FD\u4E0E\u9876\u90E8\u641C\u7D22\u548C\u72B6\u6001\u7B5B\u9009\u6309 AND \u53E0\u52A0"
    );
    assert(editPageSource.includes("const [columnFilteredValues, setColumnFilteredValues]"), "\u7F16\u8F91\u9875\u5E94\u7EF4\u62A4\u53D7\u63A7\u5217\u8FC7\u6EE4\u72B6\u6001");
    assert(editPageSource.includes("setColumnFilteredValues(filters as Record<string, (React.Key | boolean)[] | null>)"), "\u8868\u683C\u53D8\u5316\u65F6\u5E94\u4FDD\u5B58\u5217\u5934\u8FC7\u6EE4\u72B6\u6001");
    assert(editPageSource.includes("setColumnFilteredValues({})"), "\u6E05\u7A7A\u8FC7\u6EE4\u5E94\u91CD\u7F6E\u5217\u5934\u8FC7\u6EE4");
    assert(editPageSource.includes("t('posAdmin.invoiceDetail.activeColumnFilters', '\u5217\u5934\u8FC7\u6EE4\uFF1A{{count}}'"), "\u5F53\u524D\u8FC7\u6EE4\u680F\u5E94\u5C55\u793A\u5217\u5934\u8FC7\u6EE4\u6458\u8981");
    [
      "quantity",
      "lastPurchasePrice",
      "purchasePrice",
      "retailPrice",
      "pricingFloatRate",
      "newAutoRetailPrice",
      "discountRate",
      "amount"
    ].forEach((field) => {
      assert(editPageSource.includes(`...getNumberColumnFilterProps('${field}'`), `${field} \u6570\u5B57\u5217\u5E94\u6302\u8F7D\u5217\u5934\u8FC7\u6EE4`);
    });
    ["itemNumber", "barcode", "productName"].forEach((field) => {
      assert(editPageSource.includes(`...getTextColumnSearchProps('${field}'`), `${field} \u6587\u672C\u5217\u5E94\u6302\u8F7D\u5217\u5934\u8FC7\u6EE4`);
    });
    assert(editPageSource.includes("matchesActionTypeColumnFilter(record, value, rowActions)"), "\u64CD\u4F5C\u7C7B\u578B\u5217\u5E94\u6309 rowActions \u8986\u76D6\u503C\u8FC7\u6EE4");
    const seqColumnSource = editPageSource.slice(
      editPageSource.indexOf("title: renderCompactHeader(t('posAdmin.invoiceDetail.seqNo'"),
      editPageSource.indexOf("title: renderCompactHeader(t('posAdmin.invoiceDetail.image'")
    );
    const imageColumnSource = editPageSource.slice(
      editPageSource.indexOf("title: renderCompactHeader(t('posAdmin.invoiceDetail.image'"),
      editPageSource.indexOf("title: renderCompactHeader(t('posAdmin.invoiceDetail.itemNumber'")
    );
    assert(!seqColumnSource.includes("onFilter") && !seqColumnSource.includes("filters:") && !seqColumnSource.includes("filteredValue"), "\u5E8F\u53F7\u5217\u4E0D\u5E94\u6302\u8F7D\u5217\u8FC7\u6EE4");
    assert(!imageColumnSource.includes("onFilter") && !imageColumnSource.includes("filters:") && !imageColumnSource.includes("filteredValue"), "\u56FE\u7247\u5217\u4E0D\u5E94\u6302\u8F7D\u5217\u8FC7\u6EE4");
  });
  if (tableColumnFilterBehaviorFailure) failures.push(tableColumnFilterBehaviorFailure);
  const compactTableDisplayFailure = await runTest("\u7F16\u8F91\u9875\u660E\u7EC6\u8868\u5E94\u7D27\u51D1\u663E\u793A\u5E76\u56FA\u5B9A\u5173\u952E\u8BC6\u522B\u5217", () => {
    assert(editPageSource.includes("function renderCompactHeader"), "\u7F16\u8F91\u9875\u5E94\u63D0\u4F9B\u7EDF\u4E00\u5217\u5934\u6362\u884C\u6E32\u67D3 helper");
    assert(editPageSource.includes("function renderNowrapText"), "\u7F16\u8F91\u9875\u5E94\u63D0\u4F9B\u666E\u901A\u6587\u672C nowrap helper");
    assert(editPageSource.includes("function renderNumericCell"), "\u7F16\u8F91\u9875\u5E94\u63D0\u4F9B\u6570\u5B57 nowrap helper");
    assert(editPageSource.includes('className="invoice-detail-compact-table"'), "\u660E\u7EC6\u8868\u5E94\u4F7F\u7528\u4E13\u7528\u7D27\u51D1 className");
    assert(editPageSource.includes("scroll={{ x: 1600, y: tableScrollY }}"), "\u660E\u7EC6\u8868\u6A2A\u5411\u6EDA\u52A8\u5BBD\u5EA6\u5E94\u6309\u7D27\u51D1\u5217\u5BBD\u91CD\u65B0\u8BA1\u7B97");
    assert(editPageSource.includes("fixed: true") && editPageSource.includes("columnWidth: 36"), "\u9009\u62E9\u5217\u5E94\u56FA\u5B9A\u5728\u5DE6\u4FA7\u5E76\u538B\u7F29\u5BBD\u5EA6");
    assert(editPageSource.includes("width: 44,\n      align: 'right',\n      fixed: 'left'"), "\u5E8F\u53F7\u5217\u5E94\u56FA\u5B9A\u5728\u5DE6\u4FA7\u5E76\u538B\u7F29\u5BBD\u5EA6");
    assert(editPageSource.includes("width: 48,\n      fixed: 'left'"), "\u56FE\u7247\u5217\u5E94\u56FA\u5B9A\u5728\u5DE6\u4FA7\u5E76\u538B\u7F29\u5BBD\u5EA6");
    assert(editPageSource.includes("width: 108,\n      fixed: 'left'"), "\u8D27\u53F7\u5217\u5E94\u56FA\u5B9A\u5728\u5DE6\u4FA7\u5E76\u538B\u7F29\u5BBD\u5EA6");
    assert(editPageSource.includes("width={36} height={36}"), "\u56FE\u7247\u7F29\u7565\u56FE\u5E94\u538B\u7F29\u5230 36px");
    assert(editPageSource.includes("<BarcodePreview value={v} compactCopy />"), "\u6761\u7801\u6587\u672C\u4E0D\u5E94\u8BBE\u7F6E textMaxWidth \u7701\u7565\u9690\u85CF");
    assert(editPageSource.includes("additionalBarcodeCount"), "\u6761\u7801\u5217\u5E94\u663E\u793A\u526F\u7801\u6570\u91CF\u6807\u7B7E");
    assert(editPageSource.includes("record.additionalBarcodes?.join"), "\u526F\u7801\u6570\u91CF\u6807\u7B7E\u5E94\u60AC\u6D6E\u663E\u793A\u5B8C\u6574\u526F\u7801\u5217\u8868");
    assert(editPageSource.includes("formatPricingFloatRate"), "\u5B9A\u4EF7\u6D6E\u7387\u5E94\u4F7F\u7528\u4E13\u7528\u4E24\u4F4D\u5C0F\u6570\u683C\u5F0F\u5316");
    assert(!editPageSource.includes("`${(v * 100).toFixed(1)}%`"), "\u5B9A\u4EF7\u6D6E\u7387\u4E0D\u5E94\u6309\u767E\u5206\u6BD4\u5C55\u793A");
    assert(!editPageSource.includes("\n          bordered\n"), "\u660E\u7EC6\u8868\u4E0D\u5E94\u7EE7\u7EED\u4F7F\u7528 bordered \u8FB9\u6846");
    assert(editPageSource.includes("invoice-detail-nowrap"), "\u8D27\u53F7\u3001\u6761\u7801\u548C\u6570\u5B57\u5185\u5BB9\u5E94\u4F7F\u7528 nowrap class");
    assert(editPageSource.includes("invoice-detail-numeric-cell"), "\u6570\u5B57\u5217\u5E94\u4F7F\u7528 tabular nums class");
    assert(globalStyleSource.includes(".invoice-detail-compact-table .ant-table-thead > tr > th"), "\u7D27\u51D1\u8868\u683C\u5E94\u6709 scoped \u8868\u5934\u6837\u5F0F");
    assert(globalStyleSource.includes("white-space: normal"), "\u5217\u5934\u6837\u5F0F\u5E94\u5141\u8BB8\u6362\u884C");
    assert(globalStyleSource.includes(".invoice-detail-nowrap") && globalStyleSource.includes("white-space: nowrap"), "\u5185\u5BB9\u5173\u952E\u5B57\u6BB5\u5E94\u6709 nowrap \u6837\u5F0F");
    assert(globalStyleSource.includes(".invoice-detail-numeric-cell") && globalStyleSource.includes("font-variant-numeric: tabular-nums"), "\u6570\u5B57\u5217\u5E94\u4F7F\u7528\u7B49\u5BBD\u6570\u5B57\u89C6\u89C9");
  });
  if (compactTableDisplayFailure) failures.push(compactTableDisplayFailure);
  const inlineBooleanToggleFailure = await runTest("\u7F16\u8F91\u9875\u81EA\u52A8\u5B9A\u4EF7\u548C\u7279\u6B8A\u5546\u54C1\u5E94\u53CC\u51FB\u672C\u5730\u7F16\u8F91\u5E76\u968F\u4FDD\u5B58\u660E\u7EC6\u7EDF\u4E00\u843D\u5E93", () => {
    assert(editPageSource.includes("EditableBooleanCell,"), "\u7F16\u8F91\u9875\u5E94\u5BFC\u5165\u884C\u5185\u5E03\u5C14\u7F16\u8F91\u5355\u5143\u683C");
    assert(editCellsSource.includes("function EditableBooleanCell"), "\u884C\u5185\u7F16\u8F91\u7EC4\u4EF6\u6587\u4EF6\u5E94\u5B9A\u4E49\u5E03\u5C14\u7F16\u8F91\u5355\u5143\u683C");
    assert(editCellsSource.includes("const handleToggle = () => onSave(detailGuid, field, !actualValue)"), "\u5E03\u5C14\u5B57\u6BB5\u5E94\u4FDD\u7559\u672C\u5730\u53D6\u53CD\u4FDD\u5B58\u903B\u8F91");
    assert(editCellsSource.includes("onDoubleClick={toggleTrigger === 'doubleClick' ? handleToggle : undefined}"), "\u5E03\u5C14\u5B57\u6BB5\u5E94\u53CC\u51FB\u5207\u6362\u672C\u5730\u503C");
    assert(editPageSource.includes('field="autoPricing"'), "\u81EA\u52A8\u5B9A\u4EF7\u5E94\u7EB3\u5165\u53EF\u7F16\u8F91\u5B57\u6BB5");
    assert(editPageSource.includes('field="isSpecialProduct"'), "\u7279\u6B8A\u5546\u54C1\u5E94\u7EB3\u5165\u53EF\u7F16\u8F91\u5B57\u6BB5");
    assert(editPageSource.includes("const handleInlineDetailSave = useCallback"), "\u884C\u5185\u7F16\u8F91\u5E94\u5148\u5199\u5165\u672C\u5730\u660E\u7EC6");
    assert(editPageSource.includes("applyInvoiceDetailInlineEdit(prev, detailGuid, field, normalizedValue)"), "\u884C\u5185\u7F16\u8F91\u5E94\u590D\u7528\u672C\u5730\u660E\u7EC6\u66F4\u65B0 helper");
    assert(!editPageSource.includes("handleInlineBooleanToggle"), "\u5E03\u5C14\u5B57\u6BB5\u4E0D\u5E94\u518D\u4F7F\u7528\u5373\u65F6\u843D\u5E93 handler");
    assert(!editPageSource.includes("inlineBooleanUpdatingKeys"), "\u5E03\u5C14\u5B57\u6BB5\u4E0D\u5E94\u518D\u7EF4\u62A4\u5373\u65F6\u4FDD\u5B58\u4E2D\u7684\u72B6\u6001");
    assert(
      !editPageSource.includes("await batchUpdateDetails(invoiceGuid, [{ detailGUID: record.detailGUID }], editFields)"),
      "\u5E03\u5C14\u5B57\u6BB5\u53CC\u51FB\u4E0D\u5E94\u7ACB\u5373\u8C03\u7528\u6279\u91CF\u66F4\u65B0\u63A5\u53E3"
    );
    assert(editPageSource.includes("buildInvoiceDetailSaveItems(details)"), "\u4FDD\u5B58\u660E\u7EC6\u5E94\u7EDF\u4E00\u6784\u5EFA\u4E1A\u52A1\u5B57\u6BB5 payload");
    assert(editPageSource.includes("await batchUpsertDetails(invoiceGuid, items)"), "\u4FDD\u5B58\u660E\u7EC6\u5E94\u7EDF\u4E00\u8C03\u7528 batchUpsertDetails \u843D\u5E93");
  });
  if (inlineBooleanToggleFailure) failures.push(inlineBooleanToggleFailure);
  const emptyDiscountRateFailure = await runTest("\u6298\u6263\u7387\u7A7A\u503C\u53CC\u51FB\u7F16\u8F91\u4E0D\u5E94\u88AB\u515C\u5E95\u6210 0 \u843D\u5E93", () => {
    assert(editPageSource.includes("value={discountRateToPercent(v)}"), "\u6298\u6263\u7387\u7F16\u8F91\u503C\u5E94\u4FDD\u7559\u7A7A\u503C\uFF0C\u4E0D\u5E94\u628A\u7A7A\u503C\u515C\u5E95\u6210 0");
    assert(!editPageSource.includes("value={discountRateToPercent(v) ?? 0}"), "\u6298\u6263\u7387\u7A7A\u503C\u4E0D\u80FD\u5728\u8FDB\u5165\u7F16\u8F91\u6001\u65F6\u88AB\u6539\u6210 0");
  });
  if (emptyDiscountRateFailure) failures.push(emptyDiscountRateFailure);
  const pastePriceParseFailure = await runTest("\u7C98\u8D34\u89E3\u6790\u5E94\u8BC6\u522B\u5E26\u8D27\u5E01\u7B26\u53F7\u7684\u672C\u6B21\u8FDB\u8D27\u4EF7", () => {
    const [row] = parsePasteText("WEW1272	9313559661518	Folded Wrap	15	A$1.25	$3.50	AUD 2.99");
    assertEqual(row.itemNumber, "WEW1272", "\u5E94\u4FDD\u7559\u8D27\u53F7");
    assertEqual(row.quantity, 15, "\u5E94\u89E3\u6790\u6570\u91CF");
    assertEqual(row.purchasePrice, 1.25, "\u5E94\u89E3\u6790\u5E26 A$ \u7684\u672C\u6B21\u8FDB\u8D27\u4EF7");
    assertEqual(row.newAutoRetailPrice, 3.5, "\u5E94\u89E3\u6790\u5E26 $ \u7684\u65B0\u81EA\u52A8\u96F6\u552E\u4EF7");
    assertEqual(row.retailPrice, 2.99, "\u5E94\u89E3\u6790\u5E26 AUD \u7684\u96F6\u552E\u4EF7");
  });
  if (pastePriceParseFailure) failures.push(pastePriceParseFailure);
  const pasteFieldOrderFailure = await runTest("\u7C98\u8D34\u89E3\u6790\u5E94\u652F\u6301\u5F39\u7A97\u81EA\u5B9A\u4E49\u5217\u5BF9\u5E94\u5B57\u6BB5", () => {
    const [row] = parsePasteText(
      "9313559661518	WEW1272	15	Folded Wrap	A$1.25	AUD 2.99	$3.50",
      ["barcode", "itemNumber", "quantity", "productName", "purchasePrice", "retailPrice", "newAutoRetailPrice"]
    );
    assertDeepEqual(defaultPasteFieldOrder, ["itemNumber", "barcode", "productName", "quantity", "purchasePrice", "newAutoRetailPrice", "retailPrice"], "\u9ED8\u8BA4\u7C98\u8D34\u5217\u987A\u5E8F\u5E94\u4FDD\u6301\u65E7\u987A\u5E8F");
    assertEqual(row.itemNumber, "WEW1272", "\u81EA\u5B9A\u4E49\u987A\u5E8F\u5E94\u89E3\u6790\u8D27\u53F7");
    assertEqual(row.barcode, "9313559661518", "\u81EA\u5B9A\u4E49\u987A\u5E8F\u5E94\u89E3\u6790\u6761\u7801");
    assertEqual(row.productName, "Folded Wrap", "\u81EA\u5B9A\u4E49\u987A\u5E8F\u5E94\u89E3\u6790\u5546\u54C1\u540D\u79F0");
    assertEqual(row.quantity, 15, "\u81EA\u5B9A\u4E49\u987A\u5E8F\u5E94\u89E3\u6790\u6570\u91CF");
    assertEqual(row.purchasePrice, 1.25, "\u81EA\u5B9A\u4E49\u987A\u5E8F\u5E94\u89E3\u6790\u8FDB\u8D27\u4EF7");
    assertEqual(row.retailPrice, 2.99, "\u81EA\u5B9A\u4E49\u987A\u5E8F\u5E94\u89E3\u6790\u96F6\u552E\u4EF7");
    assertEqual(row.newAutoRetailPrice, 3.5, "\u81EA\u5B9A\u4E49\u987A\u5E8F\u5E94\u89E3\u6790\u65B0\u81EA\u52A8\u96F6\u552E\u4EF7");
  });
  if (pasteFieldOrderFailure) failures.push(pasteFieldOrderFailure);
  const pasteSkipFieldFailure = await runTest("\u7C98\u8D34\u89E3\u6790\u5E94\u5141\u8BB8\u8DF3\u8FC7 Excel \u591A\u4F59\u5217", () => {
    const [row] = parsePasteText(
      "WEW1272	\u5907\u6CE8\u5217	9313559661518	Folded Wrap	15",
      ["itemNumber", "skip", "barcode", "productName", "quantity"]
    );
    assertEqual(row.itemNumber, "WEW1272", "\u8DF3\u8FC7\u5217\u4E0D\u5E94\u5F71\u54CD\u540E\u7EED\u8D27\u53F7\u6620\u5C04");
    assertEqual(row.barcode, "9313559661518", "\u8DF3\u8FC7\u5217\u4E0D\u5E94\u5F71\u54CD\u540E\u7EED\u6761\u7801\u6620\u5C04");
    assertEqual(row.productName, "Folded Wrap", "\u8DF3\u8FC7\u5217\u4E0D\u5E94\u5F71\u54CD\u540E\u7EED\u5546\u54C1\u540D\u79F0\u6620\u5C04");
    assertEqual(row.quantity, 15, "\u8DF3\u8FC7\u5217\u4E0D\u5E94\u5F71\u54CD\u540E\u7EED\u6570\u91CF\u6620\u5C04");
  });
  if (pasteSkipFieldFailure) failures.push(pasteSkipFieldFailure);
  const pasteSkipExtraColumnFailure = await runTest("\u7C98\u8D34\u89E3\u6790\u8DF3\u8FC7\u591A\u4F59\u5217\u540E\u4ECD\u5E94\u4FDD\u7559\u5168\u90E8\u4E1A\u52A1\u5B57\u6BB5", () => {
    const [row] = parsePasteText(
      "WEW1272	\u5907\u6CE8\u5217	9313559661518	Folded Wrap	15	A$1.25	$3.50	AUD 2.99",
      ["itemNumber", "skip", "barcode", "productName", "quantity", "purchasePrice", "newAutoRetailPrice", "retailPrice"]
    );
    assertEqual(row.itemNumber, "WEW1272", "8 \u5217\u7C98\u8D34\u5E94\u4FDD\u7559\u8D27\u53F7");
    assertEqual(row.barcode, "9313559661518", "8 \u5217\u7C98\u8D34\u5E94\u4FDD\u7559\u6761\u7801");
    assertEqual(row.productName, "Folded Wrap", "8 \u5217\u7C98\u8D34\u5E94\u4FDD\u7559\u5546\u54C1\u540D\u79F0");
    assertEqual(row.quantity, 15, "8 \u5217\u7C98\u8D34\u5E94\u4FDD\u7559\u6570\u91CF");
    assertEqual(row.purchasePrice, 1.25, "8 \u5217\u7C98\u8D34\u5E94\u4FDD\u7559\u8FDB\u8D27\u4EF7");
    assertEqual(row.newAutoRetailPrice, 3.5, "8 \u5217\u7C98\u8D34\u5E94\u4FDD\u7559\u65B0\u81EA\u52A8\u96F6\u552E\u4EF7");
    assertEqual(row.retailPrice, 2.99, "8 \u5217\u7C98\u8D34\u5E94\u4FDD\u7559\u96F6\u552E\u4EF7");
  });
  if (pasteSkipExtraColumnFailure) failures.push(pasteSkipExtraColumnFailure);
  const pasteFieldOrderUiFailure = await runTest("\u7F16\u8F91\u9875\u7C98\u8D34\u5F39\u7A97\u5E94\u63D0\u4F9B\u5217\u5B57\u6BB5\u6620\u5C04\u5E76\u672C\u5730\u8BB0\u4F4F\u914D\u7F6E", () => {
    assert(editPageSource.includes("pasteFieldOrder"), "\u7F16\u8F91\u9875\u5E94\u7EF4\u62A4 pasteFieldOrder \u72B6\u6001");
    assert(editPageSource.includes("hbweb_rv.localSupplierInvoice.pasteFieldOrder.v1"), "\u7F16\u8F91\u9875\u5E94\u4F7F\u7528\u56FA\u5B9A localStorage key \u4FDD\u5B58\u5217\u987A\u5E8F");
    assert(editPageSource.includes("normalizeRetailPriceOnPaste"), "\u7F16\u8F91\u9875\u5E94\u7EF4\u62A4\u96F6\u552E\u4EF7\u5C0F\u6570\u89C4\u8303\u5316\u5F00\u5173");
    assert(editPageSource.includes("parsePasteText(pasteText, pasteFieldOrder, pasteParseOptions)"), "\u63D0\u4EA4\u548C\u9884\u89C8\u5E94\u4F7F\u7528\u5F53\u524D\u5217\u5B57\u6BB5\u6620\u5C04\u548C\u7C98\u8D34\u89E3\u6790\u9009\u9879\u89E3\u6790");
    assert(editPageSource.includes("pasteMultilineCellMode"), "\u7F16\u8F91\u9875\u5E94\u7EF4\u62A4\u591A\u884C\u5355\u5143\u683C\u5904\u7406\u6A21\u5F0F");
    assert(editPageSource.includes("pasteMultilineMerge"), "\u7F16\u8F91\u9875\u5E94\u63D0\u4F9B\u5355\u5143\u683C\u5185\u5408\u5E76\u9009\u9879");
    assert(editPageSource.includes("pasteMultilineSmartSplit"), "\u7F16\u8F91\u9875\u5E94\u63D0\u4F9B\u6309\u6362\u884C\u667A\u80FD\u62C6\u5206\u9009\u9879");
    assert(editPageSource.includes("pasteMultilineUnsafeWarning"), "\u7F16\u8F91\u9875\u5E94\u63D0\u793A\u65E0\u6CD5\u5B89\u5168\u62C6\u5206\u7684\u8BB0\u5F55\u4F1A\u81EA\u52A8\u5408\u5E76");
    assert(editPageSource.includes("pasteFieldDuplicateWarning"), "\u7F16\u8F91\u9875\u5E94\u63D0\u4F9B\u91CD\u590D\u5B57\u6BB5\u6821\u9A8C\u63D0\u793A");
    assert(editPageSource.includes("pasteRestoreDefaultOrder"), "\u7F16\u8F91\u9875\u5E94\u63D0\u4F9B\u6062\u590D\u9ED8\u8BA4\u5217\u987A\u5E8F\u5165\u53E3");
    assert(editPageSource.includes("pasteFieldSkip"), "\u7F16\u8F91\u9875\u5E94\u63D0\u4F9B\u8DF3\u8FC7\u5217\u9009\u9879");
    assert(editPageSource.includes("getPasteTextMaxColumnCount"), "\u7F16\u8F91\u9875\u5E94\u6309\u7C98\u8D34\u5185\u5BB9\u5217\u6570\u6269\u5C55\u6620\u5C04\u4F4D");
    assert(editPageSource.includes("analyzePasteMultilineCells"), "\u7F16\u8F91\u9875\u5E94\u4F7F\u7528\u5171\u4EAB helper \u5206\u6790\u591A\u884C\u5355\u5143\u683C");
    assert(editPageSource.includes("fill('skip')"), "\u65B0\u589E\u7684\u591A\u4F59\u5217\u6620\u5C04\u5E94\u9ED8\u8BA4\u8BBE\u7F6E\u4E3A\u8DF3\u8FC7");
  });
  if (pasteFieldOrderUiFailure) failures.push(pasteFieldOrderUiFailure);
  const serviceContractFailure = await runTest("\u670D\u52A1\u5C42\u5E94\u663E\u5F0F\u8BC6\u522B\u4E1A\u52A1\u5931\u8D25\u5E76\u4FDD\u7559 payload", () => {
    assert(serviceSource.includes("ensureHqProducts("), "\u670D\u52A1\u5C42\u5E94\u5BFC\u51FA ensureHqProducts");
    assert(serviceSource.includes("/details/ensure-hq-products"), "\u670D\u52A1\u5C42\u5E94\u8C03\u7528\u5546\u54C1\u7EA7\u540C\u6B65\u5230 HQ \u63A5\u53E3");
    assert(serviceSource.includes("updateHqProducts("), "\u670D\u52A1\u5C42\u5E94\u5BFC\u51FA\u5B57\u6BB5\u7EA7\u66F4\u65B0 HQ \u63A5\u53E3");
    assert(serviceSource.includes("/details/update-hq-products"), "\u670D\u52A1\u5C42\u5E94\u8C03\u7528\u5B57\u6BB5\u7EA7\u66F4\u65B0 HQ \u4E13\u7528\u63A5\u53E3");
    assert(serviceSource.includes("startUpdateToStorePricesJob("), "\u670D\u52A1\u5C42\u5E94\u5BFC\u51FA\u66F4\u65B0\u5230\u5206\u5E97\u540E\u53F0\u4EFB\u52A1\u63D0\u4EA4\u63A5\u53E3");
    assert(serviceSource.includes("/update-to-store-prices/jobs"), "\u670D\u52A1\u5C42\u5E94\u8C03\u7528\u66F4\u65B0\u5230\u5206\u5E97\u540E\u53F0\u4EFB\u52A1\u63D0\u4EA4\u63A5\u53E3");
    assert(serviceSource.includes("getUpdateToStorePricesJob("), "\u670D\u52A1\u5C42\u5E94\u5BFC\u51FA\u66F4\u65B0\u5230\u5206\u5E97\u540E\u53F0\u4EFB\u52A1\u67E5\u8BE2\u63A5\u53E3");
    assert(serviceSource.includes("/update-to-store-prices/jobs/${encodeURIComponent(jobId)}"), "\u670D\u52A1\u5C42\u5E94\u8C03\u7528\u66F4\u65B0\u5230\u5206\u5E97\u540E\u53F0\u4EFB\u52A1\u67E5\u8BE2\u63A5\u53E3");
    assert(serviceSource.includes("startUpdateHqProductsJob("), "\u670D\u52A1\u5C42\u5E94\u5BFC\u51FA\u66F4\u65B0HQ\u5546\u54C1\u540E\u53F0\u4EFB\u52A1\u63D0\u4EA4\u63A5\u53E3");
    assert(serviceSource.includes("/details/update-hq-products/jobs"), "\u670D\u52A1\u5C42\u5E94\u8C03\u7528\u66F4\u65B0HQ\u5546\u54C1\u540E\u53F0\u4EFB\u52A1\u63D0\u4EA4\u63A5\u53E3");
    assert(serviceSource.includes("getUpdateHqProductsJob("), "\u670D\u52A1\u5C42\u5E94\u5BFC\u51FA\u66F4\u65B0HQ\u5546\u54C1\u540E\u53F0\u4EFB\u52A1\u67E5\u8BE2\u63A5\u53E3");
    assert(serviceSource.includes("/details/update-hq-products/jobs/${encodeURIComponent(jobId)}"), "\u670D\u52A1\u5C42\u5E94\u8C03\u7528\u66F4\u65B0HQ\u5546\u54C1\u540E\u53F0\u4EFB\u52A1\u67E5\u8BE2\u63A5\u53E3");
    assert(serviceSource.includes("assertApiSuccess"), "\u670D\u52A1\u5C42\u5E94\u590D\u7528\u4E1A\u52A1\u5931\u8D25\u68C0\u67E5 helper");
    assert(serviceSource.includes("response.success === false || response.isSuccess === false"), "\u670D\u52A1\u5C42\u5E94\u8BC6\u522B success false");
    assert(serviceSource.includes("assertApiSuccess(response, '\u6279\u91CF\u6267\u884C\u64CD\u4F5C\u5931\u8D25')"), "\u6279\u91CF\u6267\u884C\u670D\u52A1\u5C42\u5E94\u8BC6\u522B\u4E1A\u52A1\u5931\u8D25");
    assert(serviceSource.includes("assertApiSuccess(response, '\u4FDD\u5B58\u660E\u7EC6\u5931\u8D25')"), "\u4FDD\u5B58\u660E\u7EC6\u670D\u52A1\u5C42\u5E94\u8BC6\u522B\u4E1A\u52A1\u5931\u8D25");
    assert(serviceSource.includes("assertApiSuccess(response, '\u6279\u91CF\u7F16\u8F91\u660E\u7EC6\u5931\u8D25')"), "\u6279\u91CF\u7F16\u8F91\u660E\u7EC6\u670D\u52A1\u5C42\u5E94\u8BC6\u522B\u4E1A\u52A1\u5931\u8D25");
    assert(serviceSource.includes("assertApiSuccess(response, '\u6279\u91CF\u8BBE\u7F6E\u64CD\u4F5C\u7C7B\u578B\u5931\u8D25')"), "\u6279\u91CF\u8BBE\u7F6E\u64CD\u4F5C\u7C7B\u578B\u670D\u52A1\u5C42\u5E94\u8BC6\u522B\u4E1A\u52A1\u5931\u8D25");
    assert(serviceSource.includes("assertApiSuccess(response, '\u66F4\u65B0\u64CD\u4F5C\u7C7B\u578B\u5931\u8D25')"), "\u884C\u64CD\u4F5C\u7C7B\u578B\u670D\u52A1\u5C42\u5E94\u8BC6\u522B\u4E1A\u52A1\u5931\u8D25");
  });
  if (serviceContractFailure) failures.push(serviceContractFailure);
  const originalFetch = globalThis.fetch;
  let capturedUrl = "";
  let capturedInit;
  const serviceSuccessFailure = await runTest("syncInvoicesFromHq \u5E94\u8C03\u7528\u9875\u9762\u4E13\u7528\u63A5\u53E3\u5E76\u4FDD\u7559 payload", async () => {
    globalThis.fetch = async (input, init) => {
      capturedUrl = String(input);
      capturedInit = init;
      return new Response(JSON.stringify({
        success: true,
        data: {
          requestId: "req-1",
          status: "Succeeded",
          startedAt: "2026-05-01T00:00:00Z",
          completedAt: "2026-05-01T00:00:01Z",
          durationMs: 1e3,
          invoiceAddedCount: 1,
          invoiceUpdatedCount: 2,
          detailAddedCount: 3,
          detailUpdatedCount: 4,
          totalProcessed: 10,
          errors: []
        }
      }), {
        status: 200,
        headers: { "Content-Type": "application/json" }
      });
    };
    await syncInvoicesFromHq({
      selectedStoreCodes: ["S01"],
      startDate: "2026-05-01",
      endDate: "2026-05-31"
    });
    assertEqual(capturedUrl, "/api/react/v1/local-supplier-invoices/sync-from-hq", "\u5E94\u8C03\u7528\u9875\u9762\u4E13\u7528\u63A5\u53E3");
    assertEqual(capturedInit?.method, "POST", "\u5E94\u4F7F\u7528 POST");
    assertDeepEqual(
      JSON.parse(String(capturedInit?.body)),
      { selectedStoreCodes: ["S01"], startDate: "2026-05-01", endDate: "2026-05-31" },
      "\u5E94\u4FDD\u7559\u540C\u6B65\u8BF7\u6C42 payload"
    );
  });
  if (serviceSuccessFailure) failures.push(serviceSuccessFailure);
  const serviceFailure = await runTest("syncInvoicesFromHq \u9047\u5230\u4E1A\u52A1\u5931\u8D25\u5E94\u629B\u51FA\u540E\u7AEF\u6D88\u606F", async () => {
    globalThis.fetch = async () => new Response(JSON.stringify({
      success: false,
      message: "HQ \u540C\u6B65\u5931\u8D25\uFF1A\u6D4B\u8BD5\u4E1A\u52A1\u9519\u8BEF"
    }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
    await assertRejects(
      () => syncInvoicesFromHq({ startDate: "2026-05-01" }),
      "HQ \u540C\u6B65\u5931\u8D25\uFF1A\u6D4B\u8BD5\u4E1A\u52A1\u9519\u8BEF",
      "\u4E1A\u52A1\u5931\u8D25\u65F6\u5E94\u900F\u4F20\u540E\u7AEF\u6D88\u606F"
    );
  });
  if (serviceFailure) failures.push(serviceFailure);
  const serviceHttpFailure = await runTest("syncInvoicesFromHq \u9047\u5230 400 \u5931\u8D25\u5E94\u4FDD\u7559\u7EDF\u8BA1 payload", async () => {
    globalThis.fetch = async () => new Response(JSON.stringify({
      success: false,
      message: "HQ \u540C\u6B65\u90E8\u5206\u5931\u8D25",
      data: {
        requestId: "req-failed",
        status: "Failed",
        startedAt: "2026-05-01T00:00:00Z",
        completedAt: "2026-05-01T00:00:01Z",
        durationMs: 1e3,
        invoiceAddedCount: 1,
        invoiceUpdatedCount: 0,
        detailAddedCount: 0,
        detailUpdatedCount: 0,
        totalProcessed: 1,
        errors: ["\u6D4B\u8BD5\u9875\u5931\u8D25"]
      }
    }), {
      status: 400,
      headers: { "Content-Type": "application/json" }
    });
    await assertRequestErrorPayload(
      () => syncInvoicesFromHq({ startDate: "2026-05-01" }),
      "HQ \u540C\u6B65\u90E8\u5206\u5931\u8D25",
      "HTTP \u5931\u8D25\u65F6\u5E94\u900F\u4F20\u540E\u7AEF\u6D88\u606F"
    );
  });
  if (serviceHttpFailure) failures.push(serviceHttpFailure);
  const ensureHqServiceSuccessFailure = await runTest("ensureHqProducts \u5E94\u8C03\u7528\u5546\u54C1\u7EA7\u63A5\u53E3\u5E76\u4FDD\u7559 payload", async () => {
    globalThis.fetch = async (input, init) => {
      capturedUrl = String(input);
      capturedInit = init;
      return new Response(JSON.stringify({
        success: true,
        data: {
          total: 2,
          hqExisting: 1,
          hbwebCreated: 1,
          hqCreated: 1,
          hqSynced: 2,
          hqPurchasePricesUpdated: 2,
          skipped: 0,
          failed: 0,
          errors: []
        }
      }), {
        status: 200,
        headers: { "Content-Type": "application/json" }
      });
    };
    await ensureHqProducts("invoice-1", {
      detailGuids: ["detail-1", "detail-2"],
      targetStoreCodes: ["1033"],
      idempotencyKey: "idem-1"
    });
    assertEqual(capturedUrl, "/api/react/v1/local-supplier-invoices/invoice-1/details/ensure-hq-products", "\u5E94\u8C03\u7528\u5546\u54C1\u7EA7\u540C\u6B65\u5230 HQ \u63A5\u53E3");
    assertEqual(capturedInit?.method, "POST", "\u5546\u54C1\u540C\u6B65\u5230 HQ \u5E94\u4F7F\u7528 POST");
    assertDeepEqual(
      JSON.parse(String(capturedInit?.body)),
      { detailGuids: ["detail-1", "detail-2"], targetStoreCodes: ["1033"], idempotencyKey: "idem-1" },
      "\u5546\u54C1\u540C\u6B65\u5230 HQ \u5E94\u4FDD\u7559\u8BF7\u6C42 payload"
    );
  });
  if (ensureHqServiceSuccessFailure) failures.push(ensureHqServiceSuccessFailure);
  const updateToStorePayloadFailure = await runTest("updateToStorePrices \u4E0D\u5E94\u4F20\u9012 updateHqProduct", async () => {
    globalThis.fetch = async (input, init) => {
      capturedUrl = String(input);
      capturedInit = init;
      return new Response(JSON.stringify({
        success: true,
        data: {
          inserted: 0,
          updated: 1,
          failed: 0
        }
      }), {
        status: 200,
        headers: { "Content-Type": "application/json" }
      });
    };
    const result = await updateToStorePrices({
      invoiceGuid: "invoice-1",
      detailGuids: ["detail-1"],
      targetStoreCodes: ["1033"],
      updateFields: {
        updatePurchasePrice: true,
        updateRetailPrice: false,
        updateIsAutoPricing: false,
        updateIsSpecialProduct: false,
        updateDiscountRate: false
      }
    });
    assertEqual(capturedUrl, "/api/react/v1/local-supplier-invoices/update-to-store-prices", "\u66F4\u65B0\u5230\u5206\u5E97\u63A5\u53E3\u5730\u5740\u5E94\u4FDD\u6301\u4E0D\u53D8");
    assertDeepEqual(
      JSON.parse(String(capturedInit?.body)),
      {
        invoiceGuid: "invoice-1",
        detailGuids: ["detail-1"],
        targetStoreCodes: ["1033"],
        updateFields: {
          updatePurchasePrice: true,
          updateRetailPrice: false,
          updateIsAutoPricing: false,
          updateIsSpecialProduct: false,
          updateDiscountRate: false
        }
      },
      "\u66F4\u65B0\u5230\u5206\u5E97\u4E0D\u5E94\u643A\u5E26 updateHqProduct payload"
    );
    assertEqual(result.updated, 1, "\u66F4\u65B0\u5230\u5206\u5E97\u7ED3\u679C\u5E94\u4FDD\u7559\u5206\u5E97\u66F4\u65B0\u7EDF\u8BA1");
  });
  if (updateToStorePayloadFailure) failures.push(updateToStorePayloadFailure);
  const updateHqProductsPayloadFailure = await runTest("updateHqProducts \u5E94\u8C03\u7528\u5B57\u6BB5\u7EA7\u66F4\u65B0 HQ \u4E13\u7528\u63A5\u53E3\u5E76\u4FDD\u7559 payload", async () => {
    globalThis.fetch = async (input, init) => {
      capturedUrl = String(input);
      capturedInit = init;
      return new Response(JSON.stringify({
        success: true,
        data: {
          total: 2,
          updated: 2,
          failed: 0,
          hqPurchasePricesUpdated: 2,
          hqRetailPricesUpdated: 2,
          hqAutoPricingUpdated: 0,
          hqSpecialProductsUpdated: 0,
          hqDiscountRatesUpdated: 0,
          errors: []
        }
      }), {
        status: 200,
        headers: { "Content-Type": "application/json" }
      });
    };
    const result = await updateHqProducts("invoice-1", {
      detailGuids: ["detail-1", "detail-2"],
      targetStoreCodes: ["1033", "1005"],
      updateFields: {
        updatePurchasePrice: true,
        updateRetailPrice: true,
        updateIsAutoPricing: false,
        updateIsSpecialProduct: false,
        updateDiscountRate: false
      },
      idempotencyKey: "hq-update-1"
    });
    assertEqual(capturedUrl, "/api/react/v1/local-supplier-invoices/invoice-1/details/update-hq-products", "\u5E94\u8C03\u7528\u5B57\u6BB5\u7EA7\u66F4\u65B0 HQ \u4E13\u7528\u63A5\u53E3");
    assertEqual(capturedInit?.method, "POST", "\u5B57\u6BB5\u7EA7\u66F4\u65B0 HQ \u5E94\u4F7F\u7528 POST");
    assertDeepEqual(
      JSON.parse(String(capturedInit?.body)),
      {
        detailGuids: ["detail-1", "detail-2"],
        targetStoreCodes: ["1033", "1005"],
        updateFields: {
          updatePurchasePrice: true,
          updateRetailPrice: true,
          updateIsAutoPricing: false,
          updateIsSpecialProduct: false,
          updateDiscountRate: false
        },
        idempotencyKey: "hq-update-1"
      },
      "\u5B57\u6BB5\u7EA7\u66F4\u65B0 HQ \u5E94\u4FDD\u7559\u8BF7\u6C42 payload"
    );
    assertEqual(result.hqRetailPricesUpdated, 2, "\u5B57\u6BB5\u7EA7\u66F4\u65B0 HQ \u5E94\u4FDD\u7559\u96F6\u552E\u4EF7\u66F4\u65B0\u7EDF\u8BA1");
  });
  if (updateHqProductsPayloadFailure) failures.push(updateHqProductsPayloadFailure);
  const pasteDetailsJobServiceFailure = await runTest("pasteDetails \u540E\u53F0 Job \u63A5\u53E3\u5E94\u8C03\u7528\u4EFB\u52A1\u521B\u5EFA\u548C\u67E5\u8BE2\u5730\u5740", async () => {
    globalThis.fetch = async (input, init) => {
      capturedUrl = String(input);
      capturedInit = init;
      return new Response(JSON.stringify({
        success: true,
        data: {
          jobId: "paste-job-1",
          invoiceGuid: "invoice-1",
          operationId: "paste-op-1",
          status: "Queued",
          result: { inserted: 1, updated: 0, failed: 0 }
        }
      }), {
        status: 200,
        headers: { "Content-Type": "application/json" }
      });
    };
    const created = await startPasteDetailsJob({
      invoiceGuid: "invoice-1",
      mode: "append",
      items: [{ itemNumber: "SKU-1", quantity: 2, purchasePrice: 1.5 }]
    });
    assertEqual(capturedUrl, "/api/react/v1/local-supplier-invoices/invoice-1/details/paste/jobs", "\u7C98\u8D34\u660E\u7EC6\u5E94\u8C03\u7528\u540E\u53F0\u4EFB\u52A1\u521B\u5EFA\u63A5\u53E3");
    assertEqual(capturedInit?.method, "POST", "\u7C98\u8D34\u660E\u7EC6\u540E\u53F0\u4EFB\u52A1\u521B\u5EFA\u5E94\u4F7F\u7528 POST");
    assertDeepEqual(
      JSON.parse(String(capturedInit?.body)),
      { mode: "append", items: [{ itemNumber: "SKU-1", quantity: 2, purchasePrice: 1.5 }] },
      "\u7C98\u8D34\u660E\u7EC6\u540E\u53F0\u4EFB\u52A1\u521B\u5EFA body \u53EA\u5E94\u5305\u542B mode \u548C items"
    );
    assertEqual(created.jobId, "paste-job-1", "\u7C98\u8D34\u660E\u7EC6\u540E\u53F0\u4EFB\u52A1\u5E94\u8FD4\u56DE jobId");
    await getPasteDetailsJob("invoice-1", "paste-job-1");
    assertEqual(capturedUrl, "/api/react/v1/local-supplier-invoices/invoice-1/details/paste/jobs/paste-job-1", "\u7C98\u8D34\u660E\u7EC6\u5E94\u8C03\u7528\u540E\u53F0\u4EFB\u52A1\u67E5\u8BE2\u63A5\u53E3");
    assertEqual(capturedInit?.method, "GET", "\u7C98\u8D34\u660E\u7EC6\u540E\u53F0\u4EFB\u52A1\u67E5\u8BE2\u5E94\u4F7F\u7528 GET");
  });
  if (pasteDetailsJobServiceFailure) failures.push(pasteDetailsJobServiceFailure);
  const checkProductsJobServiceFailure = await runTest("checkProducts \u540E\u53F0 Job \u63A5\u53E3\u5E94\u8C03\u7528\u4EFB\u52A1\u521B\u5EFA\u548C\u67E5\u8BE2\u5730\u5740", async () => {
    globalThis.fetch = async (input, init) => {
      capturedUrl = String(input);
      capturedInit = init;
      return new Response(JSON.stringify({
        success: true,
        data: {
          jobId: "check-job-1",
          invoiceGuid: "invoice-1",
          operationId: "check-op-1",
          status: "Succeeded",
          result: {
            results: [],
            summary: {
              total: 0,
              productExists: 0,
              productNotExists: 0,
              barcodeNormal: 0,
              barcodeAbnormal: 0
            }
          }
        }
      }), {
        status: 200,
        headers: { "Content-Type": "application/json" }
      });
    };
    const created = await startCheckProductsJob({
      invoiceGuid: "invoice-1",
      detailGuids: ["detail-1"]
    });
    assertEqual(capturedUrl, "/api/react/v1/local-supplier-invoices/check-products/jobs", "\u5546\u54C1\u68C0\u6D4B\u5E94\u8C03\u7528\u540E\u53F0\u4EFB\u52A1\u521B\u5EFA\u63A5\u53E3");
    assertEqual(capturedInit?.method, "POST", "\u5546\u54C1\u68C0\u6D4B\u540E\u53F0\u4EFB\u52A1\u521B\u5EFA\u5E94\u4F7F\u7528 POST");
    assertDeepEqual(
      JSON.parse(String(capturedInit?.body)),
      { invoiceGuid: "invoice-1", detailGuids: ["detail-1"] },
      "\u5546\u54C1\u68C0\u6D4B\u540E\u53F0\u4EFB\u52A1\u521B\u5EFA body \u5E94\u4FDD\u7559\u539F\u68C0\u6D4B\u8BF7\u6C42"
    );
    assertEqual(created.jobId, "check-job-1", "\u5546\u54C1\u68C0\u6D4B\u540E\u53F0\u4EFB\u52A1\u5E94\u8FD4\u56DE jobId");
    await getCheckProductsJob("invoice-1", "check-job-1");
    assertEqual(capturedUrl, "/api/react/v1/local-supplier-invoices/invoice-1/check-products/jobs/check-job-1", "\u5546\u54C1\u68C0\u6D4B\u5E94\u8C03\u7528\u540E\u53F0\u4EFB\u52A1\u67E5\u8BE2\u63A5\u53E3");
    assertEqual(capturedInit?.method, "GET", "\u5546\u54C1\u68C0\u6D4B\u540E\u53F0\u4EFB\u52A1\u67E5\u8BE2\u5E94\u4F7F\u7528 GET");
  });
  if (checkProductsJobServiceFailure) failures.push(checkProductsJobServiceFailure);
  const batchExecuteBusinessFailure = await runTest("batchExecuteActions \u9047\u5230\u4E1A\u52A1\u5931\u8D25\u5E94\u629B\u51FA\u540E\u7AEF\u6D88\u606F", async () => {
    globalThis.fetch = async () => new Response(JSON.stringify({
      success: false,
      message: "\u6279\u91CF\u6267\u884C\u4E1A\u52A1\u5931\u8D25",
      data: {
        createdProducts: 0,
        updatedPurchasePrices: 0,
        updatedItemNumbers: 0,
        addedMultiCodes: 0,
        skipped: 0,
        failed: 1,
        errors: ["\u6D4B\u8BD5\u6267\u884C\u5931\u8D25"]
      }
    }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
    await assertRejects(
      () => batchExecuteActions({
        invoiceGuid: "invoice-1",
        detailGuids: ["detail-1"],
        expectedActions: [
          { detailGuid: "detail-1", action: 1, activityType: 1 }
        ],
        confirmedCreateProductCount: 1,
        confirmedAt: "2026-06-02T09:30:00.000Z"
      }),
      "\u6279\u91CF\u6267\u884C\u4E1A\u52A1\u5931\u8D25",
      "\u6279\u91CF\u6267\u884C\u9047\u5230 success=false \u65F6\u5E94\u900F\u4F20\u540E\u7AEF\u6D88\u606F"
    );
  });
  if (batchExecuteBusinessFailure) failures.push(batchExecuteBusinessFailure);
  const updateDetailActionBusinessFailure = await runTest("updateDetailAction \u9047\u5230\u4E1A\u52A1\u5931\u8D25\u5E94\u629B\u51FA\u540E\u7AEF\u6D88\u606F", async () => {
    globalThis.fetch = async () => new Response(JSON.stringify({
      success: false,
      message: "\u66F4\u65B0\u64CD\u4F5C\u7C7B\u578B\u4E1A\u52A1\u5931\u8D25"
    }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
    await assertRejects(
      () => updateDetailAction("invoice-1", "detail-1", 1 /* CreateProduct */),
      "\u66F4\u65B0\u64CD\u4F5C\u7C7B\u578B\u4E1A\u52A1\u5931\u8D25",
      "\u884C\u64CD\u4F5C\u7C7B\u578B\u66F4\u65B0\u9047\u5230 success=false \u65F6\u5E94\u900F\u4F20\u540E\u7AEF\u6D88\u606F"
    );
  });
  if (updateDetailActionBusinessFailure) failures.push(updateDetailActionBusinessFailure);
  const batchUpdateDetailActionBusinessFailure = await runTest("batchUpdateDetailAction \u9047\u5230\u4E1A\u52A1\u5931\u8D25\u5E94\u629B\u51FA\u540E\u7AEF\u6D88\u606F", async () => {
    globalThis.fetch = async () => new Response(JSON.stringify({
      success: false,
      message: "\u6279\u91CF\u8BBE\u7F6E\u64CD\u4F5C\u7C7B\u578B\u4E1A\u52A1\u5931\u8D25"
    }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
    await assertRejects(
      () => batchUpdateDetailAction("invoice-1", ["detail-1"], 1 /* CreateProduct */),
      "\u6279\u91CF\u8BBE\u7F6E\u64CD\u4F5C\u7C7B\u578B\u4E1A\u52A1\u5931\u8D25",
      "\u6279\u91CF\u64CD\u4F5C\u7C7B\u578B\u66F4\u65B0\u9047\u5230 success=false \u65F6\u5E94\u900F\u4F20\u540E\u7AEF\u6D88\u606F"
    );
  });
  if (batchUpdateDetailActionBusinessFailure) failures.push(batchUpdateDetailActionBusinessFailure);
  const updateHqProductsFailurePayload = await runTest("updateHqProducts \u4E1A\u52A1\u5931\u8D25\u5E94\u4FDD\u7559 HQ \u9519\u8BEF payload", async () => {
    globalThis.fetch = async () => new Response(JSON.stringify({
      success: false,
      message: "\u66F4\u65B0HQ\u5546\u54C1\u90E8\u5206\u5931\u8D25",
      data: {
        total: 2,
        updated: 1,
        failed: 1,
        hqPurchasePricesUpdated: 1,
        hqRetailPricesUpdated: 0,
        errors: [{ detailGuid: "detail-2", storeCode: "1033", message: "\u6761\u7801\u591A\u5339\u914D" }]
      }
    }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
    try {
      await updateHqProducts("invoice-1", {
        detailGuids: ["detail-1", "detail-2"],
        targetStoreCodes: ["1033"],
        updateFields: {
          updatePurchasePrice: true,
          updateRetailPrice: false,
          updateIsAutoPricing: false,
          updateIsSpecialProduct: false,
          updateDiscountRate: false
        }
      });
      throw new Error("Expected updateHqProducts to reject");
    } catch (error) {
      assertEqual(error.message, "\u66F4\u65B0HQ\u5546\u54C1\u90E8\u5206\u5931\u8D25", "\u4E1A\u52A1\u5931\u8D25\u5E94\u900F\u4F20\u540E\u7AEF\u6D88\u606F");
      assertEqual(error.payload?.data?.failed, 1, "\u4E1A\u52A1\u5931\u8D25\u5E94\u4FDD\u7559 HQ \u5931\u8D25\u7EDF\u8BA1");
      assertEqual(error.payload?.data?.errors?.[0]?.message, "\u6761\u7801\u591A\u5339\u914D", "\u4E1A\u52A1\u5931\u8D25\u5E94\u4FDD\u7559 HQ \u9010\u884C\u9519\u8BEF");
    }
  });
  if (updateHqProductsFailurePayload) failures.push(updateHqProductsFailurePayload);
  const ensureHqServiceFailure = await runTest("ensureHqProducts \u9047\u5230\u4E1A\u52A1\u5931\u8D25\u5E94\u629B\u51FA\u540E\u7AEF\u6D88\u606F\u5E76\u4FDD\u7559 payload", async () => {
    globalThis.fetch = async () => new Response(JSON.stringify({
      success: false,
      message: "\u540C\u6B65\u5546\u54C1\u5230HQ\u90E8\u5206\u5931\u8D25",
      data: {
        total: 1,
        hqExisting: 0,
        hbwebCreated: 0,
        hqCreated: 0,
        hqSynced: 0,
        hqPurchasePricesUpdated: 0,
        skipped: 0,
        failed: 1,
        errors: [{ detailGuid: "detail-1", storeCode: "1033", message: "\u6761\u7801\u591A\u5339\u914D" }]
      }
    }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
    try {
      await ensureHqProducts("invoice-1", { detailGuids: ["detail-1"], targetStoreCodes: ["1033"] });
      throw new Error("Expected ensureHqProducts to reject");
    } catch (error) {
      assertEqual(error.message, "\u540C\u6B65\u5546\u54C1\u5230HQ\u90E8\u5206\u5931\u8D25", "\u4E1A\u52A1\u5931\u8D25\u5E94\u900F\u4F20\u540E\u7AEF\u6D88\u606F");
      assertEqual(error.payload?.data?.failed, 1, "\u4E1A\u52A1\u5931\u8D25\u5E94\u4FDD\u7559\u7EDF\u8BA1 payload");
      assertEqual(error.payload?.data?.errors?.[0]?.message, "\u6761\u7801\u591A\u5339\u914D", "\u4E1A\u52A1\u5931\u8D25\u5E94\u4FDD\u7559\u9010\u884C\u9519\u8BEF");
    }
  });
  if (ensureHqServiceFailure) failures.push(ensureHqServiceFailure);
  const batchUpdateDetailsFailure = await runTest("batchUpdateDetails \u9047\u5230\u4E1A\u52A1\u5931\u8D25\u5E94\u629B\u51FA\u540E\u7AEF\u6D88\u606F", async () => {
    globalThis.fetch = async () => new Response(JSON.stringify({
      success: false,
      message: "\u81EA\u52A8\u5B9A\u4EF7\u4E0D\u80FD\u4E3A\u7A7A",
      code: "VALIDATION_ERROR"
    }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
    await assertRejects(
      () => batchUpdateDetails("invoice-1", [{ detailGUID: "detail-1" }], {
        updatePurchasePrice: false,
        updateRetailPrice: false,
        updateIsAutoPricing: true,
        updateIsSpecialProduct: false,
        updateDiscountRate: false,
        updateAction: false
      }),
      "\u81EA\u52A8\u5B9A\u4EF7\u4E0D\u80FD\u4E3A\u7A7A",
      "\u6279\u91CF\u7F16\u8F91\u4E1A\u52A1\u5931\u8D25\u65F6\u5E94\u900F\u4F20\u540E\u7AEF\u6D88\u606F"
    );
  });
  if (batchUpdateDetailsFailure) failures.push(batchUpdateDetailsFailure);
  const batchUpsertDetailsFailure = await runTest("batchUpsertDetails \u9047\u5230\u4E1A\u52A1\u5931\u8D25\u5E94\u629B\u51FA\u540E\u7AEF\u6D88\u606F", async () => {
    globalThis.fetch = async () => new Response(JSON.stringify({
      success: false,
      message: "\u4FDD\u5B58\u660E\u7EC6\u4E1A\u52A1\u5931\u8D25",
      code: "VALIDATION_ERROR"
    }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
    await assertRejects(
      () => batchUpsertDetails("invoice-1", [{ detailGUID: "detail-1", purchasePrice: 1.23 }]),
      "\u4FDD\u5B58\u660E\u7EC6\u4E1A\u52A1\u5931\u8D25",
      "\u4FDD\u5B58\u660E\u7EC6\u4E1A\u52A1\u5931\u8D25\u65F6\u5E94\u900F\u4F20\u540E\u7AEF\u6D88\u606F"
    );
  });
  if (batchUpsertDetailsFailure) failures.push(batchUpsertDetailsFailure);
  globalThis.fetch = originalFetch;
  if (failures.length > 0) {
    throw new Error(`\u5171\u6709 ${failures.length} \u4E2A\u6D4B\u8BD5\u5931\u8D25
- ${failures.join("\n- ")}`);
  }
  console.log("LocalSupplierInvoices.hqSync.logic.test: ok");
}
await main();
