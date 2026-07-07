// <define:import.meta.env>
var define_import_meta_env_default = {};

// src/services/warehouseProductService.categoryFilter.test.ts
import { deepStrictEqual } from "node:assert/strict";

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

// src/services/productHqSyncPolling.ts
var PRODUCT_HQ_SYNC_TIMEOUT_MS = 10 * 60 * 1e3;

// src/services/warehouseProductService.ts
var API_BASE = "/api/react/v1/product-warehouse";
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
function transformWarehouseProduct(raw) {
  const localSupplier = readRecord(raw.localSupplier ?? raw.LocalSupplier);
  return {
    id: readString(raw.productCode, raw.ProductCode, raw.id) ?? "",
    productCode: readString(raw.productCode, raw.ProductCode) ?? "",
    name: readString(raw.productName, raw.ProductName) ?? "",
    nameEn: readString(raw.englishName, raw.EnglishName),
    itemNumber: readString(raw.itemNumber, raw.ItemNumber) ?? "",
    barcode: readString(raw.barcode, raw.Barcode),
    locationCodes: readStringArray(raw.locationCodes, raw.LocationCodes, raw.locationCode, raw.LocationCode),
    locationBarcodes: readStringArray(raw.locationBarcodes, raw.LocationBarcodes),
    categoryName: readString(raw.categoryName, raw.CategoryName),
    warehouseCategoryGUID: readString(
      raw.warehouseCategoryGUID,
      raw.WarehouseCategoryGUID,
      raw.productCategoryGUID,
      raw.ProductCategoryGUID
    ),
    categoryPath: readString(raw.categoryPath, raw.CategoryPath, raw.categoryFullPath, raw.CategoryFullPath),
    domesticSupplierName: readString(raw.domesticSupplierName, raw.DomesticSupplierName, raw.supplierName, raw.SupplierName),
    domesticSupplierCode: readString(raw.domesticSupplierCode, raw.DomesticSupplierCode, raw.supplierCode, raw.SupplierCode),
    localSupplierName: readString(
      raw.localSupplierName,
      raw.LocalSupplierName,
      localSupplier.localSupplierName,
      localSupplier.LocalSupplierName,
      localSupplier.name,
      localSupplier.Name
    ),
    // 澳洲供应商保持独立读取，避免把国内 SupplierName 误显示到澳洲供应商列。
    localSupplierCode: readString(
      raw.localSupplierCode,
      raw.LocalSupplierCode,
      localSupplier.localSupplierCode,
      localSupplier.LocalSupplierCode,
      localSupplier.code,
      localSupplier.Code
    ),
    domesticPrice: toNumber(raw.domesticPrice ?? raw.DomesticPrice),
    labelPrice: toNumber(raw.oemPrice ?? raw.OEMPrice),
    importPrice: toNumber(raw.importPrice ?? raw.ImportPrice),
    volume: toNumber(raw.volume ?? raw.Volume),
    isVolumeFallback: toBoolean(raw.isVolumeFallback ?? raw.IsVolumeFallback),
    packingQty: toNumber(raw.packingQuantity ?? raw.PackingQuantity),
    isPackingQtyFallback: toBoolean(raw.isPackingQuantityFallback ?? raw.IsPackingQuantityFallback),
    minOrderQuantity: toNumber(raw.minOrderQuantity ?? raw.MinOrderQuantity),
    productType: toNumber(raw.productType ?? raw.ProductType) ?? 0,
    productImage: readString(raw.productImage, raw.ProductImage),
    isActive: toBoolean(raw.isActive ?? raw.IsActive, true),
    createdAt: readString(raw.createdAt, raw.CreatedAt),
    updatedAt: readString(raw.updatedAt, raw.UpdatedAt),
    updatedBy: readString(raw.updatedBy, raw.UpdatedBy),
    middlePackQty: toNumber(raw.middlePackQuantity ?? raw.MiddlePackQuantity)
  };
}
function normalizeWarehouseProductsTableResponse(payload, page, pageSize) {
  const result = payload;
  const rawItems = Array.isArray(result?.data) ? result.data : [];
  return {
    items: rawItems.filter((item) => !!item && typeof item === "object").map(transformWarehouseProduct).map((item, index) => ({
      ...item,
      rowNumber: (page - 1) * pageSize + index + 1
    })),
    total: typeof result?.total === "number" ? result.total : 0,
    page,
    pageSize
  };
}
async function getWarehouseProductsTable(query) {
  const sanitizeFilters = (filters2) => {
    const sanitizedEntries = Object.entries(filters2 ?? {}).flatMap(([field, values]) => {
      const validValues = Array.isArray(values) ? values.filter((value) => typeof value === "string" && value.trim().length > 0) : [];
      return validValues.length > 0 ? [[field, validValues]] : [];
    });
    return sanitizedEntries.length > 0 ? Object.fromEntries(sanitizedEntries) : void 0;
  };
  const filters = sanitizeFilters({
    ...query.filters ?? {},
    ...query.supplierCode ? { domesticSupplierCode: [query.supplierCode] } : {},
    ...query.productType !== void 0 ? { productType: [String(query.productType)] } : {},
    ...query.isActive !== void 0 ? { isActive: [String(query.isActive)] } : {}
  });
  const uncategorizedOnly = query.uncategorizedOnly === true || query.categoryFilter === "uncategorized";
  const response = await request_default(`${API_BASE}/table`, {
    method: "POST",
    data: {
      Page: query.page,
      PageSize: query.pageSize,
      SortBy: query.sortField,
      SortOrder: query.sortOrder,
      GlobalSearch: query.searchText || void 0,
      Filters: filters,
      CategoryGuids: query.categoryGuid ? [query.categoryGuid] : void 0,
      IncludeSubCategories: true,
      UncategorizedOnly: query.categoryGuid ? false : uncategorizedOnly
    }
  });
  return normalizeWarehouseProductsTableResponse(response, query.page, query.pageSize);
}

