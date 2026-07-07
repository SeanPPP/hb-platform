// <define:import.meta.env>
var define_import_meta_env_default = {};

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
function buildApiUrl(path) {
  return `${API_BASE_URL}${path}`.replace(/([^:]\/)\/+/g, "$1");
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
request.get = (url, options) => request(url, { ...options, method: "GET" });
request.post = (url, data, options) => request(url, { ...options, method: "POST", data });
request.put = (url, data, options) => request(url, { ...options, method: "PUT", data });
request.patch = (url, data, options) => request(url, { ...options, method: "PATCH", data });
request.delete = (url, options) => request(url, { ...options, method: "DELETE" });
var request_default = request;

// src/services/storeOrderService.ts
var API_BASE = "/api/react/v1/store-order";
function isRecord(value) {
  return typeof value === "object" && value !== null;
}
function unwrapEnvelope(payload) {
  let current = payload;
  for (let depth = 0; depth < 3; depth += 1) {
    if (!isRecord(current) || !("data" in current)) {
      break;
    }
    const keys = Object.keys(current);
    const looksLikeEnvelope = keys.includes("data") && (keys.includes("success") || keys.includes("isSuccess") || keys.includes("message") || keys.includes("errorCode") || keys.includes("code"));
    if (!looksLikeEnvelope) {
      break;
    }
    current = current.data;
  }
  return current;
}
function normalizeProductPagedList(payload) {
  const result = unwrapEnvelope(payload);
  return {
    items: result?.items ?? [],
    total: result?.total ?? 0,
    page: result?.page ?? result?.pageNumber ?? 1,
    pageSize: result?.pageSize ?? 24
  };
}
function normalizeAllocatedImportAmount(item) {
  return {
    ...item,
    allocatedImportAmount: item.allocatedImportAmount ?? Number(item.allocQuantity ?? 0) * Number(item.importPrice ?? 0)
  };
}
function normalizeCart(payload, options) {
  const result = normalizeResult(payload);
  if (!result) {
    return null;
  }
  const invoiceEmailSentInfo = normalizeStoreOrderInvoiceEmailSentInfo(result.invoiceEmailSentInfo);
  const items = Array.isArray(result.items) ? result.items : [];
  return {
    orderGUID: result.orderGUID ?? "",
    orderNo: result.orderNo,
    storeCode: result.storeCode,
    storeName: result.storeName,
    totalAmount: result.totalAmount ?? 0,
    totalQuantity: result.totalQuantity ?? 0,
    totalSKU: result.totalSKU ?? 0,
    totalImportAmount: result.totalImportAmount ?? 0,
    totalAllocatedImportAmount: result.totalAllocatedImportAmount,
    totalVolume: result.totalVolume ?? 0,
    remarks: result.remarks,
    shippingFee: result.shippingFee,
    orderDate: result.orderDate,
    outboundDate: result.outboundDate,
    storeAddress: result.storeAddress,
    storeContactEmail: result.storeContactEmail,
    flowStatus: result.flowStatus,
    invoiceEmailSentInfo,
    isSummaryOnly: Boolean(options?.isSummaryOnly),
    items
  };
}
function normalizeStoreOrderDetail(payload) {
  const result = normalizeResult(payload);
  if (!result) {
    return null;
  }
  const items = Array.isArray(result.items) ? result.items.map(normalizeAllocatedImportAmount) : [];
  const invoiceEmailSentInfo = normalizeStoreOrderInvoiceEmailSentInfo(result.invoiceEmailSentInfo);
  return {
    ...result,
    orderGUID: result.orderGUID ?? "",
    totalAmount: result.totalAmount ?? 0,
    totalQuantity: result.totalQuantity ?? 0,
    totalImportAmount: result.totalImportAmount ?? 0,
    totalAllocatedImportAmount: result.totalAllocatedImportAmount ?? items.reduce((sum, item) => sum + item.allocatedImportAmount, 0),
    totalVolume: result.totalVolume ?? 0,
    itemsTotal: result.itemsTotal ?? items.length,
    invoiceEmailSentInfo,
    items
  };
}
function normalizeStoreOrderInvoiceEmailSentInfo(value) {
  if (!isRecord(value)) {
    return void 0;
  }
  return {
    hasSent: Boolean(value.hasSent),
    sentAt: typeof value.sentAt === "string" ? value.sentAt : void 0,
    toEmail: typeof value.toEmail === "string" ? value.toEmail : void 0,
    jobId: typeof value.jobId === "string" ? value.jobId : void 0
  };
}
function normalizeResult(payload) {
  return unwrapEnvelope(payload);
}
function buildStoreOrderDetailQueryParams(query) {
  const { columnFilters, ...params } = query;
  return {
    ...params,
    // 明细列头筛选走 GET；这里主动展平成一层 query，避免全局 request 把对象转成 [object Object]。
    ...columnFilters ?? {}
  };
}
function normalizeStoreOrderSyncJobStatus(status) {
  if (typeof status !== "string") {
    return "Running";
  }
  switch (status.trim().toLowerCase()) {
    case "queued":
    case "pending":
      return "Queued";
    case "running":
      return "Running";
    case "succeeded":
      return "Succeeded";
    case "failed":
      return "Failed";
    default:
      return "Running";
  }
}
function normalizeStoreOrderInvoiceEmailJobResult(payload, fallbackJobId = "") {
  const rawPayload = isRecord(payload) ? payload : null;
  const result = normalizeResult(payload);
  const job = isRecord(result) ? result : {};
  const message = typeof job.message === "string" ? job.message : rawPayload && typeof rawPayload.message === "string" ? rawPayload.message : void 0;
  return {
    jobId: typeof job.jobId === "string" ? job.jobId : fallbackJobId,
    status: normalizeStoreOrderSyncJobStatus(job.status),
    message,
    orderGUID: typeof job.orderGUID === "string" ? job.orderGUID : void 0,
    toEmail: typeof job.toEmail === "string" ? job.toEmail : void 0,
    createdAt: typeof job.createdAt === "string" ? job.createdAt : void 0,
    completedAt: typeof job.completedAt === "string" ? job.completedAt : void 0
  };
}
function normalizeStoreOrderPasteReplaceJobResult(payload, fallbackJobId = "") {
  const rawPayload = isRecord(payload) ? payload : null;
  const result = normalizeResult(payload);
  const job = isRecord(result) ? result : {};
  const success = rawPayload && typeof rawPayload.success === "boolean" ? rawPayload.success : void 0;
  const message = typeof job.message === "string" ? job.message : rawPayload && typeof rawPayload.message === "string" ? rawPayload.message : void 0;
  const status = typeof job.status === "string" ? normalizeStoreOrderSyncJobStatus(job.status) : success === false ? "Failed" : "Running";
  return {
    jobId: typeof job.jobId === "string" ? job.jobId : fallbackJobId,
    status,
    message,
    orderGUID: typeof job.orderGUID === "string" ? job.orderGUID : void 0,
    targetField: job.targetField === "allocQuantity" || job.targetField === "quantity" ? job.targetField : void 0,
    totalCount: typeof job.totalCount === "number" ? job.totalCount : void 0,
    importedCount: typeof job.importedCount === "number" ? job.importedCount : void 0,
    skippedCount: typeof job.skippedCount === "number" ? job.skippedCount : void 0,
    createdAt: typeof job.createdAt === "string" ? job.createdAt : void 0,
    completedAt: typeof job.completedAt === "string" ? job.completedAt : void 0
  };
}
async function getUnmatchedStoreOrderGroups() {
  const response = await request_default(`${API_BASE}/unmatched-store-groups`, {
    method: "GET"
  });
  const result = normalizeResult(response);
  return Array.isArray(result) ? result : [];
}
async function batchMapStoreOrderStoreCode(payload) {
  const response = await request_default(`${API_BASE}/batch-map-store-code`, {
    method: "POST",
    data: payload
  });
  return normalizeResult(response);
}
async function getStoreOrderDetail(orderGuid, query, signal) {
  const response = await request_default(
    query ? `${API_BASE}/detail/${orderGuid}` : `${API_BASE}/detail/${orderGuid}/full`,
    {
      method: "GET",
      params: query ? buildStoreOrderDetailQueryParams(query) : void 0,
      signal
    }
  );
  return normalizeStoreOrderDetail(response);
}
async function getStoreOrderDetailFull(orderGuid, signal) {
  const response = await request_default(`${API_BASE}/detail/${orderGuid}/full`, {
    method: "GET",
    signal
  });
  return normalizeStoreOrderDetail(response);
}
async function getStoreOrderDetailProductCodes(orderGuid, signal) {
  const response = await request_default(
    `${API_BASE}/detail/${orderGuid}/product-codes`,
    {
      method: "GET",
      signal
    }
  );
  const result = normalizeResult(response);
  return Array.isArray(result) ? result.filter((item) => typeof item === "string") : [];
}
async function getStoreOrderProducts(query, signal) {
  const response = await request_default(`${API_BASE}/products`, {
    method: "POST",
    data: query,
    signal
  });
  return normalizeProductPagedList(response);
}
async function getActiveStoreOrderCart(storeCode) {
  const response = await request_default(`${API_BASE}/cart/${storeCode}`, {
    method: "GET"
  });
  return normalizeCart(response);
}
async function updateStoreOrderStatus(payload) {
  await request_default(`${API_BASE}/status`, {
    method: "POST",
    data: payload
  });
}
async function createStoreOrderPasteReplaceJob(payload) {
  const response = await request_default(`${API_BASE}/line/paste-replace/jobs`, {
    method: "POST",
    data: payload
  });
  return normalizeStoreOrderPasteReplaceJobResult(response);
}
async function getStoreOrderPasteReplaceJob(jobId) {
  const response = await request_default(
    `${API_BASE}/line/paste-replace/jobs/${encodeURIComponent(jobId)}`,
    {
      method: "GET"
    }
  );
  return normalizeStoreOrderPasteReplaceJobResult(response, jobId);
}
async function updateStoreOrderLine(payload) {
  const { allocQuantity, ...restPayload } = payload;
  await request_default(`${API_BASE}/line/update`, {
    method: "POST",
    // 后端当前接口仍使用 quantity 字段表达发货数，前端类型保持 allocQuantity 语义。
    data: {
      ...restPayload,
      quantity: allocQuantity
    }
  });
}
async function batchUpdateStoreOrderLines(payload) {
  await request_default(`${API_BASE}/line/batch-update`, {
    method: "POST",
    data: payload
  });
}
async function refreshStoreOrderImportPrices(payload) {
  const response = await request_default(`${API_BASE}/line/refresh-import-prices`, {
    method: "POST",
    data: payload
  });
  const result = normalizeResult(response);
  return {
    updatedCount: result?.updatedCount ?? 0,
    unchangedCount: result?.unchangedCount ?? 0,
    skippedCount: result?.skippedCount ?? 0,
    missingWarehousePriceCount: result?.missingWarehousePriceCount ?? 0
  };
}
async function updateStoreOrderOutboundDate(payload) {
  await request_default(`${API_BASE}/outbound-date`, {
    method: "POST",
    data: {
      ...payload,
      orderGuid: payload.orderGUID
    }
  });
}
async function updateStoreOrderStoreContact(payload) {
  await request_default(`${API_BASE}/store-contact/update`, {
    method: "POST",
    data: payload
  });
}
async function sendStoreOrderInvoiceEmail(payload) {
  const response = await request_default(`${API_BASE}/invoice/email`, {
    method: "POST",
    data: payload
  });
  return normalizeStoreOrderInvoiceEmailJobResult(response);
}
async function translateStoreOrderInvoiceEmailText(payload) {
  const response = await request_default(
    `${API_BASE}/invoice/email/translate-text`,
    {
      method: "POST",
      data: payload
    }
  );
  const result = normalizeResult(response);
  return {
    subject: typeof result?.subject === "string" ? result.subject : void 0,
    body: typeof result?.body === "string" ? result.body : void 0
  };
}
async function getStoreOrderInvoiceEmailJob(jobId) {
  const response = await request_default(
    `${API_BASE}/invoice/email/jobs/${encodeURIComponent(jobId)}`,
    {
      method: "GET"
    }
  );
  return normalizeStoreOrderInvoiceEmailJobResult(response, jobId);
}

// src/services/storeOrderService.detail.test.ts
function assertEqual(actual, expected, label) {
  if (actual !== expected) {
    throw new Error(`${label}\u3002Expected: ${String(expected)}, received: ${String(actual)}`);
  }
}
function assertDeepEqual(actual, expected, label) {
  const actualJson = JSON.stringify(actual);
  const expectedJson = JSON.stringify(expected);
  if (actualJson !== expectedJson) {
    throw new Error(`${label}\u3002Expected: ${expectedJson}, received: ${actualJson}`);
  }
}
var originalFetch = globalThis.fetch;
try {
  let capturedUrl = "";
  let capturedMethod = "";
  globalThis.fetch = async (input, init) => {
    capturedUrl = String(input);
    capturedMethod = String(init?.method);
    return new Response(
      JSON.stringify({
        success: true,
        data: [
          {
            sourceStoreCode: "11111111-1111-1111-1111-111111111111",
            sourceStoreName: "Ada - Tas - Kingston",
            orderCount: 3,
            latestOrderDate: "2026-06-15T00:00:00"
          }
        ]
      }),
      {
        status: 200,
        headers: { "Content-Type": "application/json" }
      }
    );
  };
  const result = await getUnmatchedStoreOrderGroups();
  assertEqual(capturedUrl, "/api/react/v1/store-order/unmatched-store-groups", "\u672A\u5339\u914D\u5206\u5E97\u805A\u5408\u63A5\u53E3\u8DEF\u5F84\u5E94\u4FDD\u6301\u4E00\u81F4");
  assertEqual(capturedMethod, "GET", "\u672A\u5339\u914D\u5206\u5E97\u805A\u5408\u63A5\u53E3\u5E94\u4F7F\u7528 GET");
  assertDeepEqual(
    result,
    [
      {
        sourceStoreCode: "11111111-1111-1111-1111-111111111111",
        sourceStoreName: "Ada - Tas - Kingston",
        orderCount: 3,
        latestOrderDate: "2026-06-15T00:00:00"
      }
    ],
    "\u672A\u5339\u914D\u5206\u5E97\u805A\u5408\u63A5\u53E3\u5E94\u4FDD\u7559\u540E\u7AEF\u5206\u7EC4\u6570\u636E"
  );
} finally {
  globalThis.fetch = originalFetch;
}
try {
  let capturedBody = null;
  globalThis.fetch = async (_input, init) => {
    capturedBody = init?.body ? JSON.parse(String(init.body)) : null;
    return new Response(JSON.stringify({ success: true, data: null }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
  };
  await batchUpdateStoreOrderLines({
    orderGUID: "order-1",
    items: [
      {
        detailGUID: "detail-1",
        productCode: "product-1",
        quantity: 12
      }
    ]
  });
  assertDeepEqual(
    capturedBody,
    {
      orderGUID: "order-1",
      items: [
        {
          detailGUID: "detail-1",
          productCode: "product-1",
          quantity: 12
        }
      ]
    },
    "\u6279\u91CF\u4FDD\u5B58\u5E94\u4FDD\u7559 detailGUID \u4EE5\u652F\u6301\u660E\u7EC6\u7EA7\u5FEB\u8DEF\u5F84"
  );
} finally {
  globalThis.fetch = originalFetch;
}
try {
  let capturedUrl = "";
  let capturedMethod = "";
  let capturedBody = null;
  globalThis.fetch = async (input, init) => {
    capturedUrl = String(input);
    capturedMethod = String(init?.method);
    capturedBody = init?.body ? JSON.parse(String(init.body)) : null;
    return new Response(
      JSON.stringify({
        success: true,
        data: {
          updatedCount: 2,
          unchangedCount: 1,
          skippedCount: 1,
          missingWarehousePriceCount: 1
        }
      }),
      {
        status: 200,
        headers: { "Content-Type": "application/json" }
      }
    );
  };
  const result = await refreshStoreOrderImportPrices({
    orderGUID: "order-1",
    detailGUIDs: ["detail-1", "detail-2"]
  });
  assertEqual(capturedUrl, "/api/react/v1/store-order/line/refresh-import-prices", "\u66F4\u65B0\u8FDB\u8D27\u4EF7\u63A5\u53E3\u8DEF\u5F84\u5E94\u4FDD\u6301\u4E00\u81F4");
  assertEqual(capturedMethod, "POST", "\u66F4\u65B0\u8FDB\u8D27\u4EF7\u63A5\u53E3\u5E94\u4F7F\u7528 POST");
  assertDeepEqual(
    capturedBody,
    {
      orderGUID: "order-1",
      detailGUIDs: ["detail-1", "detail-2"]
    },
    "\u6709\u9009\u4E2D\u884C\u65F6\u5E94\u628A\u660E\u7EC6 GUID \u5217\u8868\u4F20\u7ED9\u540E\u7AEF"
  );
  assertDeepEqual(
    result,
    {
      updatedCount: 2,
      unchangedCount: 1,
      skippedCount: 1,
      missingWarehousePriceCount: 1
    },
    "\u66F4\u65B0\u8FDB\u8D27\u4EF7\u7ED3\u679C\u5E94\u5F52\u4E00\u5316\u7EDF\u8BA1\u5B57\u6BB5"
  );
} finally {
  globalThis.fetch = originalFetch;
}
try {
  let capturedBody = null;
  globalThis.fetch = async (_input, init) => {
    capturedBody = init?.body ? JSON.parse(String(init.body)) : null;
    return new Response(
      JSON.stringify({
        success: true,
        data: {}
      }),
      {
        status: 200,
        headers: { "Content-Type": "application/json" }
      }
    );
  };
  await refreshStoreOrderImportPrices({
    orderGUID: "order-1"
  });
  assertDeepEqual(
    capturedBody,
    {
      orderGUID: "order-1"
    },
    "\u6CA1\u6709\u9009\u4E2D\u884C\u65F6\u5E94\u53EA\u53D1\u9001\u8BA2\u5355 GUID\uFF0C\u8BA9\u540E\u7AEF\u6309\u6574\u5355\u5237\u65B0"
  );
} finally {
  globalThis.fetch = originalFetch;
}
try {
  let capturedUrl = "";
  let capturedMethod = "";
  let capturedBody = null;
  globalThis.fetch = async (input, init) => {
    capturedUrl = String(input);
    capturedMethod = String(init?.method);
    capturedBody = init?.body ? JSON.parse(String(init.body)) : null;
    return new Response(
      JSON.stringify({
        success: true,
        data: {
          updatedCount: 2,
          skippedCount: 1,
          items: [
            {
              sourceStoreCode: "11111111-1111-1111-1111-111111111111",
              targetStoreCode: "1042",
              updatedCount: 2,
              skippedCount: 1
            }
          ]
        }
      }),
      {
        status: 200,
        headers: { "Content-Type": "application/json" }
      }
    );
  };
  const result = await batchMapStoreOrderStoreCode({
    mappings: [
      {
        sourceStoreCode: "11111111-1111-1111-1111-111111111111",
        targetStoreCode: "1042"
      }
    ]
  });
  assertEqual(capturedUrl, "/api/react/v1/store-order/batch-map-store-code", "\u6279\u91CF\u4FEE\u590D\u5206\u5E97 GUID \u63A5\u53E3\u8DEF\u5F84\u5E94\u4FDD\u6301\u4E00\u81F4");
  assertEqual(capturedMethod, "POST", "\u6279\u91CF\u4FEE\u590D\u5206\u5E97 GUID \u63A5\u53E3\u5E94\u4F7F\u7528 POST");
  assertDeepEqual(
    capturedBody,
    {
      mappings: [
        {
          sourceStoreCode: "11111111-1111-1111-1111-111111111111",
          targetStoreCode: "1042"
        }
      ]
    },
    "\u6279\u91CF\u4FEE\u590D\u5206\u5E97 GUID \u5E94\u539F\u6837\u53D1\u9001\u6620\u5C04\u5173\u7CFB"
  );
  assertDeepEqual(
    result,
    {
      updatedCount: 2,
      skippedCount: 1,
      items: [
        {
          sourceStoreCode: "11111111-1111-1111-1111-111111111111",
          targetStoreCode: "1042",
          updatedCount: 2,
          skippedCount: 1
        }
      ]
    },
    "\u6279\u91CF\u4FEE\u590D\u5206\u5E97 GUID \u5E94\u5F52\u4E00\u5316\u540E\u7AEF\u7ED3\u679C"
  );
} finally {
  globalThis.fetch = originalFetch;
}
try {
  globalThis.fetch = async () => new Response(
    JSON.stringify({
      success: true,
      data: {
        orderGUID: "cart-legacy",
        totalAmount: 0,
        totalQuantity: 10,
        totalImportAmount: 55,
        totalVolume: 0,
        items: [
          {
            detailGUID: "cart-detail-legacy",
            productCode: "product-legacy",
            quantity: 10,
            allocQuantity: 2,
            price: 0,
            amount: 0,
            importPrice: 7,
            importAmount: 55,
            minOrderQuantity: 1,
            isActive: true
          }
        ]
      }
    }),
    {
      status: 200,
      headers: { "Content-Type": "application/json" }
    }
  );
  const result = await getActiveStoreOrderCart("S001");
  assertEqual(result?.totalImportAmount, 55, "\u8D2D\u7269\u8F66 totalImportAmount \u5E94\u7EE7\u7EED\u8868\u793A\u8BA2\u8D27\u91D1\u989D");
  assertEqual(result?.totalAllocatedImportAmount, void 0, "\u8D2D\u7269\u8F66\u65E7\u54CD\u5E94\u4E0D\u5E94\u5408\u6210\u53D1\u8D27\u91D1\u989D\u603B\u989D");
  assertEqual(result?.items[0]?.importAmount, 55, "\u8D2D\u7269\u8F66 importAmount \u5E94\u7EE7\u7EED\u8868\u793A\u8BA2\u8D27\u91D1\u989D");
  assertEqual(result?.items[0]?.allocatedImportAmount, void 0, "\u8D2D\u7269\u8F66\u65E7\u54CD\u5E94\u4E0D\u5E94\u5408\u6210\u660E\u7EC6\u53D1\u8D27\u91D1\u989D");
} finally {
  globalThis.fetch = originalFetch;
}
try {
  const controller = new AbortController();
  let capturedUrl = "";
  let capturedMethod = "";
  let capturedSignal = null;
  globalThis.fetch = async (input, init) => {
    capturedUrl = String(input);
    capturedMethod = String(init?.method);
    capturedSignal = init?.signal ?? null;
    return new Response(
      JSON.stringify({
        success: true,
        data: {
          orderGUID: "order-1",
          orderNo: "SO-001",
          outboundDate: "2026-06-07T00:00:00",
          totalAmount: 100,
          totalQuantity: 8,
          totalImportAmount: 88,
          totalAllocatedImportAmount: 40,
          totalVolume: 12,
          itemsTotal: 35,
          invoiceEmailSentInfo: {
            hasSent: true,
            sentAt: "2026-06-08T09:15:00Z",
            toEmail: "invoice@example.com",
            jobId: "invoice-job-1"
          },
          items: [
            {
              detailGUID: "detail-1",
              productCode: "product-1",
              quantity: 3,
              price: 10,
              amount: 30,
              importPrice: 8,
              importAmount: 24,
              allocQuantity: 5,
              allocatedImportAmount: 40,
              minOrderQuantity: 1,
              isActive: true
            }
          ]
        }
      }),
      {
        status: 200,
        headers: { "Content-Type": "application/json" }
      }
    );
  };
  const result = await getStoreOrderDetail(
    "order-1",
    {
      pageNumber: 2,
      pageSize: 20,
      keyword: "ABC-123",
      statFilter: "orderedNotShipped",
      sortBy: "importPrice",
      sortDescending: true,
      columnFilters: {
        itemNumber: "HB043",
        productName: "Money",
        barcode: "9528",
        locationCode: "A-01",
        quantityMin: 1,
        quantityMax: 48,
        allocQuantityMin: 0,
        allocQuantityMax: 12,
        importPriceMin: 1.5,
        importPriceMax: 2.5,
        isActive: false
      }
    },
    controller.signal
  );
  assertEqual(
    capturedUrl,
    "/api/react/v1/store-order/detail/order-1?pageNumber=2&pageSize=20&keyword=ABC-123&statFilter=orderedNotShipped&sortBy=importPrice&sortDescending=true&itemNumber=HB043&productName=Money&barcode=9528&locationCode=A-01&quantityMin=1&quantityMax=48&allocQuantityMin=0&allocQuantityMax=12&importPriceMin=1.5&importPriceMax=2.5&isActive=false",
    "\u8BA2\u8D27\u660E\u7EC6\u63A5\u53E3\u5E94\u901A\u8FC7\u5E73\u94FA query \u4F20\u9012\u8FDC\u7A0B\u5206\u9875\u7B5B\u9009\u6392\u5E8F\u53C2\u6570"
  );
  assertEqual(capturedUrl.includes("columnFilters"), false, "\u8BA2\u8D27\u660E\u7EC6\u5217\u5934\u8FC7\u6EE4\u4E0D\u5E94\u628A\u5D4C\u5957\u5BF9\u8C61\u5199\u5165 URL");
  assertEqual(capturedUrl.includes("[object Object]"), false, "\u8BA2\u8D27\u660E\u7EC6\u5217\u5934\u8FC7\u6EE4\u4E0D\u5E94\u88AB\u5E8F\u5217\u5316\u4E3A [object Object]");
  assertEqual(capturedMethod, "GET", "\u8BA2\u8D27\u660E\u7EC6\u63A5\u53E3\u5E94\u7EE7\u7EED\u4F7F\u7528 GET \u8BF7\u6C42");
  assertEqual(capturedSignal, controller.signal, "\u8BA2\u8D27\u660E\u7EC6\u63A5\u53E3\u5E94\u900F\u4F20\u53D6\u6D88\u4FE1\u53F7");
  assertDeepEqual(
    result,
    {
      orderGUID: "order-1",
      orderNo: "SO-001",
      outboundDate: "2026-06-07T00:00:00",
      totalAmount: 100,
      totalQuantity: 8,
      totalImportAmount: 88,
      totalAllocatedImportAmount: 40,
      totalVolume: 12,
      itemsTotal: 35,
      invoiceEmailSentInfo: {
        hasSent: true,
        sentAt: "2026-06-08T09:15:00Z",
        toEmail: "invoice@example.com",
        jobId: "invoice-job-1"
      },
      items: [
        {
          detailGUID: "detail-1",
          productCode: "product-1",
          quantity: 3,
          price: 10,
          amount: 30,
          importPrice: 8,
          importAmount: 24,
          allocQuantity: 5,
          allocatedImportAmount: 40,
          minOrderQuantity: 1,
          isActive: true
        }
      ]
    },
    "\u8BA2\u8D27\u660E\u7EC6\u63A5\u53E3\u5E94\u4FDD\u7559\u670D\u52A1\u7AEF\u8FD4\u56DE\u7684\u5F53\u524D\u9875 items\u3001itemsTotal \u548C\u53D1\u7968\u90AE\u4EF6\u53D1\u9001\u4FE1\u606F"
  );
} finally {
  globalThis.fetch = originalFetch;
}
try {
  globalThis.fetch = async () => new Response(
    JSON.stringify({
      success: true,
      data: {
        orderGUID: "order-legacy",
        totalAmount: 0,
        totalQuantity: 0,
        totalImportAmount: 55,
        totalVolume: 0,
        items: [
          {
            detailGUID: "detail-legacy",
            productCode: "product-legacy",
            quantity: 10,
            allocQuantity: 2,
            price: 0,
            amount: 0,
            importPrice: 7,
            importAmount: 55,
            minOrderQuantity: 1,
            isActive: true
          }
        ]
      }
    }),
    {
      status: 200,
      headers: { "Content-Type": "application/json" }
    }
  );
  const result = await getStoreOrderDetail("order-legacy");
  assertEqual(result?.totalImportAmount, 55, "\u65E7 totalImportAmount \u5E94\u7EE7\u7EED\u8868\u793A\u8BA2\u8D27\u91D1\u989D");
  assertEqual(result?.totalAllocatedImportAmount, 14, "\u65E7\u54CD\u5E94\u7F3A\u5C11\u53D1\u8D27\u91D1\u989D\u65F6\u5E94\u6309\u53D1\u8D27\u6570\u91CF\u548C\u8FDB\u53E3\u4EF7\u515C\u5E95");
  assertEqual(result?.items[0]?.importAmount, 55, "\u65E7 importAmount \u5E94\u7EE7\u7EED\u8868\u793A\u8BA2\u8D27\u91D1\u989D");
  assertEqual(result?.items[0]?.allocatedImportAmount, 14, "\u65E7\u54CD\u5E94\u7F3A\u5C11\u660E\u7EC6\u53D1\u8D27\u91D1\u989D\u65F6\u5E94\u6309\u53D1\u8D27\u6570\u91CF\u548C\u8FDB\u53E3\u4EF7\u515C\u5E95");
} finally {
  globalThis.fetch = originalFetch;
}
try {
  const controller = new AbortController();
  let capturedUrl = "";
  let capturedMethod = "";
  let capturedSignal = null;
  let capturedBody = null;
  globalThis.fetch = async (input, init) => {
    capturedUrl = String(input);
    capturedMethod = String(init?.method);
    capturedSignal = init?.signal ?? null;
    capturedBody = init?.body ? JSON.parse(String(init.body)) : null;
    return new Response(
      JSON.stringify({
        success: true,
        data: {
          items: [
            {
              productCode: "product-1",
              itemNumber: "HB137-001",
              productName: "\u6D4B\u8BD5\u5546\u54C1"
            }
          ],
          total: 1,
          pageNumber: 1,
          pageSize: 100
        }
      }),
      {
        status: 200,
        headers: { "Content-Type": "application/json" }
      }
    );
  };
  const result = await getStoreOrderProducts(
    {
      itemNumber: "HB137-",
      supplierCode: "CN001",
      excludeOrderGUID: "order-1",
      pageNumber: 1,
      pageSize: 100,
      sortBy: "importPrice",
      sortDescending: true,
      columnFilters: {
        itemNumber: "HB137",
        supplierKeyword: "\u4E49\u4E4C",
        stockQuantityMin: 1,
        importPriceMax: 9.5
      }
    },
    controller.signal
  );
  assertEqual(capturedUrl, "/api/react/v1/store-order/products", "\u5546\u54C1\u9009\u62E9\u67E5\u8BE2\u63A5\u53E3\u8DEF\u5F84\u5E94\u4FDD\u6301\u4E0D\u53D8");
  assertEqual(capturedMethod, "POST", "\u5546\u54C1\u9009\u62E9\u67E5\u8BE2\u63A5\u53E3\u5E94\u4F7F\u7528 POST");
  assertEqual(capturedSignal, controller.signal, "\u5546\u54C1\u9009\u62E9\u67E5\u8BE2\u63A5\u53E3\u5E94\u900F\u4F20\u53D6\u6D88\u4FE1\u53F7");
  assertDeepEqual(
    capturedBody,
    {
      itemNumber: "HB137-",
      supplierCode: "CN001",
      excludeOrderGUID: "order-1",
      pageNumber: 1,
      pageSize: 100,
      sortBy: "importPrice",
      sortDescending: true,
      columnFilters: {
        itemNumber: "HB137",
        supplierKeyword: "\u4E49\u4E4C",
        stockQuantityMin: 1,
        importPriceMax: 9.5
      }
    },
    "\u5546\u54C1\u9009\u62E9\u67E5\u8BE2\u63A5\u53E3\u5E94\u53D1\u9001\u539F\u59CB\u67E5\u8BE2\u3001\u5217\u6392\u5E8F\u548C\u5217\u8FC7\u6EE4\u6761\u4EF6"
  );
  assertDeepEqual(
    result,
    {
      items: [
        {
          productCode: "product-1",
          itemNumber: "HB137-001",
          productName: "\u6D4B\u8BD5\u5546\u54C1"
        }
      ],
      total: 1,
      page: 1,
      pageSize: 100
    },
    "\u5546\u54C1\u9009\u62E9\u67E5\u8BE2\u63A5\u53E3\u5E94\u5F52\u4E00\u5316\u5206\u9875\u54CD\u5E94"
  );
} finally {
  globalThis.fetch = originalFetch;
}
try {
  let capturedUrl = "";
  let capturedMethod = "";
  let capturedBody = null;
  globalThis.fetch = async (input, init) => {
    capturedUrl = String(input);
    capturedMethod = String(init?.method);
    capturedBody = init?.body ? JSON.parse(String(init.body)) : null;
    return new Response(
      JSON.stringify({
        success: true,
        data: {
          jobId: "paste-job-1",
          status: "Queued",
          message: "Excel \u7C98\u8D34\u5BFC\u5165\u4EFB\u52A1\u5DF2\u63D0\u4EA4",
          orderGUID: "order-1",
          targetField: "quantity",
          totalCount: 3,
          importedCount: 2,
          skippedCount: 1,
          createdAt: "2026-06-11T00:00:00Z"
        }
      }),
      {
        status: 200,
        headers: { "Content-Type": "application/json" }
      }
    );
  };
  const result = await createStoreOrderPasteReplaceJob({
    orderGUID: "order-1",
    targetField: "quantity",
    items: [
      {
        productCode: "P001",
        quantity: 2,
        action: "replace"
      }
    ]
  });
  assertEqual(capturedUrl, "/api/react/v1/store-order/line/paste-replace/jobs", "Excel \u7C98\u8D34\u5BFC\u5165 job \u521B\u5EFA\u63A5\u53E3\u8DEF\u5F84\u5E94\u4FDD\u6301\u4E0D\u53D8");
  assertEqual(capturedMethod, "POST", "Excel \u7C98\u8D34\u5BFC\u5165 job \u521B\u5EFA\u63A5\u53E3\u5E94\u4F7F\u7528 POST");
  assertDeepEqual(
    capturedBody,
    {
      orderGUID: "order-1",
      targetField: "quantity",
      items: [
        {
          productCode: "P001",
          quantity: 2,
          action: "replace"
        }
      ]
    },
    "Excel \u7C98\u8D34\u5BFC\u5165 job \u521B\u5EFA\u63A5\u53E3\u5E94\u53D1\u9001\u539F\u59CB payload"
  );
  assertDeepEqual(
    result,
    {
      jobId: "paste-job-1",
      status: "Queued",
      message: "Excel \u7C98\u8D34\u5BFC\u5165\u4EFB\u52A1\u5DF2\u63D0\u4EA4",
      orderGUID: "order-1",
      targetField: "quantity",
      totalCount: 3,
      importedCount: 2,
      skippedCount: 1,
      createdAt: "2026-06-11T00:00:00Z",
      completedAt: void 0
    },
    "Excel \u7C98\u8D34\u5BFC\u5165 job \u521B\u5EFA\u63A5\u53E3\u5E94\u5F52\u4E00\u5316\u54CD\u5E94"
  );
} finally {
  globalThis.fetch = originalFetch;
}
try {
  globalThis.fetch = async () => new Response(
    JSON.stringify({
      success: false,
      message: "Excel \u7C98\u8D34\u5BFC\u5165\u4EFB\u52A1\u521B\u5EFA\u5931\u8D25",
      data: null
    }),
    {
      status: 200,
      headers: { "Content-Type": "application/json" }
    }
  );
  const result = await createStoreOrderPasteReplaceJob({
    orderGUID: "order-1",
    targetField: "allocQuantity",
    items: []
  });
  assertDeepEqual(
    result,
    {
      jobId: "",
      status: "Failed",
      message: "Excel \u7C98\u8D34\u5BFC\u5165\u4EFB\u52A1\u521B\u5EFA\u5931\u8D25",
      orderGUID: void 0,
      targetField: void 0,
      totalCount: void 0,
      importedCount: void 0,
      skippedCount: void 0,
      createdAt: void 0,
      completedAt: void 0
    },
    "Excel \u7C98\u8D34\u5BFC\u5165 job \u5916\u5C42\u5931\u8D25\u54CD\u5E94\u5E94\u5F52\u4E00\u5316\u4E3A Failed \u5E76\u4FDD\u7559 message"
  );
} finally {
  globalThis.fetch = originalFetch;
}
try {
  let capturedUrl = "";
  let capturedMethod = "";
  globalThis.fetch = async (input, init) => {
    capturedUrl = String(input);
    capturedMethod = String(init?.method);
    return new Response(
      JSON.stringify({
        success: true,
        data: {
          jobId: "paste/job with spaces",
          status: "Succeeded",
          message: "Excel \u7C98\u8D34\u5BFC\u5165\u5B8C\u6210",
          orderGUID: "order-1",
          targetField: "allocQuantity",
          totalCount: 4,
          importedCount: 3,
          skippedCount: 1,
          completedAt: "2026-06-11T00:01:00Z"
        }
      }),
      {
        status: 200,
        headers: { "Content-Type": "application/json" }
      }
    );
  };
  const result = await getStoreOrderPasteReplaceJob("paste/job with spaces");
  assertEqual(
    capturedUrl,
    "/api/react/v1/store-order/line/paste-replace/jobs/paste%2Fjob%20with%20spaces",
    "Excel \u7C98\u8D34\u5BFC\u5165 job \u67E5\u8BE2\u63A5\u53E3\u5E94\u7F16\u7801 jobId"
  );
  assertEqual(capturedMethod, "GET", "Excel \u7C98\u8D34\u5BFC\u5165 job \u67E5\u8BE2\u63A5\u53E3\u5E94\u4F7F\u7528 GET");
  assertDeepEqual(
    result,
    {
      jobId: "paste/job with spaces",
      status: "Succeeded",
      message: "Excel \u7C98\u8D34\u5BFC\u5165\u5B8C\u6210",
      orderGUID: "order-1",
      targetField: "allocQuantity",
      totalCount: 4,
      importedCount: 3,
      skippedCount: 1,
      createdAt: void 0,
      completedAt: "2026-06-11T00:01:00Z"
    },
    "Excel \u7C98\u8D34\u5BFC\u5165 job \u67E5\u8BE2\u63A5\u53E3\u5E94\u5F52\u4E00\u5316\u5B8C\u6210\u7ED3\u679C"
  );
} finally {
  globalThis.fetch = originalFetch;
}
try {
  let capturedUrl = "";
  let capturedMethod = "";
  let capturedBody = null;
  globalThis.fetch = async (input, init) => {
    capturedUrl = String(input);
    capturedMethod = String(init?.method);
    capturedBody = init?.body ? JSON.parse(String(init.body)) : null;
    return new Response(
      JSON.stringify({
        success: true,
        data: true
      }),
      {
        status: 200,
        headers: { "Content-Type": "application/json" }
      }
    );
  };
  await updateStoreOrderOutboundDate({
    orderGUID: "order-1",
    outboundDate: "2026-06-07",
    completeOrder: true
  });
  assertEqual(
    capturedUrl,
    "/api/react/v1/store-order/outbound-date",
    "\u51FA\u5E93\u65E5\u671F\u63A5\u53E3\u8DEF\u5F84\u5E94\u4FDD\u6301\u5951\u7EA6\u4E00\u81F4"
  );
  assertEqual(capturedMethod, "POST", "\u51FA\u5E93\u65E5\u671F\u63A5\u53E3\u5E94\u4F7F\u7528 POST");
  assertDeepEqual(
    capturedBody,
    {
      orderGUID: "order-1",
      outboundDate: "2026-06-07",
      completeOrder: true,
      orderGuid: "order-1"
    },
    "\u51FA\u5E93\u65E5\u671F\u63A5\u53E3\u5E94\u53D1\u9001\u8BA2\u5355\u3001\u65E5\u671F\u548C\u662F\u5426\u5B8C\u6210\u8BA2\u5355"
  );
} finally {
  globalThis.fetch = originalFetch;
}
try {
  let capturedUrl = "";
  globalThis.fetch = async (input) => {
    capturedUrl = String(input);
    return new Response(
      JSON.stringify({
        success: true,
        data: {
          orderGUID: "order-1",
          totalAmount: 0,
          totalQuantity: 0,
          totalImportAmount: 0,
          totalVolume: 0,
          items: []
        }
      }),
      {
        status: 200,
        headers: { "Content-Type": "application/json" }
      }
    );
  };
  await getStoreOrderDetail("order-1");
  assertEqual(capturedUrl, "/api/react/v1/store-order/detail/order-1/full", "\u65E7\u8C03\u7528\u9ED8\u8BA4\u5E94\u8BFB\u53D6\u5168\u91CF\u660E\u7EC6");
  await getStoreOrderDetailFull("order-2");
  assertEqual(capturedUrl, "/api/react/v1/store-order/detail/order-2/full", "\u5168\u91CF\u660E\u7EC6\u63A5\u53E3\u5E94\u4F7F\u7528 /full \u8DEF\u5F84");
} finally {
  globalThis.fetch = originalFetch;
}
try {
  let capturedUrl = "";
  globalThis.fetch = async (input) => {
    capturedUrl = String(input);
    return new Response(
      JSON.stringify({
        success: true,
        data: ["P001", "P002", 123, null]
      }),
      {
        status: 200,
        headers: { "Content-Type": "application/json" }
      }
    );
  };
  const productCodes = await getStoreOrderDetailProductCodes("order-1");
  assertEqual(
    capturedUrl,
    "/api/react/v1/store-order/detail/order-1/product-codes",
    "\u8DE8\u9875\u53BB\u91CD\u5E94\u8BFB\u53D6\u8F7B\u91CF\u5546\u54C1\u7F16\u7801\u63A5\u53E3"
  );
  assertDeepEqual(productCodes, ["P001", "P002"], "\u5546\u54C1\u7F16\u7801\u63A5\u53E3\u5E94\u8FC7\u6EE4\u975E\u5B57\u7B26\u4E32\u503C");
} finally {
  globalThis.fetch = originalFetch;
}
try {
  let capturedUrl = "";
  let capturedMethod = "";
  let capturedBody = null;
  globalThis.fetch = async (input, init) => {
    capturedUrl = String(input);
    capturedMethod = String(init?.method);
    capturedBody = JSON.parse(String(init?.body));
    return new Response(
      JSON.stringify({
        success: true,
        data: {
          subject: "Custom subject",
          body: "Custom body"
        }
      }),
      {
        status: 200,
        headers: { "Content-Type": "application/json" }
      }
    );
  };
  const translatedEmailText = await translateStoreOrderInvoiceEmailText({
    orderGUID: "order-1",
    targetLanguage: "en",
    subject: "\u81EA\u5B9A\u4E49\u4E3B\u9898",
    body: "\u81EA\u5B9A\u4E49\u6B63\u6587"
  });
  assertEqual(
    capturedUrl,
    "/api/react/v1/store-order/invoice/email/translate-text",
    "\u53D1\u7968\u90AE\u4EF6\u6587\u672C\u7FFB\u8BD1\u63A5\u53E3\u8DEF\u5F84\u5E94\u4FDD\u6301\u5951\u7EA6\u4E00\u81F4"
  );
  assertEqual(capturedMethod, "POST", "\u53D1\u7968\u90AE\u4EF6\u6587\u672C\u7FFB\u8BD1\u63A5\u53E3\u5E94\u4F7F\u7528 POST");
  assertDeepEqual(
    capturedBody,
    {
      orderGUID: "order-1",
      targetLanguage: "en",
      subject: "\u81EA\u5B9A\u4E49\u4E3B\u9898",
      body: "\u81EA\u5B9A\u4E49\u6B63\u6587"
    },
    "\u53D1\u7968\u90AE\u4EF6\u6587\u672C\u7FFB\u8BD1\u63A5\u53E3\u5E94\u53D1\u9001\u76EE\u6807\u8BED\u8A00\u548C\u5F53\u524D\u7F16\u8F91\u5185\u5BB9"
  );
  assertDeepEqual(
    translatedEmailText,
    { subject: "Custom subject", body: "Custom body" },
    "\u53D1\u7968\u90AE\u4EF6\u6587\u672C\u7FFB\u8BD1\u63A5\u53E3\u5E94\u8FD4\u56DE\u5F52\u4E00\u5316\u7ED3\u679C"
  );
} finally {
  globalThis.fetch = originalFetch;
}
try {
  let capturedUrl = "";
  let capturedMethod = "";
  let capturedBody = null;
  globalThis.fetch = async (input, init) => {
    capturedUrl = String(input);
    capturedMethod = String(init?.method);
    capturedBody = init?.body ? JSON.parse(String(init.body)) : null;
    return new Response(
      JSON.stringify({
        success: true,
        data: null
      }),
      {
        status: 200,
        headers: { "Content-Type": "application/json" }
      }
    );
  };
  await updateStoreOrderLine({
    orderGUID: "order-1",
    productCode: "product-1",
    allocQuantity: 7,
    importPrice: 1.25,
    syncImportPrice: false
  });
  assertEqual(capturedUrl, "/api/react/v1/store-order/line/update", "\u5355\u884C\u4FDD\u5B58\u63A5\u53E3\u8DEF\u5F84\u5E94\u4FDD\u6301\u4E0D\u53D8");
  assertEqual(capturedMethod, "POST", "\u5355\u884C\u4FDD\u5B58\u63A5\u53E3\u5E94\u7EE7\u7EED\u4F7F\u7528 POST");
  assertDeepEqual(
    capturedBody,
    {
      orderGUID: "order-1",
      productCode: "product-1",
      importPrice: 1.25,
      syncImportPrice: false,
      quantity: 7
    },
    "\u5355\u884C\u4FDD\u5B58\u5E94\u6620\u5C04\u53D1\u8D27\u6570\u5B57\u6BB5\uFF0C\u5E76\u4FDD\u7559\u8FDB\u53E3\u4EF7\u540C\u6B65\u5F00\u5173"
  );
} finally {
  globalThis.fetch = originalFetch;
}
try {
  let capturedUrl = "";
  let capturedMethod = "";
  let capturedBody = null;
  globalThis.fetch = async (input, init) => {
    capturedUrl = String(input);
    capturedMethod = String(init?.method);
    capturedBody = init?.body ? JSON.parse(String(init.body)) : null;
    return new Response(
      JSON.stringify({
        success: true,
        data: null
      }),
      {
        status: 200,
        headers: { "Content-Type": "application/json" }
      }
    );
  };
  await batchUpdateStoreOrderLines({
    orderGUID: "order-1",
    items: [
      {
        productCode: "product-1",
        importPrice: 2.5,
        syncImportPrice: true
      }
    ]
  });
  assertEqual(capturedUrl, "/api/react/v1/store-order/line/batch-update", "\u6279\u91CF\u4FDD\u5B58\u63A5\u53E3\u8DEF\u5F84\u5E94\u4FDD\u6301\u4E0D\u53D8");
  assertEqual(capturedMethod, "POST", "\u6279\u91CF\u4FDD\u5B58\u63A5\u53E3\u5E94\u7EE7\u7EED\u4F7F\u7528 POST");
  assertDeepEqual(
    capturedBody,
    {
      orderGUID: "order-1",
      items: [
        {
          productCode: "product-1",
          importPrice: 2.5,
          syncImportPrice: true
        }
      ]
    },
    "\u6279\u91CF\u4FDD\u5B58\u5E94\u4FDD\u7559\u53EA\u6539\u8FDB\u53E3\u4EF7\u548C\u540C\u6B65\u5F00\u5173\u7684 payload"
  );
} finally {
  globalThis.fetch = originalFetch;
}
try {
  let capturedUrl = "";
  let capturedMethod = "";
  let capturedBody = null;
  globalThis.fetch = async (input, init) => {
    capturedUrl = String(input);
    capturedMethod = String(init?.method);
    capturedBody = init?.body ? JSON.parse(String(init.body)) : null;
    return new Response(
      JSON.stringify({
        success: true,
        data: null
      }),
      {
        status: 200,
        headers: { "Content-Type": "application/json" }
      }
    );
  };
  await updateStoreOrderStatus({
    orderGUID: "order-1",
    newStatus: 3
  });
  assertEqual(capturedUrl, "/api/react/v1/store-order/status", "\u8BE6\u60C5\u9875\u72B6\u6001\u66F4\u6539\u63A5\u53E3\u8DEF\u5F84\u5E94\u4FDD\u6301\u4E0D\u53D8");
  assertEqual(capturedMethod, "POST", "\u8BE6\u60C5\u9875\u72B6\u6001\u66F4\u6539\u63A5\u53E3\u5E94\u4F7F\u7528 POST");
  assertDeepEqual(
    capturedBody,
    {
      orderGUID: "order-1",
      newStatus: 3
    },
    "\u8BE6\u60C5\u9875\u72B6\u6001\u66F4\u6539\u5E94\u4FDD\u6301\u540E\u7AEF\u517C\u5BB9 payload"
  );
} finally {
  globalThis.fetch = originalFetch;
}
try {
  let capturedUrl = "";
  let capturedMethod = "";
  let capturedBody = null;
  globalThis.fetch = async (input, init) => {
    capturedUrl = String(input);
    capturedMethod = String(init?.method);
    capturedBody = init?.body ? JSON.parse(String(init.body)) : null;
    return new Response(
      JSON.stringify({
        success: true,
        data: null
      }),
      {
        status: 200,
        headers: { "Content-Type": "application/json" }
      }
    );
  };
  await updateStoreOrderStoreContact({
    orderGUID: "order-1",
    storeCode: "S001",
    address: "1 Test Street",
    contactEmail: "store@example.com"
  });
  assertEqual(capturedUrl, "/api/react/v1/store-order/store-contact/update", "\u5206\u5E97\u5730\u5740\u90AE\u7BB1\u66F4\u65B0\u63A5\u53E3\u8DEF\u5F84\u5E94\u4FDD\u6301\u5951\u7EA6\u4E00\u81F4");
  assertEqual(capturedMethod, "POST", "\u5206\u5E97\u5730\u5740\u90AE\u7BB1\u66F4\u65B0\u63A5\u53E3\u5E94\u4F7F\u7528 POST");
  assertDeepEqual(
    capturedBody,
    {
      orderGUID: "order-1",
      storeCode: "S001",
      address: "1 Test Street",
      contactEmail: "store@example.com"
    },
    "\u5206\u5E97\u5730\u5740\u90AE\u7BB1\u66F4\u65B0\u5E94\u539F\u6837\u53D1\u9001\u524D\u540E\u7AEF\u7EA6\u5B9A payload"
  );
} finally {
  globalThis.fetch = originalFetch;
}
try {
  let capturedUrl = "";
  let capturedMethod = "";
  let capturedBody = null;
  globalThis.fetch = async (input, init) => {
    capturedUrl = String(input);
    capturedMethod = String(init?.method);
    capturedBody = init?.body ? JSON.parse(String(init.body)) : null;
    return new Response(
      JSON.stringify({
        success: true,
        data: {
          jobId: "job-1",
          status: "Queued",
          message: "\u53D1\u7968\u90AE\u4EF6\u53D1\u9001\u4EFB\u52A1\u5DF2\u63D0\u4EA4",
          orderGUID: "order-1",
          toEmail: "invoice@example.com",
          createdAt: "2026-06-05T00:00:00Z"
        }
      }),
      {
        status: 200,
        headers: { "Content-Type": "application/json" }
      }
    );
  };
  const result = await sendStoreOrderInvoiceEmail({
    orderGUID: "order-1",
    toEmail: "invoice@example.com",
    subject: "Store Order Invoice",
    body: "Please check the attached invoice."
  });
  assertEqual(capturedUrl, "/api/react/v1/store-order/invoice/email", "\u53D1\u7968\u90AE\u4EF6\u63A5\u53E3\u8DEF\u5F84\u5E94\u4FDD\u6301\u5951\u7EA6\u4E00\u81F4");
  assertEqual(capturedMethod, "POST", "\u53D1\u7968\u90AE\u4EF6\u63A5\u53E3\u5E94\u4F7F\u7528 POST");
  assertDeepEqual(
    result,
    {
      jobId: "job-1",
      status: "Queued",
      message: "\u53D1\u7968\u90AE\u4EF6\u53D1\u9001\u4EFB\u52A1\u5DF2\u63D0\u4EA4",
      orderGUID: "order-1",
      toEmail: "invoice@example.com",
      createdAt: "2026-06-05T00:00:00Z",
      completedAt: void 0
    },
    "\u53D1\u7968\u90AE\u4EF6\u63A5\u53E3\u5E94\u8FD4\u56DE\u5F52\u4E00\u5316 job \u72B6\u6001"
  );
  assertDeepEqual(
    capturedBody,
    {
      orderGUID: "order-1",
      toEmail: "invoice@example.com",
      subject: "Store Order Invoice",
      body: "Please check the attached invoice."
    },
    "\u53D1\u7968\u90AE\u4EF6\u63A5\u53E3\u5E94\u53EA\u53D1\u9001\u786E\u8BA4\u4FE1\u606F\uFF0C\u4E0D\u4E0A\u4F20\u524D\u7AEF\u9644\u4EF6"
  );
} finally {
  globalThis.fetch = originalFetch;
}
try {
  let capturedUrl = "";
  let capturedMethod = "";
  globalThis.fetch = async (input, init) => {
    capturedUrl = String(input);
    capturedMethod = String(init?.method);
    return new Response(
      JSON.stringify({
        success: true,
        data: {
          jobId: "job/with spaces",
          status: "Succeeded",
          message: "\u53D1\u7968\u90AE\u4EF6\u53D1\u9001\u6210\u529F",
          completedAt: "2026-06-05T00:01:00Z"
        }
      }),
      {
        status: 200,
        headers: { "Content-Type": "application/json" }
      }
    );
  };
  const result = await getStoreOrderInvoiceEmailJob("job/with spaces");
  assertEqual(
    capturedUrl,
    "/api/react/v1/store-order/invoice/email/jobs/job%2Fwith%20spaces",
    "\u53D1\u7968\u90AE\u4EF6 job \u67E5\u8BE2\u63A5\u53E3\u5E94\u7F16\u7801 jobId"
  );
  assertEqual(capturedMethod, "GET", "\u53D1\u7968\u90AE\u4EF6 job \u67E5\u8BE2\u63A5\u53E3\u5E94\u4F7F\u7528 GET");
  assertDeepEqual(
    result,
    {
      jobId: "job/with spaces",
      status: "Succeeded",
      message: "\u53D1\u7968\u90AE\u4EF6\u53D1\u9001\u6210\u529F",
      orderGUID: void 0,
      toEmail: void 0,
      createdAt: void 0,
      completedAt: "2026-06-05T00:01:00Z"
    },
    "\u53D1\u7968\u90AE\u4EF6 job \u67E5\u8BE2\u63A5\u53E3\u5E94\u5F52\u4E00\u5316\u6210\u529F\u72B6\u6001"
  );
} finally {
  globalThis.fetch = originalFetch;
}
console.log("storeOrderService.detail.test: ok");
