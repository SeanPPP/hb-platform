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
request.get = (url, options) => request(url, { ...options, method: "GET" });
request.post = (url, data, options) => request(url, { ...options, method: "POST", data });
request.put = (url, data, options) => request(url, { ...options, method: "PUT", data });
request.patch = (url, data, options) => request(url, { ...options, method: "PATCH", data });
request.delete = (url, options) => request(url, { ...options, method: "DELETE" });
var request_default = request;

// src/services/productHqSyncPolling.ts
var PRODUCT_HQ_SYNC_POLL_INTERVAL_MS = 2e3;
var PRODUCT_HQ_SYNC_TIMEOUT_MS = 10 * 60 * 1e3;
var HqProductSyncPollingTimeoutError = class extends Error {
  constructor(message = "\u5546\u54C1\u540C\u6B65\u4EFB\u52A1\u8F6E\u8BE2\u8D85\u65F6") {
    super(message);
    this.name = "HqProductSyncPollingTimeoutError";
  }
};
var HqProductSyncPollingCancelledError = class extends Error {
  constructor(message = "\u5546\u54C1\u540C\u6B65\u4EFB\u52A1\u8F6E\u8BE2\u5DF2\u53D6\u6D88") {
    super(message);
    this.name = "HqProductSyncPollingCancelledError";
  }
};
function isTerminalStatus(status) {
  return status === "Succeeded" || status === "Failed";
}
function createHqSyncJobPoller({
  jobId,
  getJob,
  isTerminalStatus: isTerminalStatusOverride,
  pollIntervalMs = PRODUCT_HQ_SYNC_POLL_INTERVAL_MS,
  timeoutMs = PRODUCT_HQ_SYNC_TIMEOUT_MS,
  setTimeoutFn = setTimeout,
  clearTimeoutFn = clearTimeout
}) {
  let pollingTimer = null;
  let timeoutTimer = null;
  let stopped = false;
  let rejectPromise = null;
  const isJobTerminalStatus = isTerminalStatusOverride ?? isTerminalStatus;
  const clearTimers = () => {
    if (pollingTimer) {
      clearTimeoutFn(pollingTimer);
      pollingTimer = null;
    }
    if (timeoutTimer) {
      clearTimeoutFn(timeoutTimer);
      timeoutTimer = null;
    }
  };
  const promise = new Promise((resolve, reject) => {
    rejectPromise = reject;
    const scheduleNextPoll = () => {
      pollingTimer = setTimeoutFn(async () => {
        try {
          const result = await getJob(jobId);
          if (stopped) {
            return;
          }
          if (isJobTerminalStatus(result.status)) {
            clearTimers();
            resolve(result);
            return;
          }
          scheduleNextPoll();
        } catch (error) {
          if (stopped) {
            return;
          }
          clearTimers();
          reject(error);
        }
      }, pollIntervalMs);
    };
    timeoutTimer = setTimeoutFn(() => {
      if (stopped) {
        return;
      }
      stopped = true;
      clearTimers();
      reject(new HqProductSyncPollingTimeoutError());
    }, timeoutMs);
    scheduleNextPoll();
  });
  const stop = () => {
    if (stopped) {
      return;
    }
    stopped = true;
    clearTimers();
    rejectPromise?.(new HqProductSyncPollingCancelledError());
  };
  return {
    promise,
    stop
  };
}

