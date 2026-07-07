// <define:import.meta.env>
var define_import_meta_env_default = {};

// src/pages/PosAdmin/StoreProductPrice/StoreProductPrice.hqSync.logic.test.ts
import { readFileSync } from "node:fs";
import path from "node:path";

// src/pages/PosAdmin/StoreProductPrice/pagination.ts
function getPaginationTotalPages(total, pageSize) {
  const safePageSize = pageSize > 0 ? pageSize : 1;
  return total > 0 ? Math.ceil(total / safePageSize) : 0;
}
function formatPaginationTotalText(total, pageSize, translate) {
  return translate("posAdmin.productPrice.paginationTotal", "\u5171 {{count}} \u6761 / {{pages}} \u9875", {
    count: total,
    pages: getPaginationTotalPages(total, pageSize)
  });
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
function unwrapPagedResult(payload) {
  const result = unwrapApiData(payload);
  return {
    items: result.items ?? [],
    total: result.total ?? result.totalCount ?? 0,
    page: result.page ?? result.pageIndex ?? 1,
    pageSize: result.pageSize ?? 10,
    totalPages: result.totalPages
  };
}
request.get = (url, options) => request(url, { ...options, method: "GET" });
request.post = (url, data, options) => request(url, { ...options, method: "POST", data });
request.put = (url, data, options) => request(url, { ...options, method: "PUT", data });
request.patch = (url, data, options) => request(url, { ...options, method: "PATCH", data });
request.delete = (url, options) => request(url, { ...options, method: "DELETE" });
var request_default = request;

// src/services/storeProductPriceService.ts
var API_BASE = "/api/react/v1/store-product-prices";
function assertApiSuccess(response, fallbackMessage) {
  if (response.success === false || response.isSuccess === false) {
    throw new RequestError(response.message || fallbackMessage, 200, response);
  }
}
function isRecord(value) {
  return typeof value === "object" && value !== null;
}
function readString(source, ...keys) {
  for (const key of keys) {
    const value = source[key];
    if (typeof value === "string") return value;
  }
  return void 0;
}
function readBoolean(source, ...keys) {
  for (const key of keys) {
    const value = source[key];
    if (typeof value === "boolean") return value;
  }
  return void 0;
}
function readNumber(source, ...keys) {
  for (const key of keys) {
    const value = source[key];
    if (typeof value === "number") return value;
  }
  return 0;
}
function readStringArray(source, ...keys) {
  for (const key of keys) {
    const value = source[key];
    if (Array.isArray(value)) {
      return value.filter((item) => typeof item === "string");
    }
  }
  return [];
}
function normalizeStorePriceTransferStatus(status, payload) {
  if (typeof status !== "string") {
    throw new RequestError("\u5206\u5E97\u4EF7\u683C\u540C\u6B65\u4EFB\u52A1\u7F3A\u5C11\u72B6\u6001", 200, payload);
  }
  switch (status.trim().toLowerCase()) {
    case "running":
    case "queued":
    case "pending":
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
      throw new RequestError(`\u672A\u77E5\u5206\u5E97\u4EF7\u683C\u540C\u6B65\u4EFB\u52A1\u72B6\u6001: ${status}`, 200, payload);
  }
}
function normalizeStorePriceTransferResult(value) {
  const raw = isRecord(value) ? value : {};
  return {
    totalCount: readNumber(raw, "totalCount", "TotalCount"),
    retailPriceTotal: readNumber(raw, "retailPriceTotal", "RetailPriceTotal"),
    multiCodeTotal: readNumber(raw, "multiCodeTotal", "MultiCodeTotal"),
    totalProcessed: readNumber(raw, "totalProcessed", "TotalProcessed"),
    insertedCount: readNumber(raw, "insertedCount", "InsertedCount"),
    updatedCount: readNumber(raw, "updatedCount", "UpdatedCount"),
    skippedCount: readNumber(raw, "skippedCount", "SkippedCount"),
    failedCount: readNumber(raw, "failedCount", "FailedCount"),
    retailPriceInserted: readNumber(raw, "retailPriceInserted", "RetailPriceInserted"),
    retailPriceUpdated: readNumber(raw, "retailPriceUpdated", "RetailPriceUpdated"),
    retailPriceSkipped: readNumber(raw, "retailPriceSkipped", "RetailPriceSkipped"),
    multiCodeInserted: readNumber(raw, "multiCodeInserted", "MultiCodeInserted"),
    multiCodeUpdated: readNumber(raw, "multiCodeUpdated", "MultiCodeUpdated"),
    multiCodeSkipped: readNumber(raw, "multiCodeSkipped", "MultiCodeSkipped"),
    errors: readStringArray(raw, "errors", "Errors")
  };
}
function normalizeStorePriceTransferJob(value, fallbackJobId = "") {
  const data = unwrapApiData(value);
  const raw = isRecord(data) ? data : {};
  const resultValue = raw.result ?? raw.Result;
  const errors = readStringArray(raw, "errors", "Errors");
  return {
    jobId: readString(raw, "jobId", "JobId") || fallbackJobId,
    operationId: readString(raw, "operationId", "OperationId"),
    status: normalizeStorePriceTransferStatus(raw.status ?? raw.Status, raw),
    isDuplicateRequest: readBoolean(raw, "isDuplicateRequest", "IsDuplicateRequest"),
    request: raw.request ?? raw.Request,
    result: isRecord(resultValue) ? normalizeStorePriceTransferResult(resultValue) : void 0,
    message: readString(raw, "message", "Message"),
    errors: errors.length > 0 ? errors : isRecord(resultValue) ? readStringArray(resultValue, "errors", "Errors") : [],
    createdAt: readString(raw, "createdAt", "CreatedAt"),
    startedAt: readString(raw, "startedAt", "StartedAt"),
    completedAt: readString(raw, "completedAt", "CompletedAt"),
    expiresAt: readString(raw, "expiresAt", "ExpiresAt")
  };
}
async function getStoreProductPriceGrid(data) {
  const response = await request_default.post(
    `${API_BASE}/grid`,
    data
  );
  return unwrapPagedResult(response);
}
async function syncFromHq(data) {
  const response = await request_default.post(`${API_BASE}/sync-from-hq`, data);
  if (response.success === false || response.isSuccess === false) {
    throw new RequestError(response.message || "\u4ECEHQ\u540C\u6B65\u5931\u8D25", 200, response);
  }
  return unwrapApiData(response);
}
async function startStorePriceTransferJob(data) {
  const response = await request_default.post(`${API_BASE}/store-price-transfer-jobs`, data);
  assertApiSuccess(response, "\u521B\u5EFA\u5206\u5E97\u4EF7\u683C\u540C\u6B65\u4EFB\u52A1\u5931\u8D25");
  return normalizeStorePriceTransferJob(response);
}
async function getStorePriceTransferJob(jobId) {
  const response = await request_default.get(`${API_BASE}/store-price-transfer-jobs/${encodeURIComponent(jobId)}`);
  assertApiSuccess(response, "\u67E5\u8BE2\u5206\u5E97\u4EF7\u683C\u540C\u6B65\u4EFB\u52A1\u5931\u8D25");
  return normalizeStorePriceTransferJob(response, jobId);
}

// src/pages/PosAdmin/StoreProductPrice/StoreProductPrice.hqSync.logic.test.ts
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
var pageFile = path.resolve(process.cwd(), "src/pages/PosAdmin/StoreProductPrice/index.tsx");
var typeFile = path.resolve(process.cwd(), "src/types/storeProductPrice.ts");
var pageSource = readFileSync(pageFile, "utf8");
var typeSource = readFileSync(typeFile, "utf8");
async function main() {
  const failures = [];
  const typeFailure = await runTest("SyncFromHqRequest \u5E94\u58F0\u660E endDate", () => {
    assert(
      typeSource.includes("endDate?: string"),
      "SyncFromHqRequest \u7C7B\u578B\u4E2D\u5E94\u65B0\u589E endDate \u53EF\u9009\u5B57\u6BB5"
    );
  });
  if (typeFailure) failures.push(typeFailure);
  const transferTypeFailure = await runTest("StorePriceTransferRequest \u5E94\u58F0\u660E\u65B9\u5411\u3001\u8868\u548C\u5B57\u6BB5\u9009\u62E9", () => {
    assert(
      typeSource.includes("export type StorePriceTransferDirection = 'HqToLocal' | 'LocalToHq'"),
      "\u53CC\u5411\u4EF7\u683C\u540C\u6B65\u7C7B\u578B\u5E94\u9650\u5236\u65B9\u5411\u679A\u4E3E"
    );
    assert(
      typeSource.includes("syncRetailPrices: boolean") && typeSource.includes("syncMultiCodePrices: boolean"),
      "\u53CC\u5411\u4EF7\u683C\u540C\u6B65\u8BF7\u6C42\u5E94\u5305\u542B\u4EF7\u683C\u8868\u548C\u591A\u7801\u8868\u9009\u62E9"
    );
    assert(
      typeSource.includes("syncPurchasePrice: boolean") && typeSource.includes("syncRetailPrice: boolean"),
      "\u53CC\u5411\u4EF7\u683C\u540C\u6B65\u8BF7\u6C42\u5E94\u5305\u542B\u4EF7\u683C\u5B57\u6BB5\u9009\u62E9"
    );
  });
  if (transferTypeFailure) failures.push(transferTypeFailure);
  const pagePayloadFailure = await runTest("\u9875\u9762\u5E94\u540C\u65F6\u4F20\u9012 startDate \u548C endDate", () => {
    assert(
      pageSource.includes("selectedStoreCodes: values.selectedStoreCodes"),
      "\u9875\u9762\u5E94\u603B\u662F\u628A\u5FC5\u586B\u5206\u5E97\u5217\u8868\u5199\u5165\u8BF7\u6C42\u4F53"
    );
    assert(
      pageSource.includes("startDate: values.dateRange[0].format('YYYY-MM-DD')"),
      "\u9875\u9762\u5E94\u7EE7\u7EED\u4ECE\u8303\u56F4\u9009\u62E9\u5668\u8BFB\u53D6 startDate"
    );
    assert(
      pageSource.includes("endDate: values.dateRange[1].format('YYYY-MM-DD')"),
      "\u9875\u9762\u5E94\u4ECE\u8303\u56F4\u9009\u62E9\u5668\u8BFB\u53D6 endDate"
    );
  });
  if (pagePayloadFailure) failures.push(pagePayloadFailure);
  const pageRequiredFailure = await runTest("\u9875\u9762\u5E94\u8981\u6C42\u9009\u62E9\u5206\u5E97\u548C\u65E5\u671F\u8303\u56F4", () => {
    assert(
      pageSource.includes("rules={[{ required: true, message: t('posAdmin.productPrice.selectStoreRequired', '\u8BF7\u9009\u62E9\u5206\u5E97') }]}"),
      "HQ \u540C\u6B65\u5F39\u7A97\u5E94\u8981\u6C42\u9009\u62E9\u5206\u5E97"
    );
    assert(
      pageSource.includes("rules={[{ required: true, message: t('posAdmin.productPrice.selectDateRangeRequired', '\u8BF7\u9009\u62E9\u65E5\u671F\u8303\u56F4') }]}"),
      "HQ \u540C\u6B65\u5F39\u7A97\u5E94\u8981\u6C42\u9009\u62E9\u65E5\u671F\u8303\u56F4"
    );
  });
  if (pageRequiredFailure) failures.push(pageRequiredFailure);
  const selectAllFailure = await runTest("\u9875\u9762\u5E94\u63D0\u4F9B\u4ECE storeOptions \u751F\u6210\u5206\u5E97\u5168\u9009\u7684\u903B\u8F91", () => {
    assert(
      pageSource.includes("const selectAllHqSyncStores = () => {"),
      "\u9875\u9762\u5E94\u58F0\u660E HQ \u540C\u6B65\u5206\u5E97\u5168\u9009\u51FD\u6570"
    );
    assert(
      pageSource.includes("hqSyncForm.setFieldValue('selectedStoreCodes', storeOptions.map((option) => option.value))"),
      "\u5168\u9009\u51FD\u6570\u5E94\u628A\u6240\u6709\u5206\u5E97\u7F16\u7801\u5199\u5165 selectedStoreCodes"
    );
    assert(
      pageSource.includes("t('posAdmin.productPrice.selectAllStores', '\u5168\u9009\u5206\u5E97')"),
      "HQ \u540C\u6B65\u5F39\u7A97\u5E94\u663E\u793A\u5168\u9009\u5206\u5E97\u6309\u94AE\u6587\u6848"
    );
  });
  if (selectAllFailure) failures.push(selectAllFailure);
  const selectAllBindingFailure = await runTest("HQ \u540C\u6B65\u5206\u5E97\u591A\u9009\u6846\u5E94\u76F4\u63A5\u7ED1\u5B9A\u5230 Form.Item \u5B57\u6BB5", () => {
    assert(
      !pageSource.includes('<Form.Item name="selectedStoreCodes" label={t('),
      "selectedStoreCodes \u4E0D\u5E94\u5305\u4F4F Space.Compact\uFF0C\u5426\u5219 Select \u4E0D\u4F1A\u63A5\u6536\u8868\u5355\u5B57\u6BB5 value/onChange"
    );
    assert(
      pageSource.includes('<Form.Item name="selectedStoreCodes" noStyle rules={[{ required: true, message: t('),
      "selectedStoreCodes \u5E94\u4F7F\u7528 noStyle Form.Item \u76F4\u63A5\u5305\u4F4F Select"
    );
    assert(
      pageSource.includes('<Button htmlType="button" icon={<CheckSquareOutlined />} onClick={selectAllHqSyncStores}>'),
      '\u5168\u9009\u6309\u94AE\u5E94\u58F0\u660E htmlType="button"\uFF0C\u907F\u514D\u88AB\u8868\u5355\u4E0A\u4E0B\u6587\u5F53\u6210\u63D0\u4EA4\u6309\u94AE'
    );
  });
  if (selectAllBindingFailure) failures.push(selectAllBindingFailure);
  const pageErrorFailure = await runTest("\u9875\u9762\u5E94\u4F18\u5148\u5C55\u793A\u540E\u7AEF\u8FD4\u56DE\u7684\u5931\u8D25\u6587\u6848", () => {
    assert(
      pageSource.includes("error instanceof Error ? error.message : t('posAdmin.productPrice.hqSyncFailed', '\u4ECEHQ\u540C\u6B65\u5931\u8D25')"),
      "\u9875\u9762 catch \u5206\u652F\u5E94\u4F18\u5148\u5C55\u793A\u540E\u7AEF\u9519\u8BEF\u6D88\u606F\uFF0C\u800C\u4E0D\u662F\u56FA\u5B9A\u63D0\u793A"
    );
  });
  if (pageErrorFailure) failures.push(pageErrorFailure);
  const priceTransferPageFailure = await runTest("\u9875\u9762\u5E94\u65B0\u589E\u72EC\u7ACB HQ/\u672C\u5730\u4EF7\u683C\u540C\u6B65 job \u5F39\u7A97", () => {
    assert(
      pageSource.includes("t('posAdmin.productPrice.priceTransfer', 'HQ/\u672C\u5730\u4EF7\u683C\u540C\u6B65')"),
      "\u9875\u9762\u5E94\u663E\u793A\u72EC\u7ACB\u7684 HQ/\u672C\u5730\u4EF7\u683C\u540C\u6B65\u5165\u53E3"
    );
    assert(
      pageSource.includes("direction: 'HqToLocal'"),
      "\u5F39\u7A97\u9ED8\u8BA4\u65B9\u5411\u5E94\u4E3A HQ -> \u672C\u5730"
    );
    assert(
      pageSource.includes("startStorePriceTransferJob(dto)"),
      "\u9875\u9762\u63D0\u4EA4\u5E94\u8D70\u540E\u53F0 job \u521B\u5EFA\u63A5\u53E3"
    );
    assert(
      pageSource.includes("createHqSyncJobPoller<StorePriceTransferJobDto>"),
      "\u9875\u9762\u5E94\u590D\u7528 2 \u79D2\u8F6E\u8BE2 job \u5DE5\u5177"
    );
    assert(
      pageSource.includes("const PRICE_TRANSFER_POLL_TIMEOUT_MS = 45 * 60 * 1000") && pageSource.includes("timeoutMs: PRICE_TRANSFER_POLL_TIMEOUT_MS"),
      "20w \u884C\u4EF7\u683C\u540C\u6B65\u5E94\u4F7F\u7528 45 \u5206\u949F\u4E13\u5C5E\u8F6E\u8BE2\u8D85\u65F6"
    );
    assert(
      pageSource.includes("\u76EE\u6807\u5206\u5E97\u540C\u6B65\u4EFB\u52A1\u6B63\u5728\u6267\u884C\uFF0C\u5DF2\u5207\u6362\u5230\u5DF2\u6709\u4EFB\u52A1"),
      "\u91CD\u590D\u4EFB\u52A1\u63D0\u793A\u5E94\u8BF4\u660E\u662F\u76EE\u6807\u5206\u5E97\u540C\u6B65\u4EFB\u52A1\u6B63\u5728\u6267\u884C"
    );
    assert(
      pageSource.includes("\u4EFB\u52A1\u53EF\u80FD\u4ECD\u5728\u540E\u53F0\u6267\u884C\uFF0C\u8BF7\u7A0D\u540E\u5237\u65B0\u6216\u91CD\u65B0\u67E5\u8BE2"),
      "\u8F6E\u8BE2\u8D85\u65F6\u63D0\u793A\u5E94\u8BF4\u660E\u540E\u53F0\u4EFB\u52A1\u53EF\u80FD\u4ECD\u5728\u6267\u884C"
    );
    assert(
      pageSource.includes("syncRetailPrices: !!values.syncRetailPrices") && pageSource.includes("syncMultiCodePrices: !!values.syncMultiCodePrices"),
      "\u9875\u9762 payload \u5E94\u5305\u542B\u540C\u6B65\u8868\u9009\u62E9"
    );
  });
  if (priceTransferPageFailure) failures.push(priceTransferPageFailure);
  const priceTransferSameStoreFailure = await runTest("HQ/\u672C\u5730\u4EF7\u683C\u540C\u6B65\u5E94\u5141\u8BB8\u6E90\u76EE\u6807\u5206\u5E97\u540C\u540D", () => {
    assert(
      !pageSource.includes("sourceTargetDifferent") && !pageSource.includes("\u6E90\u5206\u5E97\u548C\u76EE\u6807\u5206\u5E97\u4E0D\u80FD\u76F8\u540C"),
      "HQ/\u672C\u5730\u8DE8\u6570\u636E\u57DF\u540C\u6B65\u4E0D\u5E94\u62E6\u622A\u540C\u540D\u6E90/\u76EE\u6807\u5206\u5E97"
    );
  });
  if (priceTransferSameStoreFailure) failures.push(priceTransferSameStoreFailure);
  const priceTransferProgressFailure = await runTest("HQ/\u672C\u5730\u4EF7\u683C\u540C\u6B65\u8FDB\u5EA6\u5E94\u4F7F\u7528\u540E\u7AEF\u771F\u5B9E totalCount", () => {
    assert(
      typeSource.includes("totalCount: number") && typeSource.includes("retailPriceTotal: number") && typeSource.includes("multiCodeTotal: number"),
      "StorePriceTransferResult \u7C7B\u578B\u5E94\u58F0\u660E\u540E\u7AEF\u8FDB\u5EA6\u603B\u6570\u5B57\u6BB5"
    );
    assert(
      pageSource.includes("function getPriceTransferProgressPercent(job: StorePriceTransferJobDto)"),
      "\u9875\u9762\u5E94\u4F7F\u7528\u72EC\u7ACB\u51FD\u6570\u8BA1\u7B97\u4EF7\u683C\u540C\u6B65\u8FDB\u5EA6"
    );
    assert(
      pageSource.includes("result.totalProcessed + result.skippedCount") && pageSource.includes("job.result?.totalCount ?? 0"),
      "Running \u8FDB\u5EA6\u5E94\u6309\u5DF2\u5904\u7406/\u603B\u91CF\u8BA1\u7B97"
    );
    assert(
      !pageSource.includes("? 100 : 50"),
      "Running \u8FDB\u5EA6\u4E0D\u5E94\u7EE7\u7EED\u786C\u7F16\u7801\u4E3A 50%"
    );
    assert(
      pageSource.includes("t('posAdmin.productPrice.processedProgress', '\u5DF2\u5904\u7406')"),
      "\u5F39\u7A97\u5E94\u663E\u793A\u5DF2\u5904\u7406 X / Y"
    );
    assert(
      pageSource.includes("const totalProcessed = getPriceTransferHandledCount(completedJob.result)"),
      "\u5B8C\u6210\u63D0\u793A\u5E94\u590D\u7528\u5DF2\u5904\u7406\u6570\u91CF\uFF0C\u907F\u514D\u6F0F\u7B97 skippedCount"
    );
    assert(
      pageSource.includes("const nextJob = await getStorePriceTransferJob(jobId)") && pageSource.includes("setPriceTransferJob(nextJob)"),
      "\u8F6E\u8BE2\u5230 Running \u5FEB\u7167\u65F6\u5E94\u7ACB\u5373\u5237\u65B0\u5F39\u7A97\u8FDB\u5EA6"
    );
  });
  if (priceTransferProgressFailure) failures.push(priceTransferProgressFailure);
  const validationErrorFailure = await runTest("\u8868\u5355\u6821\u9A8C\u5931\u8D25\u4E0D\u5E94\u5F39\u51FA\u540C\u6B65\u5931\u8D25\u5168\u5C40\u63D0\u793A", () => {
    assert(
      pageSource.includes("function isFormValidationError(error: unknown): error is { errorFields: unknown[] }"),
      "\u9875\u9762\u5E94\u58F0\u660E AntD \u8868\u5355\u6821\u9A8C\u9519\u8BEF\u8BC6\u522B\u51FD\u6570"
    );
    assert(
      pageSource.includes("if (isFormValidationError(error)) return"),
      "handleSyncFromHq \u5E94\u8BA9\u5B57\u6BB5\u7EA7\u6821\u9A8C\u9519\u8BEF\u7559\u5728\u8868\u5355\u5185\u5C55\u793A\uFF0C\u4E0D\u5F39\u51FA\u540C\u6B65\u5931\u8D25\u63D0\u793A"
    );
  });
  if (validationErrorFailure) failures.push(validationErrorFailure);
  const paginationTotalPagesFailure = await runTest("\u5206\u9875\u6587\u6848\u5E94\u540C\u65F6\u663E\u793A\u603B\u6570\u548C\u603B\u9875\u6570", () => {
    assertEqual(getPaginationTotalPages(128, 50), 3, "128 \u6761\u4E14\u6BCF\u9875 50 \u6761\u65F6\u5E94\u663E\u793A 3 \u9875");
    assertEqual(getPaginationTotalPages(0, 50), 0, "0 \u6761\u6570\u636E\u65F6\u5E94\u663E\u793A 0 \u9875");
    assertEqual(getPaginationTotalPages(10, 0), 10, "pageSize \u5F02\u5E38\u65F6\u5E94\u4F7F\u7528 1 \u4F5C\u4E3A\u515C\u5E95\u9875\u5927\u5C0F");
    const text = formatPaginationTotalText(128, 50, (_key, _fallback, values) => `\u5171 ${values?.count} \u6761 / ${values?.pages} \u9875`);
    assertEqual(text, "\u5171 128 \u6761 / 3 \u9875", "\u5206\u9875\u6587\u6848\u5E94\u628A\u603B\u6570\u548C\u603B\u9875\u6570\u4E00\u8D77\u5C55\u793A");
  });
  if (paginationTotalPagesFailure) failures.push(paginationTotalPagesFailure);
  const originalFetch = globalThis.fetch;
  const pagedFieldFailure = await runTest("\u5546\u54C1\u4EF7\u683C\u5206\u9875\u63A5\u53E3\u5E94\u517C\u5BB9 totalCount \u548C pageIndex \u5B57\u6BB5", async () => {
    globalThis.fetch = async () => new Response(JSON.stringify({
      success: true,
      data: {
        items: [{ productCode: "P001", isActive: true, isStoreAutoPricing: false, isStoreSpecialProduct: false }],
        totalCount: 128,
        pageIndex: 3,
        pageSize: 50
      }
    }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
    const result = await getStoreProductPriceGrid({
      storeCode: "S01",
      pageNumber: 3,
      pageSize: 50
    });
    assertEqual(result.total, 128, "\u5206\u9875\u7ED3\u679C\u5E94\u628A totalCount \u5F52\u4E00\u4E3A total");
    assertEqual(result.page, 3, "\u5206\u9875\u7ED3\u679C\u5E94\u628A pageIndex \u5F52\u4E00\u4E3A page");
    assertEqual(result.pageSize, 50, "\u5206\u9875\u7ED3\u679C\u5E94\u4FDD\u7559\u540E\u7AEF pageSize");
    assertEqual(result.items.length, 1, "\u5206\u9875\u7ED3\u679C\u5E94\u4FDD\u7559\u5546\u54C1\u5217\u8868");
  });
  if (pagedFieldFailure) failures.push(pagedFieldFailure);
  const businessFailure = await runTest("syncFromHq \u9047\u5230 success false \u65F6\u5E94\u629B\u51FA\u540E\u7AEF\u6D88\u606F", async () => {
    globalThis.fetch = async () => new Response(JSON.stringify({
      success: false,
      message: "HQ \u540C\u6B65\u5931\u8D25\uFF1A\u6D4B\u8BD5\u4E1A\u52A1\u9519\u8BEF",
      data: {
        addedCount: 99,
        updatedCount: 88,
        totalProcessed: 187,
        durationMs: 2e3,
        errors: []
      }
    }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
    await assertRejects(
      () => syncFromHq({ selectedStoreCodes: ["S01"], startDate: "2026-05-01", endDate: "2026-05-31" }),
      "HQ \u540C\u6B65\u5931\u8D25\uFF1A\u6D4B\u8BD5\u4E1A\u52A1\u9519\u8BEF",
      "syncFromHq \u4E0D\u5E94\u628A success false \u7684\u54CD\u5E94\u5F53\u6210\u6210\u529F\u7ED3\u679C"
    );
  });
  if (businessFailure) failures.push(businessFailure);
  const httpFailure = await runTest("syncFromHq \u9047\u5230\u975E 2xx \u65F6\u5E94\u629B\u51FA\u540E\u7AEF\u6D88\u606F", async () => {
    globalThis.fetch = async () => new Response(JSON.stringify({
      success: false,
      message: "HQ \u540C\u6B65\u5931\u8D25\uFF1A\u6D4B\u8BD5 HTTP \u9519\u8BEF"
    }), {
      status: 500,
      headers: { "Content-Type": "application/json" }
    });
    await assertRejects(
      () => syncFromHq({ selectedStoreCodes: ["S01"], startDate: "2026-05-01", endDate: "2026-05-31" }),
      "HQ \u540C\u6B65\u5931\u8D25\uFF1A\u6D4B\u8BD5 HTTP \u9519\u8BEF",
      "syncFromHq \u5E94\u628A\u975E 2xx \u7684\u540E\u7AEF\u6D88\u606F\u900F\u4F20\u51FA\u6765"
    );
  });
  if (httpFailure) failures.push(httpFailure);
  const priceTransferStartFailure = await runTest("startStorePriceTransferJob \u5E94 POST \u5230\u540E\u53F0\u4EFB\u52A1\u63A5\u53E3\u5E76\u5F52\u4E00\u5316\u8FD4\u56DE", async () => {
    globalThis.fetch = async (input, init) => {
      assertEqual(String(input), "/api/react/v1/store-product-prices/store-price-transfer-jobs", "\u521B\u5EFA\u4EFB\u52A1\u63A5\u53E3\u8DEF\u5F84\u5E94\u6B63\u786E");
      const body = JSON.parse(String(init?.body || "{}"));
      assertEqual(body.direction, "HqToLocal", "\u521B\u5EFA\u4EFB\u52A1 payload \u5E94\u5305\u542B\u65B9\u5411");
      assertEqual(body.syncMultiCodePrices, true, "\u521B\u5EFA\u4EFB\u52A1 payload \u5E94\u5305\u542B\u591A\u7801\u8868\u9009\u62E9");
      return new Response(JSON.stringify({
        success: true,
        data: {
          jobId: "job-1",
          status: "Running",
          isDuplicateRequest: true
        }
      }), {
        status: 200,
        headers: { "Content-Type": "application/json" }
      });
    };
    const job = await startStorePriceTransferJob({
      direction: "HqToLocal",
      sourceStoreCode: "S01",
      targetStoreCode: "T01",
      syncRetailPrices: true,
      syncMultiCodePrices: true,
      syncPurchasePrice: true,
      syncRetailPrice: true,
      syncDiscountRate: false,
      syncIsAutoPricing: false,
      syncIsSpecialProduct: false
    });
    assertEqual(job.jobId, "job-1", "\u521B\u5EFA\u4EFB\u52A1\u5E94\u8FD4\u56DE jobId");
    assertEqual(job.status, "Running", "\u521B\u5EFA\u4EFB\u52A1\u5E94\u5F52\u4E00\u5316\u8FD0\u884C\u4E2D\u72B6\u6001");
    assertEqual(job.isDuplicateRequest, true, "\u521B\u5EFA\u4EFB\u52A1\u5E94\u4FDD\u7559\u91CD\u590D\u63D0\u4EA4\u6807\u8BB0");
  });
  if (priceTransferStartFailure) failures.push(priceTransferStartFailure);
  const priceTransferGetFailure = await runTest("getStorePriceTransferJob \u5E94 GET \u4EFB\u52A1\u72B6\u6001\u5E76\u5F52\u4E00\u5316\u7EDF\u8BA1", async () => {
    globalThis.fetch = async (input) => {
      assertEqual(String(input), "/api/react/v1/store-product-prices/store-price-transfer-jobs/job-1", "\u67E5\u8BE2\u4EFB\u52A1\u63A5\u53E3\u8DEF\u5F84\u5E94\u6B63\u786E");
      return new Response(JSON.stringify({
        success: true,
        data: {
          jobId: "job-1",
          status: "Succeeded",
          result: {
            totalProcessed: 4,
            insertedCount: 2,
            updatedCount: 2,
            retailPriceInserted: 1,
            multiCodeInserted: 1
          }
        }
      }), {
        status: 200,
        headers: { "Content-Type": "application/json" }
      });
    };
    const job = await getStorePriceTransferJob("job-1");
    assertEqual(job.status, "Succeeded", "\u67E5\u8BE2\u4EFB\u52A1\u5E94\u5F52\u4E00\u5316\u6210\u529F\u72B6\u6001");
    assertEqual(job.result?.totalProcessed, 4, "\u67E5\u8BE2\u4EFB\u52A1\u5E94\u5F52\u4E00\u5316\u603B\u5904\u7406\u6570");
    assertEqual(job.result?.retailPriceInserted, 1, "\u67E5\u8BE2\u4EFB\u52A1\u5E94\u5F52\u4E00\u5316\u4EF7\u683C\u8868\u65B0\u589E\u6570");
    assertEqual(job.result?.multiCodeInserted, 1, "\u67E5\u8BE2\u4EFB\u52A1\u5E94\u5F52\u4E00\u5316\u591A\u7801\u8868\u65B0\u589E\u6570");
  });
  if (priceTransferGetFailure) failures.push(priceTransferGetFailure);
  const priceTransferMissingStatusFailure = await runTest("getStorePriceTransferJob \u7F3A\u5C11\u72B6\u6001\u65F6\u5E94\u66B4\u9732\u5951\u7EA6\u9519\u8BEF", async () => {
    globalThis.fetch = async () => new Response(JSON.stringify({
      success: true,
      data: {
        jobId: "job-missing-status"
      }
    }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
    await assertRejects(
      () => getStorePriceTransferJob("job-missing-status"),
      "\u5206\u5E97\u4EF7\u683C\u540C\u6B65\u4EFB\u52A1\u7F3A\u5C11\u72B6\u6001",
      "\u7F3A\u5C11 status \u65F6\u4E0D\u5E94\u88AB\u9ED8\u8BA4\u4E3A Running"
    );
  });
  if (priceTransferMissingStatusFailure) failures.push(priceTransferMissingStatusFailure);
  globalThis.fetch = originalFetch;
  if (failures.length > 0) {
    throw new Error(`\u5171\u6709 ${failures.length} \u4E2A\u6D4B\u8BD5\u5931\u8D25
- ${failures.join("\n- ")}`);
  }
  console.log("StoreProductPrice.hqSync.logic.test: ok");
}
await main();
