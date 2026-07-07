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
function normalizeResult(payload) {
  return unwrapEnvelope(payload);
}
function normalizeStoreOrderSyncPayload(payload) {
  const storeCodes = Array.from(
    new Set(
      (payload?.storeCodes?.length ? payload.storeCodes : payload?.storeCode ? [payload.storeCode] : []).map((item) => item.trim()).filter(Boolean)
    )
  );
  return storeCodes.length ? { storeCodes } : {};
}
function normalizeStoreOrderHqIncrementalSyncPayload(payload) {
  const basePayload = normalizeStoreOrderSyncPayload(payload);
  return {
    ...basePayload,
    ...payload?.startDate ? { startDate: payload.startDate } : {},
    ...payload?.endDate ? { endDate: payload.endDate } : {},
    ...payload?.conflictStrategy ? { conflictStrategy: payload.conflictStrategy } : {}
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
function normalizeStoreOrderSyncJobResult(payload, fallbackJobId = "") {
  const rawPayload = isRecord(payload) ? payload : null;
  const rawResult = normalizeResult(payload);
  const result = isRecord(rawResult) ? rawResult : {};
  const nestedResult = isRecord(result.result) ? result.result : {};
  const readNumber = (...values) => values.find((value) => typeof value === "number");
  const message = typeof result.message === "string" ? result.message : typeof nestedResult.message === "string" ? nestedResult.message : rawPayload && typeof rawPayload.message === "string" ? rawPayload.message : void 0;
  const success = typeof result.success === "boolean" ? result.success : typeof nestedResult.success === "boolean" ? nestedResult.success : rawPayload && typeof rawPayload.success === "boolean" ? rawPayload.success : void 0;
  const resolvedStatus = typeof result.status === "string" ? normalizeStoreOrderSyncJobStatus(result.status) : success === false ? "Failed" : "Running";
  return {
    jobId: typeof result.jobId === "string" ? result.jobId : fallbackJobId,
    status: resolvedStatus,
    mode: result.mode === "Full" || result.mode === "Incremental" ? result.mode : nestedResult.mode === "Full" || nestedResult.mode === "Incremental" ? nestedResult.mode : void 0,
    conflictStrategy: result.conflictStrategy === "LatestWins" || result.conflictStrategy === "HqWins" ? result.conflictStrategy : nestedResult.conflictStrategy === "LatestWins" || nestedResult.conflictStrategy === "HqWins" ? nestedResult.conflictStrategy : void 0,
    message,
    success,
    storeCodes: Array.isArray(result.storeCodes) ? result.storeCodes.filter((item) => typeof item === "string") : void 0,
    startDate: typeof result.startDate === "string" ? result.startDate : void 0,
    endDate: typeof result.endDate === "string" ? result.endDate : void 0,
    ordersSynced: readNumber(result.ordersSynced, nestedResult.ordersSynced),
    detailsSynced: readNumber(result.detailsSynced, nestedResult.detailsSynced),
    ordersUpdated: readNumber(result.ordersUpdated, nestedResult.ordersUpdated),
    detailsUpdated: readNumber(result.detailsUpdated, nestedResult.detailsUpdated),
    ordersSoftDeleted: readNumber(result.ordersSoftDeleted, nestedResult.ordersSoftDeleted),
    detailsSoftDeleted: readNumber(result.detailsSoftDeleted, nestedResult.detailsSoftDeleted),
    skippedOrdersBecauseLocalNewer: readNumber(
      result.skippedOrdersBecauseLocalNewer,
      nestedResult.skippedOrdersBecauseLocalNewer
    ),
    skippedDetailsBecauseLocalNewer: readNumber(
      result.skippedDetailsBecauseLocalNewer,
      nestedResult.skippedDetailsBecauseLocalNewer
    ),
    hqOrderCount: readNumber(result.hqOrderCount, nestedResult.hqOrderCount),
    hqDetailCount: readNumber(result.hqDetailCount, nestedResult.hqDetailCount),
    shadowRowCount: readNumber(result.shadowRowCount, nestedResult.shadowRowCount),
    durationMs: readNumber(result.durationMs, nestedResult.durationMs),
    errors: Array.isArray(nestedResult.errors) ? nestedResult.errors.filter((item) => typeof item === "string") : Array.isArray(result.errors) ? result.errors.filter((item) => typeof item === "string") : void 0
  };
}
async function syncMissingStoreOrders(payload) {
  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), 10 * 60 * 1e3);
  try {
    const response = await request_default(`${API_BASE}/sync-missing-orders`, {
      method: "POST",
      data: normalizeStoreOrderSyncPayload(payload),
      signal: controller.signal
    });
    return normalizeResult(response);
  } finally {
    clearTimeout(timeoutId);
  }
}
async function createStoreOrderSyncJob(payload) {
  const response = await request_default(`${API_BASE}/sync-missing-orders/jobs`, {
    method: "POST",
    data: normalizeStoreOrderSyncPayload(payload)
  });
  return normalizeStoreOrderSyncJobResult(response);
}
async function createStoreOrderFullHqSyncJob() {
  const response = await request_default(`${API_BASE}/hq-sync/full/jobs`, {
    method: "POST",
    data: {}
  });
  return normalizeStoreOrderSyncJobResult(response);
}
async function createStoreOrderIncrementalHqSyncJob(payload) {
  const response = await request_default(`${API_BASE}/hq-sync/incremental/jobs`, {
    method: "POST",
    data: normalizeStoreOrderHqIncrementalSyncPayload(payload)
  });
  return normalizeStoreOrderSyncJobResult(response);
}
async function getStoreOrderSyncJob(jobId) {
  const response = await request_default(
    `${API_BASE}/sync-missing-orders/jobs/${encodeURIComponent(jobId)}`,
    {
      method: "GET"
    }
  );
  return normalizeStoreOrderSyncJobResult(response, jobId);
}
async function getStoreOrderHqSyncJob(jobId) {
  const response = await request_default(
    `${API_BASE}/hq-sync/jobs/${encodeURIComponent(jobId)}`,
    {
      method: "GET"
    }
  );
  return normalizeStoreOrderSyncJobResult(response, jobId);
}

