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
function buildPushProductsToHqOperationId(containerGuid, productCodes, itemCount, updateFields = []) {
  const stableCodes = productCodes.map((item) => item.trim()).filter(Boolean).sort().join(",");
  const stableFields = updateFields.map((item) => item.trim()).filter(Boolean).sort().join(",");
  return `container-push-hq:${containerGuid || "unknown"}:${stableCodes || "items"}:${itemCount}:${stableFields || "all"}`;
}
async function createPushProductsToHqJob(data) {
  const response = await request_default.post(
    `${API_BASE}/push-to-hq/jobs`,
    data
  );
  assertApiSuccess(response, "\u521B\u5EFA\u53D1\u9001\u5546\u54C1\u5230 HQ \u4EFB\u52A1\u5931\u8D25");
  return normalizePushProductsToHqJobResult(response);
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
async function syncProductsFromHqIncremental(data = {}, options) {
  const operationId = buildProductHqSyncOperationId("incremental", data.startDate);
  const activeJob = activeHqProductSyncJobs.get(operationId);
  if (activeJob) {
    return activeJob;
  }
  const jobPromise = (async () => {
    try {
      const job = await createProductHqSyncIncrementalJob({ ...data, operationId });
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
async function syncSelectedProductsFromHq(data) {
  const response = await request_default.post(
    `${API_BASE}/sync-selected-from-hq`,
    data
  );
  assertApiSuccess(response, "\u9009\u4E2D\u5546\u54C1 HQ \u540C\u6B65\u5931\u8D25");
  return normalizeHqProductSyncResult(unwrapApiData(response));
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

// src/services/posProductService.hqSyncJob.test.ts
function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}
function assertDeepEqual(actual, expected, label) {
  const actualJson = JSON.stringify(actual);
  const expectedJson = JSON.stringify(expected);
  if (actualJson !== expectedJson) {
    throw new Error(`${label}. Expected: ${expectedJson}, received: ${actualJson}`);
  }
}
function assertEqual(actual, expected, label) {
  if (actual !== expected) {
    throw new Error(`${label}. Expected: ${String(expected)}, received: ${String(actual)}`);
  }
}
async function assertRejects(execute, expectedMessage, label) {
  try {
    await execute();
  } catch (error) {
    const actualMessage = error instanceof Error ? error.message : String(error);
    assertEqual(actualMessage, expectedMessage, label);
    return error;
  }
  throw new Error(`${label}. Expected promise to reject`);
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
function createFakeTimer() {
  let sequence = 0;
  let now = 0;
  const tasks = /* @__PURE__ */ new Map();
  return {
    setTimeout: (callback, delay) => {
      const id = sequence + 1;
      sequence = id;
      tasks.set(id, { id, execute: callback, delay, dueAt: now + delay });
      return id;
    },
    clearTimeout: (id) => {
      if (typeof id === "number") {
        tasks.delete(id);
      }
    },
    flushNext: () => {
      const next = Array.from(tasks.values()).sort((left, right) => {
        if (left.dueAt !== right.dueAt) {
          return left.dueAt - right.dueAt;
        }
        return left.id - right.id;
      })[0];
      if (!next) {
        throw new Error("\u6CA1\u6709\u53EF\u6267\u884C\u7684\u5B9A\u65F6\u4EFB\u52A1");
      }
      tasks.delete(next.id);
      now = next.dueAt;
      next.execute();
      return next.delay;
    },
    pendingCount: () => tasks.size
  };
}
function jsonResponse(payload, status = 200) {
  return new Response(JSON.stringify(payload), {
    status,
    headers: { "Content-Type": "application/json" }
  });
}
async function waitForPendingTimer(timer) {
  for (let index = 0; index < 20; index += 1) {
    if (timer.pendingCount() > 0) {
      return;
    }
    await Promise.resolve();
    await new Promise((resolve) => setTimeout(resolve, 0));
  }
}
async function captureFetch(responseBody, execute) {
  const originalFetch = globalThis.fetch;
  let capturedUrl = "";
  let capturedMethod = "";
  let capturedBody;
  globalThis.fetch = async (input, init) => {
    capturedUrl = String(input);
    capturedMethod = String(init?.method);
    capturedBody = init?.body ? JSON.parse(String(init.body)) : void 0;
    return new Response(JSON.stringify(responseBody), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
  };
  try {
    const result = await execute();
    return { capturedUrl, capturedMethod, capturedBody, result };
  } finally {
    globalThis.fetch = originalFetch;
  }
}
async function expectRejectsWithRequestError(execute, expectedMessage) {
  try {
    await execute();
  } catch (error) {
    assert(error instanceof RequestError, "\u5E94\u629B\u51FA RequestError");
    assert(error.message.includes(expectedMessage), `\u9519\u8BEF\u4FE1\u606F\u5E94\u5305\u542B ${expectedMessage}`);
    return;
  }
  throw new Error("\u9884\u671F\u8BF7\u6C42\u5931\u8D25\uFF0C\u4F46\u5B9E\u9645\u6210\u529F");
}
async function main() {
  const failures = [];
  const fullJobFailure = await runTest("\u5168\u91CF\u5546\u54C1 HQ \u540C\u6B65\u5E94\u521B\u5EFA\u540E\u53F0 job \u5E76\u643A\u5E26 operationId", async () => {
    const result = await captureFetch(
      {
        success: true,
        data: {
          jobId: "product-job-full",
          status: "Queued",
          mode: "Full",
          operationId: "product-hq-sync:full"
        }
      },
      () => createProductHqSyncFullJob({ operationId: "product-hq-sync:full" })
    );
    assertDeepEqual(
      {
        url: result.capturedUrl,
        method: result.capturedMethod,
        body: result.capturedBody,
        job: result.result
      },
      {
        url: "/api/react/v1/sync/products/jobs",
        method: "POST",
        body: { operationId: "product-hq-sync:full" },
        job: {
          jobId: "product-job-full",
          status: "Queued",
          mode: "Full",
          operationId: "product-hq-sync:full",
          errors: []
        }
      },
      "\u5168\u91CF\u5546\u54C1\u540C\u6B65 job \u8BF7\u6C42\u4E0D\u7B26\u5408\u9884\u671F"
    );
  });
  if (fullJobFailure) failures.push(fullJobFailure);
  const incrementalJobFailure = await runTest("\u589E\u91CF\u5546\u54C1 HQ \u540C\u6B65\u5E94\u521B\u5EFA\u540E\u53F0 job \u5E76\u643A\u5E26\u65E5\u671F\u4E0E operationId", async () => {
    const result = await captureFetch(
      {
        success: true,
        data: {
          jobId: "product-job-inc",
          status: "Running",
          mode: "Incremental",
          operationId: "product-hq-sync:incremental:2026-02-21",
          startDate: "2026-02-21"
        }
      },
      () => createProductHqSyncIncrementalJob({
        operationId: "product-hq-sync:incremental:2026-02-21",
        startDate: "2026-02-21"
      })
    );
    assertDeepEqual(
      {
        url: result.capturedUrl,
        method: result.capturedMethod,
        body: result.capturedBody,
        job: result.result
      },
      {
        url: "/api/react/v1/sync/products-incremental/jobs",
        method: "POST",
        body: {
          operationId: "product-hq-sync:incremental:2026-02-21",
          startDate: "2026-02-21"
        },
        job: {
          jobId: "product-job-inc",
          status: "Running",
          mode: "Incremental",
          operationId: "product-hq-sync:incremental:2026-02-21",
          startDate: "2026-02-21",
          errors: []
        }
      },
      "\u589E\u91CF\u5546\u54C1\u540C\u6B65 job \u8BF7\u6C42\u4E0D\u7B26\u5408\u9884\u671F"
    );
  });
  if (incrementalJobFailure) failures.push(incrementalJobFailure);
  const pushToHqFieldsFailure = await runTest("\u8D27\u67DC\u53D1\u9001 HQ job \u5E94\u643A\u5E26\u5B57\u6BB5\u9009\u62E9\u5E76\u8BA9 operationId \u533A\u5206\u5B57\u6BB5", async () => {
    const operationId = buildPushProductsToHqOperationId(
      "container-1",
      ["P002", "P001"],
      2,
      ["storeRetailPrice", "inventoryImportPrice"]
    );
    assertEqual(
      operationId,
      "container-push-hq:container-1:P001,P002:2:inventoryImportPrice,storeRetailPrice",
      "\u5B57\u6BB5\u9009\u62E9\u5E94\u8FDB\u5165\u53D1\u9001 HQ operationId"
    );
    const result = await captureFetch(
      {
        success: true,
        data: {
          jobId: "push-job-1",
          status: "Queued",
          operationId,
          result: { successCount: 0, failedCount: 0, totalCount: 0 }
        }
      },
      () => createPushProductsToHqJob({
        operationId,
        productCodes: ["P001"],
        updateFields: ["storeRetailPrice", "inventoryImportPrice"],
        items: [
          {
            productCode: "P001",
            isNewProduct: false,
            importPrice: 1.23,
            oemPrice: 4.56
          }
        ]
      })
    );
    assertDeepEqual(
      {
        url: result.capturedUrl,
        method: result.capturedMethod,
        body: result.capturedBody,
        job: result.result
      },
      {
        url: "/api/react/v1/products/push-to-hq/jobs",
        method: "POST",
        body: {
          operationId,
          productCodes: ["P001"],
          updateFields: ["storeRetailPrice", "inventoryImportPrice"],
          items: [
            {
              productCode: "P001",
              isNewProduct: false,
              importPrice: 1.23,
              oemPrice: 4.56
            }
          ]
        },
        job: {
          jobId: "push-job-1",
          status: "Queued",
          operationId,
          result: {
            successCount: 0,
            failedCount: 0,
            totalCount: 0,
            affectedRowCount: 0,
            errors: []
          },
          errors: []
        }
      },
      "\u53D1\u9001 HQ job \u8BF7\u6C42\u5E94\u4FDD\u7559 updateFields"
    );
  });
  if (pushToHqFieldsFailure) failures.push(pushToHqFieldsFailure);
  const selectedProductsSyncFailure = await runTest("\u9009\u4E2D\u5546\u54C1\u4ECE HQ \u540C\u6B65\u5E94\u8C03\u7528\u9009\u4E2D\u5546\u54C1\u63A5\u53E3\u5E76\u643A\u5E26\u5546\u54C1\u7F16\u7801", async () => {
    const result = await captureFetch(
      {
        success: true,
        data: {
          productsUpdated: 2,
          storeRetailPricesCreated: 3,
          storeMultiCodesCreated: 1,
          errors: []
        }
      },
      () => syncSelectedProductsFromHq({ productCodes: ["EP112", "EP194"] })
    );
    assertDeepEqual(
      {
        url: result.capturedUrl,
        method: result.capturedMethod,
        body: result.capturedBody,
        syncResult: result.result
      },
      {
        url: "/api/react/v1/products/sync-selected-from-hq",
        method: "POST",
        body: { productCodes: ["EP112", "EP194"] },
        syncResult: {
          productsUpdated: 2,
          storeRetailPricesCreated: 3,
          storeMultiCodesCreated: 1,
          errors: [],
          productsAdded: 0,
          productsDeleted: 0,
          productSetCodesCreated: 0,
          productSetCodesDeleted: 0,
          durationMs: 0
        }
      },
      "\u9009\u4E2D\u5546\u54C1 HQ \u540C\u6B65\u8BF7\u6C42\u4E0D\u7B26\u5408\u9884\u671F"
    );
  });
  if (selectedProductsSyncFailure) failures.push(selectedProductsSyncFailure);
  const operationIdBuilderFailure = await runTest("\u5546\u54C1 HQ \u540C\u6B65 operationId \u5E94\u7531\u670D\u52A1\u5C42\u552F\u4E00\u751F\u6210", () => {
    assertEqual(
      buildProductHqSyncOperationId("full"),
      "product-hq-sync:full:all",
      "\u5168\u91CF\u540C\u6B65 operationId \u5E94\u4F7F\u7528\u7EDF\u4E00\u683C\u5F0F"
    );
    assertEqual(
      buildProductHqSyncOperationId("incremental", "2026-02-21"),
      "product-hq-sync:incremental:2026-02-21",
      "\u589E\u91CF\u540C\u6B65 operationId \u5E94\u5305\u542B\u8D77\u59CB\u65E5\u671F"
    );
    assertEqual(
      buildProductHqSyncOperationId("incremental"),
      "product-hq-sync:incremental:all",
      "\u7F3A\u7701\u8D77\u59CB\u65E5\u671F\u5E94\u4F7F\u7528 all\uFF0C\u907F\u514D latest/all \u4E24\u5957\u8BED\u4E49"
    );
  });
  if (operationIdBuilderFailure) failures.push(operationIdBuilderFailure);
  const sharedPollerSuccessFailure = await runTest("\u5546\u54C1 HQ \u540C\u6B65\u5171\u4EAB\u8F6E\u8BE2\u5668\u5E94\u6301\u7EED\u67E5\u8BE2\u76F4\u5230\u6210\u529F", async () => {
    const timer = createFakeTimer();
    const statuses = [
      { jobId: "product-poller-job", status: "Queued" },
      { jobId: "product-poller-job", status: "Running" },
      {
        jobId: "product-poller-job",
        status: "Succeeded",
        result: { productsAdded: 3, productsUpdated: 5 }
      }
    ];
    const requestedJobIds = [];
    const poller = createProductHqSyncJobPoller({
      jobId: "product-poller-job",
      pollIntervalMs: 200,
      timeoutMs: 3e4,
      getJob: async (jobId) => {
        requestedJobIds.push(jobId);
        return statuses.shift();
      },
      setTimeoutFn: timer.setTimeout,
      clearTimeoutFn: timer.clearTimeout
    });
    timer.flushNext();
    await Promise.resolve();
    timer.flushNext();
    await Promise.resolve();
    timer.flushNext();
    await Promise.resolve();
    const result = await poller.promise;
    assertDeepEqual(requestedJobIds, ["product-poller-job", "product-poller-job", "product-poller-job"], "\u8F6E\u8BE2\u5668\u5E94\u6301\u7EED\u67E5\u8BE2\u540C\u4E00\u4E2A job");
    assertEqual(result.status, "Succeeded", "\u5171\u4EAB\u8F6E\u8BE2\u5668\u5E94\u8FD4\u56DE\u6700\u7EC8\u6210\u529F job");
    assertEqual(result.result?.productsAdded, 3, "\u5171\u4EAB\u8F6E\u8BE2\u5668\u5E94\u900F\u4F20\u6700\u7EC8\u7ED3\u679C");
  });
  if (sharedPollerSuccessFailure) failures.push(sharedPollerSuccessFailure);
  const sharedPollerTimeoutFailure = await runTest("\u5546\u54C1 HQ \u540C\u6B65\u5171\u4EAB\u8F6E\u8BE2\u5668\u8D85\u65F6\u5E94\u629B\u51FA\u7EDF\u4E00 timeout \u9519\u8BEF", async () => {
    const timer = createFakeTimer();
    const poller = createProductHqSyncJobPoller({
      jobId: "product-poller-timeout",
      pollIntervalMs: 200,
      timeoutMs: 200,
      getJob: async () => ({ jobId: "product-poller-timeout", status: "Running" }),
      setTimeoutFn: timer.setTimeout,
      clearTimeoutFn: timer.clearTimeout
    });
    timer.flushNext();
    const error = await assertRejects(
      () => poller.promise,
      "\u5546\u54C1\u540C\u6B65\u4EFB\u52A1\u8F6E\u8BE2\u8D85\u65F6",
      "\u5171\u4EAB\u8F6E\u8BE2\u5668 timeout \u5E94\u8FDB\u5165\u660E\u786E\u9519\u8BEF\u8DEF\u5F84"
    );
    assert(error instanceof HqProductSyncPollingTimeoutError, "\u5171\u4EAB\u8F6E\u8BE2\u5668 timeout \u5E94\u4F7F\u7528\u4E13\u95E8\u9519\u8BEF\u7C7B\u578B");
  });
  if (sharedPollerTimeoutFailure) failures.push(sharedPollerTimeoutFailure);
  const statusFallbackFailure = await runTest("job \u67E5\u8BE2\u9047\u5230 success true \u4E14\u65E0 status \u65F6\u5E94\u5F52\u4E00\u4E3A Succeeded", async () => {
    const result = await captureFetch(
      {
        success: true,
        data: {
          jobId: "product-job-ok",
          success: true,
          result: {
            productsAdded: 1,
            productsUpdated: 2,
            productsDeleted: 3
          }
        }
      },
      () => getProductHqSyncJob("product-job-ok")
    );
    assertDeepEqual(
      {
        url: result.capturedUrl,
        method: result.capturedMethod,
        job: result.result
      },
      {
        url: "/api/react/v1/sync/products/jobs/product-job-ok",
        method: "GET",
        job: {
          jobId: "product-job-ok",
          status: "Succeeded",
          success: true,
          result: {
            productsAdded: 1,
            productsUpdated: 2,
            productsDeleted: 3,
            productSetCodesCreated: 0,
            productSetCodesDeleted: 0,
            errors: [],
            durationMs: 0
          },
          errors: []
        }
      },
      "job success true \u72B6\u6001\u5F52\u4E00\u5316\u4E0D\u7B26\u5408\u9884\u671F"
    );
  });
  if (statusFallbackFailure) failures.push(statusFallbackFailure);
  const unknownStatusFailure = await runTest("job \u67E5\u8BE2\u9047\u5230\u672A\u77E5 status \u4E0D\u5E94\u9759\u9ED8\u5F53\u6210 Running", async () => {
    await expectRejectsWithRequestError(
      () => captureFetch(
        {
          success: true,
          data: {
            jobId: "product-job-weird",
            status: "AlmostDone"
          }
        },
        () => getProductHqSyncJob("product-job-weird")
      ),
      "\u672A\u77E5\u540C\u6B65\u4EFB\u52A1\u72B6\u6001"
    );
  });
  if (unknownStatusFailure) failures.push(unknownStatusFailure);
  const businessFailure = await runTest("\u65E7\u5546\u54C1 HQ \u540C\u6B65\u63A5\u53E3\u9047\u5230 success false \u5E94\u629B\u51FA\u540E\u7AEF\u6D88\u606F", async () => {
    await expectRejectsWithRequestError(
      () => captureFetch(
        {
          success: false,
          message: "HQ \u5546\u54C1\u540C\u6B65\u5931\u8D25",
          data: { productsAdded: 0, errors: ["\u5931\u8D25\u660E\u7EC6"] }
        },
        () => syncProductsFromHqFull()
      ),
      "HQ \u5546\u54C1\u540C\u6B65\u5931\u8D25"
    );
  });
  if (businessFailure) failures.push(businessFailure);
  const duplicateSubmissionFailure = await runTest("\u8FDE\u7EED\u70B9\u51FB\u786E\u8BA4\u53EA\u521B\u5EFA\u4E00\u6B21\u5546\u54C1\u540C\u6B65 job", async () => {
    const originalFetch = globalThis.fetch;
    const timer = createFakeTimer();
    let postCount = 0;
    try {
      globalThis.fetch = async (input, init) => {
        const url = String(input);
        if (init?.method === "POST" && url === "/api/react/v1/sync/products/jobs") {
          postCount += 1;
          return jsonResponse({
            success: true,
            data: {
              jobId: "product-job-once",
              status: "Running",
              operationId: "product-hq-sync:full:all"
            }
          });
        }
        if (url === "/api/react/v1/sync/products/jobs/product-job-once") {
          return jsonResponse({
            success: true,
            data: {
              jobId: "product-job-once",
              status: "Succeeded",
              productsAdded: 2
            }
          });
        }
        throw new Error(`\u672A\u9884\u671F\u7684\u8BF7\u6C42\uFF1A${url}`);
      };
      const first = syncProductsFromHqFull({
        pollIntervalMs: 100,
        timeoutMs: 1e3,
        setTimeoutFn: timer.setTimeout,
        clearTimeoutFn: timer.clearTimeout
      });
      const second = syncProductsFromHqFull({
        pollIntervalMs: 100,
        timeoutMs: 1e3,
        setTimeoutFn: timer.setTimeout,
        clearTimeoutFn: timer.clearTimeout
      });
      await waitForPendingTimer(timer);
      timer.flushNext();
      await Promise.resolve();
      const [firstResult, secondResult] = await Promise.all([first, second]);
      assertEqual(postCount, 1, "\u8FDE\u7EED\u786E\u8BA4\u65F6\u53EA\u5E94\u521B\u5EFA\u4E00\u6B21 job");
      assertDeepEqual(firstResult, secondResult, "\u8FDE\u7EED\u786E\u8BA4\u5E94\u5171\u4EAB\u540C\u4E00\u4E2A active job \u7684\u7ED3\u679C");
      assertEqual(firstResult.productsAdded, 2, "\u6700\u7EC8\u7ED3\u679C\u5E94\u6765\u81EA\u8F6E\u8BE2\u5B8C\u6210\u7684 job");
    } finally {
      globalThis.fetch = originalFetch;
    }
  });
  if (duplicateSubmissionFailure) failures.push(duplicateSubmissionFailure);
  const takeoverExistingJobFailure = await runTest("\u540E\u7AEF\u8FD4\u56DE\u76F8\u540C operationId \u7684\u5DF2\u6709 job \u65F6\u5E94\u63A5\u7BA1\u8F6E\u8BE2", async () => {
    const originalFetch = globalThis.fetch;
    const timer = createFakeTimer();
    const requestedUrls = [];
    try {
      globalThis.fetch = async (input, init) => {
        const url = String(input);
        requestedUrls.push(url);
        if (init?.method === "POST" && url === "/api/react/v1/sync/products-incremental/jobs") {
          return jsonResponse({
            success: true,
            data: {
              jobId: "product-job-existing",
              status: "Running",
              operationId: "product-hq-sync:incremental:2026-05-20",
              message: "\u5DF2\u6709\u540C\u6B65\u4EFB\u52A1\u6B63\u5728\u6267\u884C"
            }
          });
        }
        if (url === "/api/react/v1/sync/products/jobs/product-job-existing") {
          return jsonResponse({
            success: true,
            data: {
              jobId: "product-job-existing",
              status: "Succeeded",
              productsUpdated: 4
            }
          });
        }
        throw new Error(`\u672A\u9884\u671F\u7684\u8BF7\u6C42\uFF1A${url}`);
      };
      const resultPromise = syncProductsFromHqIncremental(
        { startDate: "2026-05-20" },
        {
          pollIntervalMs: 100,
          timeoutMs: 1e3,
          setTimeoutFn: timer.setTimeout,
          clearTimeoutFn: timer.clearTimeout
        }
      );
      await waitForPendingTimer(timer);
      timer.flushNext();
      await Promise.resolve();
      const result = await resultPromise;
      assertDeepEqual(
        requestedUrls,
        ["/api/react/v1/sync/products-incremental/jobs", "/api/react/v1/sync/products/jobs/product-job-existing"],
        "\u670D\u52A1\u5C42\u5E94\u521B\u5EFA\u589E\u91CF job \u540E\u63A5\u7BA1\u8FD4\u56DE\u7684\u5DF2\u6709 job \u8F6E\u8BE2"
      );
      assertEqual(result.productsUpdated, 4, "\u63A5\u7BA1\u8F6E\u8BE2\u5E94\u8FD4\u56DE\u5DF2\u6709 job \u7684\u5B8C\u6210\u7EDF\u8BA1");
    } finally {
      globalThis.fetch = originalFetch;
    }
  });
  if (takeoverExistingJobFailure) failures.push(takeoverExistingJobFailure);
  const failedStatusFailure = await runTest("\u8F6E\u8BE2\u5230 Failed \u5E94\u629B\u51FA\u540E\u7AEF\u5931\u8D25\u6D88\u606F", async () => {
    const originalFetch = globalThis.fetch;
    const timer = createFakeTimer();
    try {
      globalThis.fetch = async (input, init) => {
        const url = String(input);
        if (init?.method === "POST") {
          return jsonResponse({ success: true, data: { jobId: "product-job-failed", status: "Running" } });
        }
        if (url === "/api/react/v1/sync/products/jobs/product-job-failed") {
          return jsonResponse({
            success: true,
            data: { jobId: "product-job-failed", status: "Failed", message: "\u540E\u7AEF\u5546\u54C1\u540C\u6B65\u5931\u8D25" }
          });
        }
        throw new Error(`\u672A\u9884\u671F\u7684\u8BF7\u6C42\uFF1A${url}`);
      };
      const resultPromise = syncProductsFromHqFull({
        pollIntervalMs: 100,
        timeoutMs: 1e3,
        setTimeoutFn: timer.setTimeout,
        clearTimeoutFn: timer.clearTimeout
      });
      await waitForPendingTimer(timer);
      timer.flushNext();
      await Promise.resolve();
      await assertRejects(
        () => resultPromise,
        "\u540E\u7AEF\u5546\u54C1\u540C\u6B65\u5931\u8D25",
        "Failed \u72B6\u6001\u5E94\u8FDB\u5165\u660E\u786E\u9519\u8BEF\u8DEF\u5F84"
      );
    } finally {
      globalThis.fetch = originalFetch;
    }
  });
  if (failedStatusFailure) failures.push(failedStatusFailure);
  const timeoutFailure = await runTest("\u8F6E\u8BE2\u8D85\u65F6\u5E94\u629B\u51FA\u660E\u786E timeout \u9519\u8BEF", async () => {
    const originalFetch = globalThis.fetch;
    const timer = createFakeTimer();
    try {
      globalThis.fetch = async (_input, init) => {
        if (init?.method === "POST") {
          return jsonResponse({ success: true, data: { jobId: "product-job-timeout", status: "Running" } });
        }
        return jsonResponse({ success: true, data: { jobId: "product-job-timeout", status: "Running" } });
      };
      const resultPromise = syncProductsFromHqFull({
        pollIntervalMs: 100,
        timeoutMs: 100,
        setTimeoutFn: timer.setTimeout,
        clearTimeoutFn: timer.clearTimeout
      });
      await waitForPendingTimer(timer);
      timer.flushNext();
      await Promise.resolve();
      const error = await assertRejects(
        () => resultPromise,
        "\u5546\u54C1\u540C\u6B65\u4EFB\u52A1\u8F6E\u8BE2\u8D85\u65F6",
        "timeout \u5E94\u8FDB\u5165\u660E\u786E\u9519\u8BEF\u8DEF\u5F84"
      );
      assert(error instanceof HqProductSyncPollingTimeoutError, "timeout \u5E94\u4F7F\u7528\u4E13\u95E8\u9519\u8BEF\u7C7B\u578B");
    } finally {
      globalThis.fetch = originalFetch;
    }
  });
  if (timeoutFailure) failures.push(timeoutFailure);
  if (failures.length > 0) {
    throw new Error(`\u5171\u6709 ${failures.length} \u4E2A\u6D4B\u8BD5\u5931\u8D25
- ${failures.join("\n- ")}`);
  }
  console.log("posProductService.hqSyncJob.test: ok");
}
await main();