// src/services/warehouseProductService.ts
var API_BASE = "/api/react/v1/product-warehouse";
var WAREHOUSE_PRODUCT_BATCH_UPDATE_JOB_STATUSES = [
  "Queued",
  "Running",
  "PartiallySucceeded",
  "Succeeded",
  "Failed"
];
function unwrapResponse(response, emptyData) {
  if (response && typeof response === "object") {
    if ("data" in response && response.data !== void 0) {
      return response.data;
    }
    return response;
  }
  return emptyData;
}
function ensureApiSuccess(success, message, fallback) {
  if (success === false) {
    throw new Error(message || fallback || "\u8BF7\u6C42\u5931\u8D25");
  }
}
function toNumber(value) {
  if (typeof value === "number") {
    return value;
  }
  if (typeof value === "string" && value.trim()) {
    const parsed = Number(value);
    return Number.isNaN(parsed) ? void 0 : parsed;
  }
  return void 0;
}
function toBoolean(value, fallback = false) {
  if (typeof value === "boolean") {
    return value;
  }
  if (typeof value === "string") {
    if (value.toLowerCase() === "true") {
      return true;
    }
    if (value.toLowerCase() === "false") {
      return false;
    }
  }
  return fallback;
}
function readString(...values) {
  for (const value of values) {
    if (typeof value === "string") {
      const trimmed = value.trim();
      if (trimmed) {
        return trimmed;
      }
      continue;
    }
    if (typeof value === "number") {
      return String(value);
    }
  }
  return void 0;
}
function readRecord(value) {
  return value && typeof value === "object" && !Array.isArray(value) ? value : {};
}
function readStringArray(...values) {
  for (const value of values) {
    if (Array.isArray(value)) {
      const items = value.map((item) => String(item ?? "").trim()).filter(Boolean);
      if (items.length) return items;
    }
    if (typeof value === "string" && value.trim()) {
      return value.split(",").map((item) => item.trim()).filter(Boolean);
    }
  }
  return void 0;
}
function normalizeWarehouseProductHqImageSync(raw) {
  if (!raw || typeof raw !== "object" || Array.isArray(raw)) {
    return void 0;
  }
  const value = raw;
  const rawItems = Array.isArray(value.items) ? value.items : Array.isArray(value.Items) ? value.Items : void 0;
  return {
    requested: value.requested === void 0 && value.Requested === void 0 ? void 0 : toBoolean(value.requested ?? value.Requested),
    success: value.success === void 0 && value.Success === void 0 ? void 0 : toBoolean(value.success ?? value.Success),
    updatedCount: toNumber(value.updatedCount ?? value.UpdatedCount),
    successCount: toNumber(value.successCount ?? value.SuccessCount),
    failedCount: toNumber(value.failedCount ?? value.FailedCount),
    totalCount: toNumber(value.totalCount ?? value.TotalCount),
    errorCode: readString(value.errorCode, value.ErrorCode),
    errors: readStringArray(value.errors, value.Errors) ?? [],
    items: Array.isArray(rawItems) ? rawItems.filter((item) => !!item && typeof item === "object").map((item) => ({
      productCode: readString(item.productCode, item.ProductCode) ?? "",
      success: toBoolean(item.success ?? item.Success, false),
      message: readString(item.message, item.Message)
    })) : void 0
  };
}
function normalizeWarehouseProductBatchUpdateResult(raw) {
  if (!raw || typeof raw !== "object" || Array.isArray(raw)) {
    return void 0;
  }
  const value = raw;
  const successCount = toNumber(value.successCount ?? value.SuccessCount);
  const failedCount = toNumber(value.failedCount ?? value.FailedCount ?? value.failed ?? value.Failed);
  const imageUpdatedCount = toNumber(value.imageUpdatedCount ?? value.ImageUpdatedCount);
  const hqImageSync = normalizeWarehouseProductHqImageSync(value.hqImageSync ?? value.HqImageSync);
  return {
    ...value,
    success: toBoolean(value.success ?? value.Success, false),
    ...successCount === void 0 ? {} : { successCount },
    ...failedCount === void 0 ? {} : { failedCount },
    errors: readStringArray(value.errors, value.Errors) ?? [],
    ...imageUpdatedCount === void 0 ? {} : { imageUpdatedCount },
    hqImageSync
  };
}
function normalizeWarehouseProductBatchUpdateJob(raw, fallbackJobId = "") {
  const value = readRecord(raw);
  const rawStatus = readString(value.status, value.Status);
  const rawResult = value.result ?? value.Result;
  if (!WAREHOUSE_PRODUCT_BATCH_UPDATE_JOB_STATUSES.includes(rawStatus)) {
    throw new Error(`\u672A\u77E5\u7684\u4ED3\u5E93\u5546\u54C1\u6279\u91CF\u4FEE\u6539\u4EFB\u52A1\u72B6\u6001: ${rawStatus ?? ""}`);
  }
  return {
    jobId: readString(value.jobId, value.JobId) ?? fallbackJobId,
    operationId: readString(value.operationId, value.OperationId),
    status: rawStatus,
    isDuplicateRequest: value.isDuplicateRequest === void 0 && value.IsDuplicateRequest === void 0 ? void 0 : toBoolean(value.isDuplicateRequest ?? value.IsDuplicateRequest),
    createdAt: readString(value.createdAt, value.CreatedAt),
    startedAt: readString(value.startedAt, value.StartedAt),
    completedAt: readString(value.completedAt, value.CompletedAt),
    expiresAt: readString(value.expiresAt, value.ExpiresAt),
    message: readString(value.message, value.Message),
    result: normalizeWarehouseProductBatchUpdateResult(rawResult)
  };
}
async function batchUpdateWarehouseProducts(items, options = {}) {
  const response = await request_default(`${API_BASE}/batch-update`, {
    method: "POST",
    data: {
      Items: items,
      ...options.syncStorePurchasePrice === void 0 ? {} : { SyncStorePurchasePrice: options.syncStorePurchasePrice },
      ...options.generateImageUrls === void 0 ? {} : { GenerateImageUrls: options.generateImageUrls },
      ...options.imageBaseUrl === void 0 ? {} : { ImageBaseUrl: options.imageBaseUrl },
      ...options.syncImageToHq === void 0 ? {} : { SyncImageToHq: options.syncImageToHq }
    }
  });
  const raw = response;
  ensureApiSuccess(raw?.success ?? raw?.isSuccess, raw?.message, "\u4ED3\u5E93\u6279\u91CF\u66F4\u65B0\u5931\u8D25");
  const result = unwrapResponse(response, { success: false });
  ensureApiSuccess(result.success, result.message, "\u4ED3\u5E93\u6279\u91CF\u66F4\u65B0\u5931\u8D25");
  const imageUpdatedCount = toNumber(result.imageUpdatedCount ?? result.ImageUpdatedCount);
  const hqImageSync = normalizeWarehouseProductHqImageSync(result.hqImageSync ?? result.HqImageSync);
  return {
    ...result,
    ...imageUpdatedCount === void 0 ? {} : { imageUpdatedCount },
    hqImageSync
  };
}
function createWarehouseProductBatchUpdateJobPoller({
  jobId,
  getJob,
  ...options
}) {
  return createHqSyncJobPoller({
    jobId,
    getJob,
    isTerminalStatus: (status) => status === "Succeeded" || status === "PartiallySucceeded" || status === "Failed",
    ...options
  });
}
async function createWarehouseProductBatchUpdateJob(items, options = {}) {
  const response = await request_default.post(
    `${API_BASE}/batch-update/jobs`,
    {
      Items: items,
      ...options.syncStorePurchasePrice === void 0 ? {} : { SyncStorePurchasePrice: options.syncStorePurchasePrice },
      ...options.generateImageUrls === void 0 ? {} : { GenerateImageUrls: options.generateImageUrls },
      ...options.imageBaseUrl === void 0 ? {} : { ImageBaseUrl: options.imageBaseUrl },
      ...options.syncImageToHq === void 0 ? {} : { SyncImageToHq: options.syncImageToHq }
    }
  );
  ensureApiSuccess(response.success ?? response.isSuccess, response.message, "\u521B\u5EFA\u4ED3\u5E93\u5546\u54C1\u6279\u91CF\u4FEE\u6539\u4EFB\u52A1\u5931\u8D25");
  return normalizeWarehouseProductBatchUpdateJob(response.data, "");
}
async function getWarehouseProductBatchUpdateJob(jobId) {
  const response = await request_default.get(
    `${API_BASE}/batch-update/jobs/${encodeURIComponent(jobId)}`
  );
  ensureApiSuccess(response.success ?? response.isSuccess, response.message, "\u67E5\u8BE2\u4ED3\u5E93\u5546\u54C1\u6279\u91CF\u4FEE\u6539\u4EFB\u52A1\u5931\u8D25");
  return normalizeWarehouseProductBatchUpdateJob(response.data, jobId);
}
async function updateWarehouseProductFull(productCode, payload) {
  return request_default(`${API_BASE}/${productCode}/full-update`, {
    method: "PUT",
    data: {
      ProductName: payload.productName,
      EnglishName: payload.englishName,
      ProductSpecification: payload.productSpecification,
      Material: payload.material,
      Remark: payload.remark,
      PackingQuantity: payload.packingQuantity,
      MinOrderQuantity: payload.minOrderQuantity,
      UnitVolume: payload.unitVolume,
      GrossWeight: payload.grossWeight,
      PackingSize: payload.packingSize,
      DomesticPrice: payload.domesticPrice,
      OEMPrice: payload.oemPrice,
      ImportPrice: payload.importPrice,
      IsActive: payload.isActive,
      ProductImage: payload.productImage,
      ProductType: payload.productType,
      MiddlePackQuantity: payload.middlePackQuantity,
      IsAutoPricing: payload.isAutoPricing,
      WarehouseCategoryGUID: payload.warehouseCategoryGUID,
      SupplierCode: payload.supplierCode,
      LocalSupplierCode: payload.localSupplierCode
    }
  });
}
async function patchWarehouseProduct(productCode, payload) {
  return request_default.patch(`${API_BASE}/${encodeURIComponent(productCode)}`, {
    ...payload.minOrderQuantity !== void 0 ? { MinOrderQuantity: payload.minOrderQuantity } : {},
    ...payload.domesticPrice !== void 0 ? { DomesticPrice: payload.domesticPrice } : {},
    ...payload.importPrice !== void 0 ? { ImportPrice: payload.importPrice } : {},
    ...payload.oemPrice !== void 0 ? { OEMPrice: payload.oemPrice } : {}
  });
}