// src/services/warehouseProductService.categoryFilter.test.ts
function assertDeepEqual(actual, expected, message) {
  try {
    deepStrictEqual(actual, expected);
  } catch (error) {
    const detail = error instanceof Error ? error.message : String(error);
    throw new Error(`${message}\u3002${detail}`);
  }
}
function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}
var originalFetch = globalThis.fetch;
var capturedBody;
globalThis.fetch = async (_input, init) => {
  capturedBody = JSON.parse(String(init?.body ?? "{}"));
  return new Response(JSON.stringify({ success: true, data: [], total: 0 }), {
    status: 200,
    headers: { "Content-Type": "application/json" }
  });
};
try {
  await getWarehouseProductsTable({
    page: 1,
    pageSize: 20,
    supplierCode: "SUP-001",
    productType: 2,
    isActive: false,
    filters: {
      productName: ["\u6536\u7EB3\u7BB1"],
      localSupplierCode: ["LS-001"],
      locationCodes: ["A-01"],
      volume: ["gte:1.5", "lte:9.9"],
      createdAt: ["gte:2026-06-01", "lte:2026-06-15"],
      domesticSupplierCode: ["SHOULD-BE-OVERRIDDEN"],
      productType: ["1"],
      isActive: ["true"]
    }
  });
  assert(capturedBody, "\u5E94\u6355\u83B7\u5217\u5934\u8FC7\u6EE4\u67E5\u8BE2\u8BF7\u6C42\u4F53");
  assertDeepEqual(
    capturedBody.Filters,
    {
      productName: ["\u6536\u7EB3\u7BB1"],
      localSupplierCode: ["LS-001"],
      locationCodes: ["A-01"],
      volume: ["gte:1.5", "lte:9.9"],
      createdAt: ["gte:2026-06-01", "lte:2026-06-15"],
      domesticSupplierCode: ["SUP-001"],
      productType: ["2"],
      isActive: ["false"]
    },
    "\u5217\u5934\u8FC7\u6EE4\u5E94\u5408\u5E76\u8FDB Filters\uFF0C\u9876\u90E8\u7B5B\u9009\u5B57\u6BB5\u5E94\u8986\u76D6\u540C\u540D\u5217\u5934\u8FC7\u6EE4"
  );
  await getWarehouseProductsTable({
    page: 1,
    pageSize: 20,
    filters: {
      productType: ["1", "2"],
      isActive: ["true"]
    }
  });
  assert(capturedBody, "\u5E94\u6355\u83B7\u7EAF\u679A\u4E3E\u5217\u5934\u8FC7\u6EE4\u67E5\u8BE2\u8BF7\u6C42\u4F53");
  assertDeepEqual(
    capturedBody.Filters,
    {
      productType: ["1", "2"],
      isActive: ["true"]
    },
    "\u65E0\u9876\u90E8\u7B5B\u9009\u65F6\uFF0C\u679A\u4E3E\u5217\u5934\u8FC7\u6EE4\u5E94\u6309\u539F\u503C\u8FDB\u5165 Filters"
  );
  await getWarehouseProductsTable({
    page: 1,
    pageSize: 20,
    filters: {
      productName: [" ", ""],
      barcode: [],
      volume: ["gte:1"]
    }
  });
  assert(capturedBody, "\u5E94\u6355\u83B7\u7A7A\u5217\u5934\u8FC7\u6EE4\u6E05\u7406\u8BF7\u6C42\u4F53");
  assertDeepEqual(
    capturedBody.Filters,
    {
      volume: ["gte:1"]
    },
    "\u7A7A\u6570\u7EC4\u548C\u7A7A\u767D\u5B57\u7B26\u4E32\u5E94\u5728\u53D1\u9001\u524D\u88AB\u6E05\u7406\uFF0C\u53EA\u4FDD\u7559\u6709\u6548\u5217\u5934\u8FC7\u6EE4"
  );
  await getWarehouseProductsTable({
    page: 1,
    pageSize: 20,
    categoryFilter: "all"
  });
  assert(capturedBody, "\u5E94\u6355\u83B7\u5168\u90E8\u5546\u54C1\u67E5\u8BE2\u8BF7\u6C42\u4F53");
  assertDeepEqual(
    capturedBody.Filters,
    void 0,
    "\u65E0\u6709\u6548\u7B5B\u9009\u6761\u4EF6\u65F6\u4E0D\u5E94\u53D1\u9001\u7A7A Filters"
  );
  assertDeepEqual(
    capturedBody.CategoryGuids,
    void 0,
    "ALL \u67E5\u8BE2\u4E0D\u5E94\u9644\u52A0\u5177\u4F53\u5206\u7C7B\u8FC7\u6EE4\u6761\u4EF6"
  );
  assertDeepEqual(
    capturedBody.UncategorizedOnly,
    false,
    "ALL \u67E5\u8BE2\u4E0D\u5E94\u542F\u7528\u672A\u5206\u7C7B\u8FC7\u6EE4"
  );
  await getWarehouseProductsTable({
    page: 1,
    pageSize: 20,
    categoryFilter: "uncategorized"
  });
  assert(capturedBody, "\u5E94\u6355\u83B7\u7A7A\u5206\u7C7B\u67E5\u8BE2\u8BF7\u6C42\u4F53");
  assertDeepEqual(
    capturedBody.UncategorizedOnly,
    true,
    "\u7A7A\u5206\u7C7B\u67E5\u8BE2\u5E94\u901A\u8FC7 UncategorizedOnly \u4F20\u7ED9\u8868\u683C\u63A5\u53E3"
  );
  await getWarehouseProductsTable({
    page: 1,
    pageSize: 20,
    categoryGuid: "cat-guid-1",
    filters: {
      productName: ["\u5206\u7C7B\u5546\u54C1"]
    }
  });
  assert(capturedBody, "\u5E94\u6355\u83B7\u5177\u4F53\u5206\u7C7B\u67E5\u8BE2\u8BF7\u6C42\u4F53");
  assertDeepEqual(
    capturedBody.CategoryGuids,
    ["cat-guid-1"],
    "\u5177\u4F53\u5206\u7C7B\u67E5\u8BE2\u5E94\u901A\u8FC7 CategoryGuids \u4F20\u7ED9\u8868\u683C\u63A5\u53E3"
  );
  assertDeepEqual(
    capturedBody.IncludeSubCategories,
    true,
    "\u5177\u4F53\u5206\u7C7B\u67E5\u8BE2\u5E94\u9ED8\u8BA4\u5305\u542B\u5B50\u5206\u7C7B"
  );
  assertDeepEqual(
    capturedBody.Filters,
    { productName: ["\u5206\u7C7B\u5546\u54C1"] },
    "\u5206\u7C7B\u67E5\u8BE2\u4ECD\u5E94\u628A\u666E\u901A\u5217\u5934\u8FC7\u6EE4\u7559\u5728 Filters"
  );
  await getWarehouseProductsTable({
    page: 1,
    pageSize: 20,
    categoryGuid: "cat-guid-1",
    uncategorizedOnly: true
  });
  assert(capturedBody, "\u5E94\u6355\u83B7\u5206\u7C7B\u4F18\u5148\u7EA7\u67E5\u8BE2\u8BF7\u6C42\u4F53");
  assertDeepEqual(
    capturedBody.UncategorizedOnly,
    false,
    "\u5177\u4F53\u5206\u7C7B\u548C\u672A\u5206\u7C7B\u540C\u65F6\u5B58\u5728\u65F6\u5E94\u4EE5\u5177\u4F53\u5206\u7C7B\u4F18\u5148"
  );
  await getWarehouseProductsTable({
    page: 1,
    pageSize: 20,
    categoryFilter: "uncategorized",
    filters: {
      productName: ["\u672A\u5206\u7C7B\u5546\u54C1"],
      updatedAt: ["gte:2026-06-10", "lte:2026-06-16"]
    }
  });
  assert(capturedBody, "\u5E94\u6355\u83B7\u672A\u5206\u7C7B\u5217\u5934\u8FC7\u6EE4\u8BF7\u6C42\u4F53");
  assertDeepEqual(
    capturedBody.UncategorizedOnly,
    true,
    "\u672A\u5206\u7C7B\u7B5B\u9009\u4ECD\u5E94\u901A\u8FC7\u9876\u5C42 UncategorizedOnly \u4F20\u9012"
  );
  assertDeepEqual(
    capturedBody.CategoryGuids,
    void 0,
    "\u672A\u5206\u7C7B\u7B5B\u9009\u4E0D\u5E94\u6DF7\u5165 CategoryGuids"
  );
  assertDeepEqual(
    capturedBody.Filters,
    {
      productName: ["\u672A\u5206\u7C7B\u5546\u54C1"],
      updatedAt: ["gte:2026-06-10", "lte:2026-06-16"]
    },
    "\u672A\u5206\u7C7B\u573A\u666F\u4E0B\u666E\u901A\u5217\u5934\u8FC7\u6EE4\u4ECD\u5E94\u4FDD\u7559\u5728 Filters"
  );
  globalThis.fetch = async (_input, init) => {
    capturedBody = JSON.parse(String(init?.body ?? "{}"));
    return new Response(JSON.stringify({
      success: true,
      data: [
        {
          ProductCode: "P001",
          ProductName: "\u5206\u7C7B\u5546\u54C1",
          ItemNumber: "HB-001",
          CategoryName: "\u6850\u8349\u5DE5\u827A2",
          WarehouseCategoryGUID: "category-guid-1",
          CategoryFullPath: "\u5BB6\u5C45 / \u5DE5\u827A\u54C1 / \u6850\u8349\u5DE5\u827A2",
          LocalSupplierCode: "200",
          LocalSupplierName: "DATS",
          SupplierCode: "CN-001",
          SupplierName: "\u56FD\u5185\u4F9B\u5E94\u5546\u4E00",
          LocationCodes: ["A-01-01-01"],
          LocationBarcodes: ["LOC-A-01"]
        },
        {
          ProductCode: "P002",
          ProductName: "\u517C\u5BB9\u5546\u54C1",
          ItemNumber: "HB-002",
          categoryName: "\u6536\u7EB3",
          productCategoryGUID: "category-guid-2",
          categoryPath: "\u5BB6\u5C45 / \u6536\u7EB3",
          localSupplier: {
            localSupplierCode: "COS",
            name: "Costco AU"
          },
          supplierCode: "CN-002",
          supplierName: "\u56FD\u5185\u4F9B\u5E94\u5546\u4E8C",
          locationCodes: "B-02-02-02, B-02-02-03"
        }
      ],
      total: 2
    }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
  };
  const result = await getWarehouseProductsTable({
    page: 1,
    pageSize: 20
  });
  assertDeepEqual(
    result.items.map((item) => ({
      categoryName: item.categoryName,
      warehouseCategoryGUID: item.warehouseCategoryGUID,
      categoryPath: item.categoryPath,
      domesticSupplierCode: item.domesticSupplierCode,
      domesticSupplierName: item.domesticSupplierName,
      localSupplierCode: item.localSupplierCode,
      localSupplierName: item.localSupplierName,
      locationCodes: item.locationCodes,
      locationBarcodes: item.locationBarcodes
    })),
    [
      {
        categoryName: "\u6850\u8349\u5DE5\u827A2",
        warehouseCategoryGUID: "category-guid-1",
        categoryPath: "\u5BB6\u5C45 / \u5DE5\u827A\u54C1 / \u6850\u8349\u5DE5\u827A2",
        domesticSupplierCode: "CN-001",
        domesticSupplierName: "\u56FD\u5185\u4F9B\u5E94\u5546\u4E00",
        localSupplierCode: "200",
        localSupplierName: "DATS",
        locationCodes: ["A-01-01-01"],
        locationBarcodes: ["LOC-A-01"]
      },
      {
        categoryName: "\u6536\u7EB3",
        warehouseCategoryGUID: "category-guid-2",
        categoryPath: "\u5BB6\u5C45 / \u6536\u7EB3",
        domesticSupplierCode: "CN-002",
        domesticSupplierName: "\u56FD\u5185\u4F9B\u5E94\u5546\u4E8C",
        localSupplierCode: "COS",
        localSupplierName: "Costco AU",
        locationCodes: ["B-02-02-02", "B-02-02-03"],
        locationBarcodes: void 0
      }
    ],
    "\u4ED3\u5E93\u5546\u54C1\u5217\u8868\u5E94\u4FDD\u7559\u5206\u7C7B\u4E0E\u4F9B\u5E94\u5546\u5B57\u6BB5\uFF0C\u5E76\u517C\u5BB9\u6FB3\u6D32\u4F9B\u5E94\u5546\u5927\u5C0F\u5199\u548C\u5D4C\u5957\u5B57\u6BB5"
  );
} finally {
  globalThis.fetch = originalFetch;
}
console.log("warehouseProductService.categoryFilter.test: ok");
