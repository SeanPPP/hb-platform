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

// src/services/containerService.ts
var API_BASE = "/api/react/v1/containers";
function ensureSuccess(success, message, fallback) {
  if (success === false) {
    throw new Error(message || fallback || "\u8BF7\u6C42\u5931\u8D25");
  }
}
function normalizeHqTranslationResult(result = {}) {
  return {
    TotalCandidates: result.TotalCandidates ?? result.totalCandidates,
    TotalTranslated: result.TotalTranslated ?? result.totalTranslated,
    TotalSkipped: result.TotalSkipped ?? result.totalSkipped,
    TotalFailed: result.TotalFailed ?? result.totalFailed,
    Samples: result.Samples ?? result.samples
  };
}
function toTimestamp(value) {
  if (!value) {
    return Number.MAX_SAFE_INTEGER;
  }
  const timestamp = new Date(value).getTime();
  return Number.isNaN(timestamp) ? Number.MAX_SAFE_INTEGER : timestamp;
}
function toComingSoonProduct(item) {
  return {
    id: item.id,
    hguid: item.hguid,
    productCode: item.\u5546\u54C1\u7F16\u7801 ?? item.\u5546\u54C1\u4FE1\u606F?.\u5546\u54C1\u7F16\u7801,
    itemNumber: item.\u5546\u54C1\u4FE1\u606F?.\u8D27\u53F7,
    barcode: item.\u5546\u54C1\u4FE1\u606F?.\u6761\u5F62\u7801,
    productName: item.\u5546\u54C1\u4FE1\u606F?.\u5546\u54C1\u540D\u79F0,
    englishName: item.\u5546\u54C1\u4FE1\u606F?.\u82F1\u6587\u540D\u79F0,
    productImage: item.\u5546\u54C1\u4FE1\u606F?.\u5546\u54C1\u56FE\u7247,
    quantity: item.\u88C5\u67DC\u6570\u91CF,
    retailPrice: item.\u5546\u54C1\u4FE1\u606F?.\u96F6\u552E\u4EF7\u683C,
    isNewProduct: item.\u662F\u5426\u65B0\u5546\u54C1 ?? item.warehouseIsActive === false,
    warehouseIsActive: item.warehouseIsActive
  };
}
var itemNumberCollator = new Intl.Collator("en", {
  numeric: true,
  sensitivity: "base"
});
function compareComingSoonProducts(left, right) {
  const leftItemNumber = (left.itemNumber ?? "").trim();
  const rightItemNumber = (right.itemNumber ?? "").trim();
  if (leftItemNumber && !rightItemNumber) return -1;
  if (!leftItemNumber && rightItemNumber) return 1;
  const itemNumberCompare = itemNumberCollator.compare(leftItemNumber, rightItemNumber);
  if (itemNumberCompare !== 0) return itemNumberCompare;
  const productCodeCompare = itemNumberCollator.compare(left.productCode ?? "", right.productCode ?? "");
  if (productCodeCompare !== 0) return productCodeCompare;
  return itemNumberCollator.compare(left.productName ?? left.englishName ?? "", right.productName ?? right.englishName ?? "");
}
async function queryContainerProducts(containerGuid, query, signal) {
  const { containerGuid: _ignoredContainerGuid, ...queryBody } = query;
  const response = await request_default(
    `${API_BASE}/${encodeURIComponent(containerGuid)}/products/query`,
    {
      method: "POST",
      data: {
        // 后端契约要求 body 内也携带货柜 GUID，避免路由参数和查询体脱节。
        containerGuid,
        ...queryBody
      },
      signal
    }
  );
  ensureSuccess(response.success ?? response.isSuccess, response.message, "\u67E5\u8BE2\u8D27\u67DC\u5546\u54C1\u660E\u7EC6\u5931\u8D25");
  if (!response.data) {
    throw new Error("\u67E5\u8BE2\u8D27\u67DC\u5546\u54C1\u660E\u7EC6\u5931\u8D25");
  }
  return response.data;
}
async function getContainerDomesticSetCodes(productCode, signal) {
  const response = await request_default(
    `${API_BASE}/products/${encodeURIComponent(productCode)}/domestic-set-codes`,
    {
      method: "GET",
      signal
    }
  );
  ensureSuccess(response.success ?? response.isSuccess, response.message, "\u83B7\u53D6\u5957\u88C5\u591A\u7801\u6570\u636E\u5931\u8D25");
  return response.data ?? [];
}
async function updateContainerDomesticSetCodePrices(productCode, items) {
  const response = await request_default(
    `${API_BASE}/products/${encodeURIComponent(productCode)}/domestic-set-codes/prices`,
    {
      method: "PATCH",
      data: {
        // 只回写国内套装表价格字段，不同步仓库/POS 多码表。
        items
      }
    }
  );
  ensureSuccess(response.success ?? response.isSuccess, response.message, "\u4FDD\u5B58\u5957\u88C5\u591A\u7801\u4EF7\u683C\u5931\u8D25");
  return response.data ?? { updatedCount: 0 };
}
async function postContainerDetailAction(containerGuid, action, body, fallbackMessage) {
  const response = await request_default(
    `${API_BASE}/${encodeURIComponent(containerGuid)}/actions/${action}`,
    {
      method: "POST",
      data: body
    }
  );
  ensureSuccess(response.success ?? response.isSuccess, response.message, fallbackMessage);
  return response.data ?? { totalUpdated: 0 };
}
async function recalculateContainerCostsByScope(containerGuid, scope) {
  return postContainerDetailAction(containerGuid, "recalculate-costs", scope, "\u91CD\u7B97\u6210\u672C\u5931\u8D25");
}
async function createContainer(data) {
  const response = await request_default(API_BASE, {
    method: "POST",
    data
  });
  ensureSuccess(response.success, response.message, "\u521B\u5EFA\u8D27\u67DC\u5931\u8D25");
  return response.data?.containerGuid ?? "";
}
async function updateContainer(containerGuid, data) {
  const response = await request_default(`${API_BASE}/${encodeURIComponent(containerGuid)}`, {
    method: "PUT",
    data
  });
  ensureSuccess(response.success, response.message, "\u66F4\u65B0\u8D27\u67DC\u5931\u8D25");
  return true;
}
async function batchUpdateDetails(updates) {
  const response = await request_default(`${API_BASE}/batch-update-details`, {
    method: "POST",
    data: updates.map((item) => ({
      HGUID: item.hguid,
      \u8C03\u6574\u6D6E\u7387: item.\u8C03\u6574\u6D6E\u7387,
      \u56FD\u5185\u4EF7\u683C: item.\u56FD\u5185\u4EF7\u683C,
      \u8FDB\u53E3\u4EF7\u683C: item.\u8FDB\u53E3\u4EF7\u683C,
      \u8FD0\u8F93\u6210\u672C: item.\u8FD0\u8F93\u6210\u672C,
      \u5546\u54C1\u540D\u79F0: item.\u5546\u54C1\u540D\u79F0,
      \u82F1\u6587\u540D\u79F0: item.\u82F1\u6587\u540D\u79F0,
      ClearEnglishName: item.ClearEnglishName,
      ProductCategoryGUID: item.ProductCategoryGUID,
      \u8D34\u724C\u4EF7\u683C: item.\u8D34\u724C\u4EF7\u683C,
      \u5355\u4EF6\u88C5\u7BB1\u6570: item.\u5355\u4EF6\u88C5\u7BB1\u6570,
      \u4E2D\u5305\u6570: item.\u4E2D\u5305\u6570,
      \u5355\u4EF6\u4F53\u79EF: item.\u5355\u4EF6\u4F53\u79EF,
      \u88C5\u67DC\u6570\u91CF: item.\u88C5\u67DC\u6570\u91CF,
      \u5408\u8BA1\u88C5\u67DC\u4F53\u79EF: item.\u5408\u8BA1\u88C5\u67DC\u4F53\u79EF,
      \u5408\u8BA1\u88C5\u67DC\u91D1\u989D: item.\u5408\u8BA1\u88C5\u67DC\u91D1\u989D,
      IsActive: item.IsActive,
      SkipRelatedProductSync: item.SkipRelatedProductSync
    }))
  });
  ensureSuccess(response.success, response.message, "\u6279\u91CF\u66F4\u65B0\u8D27\u67DC\u660E\u7EC6\u5931\u8D25");
  return {
    totalUpdated: response.data?.totalUpdated ?? updates.length,
    totalRequested: response.data?.totalRequested ?? updates.length
  };
}
function normalizeAlignDomesticProductCodeResult(result) {
  return {
    oldProductCode: result?.oldProductCode ?? result?.OldProductCode ?? "",
    OldProductCode: result?.OldProductCode,
    newProductCode: result?.newProductCode ?? result?.NewProductCode ?? "",
    NewProductCode: result?.NewProductCode,
    updatedDomesticProducts: result?.updatedDomesticProducts ?? result?.UpdatedDomesticProducts ?? 0,
    UpdatedDomesticProducts: result?.UpdatedDomesticProducts,
    updatedContainerDetails: result?.updatedContainerDetails ?? result?.UpdatedContainerDetails ?? 0,
    UpdatedContainerDetails: result?.UpdatedContainerDetails,
    updatedDomesticSetProducts: result?.updatedDomesticSetProducts ?? result?.UpdatedDomesticSetProducts ?? 0,
    UpdatedDomesticSetProducts: result?.UpdatedDomesticSetProducts,
    updatedProductGrades: result?.updatedProductGrades ?? result?.UpdatedProductGrades ?? 0,
    UpdatedProductGrades: result?.UpdatedProductGrades,
    updatedDomesticProductCreationLogs: result?.updatedDomesticProductCreationLogs ?? result?.UpdatedDomesticProductCreationLogs ?? 0,
    UpdatedDomesticProductCreationLogs: result?.UpdatedDomesticProductCreationLogs
  };
}
async function alignDomesticProductCode(payload) {
  const response = await request_default(
    `${API_BASE}/details/align-domestic-product-code`,
    {
      method: "POST",
      data: {
        DetailHguid: payload.detailHguid,
        ExpectedDomesticProductCode: payload.expectedDomesticProductCode,
        TargetProductCode: payload.targetProductCode,
        SupplierCode: payload.supplierCode
      }
    }
  );
  ensureSuccess(response.success ?? response.isSuccess, response.message, "\u5BF9\u9F50\u56FD\u5185\u5546\u54C1\u7F16\u7801\u5931\u8D25");
  return normalizeAlignDomesticProductCodeResult(response.data);
}
async function syncContainersFromHq(startDate) {
  const response = await request_default(`${API_BASE}/sync-from-hq`, {
    method: "POST",
    data: { startDate }
  });
  ensureSuccess(response.success, response.message, "\u4ECEHQ\u540C\u6B65\u8D27\u67DC\u5931\u8D25");
  return response.data ?? { isSuccess: response.success, message: response.message };
}
async function translateHqProductNamesByContainerNumber(containerNumber) {
  const response = await request_default(
    "/api/react/v1/hq-products/translate-names/by-container-number",
    {
      method: "POST",
      data: {
        ContainerNumbers: [containerNumber],
        OverwriteExisting: false
      }
    }
  );
  ensureSuccess(response.success, response.message, "\u7FFB\u8BD1HQ\u6570\u636E\u5931\u8D25");
  return normalizeHqTranslationResult(response.data ?? response);
}
async function getComingSoonContainerSummaries() {
  const response = await request_default(
    `${API_BASE}/coming-soon/summaries`,
    { method: "GET" }
  );
  const containers = Array.isArray(response) ? response : response.data ?? [];
  return containers.sort((left, right) => {
    const leftDate = left.\u5B9E\u9645\u5230\u8D27\u65E5\u671F || left.\u9884\u8BA1\u5230\u5CB8\u65E5\u671F;
    const rightDate = right.\u5B9E\u9645\u5230\u8D27\u65E5\u671F || right.\u9884\u8BA1\u5230\u5CB8\u65E5\u671F;
    return toTimestamp(leftDate) - toTimestamp(rightDate);
  });
}
async function getComingSoonContainerProducts(containerGuid) {
  const response = await request_default(
    `${API_BASE}/coming-soon/${containerGuid}/products`,
    { method: "GET" }
  );
  const products = Array.isArray(response) ? response : response.data ?? [];
  return products.map(toComingSoonProduct).sort(compareComingSoonProducts);
}