// src/services/warehouseProductService.batchUpdate.test.ts
import { readFileSync } from "node:fs";
import path from "node:path";
function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}
function assertDeepEqual(actual, expected, message) {
  const actualJson = JSON.stringify(actual);
  const expectedJson = JSON.stringify(expected);
  if (actualJson !== expectedJson) {
    throw new Error(`${message}\u3002Expected: ${expectedJson}, received: ${actualJson}`);
  }
}
var originalFetch = globalThis.fetch;
var capturedUrl = "";
var capturedMethod;
var capturedBody;
var serviceSource = readFileSync(path.resolve(process.cwd(), "src/services/warehouseProductService.ts"), "utf8");
assert(
  serviceSource.includes("MinOrderQuantity?: number") && serviceSource.includes("PackingQuantity?: number"),
  "\u4ED3\u5E93\u5546\u54C1\u6279\u91CF\u66F4\u65B0\u7C7B\u578B\u5E94\u58F0\u660E MinOrderQuantity \u548C PackingQuantity"
);
globalThis.fetch = async (input, init) => {
  capturedUrl = String(input);
  capturedMethod = init?.method;
  capturedBody = JSON.parse(String(init?.body ?? "{}"));
  return new Response(JSON.stringify({ success: true, data: { success: true, successCount: 1 } }), {
    status: 200,
    headers: { "Content-Type": "application/json" }
  });
};
try {
  await batchUpdateWarehouseProducts([
    {
      ProductCode: "P001",
      SupplierCode: "SUPPLIER-NEW",
      MinOrderQuantity: 0,
      PackingQuantity: 0,
      IsActive: false,
      DomesticPrice: void 0
    }
  ], { syncStorePurchasePrice: false });
  assert(capturedBody, "\u5E94\u6355\u83B7\u4ED3\u5E93\u5546\u54C1\u6279\u91CF\u66F4\u65B0\u8BF7\u6C42\u4F53");
  assert(capturedUrl.endsWith("/api/react/v1/product-warehouse/batch-update"), "\u6279\u91CF\u66F4\u65B0\u5E94\u8C03\u7528\u4ED3\u5E93\u5546\u54C1 batch-update \u63A5\u53E3");
  assert(capturedMethod === "POST", "\u6279\u91CF\u66F4\u65B0\u5E94\u4F7F\u7528 POST \u65B9\u6CD5");
  assertDeepEqual(
    capturedBody,
    {
      Items: [
        {
          ProductCode: "P001",
          SupplierCode: "SUPPLIER-NEW",
          MinOrderQuantity: 0,
          PackingQuantity: 0,
          IsActive: false
        }
      ],
      SyncStorePurchasePrice: false
    },
    "\u6279\u91CF\u66F4\u65B0\u8BF7\u6C42\u4F53\u5E94\u4FDD\u7559\u6570\u91CF\u96F6\u503C\u548C false\uFF0C\u5E76\u5FFD\u7565 undefined \u5B57\u6BB5"
  );
  globalThis.fetch = async (input, init) => {
    capturedUrl = String(input);
    capturedMethod = init?.method;
    capturedBody = JSON.parse(String(init?.body ?? "{}"));
    return new Response(JSON.stringify({
      success: true,
      successCount: 1,
      failedCount: 0,
      imageUpdatedCount: 1,
      hqImageSync: {
        requested: true,
        success: false,
        updatedCount: 0,
        failedCount: 1,
        errorCode: "HQ_IMAGE_SYNC_ITEM_ERRORS",
        errors: ["HQ \u5546\u54C1\u4E0D\u5B58\u5728: P001"]
      }
    }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
  };
  const imageResult = await batchUpdateWarehouseProducts(
    [{ ProductCode: "P001" }],
    {
      generateImageUrls: true,
      imageBaseUrl: "https://images.example.com/catalog/",
      syncImageToHq: true
    }
  );
  assertDeepEqual(
    capturedBody,
    {
      Items: [{ ProductCode: "P001" }],
      GenerateImageUrls: true,
      ImageBaseUrl: "https://images.example.com/catalog/",
      SyncImageToHq: true
    },
    "\u6279\u91CF\u56FE\u7247\u66F4\u65B0\u5E94\u53D1\u9001\u56FE\u7247\u751F\u6210\u548C HQ \u540C\u6B65\u9009\u9879"
  );
  assert(imageResult.imageUpdatedCount === 1, "\u5E94\u5F52\u4E00\u5316\u672C\u5730\u56FE\u7247\u66F4\u65B0\u6570\u91CF");
  assert(imageResult.hqImageSync?.success === false, "HQ \u9010\u9879\u5931\u8D25\u4E0D\u5E94\u7531\u670D\u52A1\u5C42\u629B\u9519");
  assert(imageResult.hqImageSync?.failedCount === 1, "\u5E94\u5F52\u4E00\u5316 HQ \u5931\u8D25\u6570\u91CF");
  assertDeepEqual(
    imageResult.hqImageSync?.errors,
    ["HQ \u5546\u54C1\u4E0D\u5B58\u5728: P001"],
    "\u5E94\u4FDD\u7559 HQ \u540C\u6B65\u9519\u8BEF\u660E\u7EC6"
  );
  globalThis.fetch = async (input, init) => {
    capturedUrl = String(input);
    capturedMethod = init?.method;
    capturedBody = JSON.parse(String(init?.body ?? "{}"));
    const isStatusRequest = capturedMethod === "GET";
    const data = isStatusRequest ? {
      jobId: "batch-job-1",
      operationId: "warehouse-product-batch-update:test",
      status: "PartiallySucceeded",
      result: {
        success: true,
        successCount: 1,
        failedCount: 1,
        imageUpdatedCount: 1,
        hqImageSync: {
          requested: true,
          success: false,
          failedCount: 1,
          errors: ["HQ \u5546\u54C1\u4E0D\u5B58\u5728: P001"]
        }
      }
    } : {
      jobId: "batch-job-1",
      operationId: "warehouse-product-batch-update:test",
      status: "Queued",
      createdAt: "2026-08-13T00:00:00Z"
    };
    return new Response(JSON.stringify({ success: true, data }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
  };
  const createdJob = await createWarehouseProductBatchUpdateJob(
    [{ ProductCode: "P001", SupplierCode: "SUPPLIER-NEW" }],
    {
      generateImageUrls: true,
      imageBaseUrl: "https://images.example.com/catalog/",
      syncImageToHq: true
    }
  );
  assert(capturedUrl.endsWith("/api/react/v1/product-warehouse/batch-update/jobs"), "\u540E\u53F0\u6279\u91CF\u4FEE\u6539\u5E94\u8C03\u7528 jobs \u521B\u5EFA\u63A5\u53E3");
  assert(capturedMethod === "POST", "\u540E\u53F0\u6279\u91CF\u4FEE\u6539\u4EFB\u52A1\u5E94\u4F7F\u7528 POST \u521B\u5EFA");
  assert(createdJob.status === "Queued", "\u521B\u5EFA\u4EFB\u52A1\u5E94\u4FDD\u7559 Queued \u72B6\u6001");
  assertDeepEqual(
    capturedBody,
    {
      Items: [{ ProductCode: "P001", SupplierCode: "SUPPLIER-NEW" }],
      GenerateImageUrls: true,
      ImageBaseUrl: "https://images.example.com/catalog/",
      SyncImageToHq: true
    },
    "\u540E\u53F0\u4EFB\u52A1\u8BF7\u6C42\u5E94\u5B8C\u6574\u643A\u5E26\u6279\u91CF\u4FEE\u6539\u4E0E\u56FE\u7247\u540C\u6B65\u9009\u9879"
  );
  const completedJob = await getWarehouseProductBatchUpdateJob("batch-job-1");
  assert(capturedUrl.endsWith("/api/react/v1/product-warehouse/batch-update/jobs/batch-job-1"), "\u5E94\u6309 jobId \u67E5\u8BE2\u540E\u53F0\u6279\u91CF\u4FEE\u6539\u72B6\u6001");
  assert(String(capturedMethod) === "GET", "\u67E5\u8BE2\u540E\u53F0\u6279\u91CF\u4FEE\u6539\u72B6\u6001\u5E94\u4F7F\u7528 GET");
  assert(completedJob.status === "PartiallySucceeded", "\u5E94\u4FDD\u7559 PartiallySucceeded \u7EC8\u6001");
  assert(completedJob.result?.hqImageSync?.failedCount === 1, "\u5E94\u5F52\u4E00\u5316\u540E\u53F0\u4EFB\u52A1\u4E2D\u7684 HQ \u5931\u8D25\u660E\u7EC6");
  const scheduledCallbacks = [];
  const poller = createWarehouseProductBatchUpdateJobPoller({
    jobId: "batch-job-1",
    getJob: async () => completedJob,
    setTimeoutFn: (callback) => {
      scheduledCallbacks.push(callback);
      return scheduledCallbacks.length;
    },
    clearTimeoutFn: () => void 0
  });
  assert(scheduledCallbacks.length === 2, "\u540E\u53F0\u4EFB\u52A1\u8F6E\u8BE2\u5E94\u540C\u65F6\u5B89\u6392\u8D85\u65F6\u4E0E\u9996\u6B21\u72B6\u6001\u67E5\u8BE2");
  scheduledCallbacks[1]?.();
  const polledJob = await poller.promise;
  assert(polledJob.status === "PartiallySucceeded", "PartiallySucceeded \u5E94\u4F5C\u4E3A\u6279\u91CF\u4FEE\u6539\u8F6E\u8BE2\u7EC8\u6001");
  capturedMethod = void 0;
  capturedBody = void 0;
  await patchWarehouseProduct("HB 001", { oemPrice: 0 });
  assert(capturedBody, "\u5E94\u6355\u83B7\u4ED3\u5E93\u5546\u54C1\u5355\u5B57\u6BB5\u66F4\u65B0\u8BF7\u6C42\u4F53");
  assert(capturedUrl.endsWith("/api/react/v1/product-warehouse/HB%20001"), "\u5355\u5B57\u6BB5\u66F4\u65B0\u5E94\u7F16\u7801\u5546\u54C1\u8D27\u53F7\u5E76\u8C03\u7528\u4ED3\u5E93\u5546\u54C1\u6839 PATCH \u63A5\u53E3");
  assert(capturedMethod === "PATCH", "\u5355\u5B57\u6BB5\u66F4\u65B0\u5E94\u4F7F\u7528 PATCH \u65B9\u6CD5");
  assertDeepEqual(
    capturedBody,
    { OEMPrice: 0 },
    "\u5355\u5B57\u6BB5\u66F4\u65B0\u5E94\u53EA\u53D1\u9001\u4E00\u4E2A PascalCase \u5B57\u6BB5\u5E76\u4FDD\u7559\u96F6\u503C"
  );
  capturedMethod = void 0;
  capturedBody = void 0;
  await updateWarehouseProductFull("P001", {
    minOrderQuantity: 0,
    isActive: true
  });
  const fullUpdateBody = capturedBody;
  assert(fullUpdateBody, "\u5E94\u6355\u83B7\u4ED3\u5E93\u5546\u54C1\u5B8C\u6574\u66F4\u65B0\u8BF7\u6C42\u4F53");
  assert(capturedMethod === "PUT", "\u5B8C\u6574\u66F4\u65B0\u5E94\u7EE7\u7EED\u4F7F\u7528 PUT \u65B9\u6CD5");
  assert(fullUpdateBody.MinOrderQuantity === 0, "\u7F16\u8F91\u5F39\u7A97\u5B8C\u6574\u66F4\u65B0\u5E94\u53D1\u9001 MinOrderQuantity \u5E76\u4FDD\u7559\u96F6\u503C");
  assert(!("MiddlePackQuantity" in fullUpdateBody), "\u7F16\u8F91\u5F39\u7A97\u4E0D\u5E94\u518D\u628A\u4E2D\u5305\u6570\u53D1\u9001\u4E3A Product.MiddlePackageQuantity \u5BF9\u5E94\u5B57\u6BB5");
} finally {
  globalThis.fetch = originalFetch;
}
console.log("warehouseProductService.batchUpdate.test: ok");
