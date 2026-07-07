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
  pollIntervalMs = PRODUCT_HQ_SYNC_POLL_INTERVAL_MS,
  timeoutMs = PRODUCT_HQ_SYNC_TIMEOUT_MS,
  setTimeoutFn = setTimeout,
  clearTimeoutFn = clearTimeout
}) {
  let pollingTimer = null;
  let timeoutTimer = null;
  let stopped = false;
  let rejectPromise = null;
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
          if (isTerminalStatus(result.status)) {
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
function createProductHqSyncJobPoller(options) {
  return createHqSyncJobPoller(options);
}

// src/services/posProductService.ts
var API_BASE = "/api/react/v1/products";
var SYNC_API_BASE = "/api/react/v1/sync";
var activeHqProductSyncJobs = /* @__PURE__ */ new Map();
function isRecord(value) {
  return typeof value === "object" && value !== null;
}
function assertApiSuccess(response, fallbackMessage) {
  if (response.success === false || response.isSuccess === false) {
    throw new RequestError(response.message || fallbackMessage, 200, response);
  }
}
function withOperationId(data, prefix) {
  return {
    ...data,
    // 缺省时使用稳定前缀，避免时间戳破坏后端幂等。
    operationId: data?.operationId || prefix
  };
}
function pickDefinedHqProductSyncFields(raw) {
  const fields = {};
  if (raw.productsAdded !== void 0 || raw.addedCount !== void 0) {
    fields.productsAdded = Number(raw.productsAdded ?? raw.addedCount ?? 0);
  }
  if (raw.productsUpdated !== void 0 || raw.updatedCount !== void 0) {
    fields.productsUpdated = Number(raw.productsUpdated ?? raw.updatedCount ?? 0);
  }
  if (raw.productsDeleted !== void 0 || raw.productsSoftDeleted !== void 0 || raw.deletedCount !== void 0) {
    fields.productsDeleted = Number(raw.productsDeleted ?? raw.productsSoftDeleted ?? raw.deletedCount ?? 0);
  }
  if (raw.productSetCodesCreated !== void 0 || raw.productSetCodesAdded !== void 0) {
    fields.productSetCodesCreated = Number(raw.productSetCodesCreated ?? raw.productSetCodesAdded ?? 0);
  }
  if (raw.productSetCodesDeleted !== void 0 || raw.productSetCodesSoftDeleted !== void 0) {
    fields.productSetCodesDeleted = Number(raw.productSetCodesDeleted ?? raw.productSetCodesSoftDeleted ?? 0);
  }
  const fieldNames = [
    "addedCount",
    "updatedCount",
    "deletedCount",
    "totalCount",
    "errorCount",
    "totalHqProducts",
    "totalLocalProducts",
    "productsAdded",
    "productsUpdated",
    "productsDeleted",
    "productsSoftDeleted",
    "storeRetailPricesCreated",
    "storeRetailPricesDeleted",
    "productSetCodesCreated",
    "productSetCodesAdded",
    "productSetCodesUpdated",
    "productSetCodesDeleted",
    "productSetCodesSoftDeleted",
    "storeMultiCodesCreated",
    "storeMultiCodesDeleted",
    "durationMs"
  ];
  fieldNames.forEach((fieldName) => {
    if (raw[fieldName] !== void 0) {
      ;
      fields[fieldName] = raw[fieldName];
    }
  });
  return fields;
}
function normalizePushProductsToHqResult(raw) {
  const payload = raw;
  const relationCount = Number(payload.productsAdded ?? 0) + Number(payload.productsUpdated ?? 0) + Number(payload.warehouseInventoriesCreated ?? 0) + Number(payload.warehouseInventoriesUpdated ?? 0) + Number(payload.storeRetailPricesCreated ?? 0) + Number(payload.storeRetailPricesUpdated ?? 0) + Number(payload.productSetCodesCreated ?? payload.productSetCodesAdded ?? 0) + Number(payload.productSetCodesUpdated ?? 0) + Number(payload.storeMultiCodesCreated ?? 0) + Number(payload.storeMultiCodesUpdated ?? 0);
  const successCount = Number(payload.successCount ?? payload.pushedCount ?? payload.productsAdded ?? 0) + Number(payload.productsUpdated !== void 0 && payload.successCount === void 0 && payload.pushedCount === void 0 ? payload.productsUpdated : 0);
  const failedCount = Number(payload.failedCount ?? payload.errorCount ?? 0);
  const totalCount = payload.totalCount === void 0 ? successCount + failedCount : Number(payload.totalCount);
  return {
    ...raw,
    successCount,
    failedCount,
    totalCount,
    affectedRowCount: Number(payload.affectedRowCount ?? relationCount),
    errors: Array.isArray(raw.errors) ? raw.errors : []
  };
}
function normalizePushProductsToHqJobStatus(status, success, payload) {
  if (typeof status === "string") {
    switch (status.trim().toLowerCase()) {
      case "queued":
      case "pending":
        return "Queued";
      case "running":
        return "Running";
      case "succeeded":
      case "success":
      case "completed":
        return "Succeeded";
      case "failed":
      case "failure":
      case "error":
        return "Failed";
      default:
        throw new RequestError(`\u672A\u77E5\u53D1\u9001 HQ \u4EFB\u52A1\u72B6\u6001: ${status}`, 200, payload);
    }
  }
  if (success === true) {
    return "Succeeded";
  }
  if (success === false) {
    return "Failed";
  }
  return "Running";
}
function normalizePushProductsToHqJobResult(payload, fallbackJobId = "") {
  const result = unwrapApiData(payload);
  const raw = isRecord(result) ? result : {};
  const nestedResult = isRecord(raw.result) ? normalizePushProductsToHqResult(raw.result) : void 0;
  const success = typeof raw.success === "boolean" ? raw.success : nestedResult?.failedCount === 0;
  return {
    jobId: typeof raw.jobId === "string" ? raw.jobId : fallbackJobId,
    status: normalizePushProductsToHqJobStatus(raw.status, success, raw),
    operationId: typeof raw.operationId === "string" ? raw.operationId : void 0,
    result: nestedResult,
    message: typeof raw.message === "string" ? raw.message : nestedResult?.message,
    errors: Array.isArray(raw.errors) ? raw.errors.filter((item) => typeof item === "string") : []
  };
}
async function createProductWithPrices(data) {
  const response = await request_default.post(`${API_BASE}/create-with-prices`, data);
  assertApiSuccess(response, "\u521B\u5EFA\u5546\u54C1\u5931\u8D25");
  return unwrapApiData(response);
}
function normalizeSupplierImageBatchUpdateResult(raw) {
  if (!isRecord(raw)) {
    return void 0;
  }
  return {
    totalCount: Number(raw.totalCount ?? 0),
    hbwebUpdatedCount: Number(raw.hbwebUpdatedCount ?? 0),
    hqUpdatedCount: Number(raw.hqUpdatedCount ?? 0),
    hbwebSkippedExistingImageCount: Number(raw.hbwebSkippedExistingImageCount ?? 0),
    hqSkippedExistingImageCount: Number(raw.hqSkippedExistingImageCount ?? 0),
    skippedCount: Number(raw.skippedCount ?? 0),
    hqFailedCount: Number(raw.hqFailedCount ?? 0),
    errors: Array.isArray(raw.errors) ? raw.errors.filter((item) => typeof item === "string") : [],
    message: typeof raw.message === "string" ? raw.message : void 0
  };
}
function normalizeSyncProductsToStoresResult(raw) {
  if (!isRecord(raw)) {
    return void 0;
  }
  const errors = Array.isArray(raw.errors) ? raw.errors.filter((item) => typeof item === "string") : [];
  const createdCount = Number(raw.createdCount ?? raw.successCount ?? 0);
  const updatedCount = Number(raw.updatedCount ?? 0);
  const failedCount = Number(raw.failedCount ?? raw.errorCount ?? errors.length);
  return {
    createdCount,
    updatedCount,
    failedCount,
    // 兼容仍返回 successCount 的旧 payload，便于页面按真实统计展示结果。
    successCount: Number(raw.successCount ?? createdCount + updatedCount),
    errors,
    message: typeof raw.message === "string" ? raw.message : void 0
  };
}
function normalizeSyncProductsToStoresJobStatus(status, success) {
  if (typeof status === "string") {
    switch (status.trim().toLowerCase()) {
      case "queued":
      case "pending":
        return "Queued";
      case "running":
      case "processing":
      case "inprogress":
      case "in-progress":
        return "Running";
      case "succeeded":
      case "success":
      case "completed":
        return "Succeeded";
      case "failed":
      case "failure":
      case "error":
        return "Failed";
      default:
        return status.trim();
    }
  }
  if (success === true) {
    return "Succeeded";
  }
  if (success === false) {
    return "Failed";
  }
  return "Running";
}
function normalizeSyncProductsToStoresJobResult(payload, fallbackJobId = "") {
  const result = unwrapApiData(payload);
  const raw = isRecord(result) ? result : {};
  const nestedResult = normalizeSyncProductsToStoresResult(raw.result);
  const topLevelResult = normalizeSyncProductsToStoresResult(raw);
  const mergedErrors = Array.isArray(raw.errors) ? raw.errors.filter((item) => typeof item === "string") : nestedResult?.errors ?? topLevelResult?.errors ?? [];
  const normalizedResult = nestedResult ?? topLevelResult ?? {
    createdCount: 0,
    updatedCount: 0,
    failedCount: mergedErrors.length ? mergedErrors.length : 0,
    successCount: 0,
    errors: mergedErrors,
    message: typeof raw.message === "string" ? raw.message : void 0
  };
  if (!normalizedResult.errors.length && mergedErrors.length) {
    normalizedResult.errors = mergedErrors;
  }
  const success = typeof raw.success === "boolean" ? raw.success : raw.isSuccess;
  return {
    jobId: typeof raw.jobId === "string" ? raw.jobId : fallbackJobId,
    status: normalizeSyncProductsToStoresJobStatus(raw.status, success),
    operationId: typeof raw.operationId === "string" ? raw.operationId : void 0,
    result: normalizedResult,
    message: typeof raw.message === "string" ? raw.message : normalizedResult.message,
    isDuplicateRequest: typeof raw.isDuplicateRequest === "boolean" ? raw.isDuplicateRequest : void 0,
    errors: mergedErrors
  };
}
function normalizeSupplierImageBatchUpdateJobResult(payload, fallbackJobId = "") {
  const result = unwrapApiData(payload);
  const raw = isRecord(result) ? result : {};
  const nestedResult = normalizeSupplierImageBatchUpdateResult(raw.result);
  const success = typeof raw.success === "boolean" ? raw.success : void 0;
  return {
    jobId: typeof raw.jobId === "string" ? raw.jobId : fallbackJobId,
    operationId: typeof raw.operationId === "string" ? raw.operationId : void 0,
    status: normalizeHqProductSyncJobStatus(raw.status, success, raw),
    request: isRecord(raw.request) ? raw.request : void 0,
    result: nestedResult,
    message: typeof raw.message === "string" ? raw.message : void 0,
    errorMessage: typeof raw.errorMessage === "string" ? raw.errorMessage : void 0,
    errors: Array.isArray(raw.errors) ? raw.errors.filter((item) => typeof item === "string") : nestedResult?.errors ?? [],
    createdAt: typeof raw.createdAt === "string" ? raw.createdAt : void 0,
    startedAt: typeof raw.startedAt === "string" ? raw.startedAt : void 0,
    completedAt: typeof raw.completedAt === "string" ? raw.completedAt : void 0
  };
}
function buildSupplierImageBatchUpdateOperationId(data) {
  const targets = [
    data.updateHbweb ? "hbweb" : "",
    data.updateHq ? "hq" : "",
    data.saveSupplierImageBaseUrl ? "save-url" : ""
  ].filter(Boolean).join("+") || "none";
  const productScope = data.productCodes?.length ? `selected:${data.productCodes.join(",")}` : "supplier-all";
  return `supplier-image:${data.localSupplierCode}:${targets}:${productScope}:${data.urlTemplate}`;
}
async function createSupplierImageBatchUpdateJob(data) {
  const response = await request_default.post(
    `${API_BASE}/batch-update-supplier-images/job`,
    withOperationId(data, buildSupplierImageBatchUpdateOperationId(data))
  );
  assertApiSuccess(response, "\u521B\u5EFA\u4F9B\u5E94\u5546\u56FE\u7247\u6279\u91CF\u4FEE\u6539\u4EFB\u52A1\u5931\u8D25");
  return normalizeSupplierImageBatchUpdateJobResult(response);
}
async function getSupplierImageBatchUpdateJob(jobId) {
  const response = await request_default.get(
    `${API_BASE}/batch-update-supplier-images/job/${encodeURIComponent(jobId)}`
  );
  assertApiSuccess(response, "\u67E5\u8BE2\u4F9B\u5E94\u5546\u56FE\u7247\u6279\u91CF\u4FEE\u6539\u4EFB\u52A1\u5931\u8D25");
  return normalizeSupplierImageBatchUpdateJobResult(response, jobId);
}
async function startSyncProductsToStoresJob(syncRequest) {
  const response = await request_default.post(
    `${API_BASE}/sync-to-stores/jobs`,
    syncRequest
  );
  assertApiSuccess(response, "\u521B\u5EFA\u540C\u6B65\u5230\u5206\u5E97\u4EFB\u52A1\u5931\u8D25");
  return normalizeSyncProductsToStoresJobResult(response);
}
async function getSyncProductsToStoresJob(jobId) {
  const response = await request_default.get(
    `${API_BASE}/sync-to-stores/jobs/${encodeURIComponent(jobId)}`
  );
  assertApiSuccess(response, "\u67E5\u8BE2\u540C\u6B65\u5230\u5206\u5E97\u4EFB\u52A1\u5931\u8D25");
  return normalizeSyncProductsToStoresJobResult(response, jobId);
}
async function batchUpdateProductStoreRecords(productCode, data) {
  const response = await request_default.post(
    // 商品编码可能包含空格或斜杠，这里必须编码后再拼路径，避免路由误拆。
    `${API_BASE}/${encodeURIComponent(productCode)}/store-records/batch-update`,
    data
  );
  return unwrapApiData(response);
}
async function pushProductsToHq(data) {
  const response = await request_default.post(
    `${API_BASE}/push-to-hq`,
    data
  );
  assertApiSuccess(response, "\u53D1\u9001\u5546\u54C1\u5230 HQ \u5931\u8D25");
  return normalizePushProductsToHqResult(unwrapApiData(response));
}
async function createPushProductsToHqJob(data) {
  const response = await request_default.post(
    `${API_BASE}/push-to-hq/jobs`,
    data
  );
  assertApiSuccess(response, "\u521B\u5EFA\u53D1\u9001\u5546\u54C1\u5230 HQ \u4EFB\u52A1\u5931\u8D25");
  return normalizePushProductsToHqJobResult(response);
}
async function getPushProductsToHqJob(jobId) {
  const response = await request_default.get(
    `${API_BASE}/push-to-hq/jobs/${encodeURIComponent(jobId)}`
  );
  assertApiSuccess(response, "\u67E5\u8BE2\u53D1\u9001\u5546\u54C1\u5230 HQ \u4EFB\u52A1\u5931\u8D25");
  return normalizePushProductsToHqJobResult(response, jobId);
}
function normalizeHqProductSyncResult(raw) {
  const productsDeleted = raw.productsDeleted ?? raw.productsSoftDeleted ?? raw.deletedCount ?? 0;
  const productSetCodesDeleted = raw.productSetCodesDeleted ?? raw.productSetCodesSoftDeleted ?? 0;
  const productSetCodesCreated = raw.productSetCodesCreated ?? raw.productSetCodesAdded ?? 0;
  return {
    ...raw,
    productsAdded: raw.productsAdded ?? raw.addedCount ?? 0,
    productsUpdated: raw.productsUpdated ?? raw.updatedCount ?? 0,
    productsDeleted,
    productSetCodesCreated,
    productSetCodesDeleted,
    errors: raw.errors ?? [],
    durationMs: raw.durationMs ?? 0
  };
}
function normalizeHqProductSyncJobStatus(status, success, payload) {
  if (typeof status === "string") {
    switch (status.trim().toLowerCase()) {
      case "queued":
      case "pending":
        return "Queued";
      case "running":
        return "Running";
      case "succeeded":
      case "success":
      case "completed":
        return "Succeeded";
      case "failed":
      case "failure":
      case "error":
        return "Failed";
      default:
        throw new RequestError(`\u672A\u77E5\u540C\u6B65\u4EFB\u52A1\u72B6\u6001: ${status}`, 200, payload);
    }
  }
  if (success === true) {
    return "Succeeded";
  }
  if (success === false) {
    return "Failed";
  }
  return "Running";
}
function normalizeHqProductSyncJobResult(payload, fallbackJobId = "") {
  const result = unwrapApiData(payload);
  const raw = isRecord(result) ? result : {};
  const nestedResult = isRecord(raw.result) ? raw.result : {};
  const success = typeof raw.success === "boolean" ? raw.success : nestedResult.success;
  const normalizedRawResult = pickDefinedHqProductSyncFields(raw);
  const normalizedNestedResult = isRecord(raw.result) ? normalizeHqProductSyncResult(nestedResult) : void 0;
  return {
    ...normalizedRawResult,
    jobId: typeof raw.jobId === "string" ? raw.jobId : fallbackJobId,
    status: normalizeHqProductSyncJobStatus(raw.status, success, raw),
    mode: raw.mode === "Full" || raw.mode === "Incremental" ? raw.mode : nestedResult.mode === "Full" || nestedResult.mode === "Incremental" ? nestedResult.mode : void 0,
    operationId: typeof raw.operationId === "string" ? raw.operationId : void 0,
    success: typeof success === "boolean" ? success : void 0,
    startDate: typeof raw.startDate === "string" ? raw.startDate : void 0,
    result: normalizedNestedResult,
    message: typeof raw.message === "string" ? raw.message : typeof nestedResult.message === "string" ? nestedResult.message : void 0,
    errors: Array.isArray(raw.errors) ? raw.errors.filter((item) => typeof item === "string") : []
  };
}
function buildHqProductSyncResult(job) {
  if (job.result) {
    return normalizeHqProductSyncResult(job.result);
  }
  return normalizeHqProductSyncResult(job);
}
function resolveHqProductSyncJobResult(job) {
  if (job.status === "Succeeded") {
    return buildHqProductSyncResult(job);
  }
  if (job.status === "Failed") {
    throw new RequestError(job.message || "\u5546\u54C1\u540C\u6B65\u4EFB\u52A1\u6267\u884C\u5931\u8D25", 200, job);
  }
  return buildHqProductSyncResult(job);
}
function normalizeUnknownStatusError(error) {
  if (error instanceof RequestError && error.message.startsWith("\u672A\u77E5\u540C\u6B65\u4EFB\u52A1\u72B6\u6001")) {
    const status = error.message.split(":").slice(1).join(":").trim();
    throw new RequestError(`\u672A\u77E5\u5546\u54C1\u540C\u6B65\u4EFB\u52A1\u72B6\u6001\uFF1A${status}`, error.status, error.payload);
  }
  throw error;
}
function buildProductHqSyncOperationId(mode, startDate) {
  return `product-hq-sync:${mode}:${startDate || "all"}`;
}
async function syncProductsFromHqFull(options) {
  const operationId = buildProductHqSyncOperationId("full");
  const activeJob = activeHqProductSyncJobs.get(operationId);
  if (activeJob) {
    return activeJob;
  }
  const jobPromise = (async () => {
    try {
      const job = await createProductHqSyncFullJob({ operationId });
      if (job.status === "Queued" || job.status === "Running") {
        const poller = createProductHqSyncJobPoller({
          jobId: job.jobId,
          getJob: getProductHqSyncJob,
          ...options
        });
        return resolveHqProductSyncJobResult(await poller.promise);
      }
      return resolveHqProductSyncJobResult(job);
    } catch (error) {
      normalizeUnknownStatusError(error);
    } finally {
      activeHqProductSyncJobs.delete(operationId);
    }
  })();
  activeHqProductSyncJobs.set(operationId, jobPromise);
  return jobPromise;
}
async function createProductHqSyncFullJob(data) {
  const response = await request_default.post(
    `${SYNC_API_BASE}/products/jobs`,
    withOperationId(data, buildProductHqSyncOperationId("full"))
  );
  assertApiSuccess(response, "\u521B\u5EFA\u5546\u54C1 HQ \u5168\u91CF\u540C\u6B65\u4EFB\u52A1\u5931\u8D25");
  return normalizeHqProductSyncJobResult(response);
}
async function createProductHqSyncIncrementalJob(data) {
  const response = await request_default.post(
    `${SYNC_API_BASE}/products-incremental/jobs`,
    withOperationId(data, buildProductHqSyncOperationId("incremental", data.startDate))
  );
  assertApiSuccess(response, "\u521B\u5EFA\u5546\u54C1 HQ \u589E\u91CF\u540C\u6B65\u4EFB\u52A1\u5931\u8D25");
  return normalizeHqProductSyncJobResult(response);
}
async function getProductHqSyncJob(jobId) {
  const response = await request_default.get(`${SYNC_API_BASE}/products/jobs/${encodeURIComponent(jobId)}`);
  assertApiSuccess(response, "\u67E5\u8BE2\u5546\u54C1 HQ \u540C\u6B65\u4EFB\u52A1\u5931\u8D25");
  return normalizeHqProductSyncJobResult(response, jobId);
}
var createHqProductFullSyncJob = createProductHqSyncFullJob;
var createHqProductIncrementalSyncJob = createProductHqSyncIncrementalJob;
var getHqProductSyncJob = getProductHqSyncJob;

// src/services/posProductService.test.ts
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
function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}
async function assertRequestError(execute, expectedMessage, expectedPayload, label) {
  try {
    await execute();
  } catch (error) {
    assert(error instanceof RequestError, `${label} \u5E94\u629B\u51FA RequestError`);
    assertEqual(error.message, expectedMessage, `${label} \u5E94\u4FDD\u7559\u540E\u7AEF\u9519\u8BEF\u6D88\u606F`);
    assertEqual(error.status, 200, `${label} \u4E1A\u52A1\u5931\u8D25\u5E94\u4FDD\u7559 HTTP 200 \u72B6\u6001`);
    assertDeepEqual(error.payload, expectedPayload, `${label} \u5E94\u4FDD\u7559\u5B8C\u6574 payload`);
    return;
  }
  throw new Error(`${label} \u5E94\u62D2\u7EDD Promise`);
}
var originalFetch = globalThis.fetch;
var capturedUrl = "";
var capturedInit;
var nextPayload = {};
globalThis.fetch = async (input, init) => {
  capturedUrl = String(input);
  capturedInit = init;
  return new Response(JSON.stringify(nextPayload), {
    status: 200,
    headers: { "Content-Type": "application/json" }
  });
};
try {
  nextPayload = {
    success: true,
    data: {
      jobId: "job-full-1",
      status: "queued",
      mode: "Full"
    }
  };
  const fullJob = await createHqProductFullSyncJob({ operationId: "op-full-1" });
  assertEqual(capturedUrl, "/api/react/v1/sync/products/jobs", "\u5168\u91CF\u5546\u54C1 HQ job \u5E94\u8C03\u7528\u540E\u53F0\u4EFB\u52A1\u63A5\u53E3");
  assertEqual(capturedInit?.method, "POST", "\u5168\u91CF\u5546\u54C1 HQ job \u5E94\u4F7F\u7528 POST");
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    { operationId: "op-full-1" },
    "\u5168\u91CF\u5546\u54C1 HQ job \u8BF7\u6C42\u5E94\u643A\u5E26 operationId"
  );
  assertEqual(fullJob.status, "Queued", "queued \u5E94\u5F52\u4E00\u4E3A Queued");
  nextPayload = {
    success: true,
    data: {
      jobId: "job-incremental-1",
      status: "running",
      mode: "Incremental"
    }
  };
  const incrementalJob = await createHqProductIncrementalSyncJob({
    operationId: "op-incremental-1",
    startDate: "2026-05-01"
  });
  assertEqual(
    capturedUrl,
    "/api/react/v1/sync/products-incremental/jobs",
    "\u589E\u91CF\u5546\u54C1 HQ job \u5E94\u8C03\u7528\u540E\u53F0\u4EFB\u52A1\u63A5\u53E3"
  );
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    { operationId: "op-incremental-1", startDate: "2026-05-01" },
    "\u589E\u91CF\u5546\u54C1 HQ job \u8BF7\u6C42\u5E94\u643A\u5E26 operationId \u548C startDate"
  );
  assertEqual(incrementalJob.status, "Running", "running \u5E94\u5F52\u4E00\u4E3A Running");
  nextPayload = {
    success: true,
    data: {
      jobId: "job-success-1",
      success: true,
      result: {
        productsAdded: 1,
        productsUpdated: 2
      }
    }
  };
  const succeededJob = await getHqProductSyncJob("job-success-1");
  assertEqual(
    capturedUrl,
    "/api/react/v1/sync/products/jobs/job-success-1",
    "\u67E5\u8BE2\u5546\u54C1 HQ job \u5E94\u8C03\u7528 job \u67E5\u8BE2\u63A5\u53E3"
  );
  assertEqual(succeededJob.status, "Succeeded", "success:true \u5E94\u5F52\u4E00\u4E3A Succeeded");
  assertEqual(succeededJob.result?.productsAdded, 1, "\u67E5\u8BE2\u5546\u54C1 HQ job \u5E94\u4FDD\u7559 result \u4E2D\u7684\u540C\u6B65\u8BA1\u6570");
  assertEqual(succeededJob.result?.productsUpdated, 2, "\u67E5\u8BE2\u5546\u54C1 HQ job \u5E94\u4FDD\u7559 result \u4E2D\u7684\u66F4\u65B0\u8BA1\u6570");
  nextPayload = {
    success: true,
    data: {
      jobId: "job-top-level-counts",
      status: "Succeeded",
      addedCount: 3,
      updatedCount: 4,
      deletedCount: 5
    }
  };
  const topLevelCountsJob = await getHqProductSyncJob("job-top-level-counts");
  assertEqual(topLevelCountsJob.productsAdded, 3, "\u67E5\u8BE2\u5546\u54C1 HQ job \u5E94\u628A\u9876\u5C42 addedCount \u5F52\u4E00\u4E3A productsAdded");
  assertEqual(topLevelCountsJob.productsUpdated, 4, "\u67E5\u8BE2\u5546\u54C1 HQ job \u5E94\u628A\u9876\u5C42 updatedCount \u5F52\u4E00\u4E3A productsUpdated");
  assertEqual(topLevelCountsJob.productsDeleted, 5, "\u67E5\u8BE2\u5546\u54C1 HQ job \u5E94\u628A\u9876\u5C42 deletedCount \u5F52\u4E00\u4E3A productsDeleted");
  nextPayload = {
    success: true,
    data: {
      jobId: "job-failed-1",
      success: false,
      message: "\u540C\u6B65\u5931\u8D25"
    }
  };
  const failedJob = await getHqProductSyncJob("job-failed-1");
  assertEqual(failedJob.status, "Failed", "success:false \u5E94\u5F52\u4E00\u4E3A Failed");
  const unknownStatusPayload = {
    success: true,
    data: {
      jobId: "job-unknown-1",
      status: "paused"
    }
  };
  nextPayload = unknownStatusPayload;
  await assertRequestError(
    () => getHqProductSyncJob("job-unknown-1"),
    "\u672A\u77E5\u540C\u6B65\u4EFB\u52A1\u72B6\u6001: paused",
    unknownStatusPayload.data,
    "\u672A\u77E5 job status"
  );
  const fullSyncFailurePayload = {
    success: false,
    message: "HQ \u5546\u54C1\u540C\u6B65\u5931\u8D25",
    data: {
      productsAdded: 0,
      errors: ["\u540E\u7AEF\u4E1A\u52A1\u5931\u8D25"]
    }
  };
  nextPayload = fullSyncFailurePayload;
  await assertRequestError(
    () => syncProductsFromHqFull(),
    "HQ \u5546\u54C1\u540C\u6B65\u5931\u8D25",
    fullSyncFailurePayload,
    "\u540C\u6B65\u63A5\u53E3 success:false"
  );
  nextPayload = {
    success: true,
    data: {
      successCount: 2,
      failedCount: 0,
      totalCount: 2,
      productsAdded: 1,
      productsUpdated: 2,
      warehouseInventoriesCreated: 9,
      warehouseInventoriesUpdated: 10,
      storeRetailPricesCreated: 3,
      storeRetailPricesUpdated: 4,
      productSetCodesCreated: 5,
      productSetCodesUpdated: 6,
      storeMultiCodesCreated: 7,
      storeMultiCodesUpdated: 8,
      errors: []
    }
  };
  const pushResult = await pushProductsToHq({
    productCodes: ["HB001", "HB002"],
    items: [
      {
        productCode: "HB001",
        localSupplierCode: "DATS",
        itemNumber: "72653",
        domesticPrice: 3.8,
        importPrice: 1.21,
        oemPrice: 1.45,
        isNewProduct: false,
        warehouseIsActive: true
      },
      {
        localSupplierCode: "DATS",
        itemNumber: "72654",
        domesticPrice: 4.2,
        importPrice: 1.33,
        oemPrice: 1.58,
        isNewProduct: false,
        warehouseIsActive: false
      }
    ]
  });
  assertEqual(capturedUrl, "/api/react/v1/products/push-to-hq", "\u9009\u4E2D\u5546\u54C1\u53D1\u9001 HQ \u5E94\u8C03\u7528\u56FA\u5B9A\u63A5\u53E3");
  assertEqual(capturedInit?.method, "POST", "\u9009\u4E2D\u5546\u54C1\u53D1\u9001 HQ \u5E94\u4F7F\u7528 POST");
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    {
      productCodes: ["HB001", "HB002"],
      items: [
        {
          productCode: "HB001",
          localSupplierCode: "DATS",
          itemNumber: "72653",
          domesticPrice: 3.8,
          importPrice: 1.21,
          oemPrice: 1.45,
          isNewProduct: false,
          warehouseIsActive: true
        },
        {
          localSupplierCode: "DATS",
          itemNumber: "72654",
          domesticPrice: 4.2,
          importPrice: 1.33,
          oemPrice: 1.58,
          isNewProduct: false,
          warehouseIsActive: false
        }
      ]
    },
    "\u9009\u4E2D\u5546\u54C1\u53D1\u9001 HQ \u8BF7\u6C42\u5E94\u517C\u5BB9\u65E7 productCodes\uFF0C\u5E76\u643A\u5E26 items \u4E0E\u4EF7\u683C\u5B57\u6BB5"
  );
  assertEqual(pushResult.successCount, 2, "\u53D1\u9001 HQ \u5E94\u4F7F\u7528\u540E\u7AEF\u8FD4\u56DE\u7684\u5546\u54C1\u6210\u529F\u6570");
  assertEqual(pushResult.failedCount, 0, "\u53D1\u9001 HQ \u65E0\u9519\u8BEF\u660E\u7EC6\u65F6\u5931\u8D25\u6570\u5E94\u4E3A 0");
  assertEqual(pushResult.totalCount, 2, "\u53D1\u9001 HQ \u5E94\u4F7F\u7528\u540E\u7AEF\u8FD4\u56DE\u7684\u5546\u54C1\u5408\u8BA1\u6570");
  assertEqual(pushResult.affectedRowCount, 55, "\u53D1\u9001 HQ \u7F3A\u5C11\u540E\u7AEF\u6C47\u603B\u65F6\u5E94\u628A\u5E93\u5B58\u3001\u5206\u5E97\u4EF7\u683C\u548C\u591A\u7801\u7EDF\u8BA1\u5408\u5E76\u4E3A\u5F71\u54CD\u8BB0\u5F55\u6570");
  assertEqual(pushResult.warehouseInventoriesCreated, 9, "\u53D1\u9001 HQ \u5E94\u4FDD\u7559\u4ED3\u5E93\u5E93\u5B58\u65B0\u589E\u7EDF\u8BA1");
  assertEqual(pushResult.warehouseInventoriesUpdated, 10, "\u53D1\u9001 HQ \u5E94\u4FDD\u7559\u4ED3\u5E93\u5E93\u5B58\u66F4\u65B0\u7EDF\u8BA1");
  nextPayload = {
    success: true,
    data: {
      jobId: "push-hq-job-1",
      operationId: "container-push-hq:container-1:HB001",
      status: "queued",
      message: "\u4EFB\u52A1\u5DF2\u63D0\u4EA4"
    }
  };
  const pushJob = await createPushProductsToHqJob({
    operationId: "container-push-hq:container-1:HB001",
    productCodes: ["HB001"],
    items: [
      {
        productCode: "HB001",
        localSupplierCode: "DATS",
        itemNumber: "72653",
        domesticPrice: 3.8,
        importPrice: 1.21,
        oemPrice: 1.45,
        isNewProduct: false,
        warehouseIsActive: true
      }
    ]
  });
  assertEqual(capturedUrl, "/api/react/v1/products/push-to-hq/jobs", "\u53D1\u9001 HQ job \u5E94\u8C03\u7528\u540E\u53F0\u4EFB\u52A1\u521B\u5EFA\u63A5\u53E3");
  assertEqual(capturedInit?.method, "POST", "\u53D1\u9001 HQ job \u5E94\u4F7F\u7528 POST");
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    {
      operationId: "container-push-hq:container-1:HB001",
      productCodes: ["HB001"],
      items: [
        {
          productCode: "HB001",
          localSupplierCode: "DATS",
          itemNumber: "72653",
          domesticPrice: 3.8,
          importPrice: 1.21,
          oemPrice: 1.45,
          isNewProduct: false,
          warehouseIsActive: true
        }
      ]
    },
    "\u53D1\u9001 HQ job \u8BF7\u6C42\u5E94\u4FDD\u7559 operationId\u3001productCodes \u548C\u5019\u9009 items"
  );
  assertEqual(pushJob.status, "Queued", "\u53D1\u9001 HQ job queued \u5E94\u5F52\u4E00\u4E3A Queued");
  nextPayload = {
    success: true,
    data: {
      jobId: "push-hq-job-1",
      status: "completed",
      result: {
        successCount: 1,
        failedCount: 1,
        totalCount: 2,
        productsAdded: 1,
        productsUpdated: 2,
        warehouseInventoriesCreated: 3,
        warehouseInventoriesUpdated: 4,
        storeRetailPricesCreated: 5,
        storeRetailPricesUpdated: 6,
        productSetCodesCreated: 7,
        productSetCodesUpdated: 8,
        storeMultiCodesCreated: 9,
        storeMultiCodesUpdated: 10,
        errors: ["HB002 \u5199\u5165\u5931\u8D25"]
      },
      errors: ["\u540E\u53F0\u4EFB\u52A1\u5B58\u5728\u9519\u8BEF"]
    }
  };
  const completedPushJob = await getPushProductsToHqJob("push-hq-job-1");
  assertEqual(
    capturedUrl,
    "/api/react/v1/products/push-to-hq/jobs/push-hq-job-1",
    "\u67E5\u8BE2\u53D1\u9001 HQ job \u5E94\u8C03\u7528\u4EFB\u52A1\u67E5\u8BE2\u63A5\u53E3"
  );
  assertEqual(completedPushJob.status, "Succeeded", "completed \u5E94\u5F52\u4E00\u4E3A Succeeded");
  assertEqual(completedPushJob.result?.productsAdded, 1, "\u53D1\u9001 HQ job \u5E94\u4FDD\u7559\u5546\u54C1\u65B0\u589E\u7EDF\u8BA1");
  assertEqual(completedPushJob.result?.warehouseInventoriesCreated, 3, "\u53D1\u9001 HQ job \u5E94\u4FDD\u7559\u5E93\u5B58\u65B0\u589E\u7EDF\u8BA1");
  assertEqual(completedPushJob.result?.storeRetailPricesUpdated, 6, "\u53D1\u9001 HQ job \u5E94\u4FDD\u7559\u96F6\u552E\u4EF7\u66F4\u65B0\u7EDF\u8BA1");
  assertEqual(completedPushJob.result?.productSetCodesCreated, 7, "\u53D1\u9001 HQ job \u5E94\u4FDD\u7559\u5957\u88C5\u7F16\u7801\u65B0\u589E\u7EDF\u8BA1");
  assertEqual(completedPushJob.result?.storeMultiCodesUpdated, 10, "\u53D1\u9001 HQ job \u5E94\u4FDD\u7559\u591A\u7801\u66F4\u65B0\u7EDF\u8BA1");
  assertDeepEqual(completedPushJob.result?.errors, ["HB002 \u5199\u5165\u5931\u8D25"], "\u53D1\u9001 HQ job \u5E94\u4FDD\u7559 result \u9519\u8BEF\u660E\u7EC6");
  assertDeepEqual(completedPushJob.errors, ["\u540E\u53F0\u4EFB\u52A1\u5B58\u5728\u9519\u8BEF"], "\u53D1\u9001 HQ job \u5E94\u4FDD\u7559\u9876\u5C42\u9519\u8BEF\u6458\u8981");
  nextPayload = {
    success: true,
    data: {
      jobId: "supplier-image-job-1",
      operationId: "supplier-image:DATS",
      status: "queued",
      request: {
        localSupplierCode: "DATS"
      }
    }
  };
  const imageJob = await createSupplierImageBatchUpdateJob({
    localSupplierCode: "DATS",
    urlTemplate: "https://www.dats.com.au/images/ProductImages/500/{itemNumber}.jpg",
    updateHbweb: true,
    updateHq: false,
    saveSupplierImageBaseUrl: false,
    productCodes: ["P001", "P002"],
    operationId: "supplier-image:DATS"
  });
  assertEqual(
    capturedUrl,
    "/api/react/v1/products/batch-update-supplier-images/job",
    "\u4F9B\u5E94\u5546\u56FE\u7247\u6279\u91CF\u4FEE\u6539 job \u5E94\u8C03\u7528\u540E\u53F0\u4EFB\u52A1\u521B\u5EFA\u63A5\u53E3"
  );
  assertEqual(capturedInit?.method, "POST", "\u4F9B\u5E94\u5546\u56FE\u7247\u6279\u91CF\u4FEE\u6539 job \u5E94\u4F7F\u7528 POST");
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    {
      localSupplierCode: "DATS",
      urlTemplate: "https://www.dats.com.au/images/ProductImages/500/{itemNumber}.jpg",
      updateHbweb: true,
      updateHq: false,
      saveSupplierImageBaseUrl: false,
      productCodes: ["P001", "P002"],
      operationId: "supplier-image:DATS"
    },
    "\u4F9B\u5E94\u5546\u56FE\u7247\u6279\u91CF\u4FEE\u6539 job \u8BF7\u6C42\u5E94\u4FDD\u7559\u6A21\u677F\u3001\u76EE\u6807\u5E93\u3001\u9009\u62E9\u5546\u54C1\u3001\u4FDD\u5B58\u6807\u8BB0\u548C operationId"
  );
  assertEqual(imageJob.status, "Queued", "\u4F9B\u5E94\u5546\u56FE\u7247\u6279\u91CF\u4FEE\u6539 job queued \u5E94\u5F52\u4E00\u4E3A Queued");
  nextPayload = {
    success: true,
    data: {
      jobId: "supplier-image-job-1",
      status: "succeeded",
      result: {
        totalCount: 12,
        hbwebUpdatedCount: 12,
        hqUpdatedCount: 0,
        hbwebSkippedExistingImageCount: 3,
        hqSkippedExistingImageCount: 4,
        skippedCount: 0,
        hqFailedCount: 0,
        errors: []
      }
    }
  };
  const completedImageJob = await getSupplierImageBatchUpdateJob("supplier-image-job-1");
  assertEqual(
    capturedUrl,
    "/api/react/v1/products/batch-update-supplier-images/job/supplier-image-job-1",
    "\u67E5\u8BE2\u4F9B\u5E94\u5546\u56FE\u7247\u6279\u91CF\u4FEE\u6539 job \u5E94\u8C03\u7528\u4EFB\u52A1\u67E5\u8BE2\u63A5\u53E3"
  );
  assertEqual(completedImageJob.status, "Succeeded", "\u4F9B\u5E94\u5546\u56FE\u7247\u6279\u91CF\u4FEE\u6539 job succeeded \u5E94\u5F52\u4E00\u4E3A Succeeded");
  assertEqual(completedImageJob.result?.hbwebUpdatedCount, 12, "\u4F9B\u5E94\u5546\u56FE\u7247\u6279\u91CF\u4FEE\u6539 job \u5E94\u4FDD\u7559\u7ED3\u679C\u7EDF\u8BA1");
  assertEqual(completedImageJob.result?.hbwebSkippedExistingImageCount, 3, "\u4F9B\u5E94\u5546\u56FE\u7247\u6279\u91CF\u4FEE\u6539 job \u5E94\u4FDD\u7559 Hbweb \u5DF2\u6709\u56FE\u7247\u8DF3\u8FC7\u6570\u91CF");
  assertEqual(completedImageJob.result?.hqSkippedExistingImageCount, 4, "\u4F9B\u5E94\u5546\u56FE\u7247\u6279\u91CF\u4FEE\u6539 job \u5E94\u4FDD\u7559 HQ \u5DF2\u6709\u56FE\u7247\u8DF3\u8FC7\u6570\u91CF");
  nextPayload = {
    success: true,
    data: {
      jobId: "sync-store-job-1",
      operationId: "sync-store:HB001:S001",
      status: "pending",
      isDuplicateRequest: true,
      message: "\u4EFB\u52A1\u5DF2\u5B58\u5728\uFF0C\u7EE7\u7EED\u590D\u7528\u540E\u53F0\u6267\u884C"
    }
  };
  const syncToStoresJob = await startSyncProductsToStoresJob({
    productCodes: ["HB001"],
    storeCodes: ["S001"],
    overwrite: false,
    fields: ["purchasePrice", "retailPrice"]
  });
  assertEqual(
    capturedUrl,
    "/api/react/v1/products/sync-to-stores/jobs",
    "\u540C\u6B65\u5230\u5206\u5E97 job \u5E94\u8C03\u7528\u540E\u53F0\u4EFB\u52A1\u521B\u5EFA\u63A5\u53E3"
  );
  assertEqual(capturedInit?.method, "POST", "\u540C\u6B65\u5230\u5206\u5E97 job \u5E94\u4F7F\u7528 POST");
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    {
      productCodes: ["HB001"],
      storeCodes: ["S001"],
      overwrite: false,
      fields: ["purchasePrice", "retailPrice"]
    },
    "\u540C\u6B65\u5230\u5206\u5E97 job \u8BF7\u6C42\u5E94\u4FDD\u7559\u5546\u54C1\u3001\u5206\u5E97\u3001\u8986\u76D6\u5F00\u5173\u548C\u5B57\u6BB5\u5217\u8868"
  );
  assertEqual(syncToStoresJob.status, "Queued", "pending \u5E94\u5F52\u4E00\u4E3A Queued");
  assertEqual(syncToStoresJob.isDuplicateRequest, true, "\u540C\u6B65\u5230\u5206\u5E97 job \u5E94\u4FDD\u7559\u91CD\u590D\u63D0\u4EA4\u6807\u8BB0");
  nextPayload = {
    success: true,
    data: {
      jobId: "sync-store-job-1",
      operationId: "sync-store:HB001:S001",
      status: "completed",
      message: "\u540C\u6B65\u5B8C\u6210",
      result: {
        createdCount: 2,
        updatedCount: 3,
        failedCount: 1,
        errors: ["S003 \u540C\u6B65\u5931\u8D25"]
      }
    }
  };
  const completedSyncToStoresJob = await getSyncProductsToStoresJob("sync-store-job-1");
  assertEqual(
    capturedUrl,
    "/api/react/v1/products/sync-to-stores/jobs/sync-store-job-1",
    "\u67E5\u8BE2\u540C\u6B65\u5230\u5206\u5E97 job \u5E94\u8C03\u7528\u4EFB\u52A1\u67E5\u8BE2\u63A5\u53E3"
  );
  assertEqual(completedSyncToStoresJob.status, "Succeeded", "completed \u5E94\u5F52\u4E00\u4E3A Succeeded");
  assertEqual(completedSyncToStoresJob.result?.createdCount, 2, "\u540C\u6B65\u5230\u5206\u5E97 job \u5E94\u4FDD\u7559\u521B\u5EFA\u6570\u91CF");
  assertEqual(completedSyncToStoresJob.result?.updatedCount, 3, "\u540C\u6B65\u5230\u5206\u5E97 job \u5E94\u4FDD\u7559\u66F4\u65B0\u6570\u91CF");
  assertEqual(completedSyncToStoresJob.result?.failedCount, 1, "\u540C\u6B65\u5230\u5206\u5E97 job \u5E94\u4FDD\u7559\u5931\u8D25\u6570\u91CF");
  assertDeepEqual(completedSyncToStoresJob.result?.errors, ["S003 \u540C\u6B65\u5931\u8D25"], "\u540C\u6B65\u5230\u5206\u5E97 job \u5E94\u4FDD\u7559\u9519\u8BEF\u660E\u7EC6");
  nextPayload = {
    success: true,
    data: {
      jobId: "sync-store-job-failed-1",
      operationId: "sync-store:HB001:S001",
      status: "failed",
      message: "\u540C\u6B65\u5230\u5206\u5E97\u4EFB\u52A1\u5931\u8D25",
      result: {
        createdCount: 0,
        updatedCount: 0,
        failedCount: 2,
        errors: ["S001 \u5199\u5165\u5931\u8D25", "S002 \u5199\u5165\u5931\u8D25"],
        message: "\u5168\u90E8\u5206\u5E97\u5199\u5165\u5931\u8D25"
      },
      errors: ["\u540E\u7AEF\u4EFB\u52A1\u6267\u884C\u5931\u8D25"]
    }
  };
  const failedSyncToStoresJob = await getSyncProductsToStoresJob("sync-store-job-failed-1");
  assertEqual(failedSyncToStoresJob.status, "Failed", "\u540C\u6B65\u5230\u5206\u5E97 job failed payload \u5E94\u5F52\u4E00\u4E3A Failed");
  assertEqual(failedSyncToStoresJob.message, "\u540C\u6B65\u5230\u5206\u5E97\u4EFB\u52A1\u5931\u8D25", "\u540C\u6B65\u5230\u5206\u5E97 job Failed payload \u5E94\u4FDD\u7559\u9876\u5C42 message");
  assertEqual(failedSyncToStoresJob.result?.message, "\u5168\u90E8\u5206\u5E97\u5199\u5165\u5931\u8D25", "\u540C\u6B65\u5230\u5206\u5E97 job Failed payload \u5E94\u4FDD\u7559 result.message");
  assertEqual(failedSyncToStoresJob.result?.failedCount, 2, "\u540C\u6B65\u5230\u5206\u5E97 job Failed payload \u5E94\u4FDD\u7559 result.failedCount");
  assertDeepEqual(
    failedSyncToStoresJob.result?.errors,
    ["S001 \u5199\u5165\u5931\u8D25", "S002 \u5199\u5165\u5931\u8D25"],
    "\u540C\u6B65\u5230\u5206\u5E97 job Failed payload \u5E94\u4FDD\u7559 result.errors"
  );
  assertDeepEqual(
    failedSyncToStoresJob.errors,
    ["\u540E\u7AEF\u4EFB\u52A1\u6267\u884C\u5931\u8D25"],
    "\u540C\u6B65\u5230\u5206\u5E97 job Failed payload \u5E94\u4FDD\u7559\u9876\u5C42 errors"
  );
  nextPayload = {
    success: true,
    data: {
      successCount: 2,
      failedCount: 1,
      errors: ["S003 \u66F4\u65B0\u5931\u8D25"]
    }
  };
  const batchStoreRecordResult = await batchUpdateProductStoreRecords("HB 001/\u6D4B\u8BD5", {
    storeCodes: ["S001", "S002"],
    changes: {
      purchasePrice: 10.5,
      storeRetailPriceValue: 19.9,
      discountRate: 0.88,
      isAutoPricing: true,
      isSpecialProduct: false,
      isActive: true
    }
  });
  assertEqual(
    capturedUrl,
    "/api/react/v1/products/HB%20001%2F%E6%B5%8B%E8%AF%95/store-records/batch-update",
    "\u5206\u5E97\u8BB0\u5F55\u6279\u91CF\u4FEE\u6539\u5E94\u5BF9 productCode \u505A encode \u540E\u518D\u62FC\u63A5\u8DEF\u5F84"
  );
  assertEqual(capturedInit?.method, "POST", "\u5206\u5E97\u8BB0\u5F55\u6279\u91CF\u4FEE\u6539\u5E94\u4F7F\u7528 POST");
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    {
      storeCodes: ["S001", "S002"],
      changes: {
        purchasePrice: 10.5,
        storeRetailPriceValue: 19.9,
        discountRate: 0.88,
        isAutoPricing: true,
        isSpecialProduct: false,
        isActive: true
      }
    },
    "\u5206\u5E97\u8BB0\u5F55\u6279\u91CF\u4FEE\u6539\u8BF7\u6C42\u4F53\u53EA\u5E94\u5305\u542B storeCodes \u548C changes"
  );
  assertDeepEqual(
    batchStoreRecordResult,
    {
      successCount: 2,
      failedCount: 1,
      errors: ["S003 \u66F4\u65B0\u5931\u8D25"]
    },
    "\u5206\u5E97\u8BB0\u5F55\u6279\u91CF\u4FEE\u6539\u5E94\u8FD4\u56DE unwrap \u540E\u7684\u7EDF\u8BA1\u7ED3\u679C"
  );
  nextPayload = {
    success: true,
    data: {
      productCode: "HB10001",
      storeProductCodes: {
        S001: "S001-HB10001",
        S002: "S002-HB10001"
      },
      product: {
        productCode: "HB10001",
        productName: "\u6D4B\u8BD5\u65B0\u5546\u54C1"
      }
    }
  };
  const createWithPricesResult = await createProductWithPrices({
    barcode: "930000000001",
    productName: "\u6D4B\u8BD5\u65B0\u5546\u54C1",
    productImage: "https://img.example.com/HB10001.jpg",
    purchasePrice: 5.2,
    retailPrice: 9.9,
    localSupplierCode: "SUP01",
    isAutoPricing: true,
    isSpecialProduct: false,
    isActive: true
  });
  assertEqual(capturedUrl, "/api/react/v1/products/create-with-prices", "\u521B\u5EFA\u5546\u54C1\u5E26\u5206\u5E97\u4EF7\u683C\u5E94\u8C03\u7528\u56FA\u5B9A\u63A5\u53E3");
  assertEqual(capturedInit?.method, "POST", "\u521B\u5EFA\u5546\u54C1\u5E26\u5206\u5E97\u4EF7\u683C\u5E94\u4F7F\u7528 POST");
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    {
      barcode: "930000000001",
      productName: "\u6D4B\u8BD5\u65B0\u5546\u54C1",
      productImage: "https://img.example.com/HB10001.jpg",
      purchasePrice: 5.2,
      retailPrice: 9.9,
      localSupplierCode: "SUP01",
      isAutoPricing: true,
      isSpecialProduct: false,
      isActive: true
    },
    "\u521B\u5EFA\u5546\u54C1\u5E26\u5206\u5E97\u4EF7\u683C\u8BF7\u6C42\u4F53\u5E94\u539F\u6837\u63D0\u4EA4 DTO"
  );
  assertDeepEqual(
    createWithPricesResult,
    {
      productCode: "HB10001",
      storeProductCodes: {
        S001: "S001-HB10001",
        S002: "S002-HB10001"
      },
      product: {
        productCode: "HB10001",
        productName: "\u6D4B\u8BD5\u65B0\u5546\u54C1"
      }
    },
    "\u521B\u5EFA\u5546\u54C1\u5E26\u5206\u5E97\u4EF7\u683C\u5E94\u8FD4\u56DE unwrap \u540E\u7684\u7ED3\u679C"
  );
  const jobFailurePayload = {
    isSuccess: false,
    message: "\u521B\u5EFA\u4EFB\u52A1\u5931\u8D25",
    data: {
      reason: "duplicate operationId"
    }
  };
  nextPayload = jobFailurePayload;
  await assertRequestError(
    () => createHqProductFullSyncJob({ operationId: "op-full-1" }),
    "\u521B\u5EFA\u4EFB\u52A1\u5931\u8D25",
    jobFailurePayload,
    "job \u63A5\u53E3 isSuccess:false"
  );
  const syncToStoresJobFailurePayload = {
    success: false,
    message: "\u521B\u5EFA\u540C\u6B65\u5230\u5206\u5E97\u4EFB\u52A1\u5931\u8D25",
    data: {
      reason: "duplicate operationId",
      request: {
        productCodes: ["HB001"],
        storeCodes: ["S001"]
      }
    }
  };
  nextPayload = syncToStoresJobFailurePayload;
  await assertRequestError(
    () => startSyncProductsToStoresJob({
      productCodes: ["HB001"],
      storeCodes: ["S001"],
      overwrite: false,
      fields: ["purchasePrice"]
    }),
    "\u521B\u5EFA\u540C\u6B65\u5230\u5206\u5E97\u4EFB\u52A1\u5931\u8D25",
    syncToStoresJobFailurePayload,
    "\u540C\u6B65\u5230\u5206\u5E97 job \u63A5\u53E3 success:false"
  );
  const createWithPricesFailurePayload = {
    success: false,
    message: "\u521B\u5EFA\u5546\u54C1\u5931\u8D25",
    data: {
      errors: ["\u6761\u7801\u5DF2\u5B58\u5728"]
    }
  };
  nextPayload = createWithPricesFailurePayload;
  await assertRequestError(
    () => createProductWithPrices({
      barcode: "930000000001",
      productName: "\u6D4B\u8BD5\u65B0\u5546\u54C1",
      purchasePrice: 5.2,
      retailPrice: 9.9,
      isAutoPricing: true,
      isSpecialProduct: false,
      isActive: true
    }),
    "\u521B\u5EFA\u5546\u54C1\u5931\u8D25",
    createWithPricesFailurePayload,
    "\u521B\u5EFA\u5546\u54C1\u5E26\u5206\u5E97\u4EF7\u683C\u63A5\u53E3 success:false"
  );
} finally {
  globalThis.fetch = originalFetch;
}