// src/services/containerService.test.ts
function assertEqual(actual, expected, label) {
  if (actual !== expected) {
    throw new Error(`${label}. Expected: ${String(expected)}, received: ${String(actual)}`);
  }
}
function assertDeepEqual(actual, expected, label) {
  const actualJson = JSON.stringify(actual);
  const expectedJson = JSON.stringify(expected);
  if (actualJson !== expectedJson) {
    throw new Error(`${label}. Expected: ${expectedJson}, received: ${actualJson}`);
  }
}
async function assertRejects(execute, expectedMessage, label) {
  try {
    await execute();
  } catch (error) {
    const actualMessage = error instanceof Error ? error.message : String(error);
    assertEqual(actualMessage, expectedMessage, label);
    return;
  }
  throw new Error(`${label}. Expected promise to reject`);
}
var originalFetch = globalThis.fetch;
var capturedUrl = "";
var capturedInit;
var capturedUrls = [];
globalThis.fetch = async (input, init) => {
  capturedUrl = String(input);
  capturedInit = init;
  return new Response(JSON.stringify({
    success: true,
    data: {
      TotalCandidates: 1,
      TotalTranslated: 1,
      TotalSkipped: 0,
      TotalFailed: 0,
      Samples: { \u81EA\u52A8\u8131\u6BDB\u68B3: "Pet Grooming Comb" }
    }
  }), {
    status: 200,
    headers: { "Content-Type": "application/json" }
  });
};
try {
  globalThis.fetch = async (input, init) => {
    capturedUrl = String(input);
    capturedInit = init;
    return new Response(JSON.stringify({ success: true }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
  };
  const updatePayload = {
    \u5B9E\u9645\u5230\u8D27\u65E5\u671F: "2026-06-16",
    \u6C47\u7387: 4.5,
    \u8FD0\u8D39: 1280,
    \u5907\u6CE8: "\u72B6\u6001\u5207\u6362\u6D4B\u8BD5",
    \u72B6\u6001: 1
  };
  await updateContainer("OOCU5568972", updatePayload);
  assertEqual(
    capturedUrl,
    "/api/react/v1/containers/OOCU5568972",
    "updateContainer should keep the React container update URL unchanged"
  );
  assertEqual(capturedInit?.method, "PUT", "updateContainer should use PUT");
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    updatePayload,
    "updateContainer should send container status with the header update payload"
  );
  globalThis.fetch = async (input, init) => {
    capturedUrl = String(input);
    capturedInit = init;
    return new Response(JSON.stringify({
      success: false,
      message: "\u8D27\u67DC\u7F16\u53F7 CSNU6209359 \u5728\u88C5\u67DC\u65E5\u671F 2026-05-29 \u5DF2\u5B58\u5728"
    }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
  };
  await assertRejects(
    () => createContainer({ \u8D27\u67DC\u7F16\u53F7: "CSNU6209359", \u88C5\u67DC\u65E5\u671F: "2026-05-29" }),
    "\u8D27\u67DC\u7F16\u53F7 CSNU6209359 \u5728\u88C5\u67DC\u65E5\u671F 2026-05-29 \u5DF2\u5B58\u5728",
    "createContainer \u5E94\u900F\u4F20\u540E\u7AEF\u8D27\u67DC\u7F16\u53F7\u548C\u88C5\u67DC\u65E5\u671F\u7EC4\u5408\u91CD\u590D\u63D0\u793A"
  );
  assertEqual(capturedUrl, "/api/react/v1/containers", "createContainer \u5E94\u8C03\u7528 React \u8D27\u67DC\u521B\u5EFA\u63A5\u53E3");
  assertEqual(capturedInit?.method, "POST", "createContainer \u5E94\u4F7F\u7528 POST");
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    { \u8D27\u67DC\u7F16\u53F7: "CSNU6209359", \u88C5\u67DC\u65E5\u671F: "2026-05-29" },
    "createContainer \u5E94\u7EE7\u7EED\u53D1\u9001\u8D27\u67DC\u7F16\u53F7\u548C\u88C5\u67DC\u65E5\u671F\u7ED9\u540E\u7AEF\u7EC4\u5408\u5224\u91CD"
  );
  globalThis.fetch = async (input, init) => {
    capturedUrl = String(input);
    capturedInit = init;
    return new Response(JSON.stringify({ success: true }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
  };
  const detailUpdates = [
    {
      hguid: "D-CLEAR-EN",
      ClearEnglishName: true,
      \u4E2D\u5305\u6570: 12,
      ProductCategoryGUID: "CAT-TARGET",
      SkipRelatedProductSync: true
    }
  ];
  await batchUpdateDetails(detailUpdates);
  assertEqual(
    capturedUrl,
    "/api/react/v1/containers/batch-update-details",
    "batchUpdateDetails should keep the React detail update URL unchanged"
  );
  assertEqual(capturedInit?.method, "POST", "batchUpdateDetails should use POST");
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    [{ HGUID: "D-CLEAR-EN", ClearEnglishName: true, ProductCategoryGUID: "CAT-TARGET", \u4E2D\u5305\u6570: 12, SkipRelatedProductSync: true }],
    "batchUpdateDetails should send explicit fields including the related-product sync guard"
  );
  globalThis.fetch = async (input, init) => {
    capturedUrl = String(input);
    capturedInit = init;
    return new Response(JSON.stringify({
      success: true,
      data: {
        oldProductCode: "DOM-OLD",
        newProductCode: "LOCAL-NEW",
        updatedDomesticProducts: 1,
        updatedContainerDetails: 2
      }
    }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
  };
  const alignResult = await alignDomesticProductCode({
    detailHguid: "D-ALIGN",
    expectedDomesticProductCode: "DOM-OLD",
    targetProductCode: "LOCAL-NEW",
    supplierCode: "200"
  });
  assertEqual(
    capturedUrl,
    "/api/react/v1/containers/details/align-domestic-product-code",
    "alignDomesticProductCode should call the manual alignment endpoint"
  );
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    {
      DetailHguid: "D-ALIGN",
      ExpectedDomesticProductCode: "DOM-OLD",
      TargetProductCode: "LOCAL-NEW",
      SupplierCode: "200"
    },
    "alignDomesticProductCode should send the confirmed old and target product codes"
  );
  assertDeepEqual(
    {
      oldProductCode: alignResult.oldProductCode,
      newProductCode: alignResult.newProductCode,
      updatedDomesticProducts: alignResult.updatedDomesticProducts,
      updatedContainerDetails: alignResult.updatedContainerDetails
    },
    {
      oldProductCode: "DOM-OLD",
      newProductCode: "LOCAL-NEW",
      updatedDomesticProducts: 1,
      updatedContainerDetails: 2
    },
    "alignDomesticProductCode should normalize response counts"
  );
  const abortController = new AbortController();
  globalThis.fetch = async (input, init) => {
    capturedUrl = String(input);
    capturedInit = init;
    return new Response(JSON.stringify({
      success: true,
      data: {
        items: [{ id: 101, hguid: "remote-101", \u5546\u54C1\u540D\u79F0: "\u8FDC\u7A0B\u660E\u7EC6" }],
        itemsTotal: 12,
        pageNumber: 2,
        pageSize: 20,
        hasMore: true,
        totalComputed: false,
        statsComputed: false,
        tagStats: { all: 12, new: 3, existing: 9, noOemPrice: 1, abnormalImport: 2, active: 8, inactive: 4 }
      }
    }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
  };
  const queryResult = await queryContainerProducts("GUID/\u9700\u8981\u7F16\u7801", {
    pageNumber: 2,
    pageSize: 20,
    itemNumber: "HB308",
    selectedTags: ["new", "inactive"],
    sortBy: "itemNumber",
    sortOrder: "ascend",
    includeTotal: false,
    includeStats: false
  }, abortController.signal);
  assertEqual(
    capturedUrl,
    "/api/react/v1/containers/GUID%2F%E9%9C%80%E8%A6%81%E7%BC%96%E7%A0%81/products/query",
    "queryContainerProducts \u5E94\u8C03\u7528\u6309\u8D27\u67DC GUID \u7F16\u7801\u540E\u7684\u8FDC\u7A0B\u67E5\u8BE2\u63A5\u53E3"
  );
  assertEqual(capturedInit?.method, "POST", "queryContainerProducts \u5E94\u4F7F\u7528 POST");
  assertEqual(capturedInit?.signal, abortController.signal, "queryContainerProducts \u5E94\u900F\u4F20 AbortSignal");
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    {
      containerGuid: "GUID/\u9700\u8981\u7F16\u7801",
      pageNumber: 2,
      pageSize: 20,
      itemNumber: "HB308",
      selectedTags: ["new", "inactive"],
      sortBy: "itemNumber",
      sortOrder: "ascend",
      includeTotal: false,
      includeStats: false
    },
    "queryContainerProducts \u5E94\u53D1\u9001\u8FDC\u7A0B\u67E5\u8BE2 body \u4E14\u4FDD\u7559 containerGuid"
  );
  assertDeepEqual(
    queryResult,
    {
      items: [{ id: 101, hguid: "remote-101", \u5546\u54C1\u540D\u79F0: "\u8FDC\u7A0B\u660E\u7EC6" }],
      itemsTotal: 12,
      pageNumber: 2,
      pageSize: 20,
      hasMore: true,
      totalComputed: false,
      statsComputed: false,
      tagStats: { all: 12, new: 3, existing: 9, noOemPrice: 1, abnormalImport: 2, active: 8, inactive: 4 }
    },
    "queryContainerProducts \u5E94\u8FD4\u56DE data \u5185\u7684\u5206\u9875\u660E\u7EC6\u7ED3\u679C"
  );
  globalThis.fetch = async (input, init) => {
    capturedUrl = String(input);
    capturedInit = init;
    return new Response(JSON.stringify({
      success: true,
      data: { totalUpdated: 87, totalRequested: 87 }
    }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
  };
  const recalculateResult = await recalculateContainerCostsByScope("WHOLE-CONTAINER-GUID", {
    query: {
      containerGuid: "WHOLE-CONTAINER-GUID",
      pageNumber: 1,
      pageSize: 50,
      selectedTags: []
    }
  });
  assertEqual(
    capturedUrl,
    "/api/react/v1/containers/WHOLE-CONTAINER-GUID/actions/recalculate-costs",
    "recalculateContainerCostsByScope \u5E94\u8C03\u7528\u8D27\u67DC\u6210\u672C\u91CD\u7B97\u63A5\u53E3"
  );
  assertEqual(capturedInit?.method, "POST", "recalculateContainerCostsByScope \u5E94\u4F7F\u7528 POST");
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    {
      query: {
        containerGuid: "WHOLE-CONTAINER-GUID",
        pageNumber: 1,
        pageSize: 50,
        selectedTags: []
      }
    },
    "recalculateContainerCostsByScope \u5E94\u539F\u6837\u53D1\u9001\u6574\u67DC query scope"
  );
  assertDeepEqual(
    recalculateResult,
    { totalUpdated: 87, totalRequested: 87 },
    "recalculateContainerCostsByScope \u5E94\u8FD4\u56DE\u540E\u7AEF\u66F4\u65B0\u7EDF\u8BA1"
  );
  globalThis.fetch = async (input, init) => {
    capturedUrl = String(input);
    capturedInit = init;
    return new Response(JSON.stringify({
      success: true,
      data: {
        TotalCandidates: 1,
        TotalTranslated: 1,
        TotalSkipped: 0,
        TotalFailed: 0,
        Samples: { \u81EA\u52A8\u8131\u6BDB\u68B3: "Pet Grooming Comb" }
      }
    }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
  };
  await translateHqProductNamesByContainerNumber("CSNU6601647");
  assertEqual(
    capturedUrl,
    "/api/react/v1/hq-products/translate-names/by-container-number",
    "HQ container translation should call the body-based endpoint"
  );
  assertEqual(capturedInit?.method, "POST", "HQ container translation should use POST");
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    { ContainerNumbers: ["CSNU6601647"], OverwriteExisting: false },
    "HQ container translation should send container numbers in the request body"
  );
  globalThis.fetch = async () => new Response(JSON.stringify({
    success: true,
    data: {
      totalCandidates: 9,
      totalTranslated: 7,
      totalSkipped: 1,
      totalFailed: 1,
      samples: { \u5927\u8349\u8393: "Large Strawberry" }
    }
  }), {
    status: 200,
    headers: { "Content-Type": "application/json" }
  });
  const camelCaseTranslation = await translateHqProductNamesByContainerNumber("CSGU7149907");
  assertDeepEqual(
    camelCaseTranslation,
    {
      TotalCandidates: 9,
      TotalTranslated: 7,
      TotalSkipped: 1,
      TotalFailed: 1,
      Samples: { \u5927\u8349\u8393: "Large Strawberry" }
    },
    "HQ container translation should normalize camelCase backend statistics"
  );
  globalThis.fetch = async (input, init) => {
    capturedUrl = String(input);
    capturedInit = init;
    return new Response(JSON.stringify({
      success: false,
      message: "HQ \u540C\u6B65\u5931\u8D25\uFF1A\u4E1A\u52A1\u9519\u8BEF",
      data: {
        isSuccess: false,
        message: "\u4E0D\u5E94\u8FD4\u56DE\u6210\u529F\u7ED3\u679C"
      }
    }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
  };
  await assertRejects(
    () => syncContainersFromHq("2026-05-01"),
    "HQ \u540C\u6B65\u5931\u8D25\uFF1A\u4E1A\u52A1\u9519\u8BEF",
    "syncContainersFromHq should throw backend message when success is false"
  );
  assertEqual(capturedUrl, "/api/react/v1/containers/sync-from-hq", "syncContainersFromHq should keep the sync URL unchanged");
  assertEqual(capturedInit?.method, "POST", "syncContainersFromHq should use POST");
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    { startDate: "2026-05-01" },
    "syncContainersFromHq should keep the request body unchanged"
  );
  globalThis.fetch = async () => new Response(JSON.stringify({
    success: false,
    message: "HQ \u540C\u6B65\u5931\u8D25\uFF1AHTTP \u9519\u8BEF"
  }), {
    status: 500,
    headers: { "Content-Type": "application/json" }
  });
  await assertRejects(
    () => syncContainersFromHq(),
    "HQ \u540C\u6B65\u5931\u8D25\uFF1AHTTP \u9519\u8BEF",
    "syncContainersFromHq should throw backend message when HTTP status is not 2xx"
  );
  capturedUrls.length = 0;
  globalThis.fetch = async (input, init) => {
    capturedUrl = String(input);
    capturedInit = init;
    capturedUrls.push(capturedUrl);
    return new Response(JSON.stringify({
      success: true,
      data: [
        { id: 1, hguid: "ARRIVED-GUID", \u8D27\u67DC\u7F16\u53F7: "ARRIVED-1", \u5B9E\u9645\u5230\u8D27\u65E5\u671F: "2026-06-01" },
        { id: 2, hguid: "UPCOMING-GUID", \u8D27\u67DC\u7F16\u53F7: "UPCOMING-1", \u9884\u8BA1\u5230\u5CB8\u65E5\u671F: "2026-06-16" }
      ]
    }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
  };
  const summaries = await getComingSoonContainerSummaries();
  assertEqual(summaries.length, 2, "Coming Soon \u6458\u8981\u5E94\u8FD4\u56DE\u8D27\u67DC\u5934\u5217\u8868");
  assertEqual(
    capturedUrls.filter((url) => url.includes("/products")).length,
    0,
    "Coming Soon \u6458\u8981\u9996\u5C4F\u4E0D\u5E94\u63D0\u524D\u8BF7\u6C42\u8D27\u67DC\u5546\u54C1\u660E\u7EC6"
  );
  assertEqual(
    capturedUrl,
    "/api/react/v1/containers/coming-soon/summaries",
    "Coming Soon \u6458\u8981\u5E94\u8C03\u7528\u540E\u7AEF\u5171\u4EAB\u7F13\u5B58\u4E13\u7528\u63A5\u53E3"
  );
  assertEqual(capturedInit?.method, "GET", "Coming Soon \u6458\u8981\u5E94\u4F7F\u7528 GET \u7F13\u5B58\u63A5\u53E3");
  globalThis.fetch = async (input, init) => {
    capturedUrl = String(input);
    capturedInit = init;
    return new Response(JSON.stringify({
      success: true,
      data: [
        {
          id: 1,
          hguid: "DETAIL-1",
          \u5546\u54C1\u7F16\u7801: "P-3",
          \u5546\u54C1\u4FE1\u606F: { \u8D27\u53F7: "HB10", \u6761\u5F62\u7801: "9300000000100", \u5546\u54C1\u540D\u79F0: "Item 10", \u96F6\u552E\u4EF7\u683C: 1.99 },
          \u88C5\u67DC\u6570\u91CF: 10,
          \u662F\u5426\u65B0\u5546\u54C1: false
        },
        {
          id: 2,
          hguid: "DETAIL-2",
          \u5546\u54C1\u7F16\u7801: "P-1",
          \u5546\u54C1\u4FE1\u606F: { \u8D27\u53F7: "HB2", \u5546\u54C1\u540D\u79F0: "Item 2" },
          \u88C5\u67DC\u6570\u91CF: 20,
          \u662F\u5426\u65B0\u5546\u54C1: true
        },
        {
          id: 3,
          hguid: "DETAIL-3",
          \u5546\u54C1\u7F16\u7801: "P-2",
          \u5546\u54C1\u4FE1\u606F: { \u5546\u54C1\u540D\u79F0: "No Item Number" },
          \u88C5\u67DC\u6570\u91CF: 30,
          \u662F\u5426\u65B0\u5546\u54C1: false
        }
      ]
    }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
  };
  const products = await getComingSoonContainerProducts("CONTAINER-GUID");
  assertEqual(
    capturedUrl,
    "/api/react/v1/containers/coming-soon/CONTAINER-GUID/products",
    "Coming Soon \u5355\u8D27\u67DC\u5546\u54C1\u5E94\u8C03\u7528\u540E\u7AEF\u5171\u4EAB\u7F13\u5B58\u4E13\u7528\u63A5\u53E3"
  );
  assertEqual(capturedInit?.method, "GET", "Coming Soon \u5355\u8D27\u67DC\u5546\u54C1\u5E94\u4F7F\u7528 GET");
  assertDeepEqual(
    products.map((item) => item.itemNumber ?? ""),
    ["HB2", "HB10", ""],
    "Coming Soon \u5355\u8D27\u67DC\u5546\u54C1\u5E94\u6309\u8D27\u53F7\u81EA\u7136\u6392\u5E8F\uFF0C\u7A7A\u8D27\u53F7\u6392\u6700\u540E"
  );
  assertEqual(
    products.find((item) => item.itemNumber === "HB10")?.barcode,
    "9300000000100",
    "Coming Soon \u5355\u8D27\u67DC\u5546\u54C1\u5E94\u6620\u5C04\u5546\u54C1\u6761\u5F62\u7801\u7528\u4E8E\u751F\u6210\u6761\u7801\u56FE"
  );
  assertEqual(
    products.find((item) => item.itemNumber === "HB10")?.retailPrice,
    1.99,
    "Coming Soon \u5355\u8D27\u67DC\u5546\u54C1\u5E94\u6620\u5C04\u5546\u54C1\u5EFA\u8BAE\u96F6\u552E\u4EF7"
  );
  const setCodeAbortController = new AbortController();
  globalThis.fetch = async (input, init) => {
    capturedUrl = String(input);
    capturedInit = init;
    return new Response(JSON.stringify({
      success: true,
      data: [
        {
          productCode: "P/\u5957\u88C5",
          setProductCode: "SET-1",
          setItemNumber: "HB137-480-01",
          barcode: "9525811370252",
          retailPrice: 11.47,
          purchasePrice: 3.04
        }
      ]
    }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
  };
  const setCodes = await getContainerDomesticSetCodes("P/\u5957\u88C5", setCodeAbortController.signal);
  assertEqual(
    capturedUrl,
    "/api/react/v1/containers/products/P%2F%E5%A5%97%E8%A3%85/domestic-set-codes",
    "getContainerDomesticSetCodes \u5E94\u6309\u5546\u54C1\u7F16\u7801\u7F16\u7801\u540E\u8BF7\u6C42\u56FD\u5185\u5957\u88C5\u660E\u7EC6"
  );
  assertEqual(capturedInit?.method, "GET", "getContainerDomesticSetCodes \u5E94\u4F7F\u7528 GET");
  assertEqual(capturedInit?.signal, setCodeAbortController.signal, "getContainerDomesticSetCodes \u5E94\u900F\u4F20 AbortSignal");
  assertDeepEqual(
    setCodes,
    [{
      productCode: "P/\u5957\u88C5",
      setProductCode: "SET-1",
      setItemNumber: "HB137-480-01",
      barcode: "9525811370252",
      retailPrice: 11.47,
      purchasePrice: 3.04
    }],
    "getContainerDomesticSetCodes \u5E94\u8FD4\u56DE data \u5185\u56FD\u5185\u5957\u88C5\u660E\u7EC6"
  );
  globalThis.fetch = async (input, init) => {
    capturedUrl = String(input);
    capturedInit = init;
    return new Response(JSON.stringify({
      success: true,
      data: { updatedCount: 1 }
    }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
  };
  const updateSetCodeResult = await updateContainerDomesticSetCodePrices("P/\u5957\u88C5", [
    { setProductCode: "SET-1", retailPrice: 12.34, purchasePrice: 4.56 }
  ]);
  assertEqual(
    capturedUrl,
    "/api/react/v1/containers/products/P%2F%E5%A5%97%E8%A3%85/domestic-set-codes/prices",
    "updateContainerDomesticSetCodePrices \u5E94\u6309\u5546\u54C1\u7F16\u7801\u7F16\u7801\u540E\u8BF7\u6C42\u4EF7\u683C\u56DE\u5199\u63A5\u53E3"
  );
  assertEqual(capturedInit?.method, "PATCH", "updateContainerDomesticSetCodePrices \u5E94\u4F7F\u7528 PATCH");
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    { items: [{ setProductCode: "SET-1", retailPrice: 12.34, purchasePrice: 4.56 }] },
    "updateContainerDomesticSetCodePrices \u5E94\u53EA\u53D1\u9001\u5957\u88C5\u7F16\u7801\u548C\u4EF7\u683C\u5B57\u6BB5"
  );
  assertDeepEqual(updateSetCodeResult, { updatedCount: 1 }, "updateContainerDomesticSetCodePrices \u5E94\u8FD4\u56DE\u66F4\u65B0\u6570\u91CF");
  globalThis.fetch = async (input, init) => {
    capturedUrl = String(input);
    capturedInit = init;
    return new Response(JSON.stringify({
      success: false,
      message: "\u4FDD\u5B58\u5931\u8D25"
    }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
  };
  await assertRejects(
    () => updateContainerDomesticSetCodePrices("P-FAIL", [{ setProductCode: "SET-FAIL", retailPrice: 1, purchasePrice: 2 }]),
    "\u4FDD\u5B58\u5931\u8D25",
    "updateContainerDomesticSetCodePrices \u5E94\u900F\u4F20\u540E\u7AEF\u5931\u8D25\u6D88\u606F"
  );
} finally {
  globalThis.fetch = originalFetch;
}