// src/pages/Warehouse/StoreOrders/syncRequest.test.ts
function assertDeepEqual(actual, expected, label) {
  const actualJson = JSON.stringify(actual);
  const expectedJson = JSON.stringify(expected);
  if (actualJson !== expectedJson) {
    throw new Error(`${label}. Expected: ${expectedJson}, received: ${actualJson}`);
  }
}
async function captureSyncBody(payload) {
  const originalFetch = globalThis.fetch;
  let capturedBody;
  let capturedUrl = "";
  let capturedMethod = "";
  globalThis.fetch = async (_input, init) => {
    capturedUrl = String(_input);
    capturedMethod = String(init?.method);
    capturedBody = init?.body ? JSON.parse(String(init.body)) : void 0;
    return new Response(
      JSON.stringify({
        success: true,
        ordersSynced: 0,
        detailsSynced: 0,
        ordersUpdated: 0,
        detailsUpdated: 0,
        message: "ok"
      }),
      {
        status: 200,
        headers: { "Content-Type": "application/json" }
      }
    );
  };
  try {
    await syncMissingStoreOrders(payload);
    return { body: capturedBody, url: capturedUrl, method: capturedMethod };
  } finally {
    globalThis.fetch = originalFetch;
  }
}
assertDeepEqual(
  await captureSyncBody({ storeCodes: ["S001", "S002"] }),
  {
    body: { storeCodes: ["S001", "S002"] },
    url: "/api/react/v1/store-order/sync-missing-orders",
    method: "POST"
  },
  "\u540C\u6B65\u8BA2\u5355\u5E94\u8BE5\u53D1\u9001\u5168\u90E8\u5DF2\u9009\u5206\u5E97"
);
assertDeepEqual(
  await captureSyncBody(),
  {
    body: {},
    url: "/api/react/v1/store-order/sync-missing-orders",
    method: "POST"
  },
  "\u672A\u9009\u62E9\u5206\u5E97\u65F6\u4E0D\u5E94\u5077\u5077\u53D1\u9001\u7B2C\u4E00\u4E2A\u5206\u5E97"
);
assertDeepEqual(
  await captureSyncBody({ storeCode: "S001" }),
  {
    body: { storeCodes: ["S001"] },
    url: "/api/react/v1/store-order/sync-missing-orders",
    method: "POST"
  },
  "\u65E7 storeCode \u53C2\u6570\u5E94\u8BE5\u5F52\u4E00\u4E3A storeCodes \u6570\u7EC4"
);
async function captureCreateJobRequest(payload) {
  const originalFetch = globalThis.fetch;
  let capturedBody;
  let capturedUrl = "";
  let capturedMethod = "";
  globalThis.fetch = async (input, init) => {
    capturedUrl = String(input);
    capturedMethod = String(init?.method);
    capturedBody = init?.body ? JSON.parse(String(init.body)) : void 0;
    return new Response(
      JSON.stringify({
        success: true,
        message: "\u540C\u6B65\u4EFB\u52A1\u5DF2\u63D0\u4EA4",
        data: {
          jobId: "job-001",
          status: "Queued"
        }
      }),
      {
        status: 200,
        headers: { "Content-Type": "application/json" }
      }
    );
  };
  try {
    const result = await createStoreOrderSyncJob(payload);
    return { body: capturedBody, url: capturedUrl, method: capturedMethod, result };
  } finally {
    globalThis.fetch = originalFetch;
  }
}
assertDeepEqual(
  await captureCreateJobRequest({ storeCodes: ["S001", "S002", "S001"] }),
  {
    body: { storeCodes: ["S001", "S002"] },
    url: "/api/react/v1/store-order/sync-missing-orders/jobs",
    method: "POST",
    result: {
      jobId: "job-001",
      status: "Queued",
      message: "\u540C\u6B65\u4EFB\u52A1\u5DF2\u63D0\u4EA4",
      success: true
    }
  },
  "\u521B\u5EFA\u540C\u6B65\u4EFB\u52A1\u5E94\u547D\u4E2D\u65B0 job \u63A5\u53E3\u5E76\u4FDD\u7559\u63D0\u4EA4\u53CD\u9988"
);
async function captureFullHqJobRequest() {
  const originalFetch = globalThis.fetch;
  let capturedBody;
  let capturedUrl = "";
  let capturedMethod = "";
  globalThis.fetch = async (input, init) => {
    capturedUrl = String(input);
    capturedMethod = String(init?.method);
    capturedBody = init?.body ? JSON.parse(String(init.body)) : void 0;
    return new Response(
      JSON.stringify({
        success: true,
        data: {
          jobId: "job-full",
          status: "Running",
          mode: "Full"
        }
      }),
      { status: 200, headers: { "Content-Type": "application/json" } }
    );
  };
  try {
    const result = await createStoreOrderFullHqSyncJob();
    return { body: capturedBody, url: capturedUrl, method: capturedMethod, result };
  } finally {
    globalThis.fetch = originalFetch;
  }
}
assertDeepEqual(
  await captureFullHqJobRequest(),
  {
    body: {},
    url: "/api/react/v1/store-order/hq-sync/full/jobs",
    method: "POST",
    result: {
      jobId: "job-full",
      status: "Running",
      mode: "Full",
      success: true
    }
  },
  "\u5168\u91CF\u540C\u6B65\u4EFB\u52A1\u4E0D\u5E94\u643A\u5E26\u5F53\u524D\u5206\u5E97\u7B5B\u9009"
);
async function captureIncrementalHqJobRequest(payload) {
  const originalFetch = globalThis.fetch;
  let capturedBody;
  let capturedUrl = "";
  let capturedMethod = "";
  globalThis.fetch = async (input, init) => {
    capturedUrl = String(input);
    capturedMethod = String(init?.method);
    capturedBody = init?.body ? JSON.parse(String(init.body)) : void 0;
    return new Response(
      JSON.stringify({
        success: true,
        data: {
          jobId: "job-inc",
          status: "Running",
          mode: "Incremental"
        }
      }),
      { status: 200, headers: { "Content-Type": "application/json" } }
    );
  };
  try {
    const result = await createStoreOrderIncrementalHqSyncJob(payload);
    return { body: capturedBody, url: capturedUrl, method: capturedMethod, result };
  } finally {
    globalThis.fetch = originalFetch;
  }
}
assertDeepEqual(
  await captureIncrementalHqJobRequest({
    storeCodes: ["S001", "S002", "S001"],
    startDate: "2026-05-01T00:00:00.000Z",
    endDate: "2026-06-01T00:00:00.000Z",
    conflictStrategy: "HqWins"
  }),
  {
    body: {
      storeCodes: ["S001", "S002"],
      startDate: "2026-05-01T00:00:00.000Z",
      endDate: "2026-06-01T00:00:00.000Z",
      conflictStrategy: "HqWins"
    },
    url: "/api/react/v1/store-order/hq-sync/incremental/jobs",
    method: "POST",
    result: {
      jobId: "job-inc",
      status: "Running",
      mode: "Incremental",
      success: true
    }
  },
  "\u589E\u91CF\u540C\u6B65\u4EFB\u52A1\u5E94\u53D1\u9001\u65E5\u671F\u8303\u56F4\u548C\u53BB\u91CD\u540E\u7684\u5206\u5E97\u96C6\u5408"
);
async function captureGetJobRequest(jobId) {
  const originalFetch = globalThis.fetch;
  let capturedUrl = "";
  let capturedMethod = "";
  globalThis.fetch = async (input, init) => {
    capturedUrl = String(input);
    capturedMethod = String(init?.method);
    return new Response(
      JSON.stringify({
        success: true,
        message: "\u540C\u6B65\u5B8C\u6210",
        data: {
          jobId,
          status: "Succeeded",
          result: {
            success: true,
            message: "\u540C\u6B65\u5B8C\u6210\uFF1A\u65B0\u589E\u8BA2\u5355 3 \u6761\u3001\u8BE6\u60C5 9 \u6761\uFF1B\u66F4\u65B0\u8BA2\u5355 1 \u6761\u3001\u8BE6\u60C5 2 \u6761",
            ordersSynced: 3,
            detailsSynced: 9,
            ordersUpdated: 1,
            detailsUpdated: 2
          }
        }
      }),
      {
        status: 200,
        headers: { "Content-Type": "application/json" }
      }
    );
  };
  try {
    const result = await getStoreOrderSyncJob(jobId);
    return { url: capturedUrl, method: capturedMethod, result };
  } finally {
    globalThis.fetch = originalFetch;
  }
}
assertDeepEqual(
  await captureGetJobRequest("job-002"),
  {
    url: "/api/react/v1/store-order/sync-missing-orders/jobs/job-002",
    method: "GET",
    result: {
      jobId: "job-002",
      status: "Succeeded",
      message: "\u540C\u6B65\u5B8C\u6210\uFF1A\u65B0\u589E\u8BA2\u5355 3 \u6761\u3001\u8BE6\u60C5 9 \u6761\uFF1B\u66F4\u65B0\u8BA2\u5355 1 \u6761\u3001\u8BE6\u60C5 2 \u6761",
      success: true,
      ordersSynced: 3,
      detailsSynced: 9,
      ordersUpdated: 1,
      detailsUpdated: 2
    }
  },
  "\u8F6E\u8BE2\u540C\u6B65\u4EFB\u52A1\u5E94\u547D\u4E2D job \u72B6\u6001\u63A5\u53E3\u5E76\u5C55\u5F00\u7ED3\u679C"
);
async function captureGetHqJobRequest(jobId) {
  const originalFetch = globalThis.fetch;
  let capturedUrl = "";
  let capturedMethod = "";
  globalThis.fetch = async (input, init) => {
    capturedUrl = String(input);
    capturedMethod = String(init?.method);
    return new Response(
      JSON.stringify({
        success: true,
        data: {
          jobId,
          status: "Succeeded",
          mode: "Full",
          conflictStrategy: "LatestWins",
          result: {
            success: true,
            message: "\u5168\u91CF\u540C\u6B65\u5B8C\u6210",
            ordersSynced: 1,
            detailsSynced: 2,
            ordersSoftDeleted: 3,
            detailsSoftDeleted: 4,
            skippedOrdersBecauseLocalNewer: 5,
            skippedDetailsBecauseLocalNewer: 6
          }
        }
      }),
      { status: 200, headers: { "Content-Type": "application/json" } }
    );
  };
  try {
    const result = await getStoreOrderHqSyncJob(jobId);
    return { url: capturedUrl, method: capturedMethod, result };
  } finally {
    globalThis.fetch = originalFetch;
  }
}
assertDeepEqual(
  await captureGetHqJobRequest("job-hq"),
  {
    url: "/api/react/v1/store-order/hq-sync/jobs/job-hq",
    method: "GET",
    result: {
      jobId: "job-hq",
      status: "Succeeded",
      mode: "Full",
      conflictStrategy: "LatestWins",
      message: "\u5168\u91CF\u540C\u6B65\u5B8C\u6210",
      success: true,
      ordersSynced: 1,
      detailsSynced: 2,
      ordersSoftDeleted: 3,
      detailsSoftDeleted: 4,
      skippedOrdersBecauseLocalNewer: 5,
      skippedDetailsBecauseLocalNewer: 6
    }
  },
  "HQ \u540C\u6B65\u4EFB\u52A1\u5E94\u547D\u4E2D\u65B0\u72B6\u6001\u63A5\u53E3\u5E76\u5C55\u5F00\u51B2\u7A81\u7B56\u7565\u4E0E\u8DF3\u8FC7\u7EDF\u8BA1"
);
