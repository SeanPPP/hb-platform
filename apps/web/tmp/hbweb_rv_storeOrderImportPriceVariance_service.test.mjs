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
function readFiniteNumber(value, fallback = 0) {
  if (typeof value === "number" && Number.isFinite(value)) {
    return value;
  }
  if (typeof value === "string" && value.trim()) {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : fallback;
  }
  return fallback;
}
function normalizeImportPriceVarianceSummary(payload) {
  const summary = isRecord(payload) ? payload : {};
  return {
    totalRows: readFiniteNumber(summary.totalRows),
    originalImportAmountTotal: readFiniteNumber(summary.originalImportAmountTotal),
    baselineImportAmountTotal: readFiniteNumber(summary.baselineImportAmountTotal),
    varianceAmountTotal: readFiniteNumber(summary.varianceAmountTotal)
  };
}
function readOptionalText(value) {
  return typeof value === "string" && value.trim() ? value : void 0;
}
function normalizeImportPriceVarianceSupplierSummary(payload) {
  const summary = isRecord(payload) ? payload : {};
  return {
    supplierCode: readOptionalText(summary.supplierCode),
    supplierName: readOptionalText(summary.supplierName),
    productCount: readFiniteNumber(summary.productCount),
    detailCount: readFiniteNumber(summary.detailCount),
    originalImportAmountTotal: readFiniteNumber(summary.originalImportAmountTotal),
    baselineImportAmountTotal: readFiniteNumber(summary.baselineImportAmountTotal),
    increaseVarianceAmountTotal: readFiniteNumber(summary.increaseVarianceAmountTotal),
    decreaseVarianceAmountTotal: readFiniteNumber(summary.decreaseVarianceAmountTotal),
    varianceAmountTotal: readFiniteNumber(summary.varianceAmountTotal)
  };
}
function normalizeStoreOrderImportPriceVarianceResult(payload, query) {
  const result = unwrapEnvelope(payload);
  return {
    items: Array.isArray(result?.items) ? result.items : [],
    total: readFiniteNumber(result?.total),
    page: readFiniteNumber(result?.page ?? result?.pageNumber, query.pageNumber || 1),
    pageSize: readFiniteNumber(result?.pageSize, query.pageSize || 20),
    summary: normalizeImportPriceVarianceSummary(result?.summary),
    supplierSummaries: Array.isArray(result?.supplierSummaries) ? result.supplierSummaries.map(normalizeImportPriceVarianceSupplierSummary) : []
  };
}
function normalizeStoreOrderImportPriceVarianceDetailResult(payload, query) {
  const result = unwrapEnvelope(payload);
  return {
    items: Array.isArray(result?.items) ? result.items : [],
    total: readFiniteNumber(result?.total),
    page: readFiniteNumber(result?.page ?? result?.pageNumber, query.pageNumber || 1),
    pageSize: readFiniteNumber(result?.pageSize, query.pageSize || 20),
    summary: normalizeImportPriceVarianceSummary(result?.summary)
  };
}
function normalizeStoreOrderImportPriceVarianceDomesticPriceUpdateResult(payload) {
  const result = unwrapEnvelope(payload);
  return {
    productCode: typeof result?.productCode === "string" ? result.productCode : "",
    domesticPrice: readFiniteNumber(result?.domesticPrice)
  };
}
function normalizeStoreOrderImportPriceVarianceWarehouseImportPriceUpdateResult(payload) {
  const result = unwrapEnvelope(payload);
  return {
    productCode: typeof result?.productCode === "string" ? result.productCode : "",
    warehouseImportPrice: readFiniteNumber(result?.warehouseImportPrice)
  };
}
function normalizeStoreOrderImportPriceVarianceWarehouseImportPriceBatchUpdateResult(payload) {
  const result = unwrapEnvelope(payload);
  return {
    updatedCount: readFiniteNumber(result?.updatedCount),
    warehouseImportPrice: readFiniteNumber(result?.warehouseImportPrice),
    productCodes: Array.isArray(result?.productCodes) ? result.productCodes.filter((code) => typeof code === "string") : []
  };
}
async function getStoreOrderImportPriceVariance(query) {
  const response = await request_default(`${API_BASE}/import-price-variance`, {
    method: "POST",
    data: query
  });
  return normalizeStoreOrderImportPriceVarianceResult(response, query);
}
async function getStoreOrderImportPriceVarianceDetails(query) {
  const response = await request_default(`${API_BASE}/import-price-variance/details`, {
    method: "POST",
    data: query
  });
  return normalizeStoreOrderImportPriceVarianceDetailResult(response, query);
}
async function updateStoreOrderImportPriceVarianceDomesticPrice(payload) {
  const response = await request_default(`${API_BASE}/import-price-variance/domestic-price`, {
    method: "POST",
    data: payload
  });
  return normalizeStoreOrderImportPriceVarianceDomesticPriceUpdateResult(response);
}
async function updateStoreOrderImportPriceVarianceWarehouseImportPrice(payload) {
  const response = await request_default(
    `${API_BASE}/import-price-variance/warehouse-import-price`,
    {
      method: "POST",
      data: payload
    }
  );
  return normalizeStoreOrderImportPriceVarianceWarehouseImportPriceUpdateResult(response);
}
async function batchUpdateStoreOrderImportPriceVarianceWarehouseImportPrice(payload) {
  const response = await request_default(
    `${API_BASE}/import-price-variance/warehouse-import-price/batch`,
    {
      method: "POST",
      data: payload
    }
  );
  return normalizeStoreOrderImportPriceVarianceWarehouseImportPriceBatchUpdateResult(response);
}

// src/services/storeOrderService.importPriceVariance.test.ts
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
  let capturedBody = null;
  globalThis.fetch = async (input, init) => {
    capturedUrl = String(input);
    capturedMethod = String(init?.method);
    capturedBody = init?.body ? JSON.parse(String(init.body)) : null;
    return new Response(
      JSON.stringify({
        success: true,
        data: {
          items: [
            {
              productCode: "P-001",
              itemNumber: "HB013-001",
              productName: "Test Product",
              productImage: "product.jpg",
              supplierCode: "CN1",
              supplierName: "\u4F9B\u5E94\u5546\u4E00",
              domesticPrice: 8.8,
              warehouseImportPrice: 2.22,
              unitVolume: 0.25,
              packingQuantity: 12,
              firstContainerImportPrice: 2.5,
              allocQuantityTotal: 10,
              originalImportAmountTotal: 35,
              baselineImportAmountTotal: 25,
              varianceAmountTotal: 10,
              detailCount: 2
            }
          ],
          total: "1",
          pageNumber: "2",
          pageSize: "20",
          summary: {
            totalRows: "1",
            originalImportAmountTotal: "35",
            baselineImportAmountTotal: "25",
            varianceAmountTotal: "10"
          }
        }
      }),
      {
        status: 200,
        headers: { "Content-Type": "application/json" }
      }
    );
  };
  const query = {
    keyword: "HB013",
    storeCode: "1042",
    supplierCode: "CN1",
    orderNo: "SO-001",
    startDate: "2026-06-01",
    endDate: "2026-06-23",
    varianceDirection: "increase",
    pageNumber: 2,
    pageSize: 20,
    sortBy: "absoluteVarianceAmount",
    sortDescending: true
  };
  const result = await getStoreOrderImportPriceVariance(query);
  assertEqual(capturedUrl, "/api/react/v1/store-order/import-price-variance", "\u9996\u6B21\u8D27\u67DC\u4EF7\u5DEE\u5F02\u63A5\u53E3\u8DEF\u5F84\u5E94\u4FDD\u6301\u4E00\u81F4");
  assertEqual(capturedMethod, "POST", "\u9996\u6B21\u8D27\u67DC\u4EF7\u5DEE\u5F02\u63A5\u53E3\u5E94\u4F7F\u7528 POST");
  assertDeepEqual(capturedBody, query, "\u9996\u6B21\u8D27\u67DC\u4EF7\u5DEE\u5F02\u67E5\u8BE2\u5E94\u6309\u540E\u7AEF\u5951\u7EA6\u539F\u6837\u63D0\u4EA4\u56FD\u5185\u4F9B\u5E94\u5546\u8FC7\u6EE4");
  assertEqual(result.items.length, 1, "\u5E94\u4FDD\u7559\u540E\u7AEF\u8FD4\u56DE\u7684\u5546\u54C1\u6C47\u603B\u5217\u8868");
  assertEqual(result.items[0].supplierCode, "CN1", "\u5546\u54C1\u6C47\u603B\u884C\u5E94\u5305\u542B\u56FD\u5185\u4F9B\u5E94\u5546\u7F16\u7801");
  assertEqual(result.items[0].domesticPrice, 8.8, "\u5546\u54C1\u6C47\u603B\u884C\u5E94\u5305\u542B\u56FD\u5185\u4EF7\u683C");
  assertEqual(result.items[0].warehouseImportPrice, 2.22, "\u5546\u54C1\u6C47\u603B\u884C\u5E94\u5305\u542B\u5F53\u524D\u4ED3\u5E93\u8FDB\u8D27\u4EF7\u683C");
  assertEqual(result.items[0].unitVolume, 0.25, "\u5546\u54C1\u6C47\u603B\u884C\u5E94\u5305\u542B\u4F53\u79EF");
  assertEqual(result.items[0].packingQuantity, 12, "\u5546\u54C1\u6C47\u603B\u884C\u5E94\u5305\u542B\u88C5\u7BB1\u6570");
  assertEqual(result.items[0].detailCount, 2, "\u5546\u54C1\u6C47\u603B\u884C\u5E94\u5305\u542B\u660E\u7EC6\u6570\u91CF");
  assertEqual(result.total, 1, "total \u5E94\u5F52\u4E00\u5316\u4E3A\u6570\u5B57");
  assertEqual(result.page, 2, "pageNumber \u5E94\u5F52\u4E00\u5316\u4E3A page");
  assertEqual(result.pageSize, 20, "pageSize \u5E94\u5F52\u4E00\u5316\u4E3A\u6570\u5B57");
  assertDeepEqual(
    result.summary,
    {
      totalRows: 1,
      originalImportAmountTotal: 35,
      baselineImportAmountTotal: 25,
      varianceAmountTotal: 10
    },
    "summary \u6570\u5B57\u5B57\u6BB5\u5E94\u5F52\u4E00\u5316"
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
          updatedCount: "2",
          warehouseImportPrice: "6.79",
          productCodes: ["P-001", "P-002", 123]
        }
      }),
      {
        status: 200,
        headers: { "Content-Type": "application/json" }
      }
    );
  };
  const result = await batchUpdateStoreOrderImportPriceVarianceWarehouseImportPrice({
    productCodes: ["P-001", "P-002"],
    warehouseImportPrice: 6.789
  });
  assertEqual(
    capturedUrl,
    "/api/react/v1/store-order/import-price-variance/warehouse-import-price/batch",
    "\u4ED3\u5E93\u8FDB\u8D27\u4EF7\u683C\u6279\u91CF\u4FDD\u5B58\u63A5\u53E3\u8DEF\u5F84\u5E94\u6307\u5411\u4EF7\u5DEE\u7EDF\u8BA1\u9875\u4E13\u7528\u7A84\u63A5\u53E3"
  );
  assertEqual(capturedMethod, "POST", "\u4ED3\u5E93\u8FDB\u8D27\u4EF7\u683C\u6279\u91CF\u4FDD\u5B58\u63A5\u53E3\u5E94\u4F7F\u7528 POST");
  assertDeepEqual(
    capturedBody,
    { productCodes: ["P-001", "P-002"], warehouseImportPrice: 6.789 },
    "\u4ED3\u5E93\u8FDB\u8D27\u4EF7\u683C\u6279\u91CF\u4FDD\u5B58\u63A5\u53E3\u5E94\u63D0\u4EA4\u5546\u54C1\u7F16\u7801\u5217\u8868\u548C\u7EDF\u4E00\u65B0\u4EF7\u683C"
  );
  assertEqual(result.updatedCount, 2, "\u6279\u91CF\u4FDD\u5B58\u54CD\u5E94\u5E94\u5F52\u4E00\u5316 updatedCount");
  assertEqual(result.warehouseImportPrice, 6.79, "\u6279\u91CF\u4FDD\u5B58\u54CD\u5E94\u4ED3\u5E93\u8FDB\u8D27\u4EF7\u683C\u5E94\u5F52\u4E00\u5316\u4E3A\u6570\u5B57");
  assertDeepEqual(result.productCodes, ["P-001", "P-002"], "\u6279\u91CF\u4FDD\u5B58\u54CD\u5E94\u5E94\u8FC7\u6EE4\u975E\u5B57\u7B26\u4E32\u5546\u54C1\u7F16\u7801");
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
          productCode: "P-001",
          warehouseImportPrice: "4.57"
        }
      }),
      {
        status: 200,
        headers: { "Content-Type": "application/json" }
      }
    );
  };
  const result = await updateStoreOrderImportPriceVarianceWarehouseImportPrice({
    productCode: "P-001",
    warehouseImportPrice: 4.567
  });
  assertEqual(
    capturedUrl,
    "/api/react/v1/store-order/import-price-variance/warehouse-import-price",
    "\u4ED3\u5E93\u8FDB\u8D27\u4EF7\u683C\u4FDD\u5B58\u63A5\u53E3\u8DEF\u5F84\u5E94\u6307\u5411\u4EF7\u5DEE\u7EDF\u8BA1\u9875\u4E13\u7528\u7A84\u63A5\u53E3"
  );
  assertEqual(capturedMethod, "POST", "\u4ED3\u5E93\u8FDB\u8D27\u4EF7\u683C\u4FDD\u5B58\u63A5\u53E3\u5E94\u4F7F\u7528 POST");
  assertDeepEqual(
    capturedBody,
    { productCode: "P-001", warehouseImportPrice: 4.567 },
    "\u4ED3\u5E93\u8FDB\u8D27\u4EF7\u683C\u4FDD\u5B58\u63A5\u53E3\u5E94\u63D0\u4EA4\u5546\u54C1\u7F16\u7801\u548C\u65B0\u4EF7\u683C"
  );
  assertEqual(result.productCode, "P-001", "\u4ED3\u5E93\u8FDB\u8D27\u4EF7\u683C\u4FDD\u5B58\u54CD\u5E94\u5E94\u4FDD\u7559\u5546\u54C1\u7F16\u7801");
  assertEqual(result.warehouseImportPrice, 4.57, "\u4FDD\u5B58\u54CD\u5E94\u4ED3\u5E93\u8FDB\u8D27\u4EF7\u683C\u5E94\u5F52\u4E00\u5316\u4E3A\u6570\u5B57");
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
          productCode: "P-001",
          domesticPrice: "12.35"
        }
      }),
      {
        status: 200,
        headers: { "Content-Type": "application/json" }
      }
    );
  };
  const result = await updateStoreOrderImportPriceVarianceDomesticPrice({
    productCode: "P-001",
    domesticPrice: 12.345
  });
  assertEqual(
    capturedUrl,
    "/api/react/v1/store-order/import-price-variance/domestic-price",
    "\u56FD\u5185\u4EF7\u683C\u4FDD\u5B58\u63A5\u53E3\u8DEF\u5F84\u5E94\u6307\u5411\u4EF7\u5DEE\u7EDF\u8BA1\u9875\u4E13\u7528\u7A84\u63A5\u53E3"
  );
  assertEqual(capturedMethod, "POST", "\u56FD\u5185\u4EF7\u683C\u4FDD\u5B58\u63A5\u53E3\u5E94\u4F7F\u7528 POST");
  assertDeepEqual(
    capturedBody,
    { productCode: "P-001", domesticPrice: 12.345 },
    "\u56FD\u5185\u4EF7\u683C\u4FDD\u5B58\u63A5\u53E3\u5E94\u63D0\u4EA4\u5546\u54C1\u7F16\u7801\u548C\u65B0\u4EF7\u683C"
  );
  assertEqual(result.productCode, "P-001", "\u4FDD\u5B58\u54CD\u5E94\u5E94\u4FDD\u7559\u5546\u54C1\u7F16\u7801");
  assertEqual(result.domesticPrice, 12.35, "\u4FDD\u5B58\u54CD\u5E94\u56FD\u5185\u4EF7\u683C\u5E94\u5F52\u4E00\u5316\u4E3A\u6570\u5B57");
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
          items: [
            {
              orderGUID: "order-1",
              detailGUID: "detail-1",
              orderNo: "SO-001",
              orderImportPrice: 3.5,
              firstContainerImportPrice: 2.5,
              allocQuantity: 10,
              originalImportAmount: 35,
              baselineImportAmount: 25,
              varianceAmount: 10,
              firstContainerCode: "container-1"
            }
          ],
          total: "1",
          pageNumber: "1",
          pageSize: "20",
          summary: {
            totalRows: "1",
            originalImportAmountTotal: "35",
            baselineImportAmountTotal: "25",
            varianceAmountTotal: "10"
          }
        }
      }),
      {
        status: 200,
        headers: { "Content-Type": "application/json" }
      }
    );
  };
  const query = {
    productCode: "P-001",
    supplierCode: "CN1",
    varianceDirection: "increase",
    pageNumber: 1,
    pageSize: 20,
    sortBy: "orderDate",
    sortDescending: true
  };
  const result = await getStoreOrderImportPriceVarianceDetails(query);
  assertEqual(
    capturedUrl,
    "/api/react/v1/store-order/import-price-variance/details",
    "\u9996\u6B21\u8D27\u67DC\u4EF7\u5DEE\u5F02\u660E\u7EC6\u63A5\u53E3\u8DEF\u5F84\u5E94\u6307\u5411 details"
  );
  assertEqual(capturedMethod, "POST", "\u9996\u6B21\u8D27\u67DC\u4EF7\u5DEE\u5F02\u660E\u7EC6\u63A5\u53E3\u5E94\u4F7F\u7528 POST");
  assertDeepEqual(capturedBody, query, "\u660E\u7EC6\u67E5\u8BE2\u5E94\u643A\u5E26 productCode \u548C\u5F53\u524D\u4F9B\u5E94\u5546\u8FC7\u6EE4");
  assertEqual(result.items.length, 1, "\u660E\u7EC6\u63A5\u53E3\u5E94\u4FDD\u7559\u540E\u7AEF\u8FD4\u56DE\u7684\u8BA2\u5355\u660E\u7EC6\u5217\u8868");
  assertEqual(result.items[0].orderGUID, "order-1", "\u660E\u7EC6\u884C\u5E94\u5305\u542B\u8BA2\u5355 GUID");
  assertEqual(result.total, 1, "\u660E\u7EC6 total \u5E94\u5F52\u4E00\u5316\u4E3A\u6570\u5B57");
  assertEqual(result.page, 1, "\u660E\u7EC6 pageNumber \u5E94\u5F52\u4E00\u5316\u4E3A page");
  assertEqual(result.pageSize, 20, "\u660E\u7EC6 pageSize \u5E94\u5F52\u4E00\u5316\u4E3A\u6570\u5B57");
  assertEqual(result.summary.varianceAmountTotal, 10, "\u660E\u7EC6 summary \u5E94\u5F52\u4E00\u5316");
} finally {
  globalThis.fetch = originalFetch;
}
try {
  globalThis.fetch = async () => new Response(
    JSON.stringify({
      success: true,
      data: {
        items: null,
        total: void 0,
        summary: null
      }
    }),
    {
      status: 200,
      headers: { "Content-Type": "application/json" }
    }
  );
  const result = await getStoreOrderImportPriceVariance({
    pageNumber: 3,
    pageSize: 50
  });
  assertDeepEqual(result.items, [], "items \u975E\u6570\u7EC4\u65F6\u5E94\u5F52\u4E00\u5316\u4E3A\u7A7A\u5217\u8868");
  assertEqual(result.total, 0, "\u7F3A\u5931 total \u65F6\u5E94\u5F52\u4E00\u5316\u4E3A 0");
  assertEqual(result.page, 3, "\u7F3A\u5931\u9875\u7801\u65F6\u5E94\u56DE\u9000\u67E5\u8BE2 pageNumber");
  assertEqual(result.pageSize, 50, "\u7F3A\u5931 pageSize \u65F6\u5E94\u56DE\u9000\u67E5\u8BE2 pageSize");
  assertDeepEqual(
    result.summary,
    {
      totalRows: 0,
      originalImportAmountTotal: 0,
      baselineImportAmountTotal: 0,
      varianceAmountTotal: 0
    },
    "\u7F3A\u5931 summary \u65F6\u5E94\u5F52\u4E00\u5316\u4E3A 0 \u6C47\u603B"
  );
} finally {
  globalThis.fetch = originalFetch;
}
console.log("storeOrderService.importPriceVariance.test: ok");
