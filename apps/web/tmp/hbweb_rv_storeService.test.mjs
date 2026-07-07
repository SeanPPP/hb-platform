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

// src/utils/managedStoreScope.ts
function sortStoreOptionsByName(stores) {
  return [...stores].sort(
    (left, right) => left.label.localeCompare(right.label, "zh-CN", {
      numeric: true,
      sensitivity: "base"
    })
  );
}

// src/services/storeService.ts
var mapStoreUser = (user) => {
  const { isPrimary, isManageable, ...rest } = user;
  return {
    ...rest,
    isManageable: isManageable ?? isPrimary ?? false
  };
};
var mapStoreDetail = (store) => ({
  ...store,
  users: store.users?.map(mapStoreUser)
});
async function getStores(params) {
  const response = await request_default.get("/api/stores", {
    params
  });
  return unwrapPagedResult(response);
}
async function getActiveStores() {
  const response = await request_default.get("/api/stores/active");
  const stores = Array.isArray(response) ? response : Array.isArray(response.data) ? response.data : [];
  return sortStoreOptionsByName(
    stores.map((store) => ({
      label: store.storeName || store.storeCode,
      value: store.storeCode
    }))
  );
}
async function getNextStoreCode() {
  const response = await request_default.get("/api/stores/next-code");
  if (response.success === false) {
    throw new RequestError(response.message || "\u83B7\u53D6\u4E0B\u4E00\u4E2A\u5206\u5E97\u7F16\u7801\u5931\u8D25", 400, response);
  }
  return unwrapApiData(response);
}
async function createStore(payload) {
  const response = await request_default.post("/api/stores", payload);
  if (response.success === false) {
    throw new RequestError(response.message || "\u521B\u5EFA\u5206\u5E97\u5931\u8D25", 400, response);
  }
  return mapStoreDetail(unwrapApiData(response));
}
async function syncStoreToHq(guid) {
  const response = await request_default.post(`/api/stores/guid/${guid}/sync-hq`);
  if (response.success === false) {
    throw new RequestError(response.message || "\u540C\u6B65HQ\u5206\u5E97\u5931\u8D25", 400, response);
  }
  return unwrapApiData(response);
}

// src/services/storeService.test.ts
function assertDeepEqual(actual, expected, label) {
  const actualJson = JSON.stringify(actual);
  const expectedJson = JSON.stringify(expected);
  if (actualJson !== expectedJson) {
    throw new Error(`${label}. Expected: ${expectedJson}, received: ${actualJson}`);
  }
}
function buildStore(storeCode, storeName) {
  return {
    storeGUID: `${storeCode}-guid`,
    storeCode,
    storeName,
    isActive: true,
    createdAt: "2026-06-01T00:00:00Z",
    updatedAt: "2026-06-01T00:00:00Z"
  };
}
var originalFetch = globalThis.fetch;
globalThis.fetch = async () => new Response(JSON.stringify({
  success: true,
  data: [
    buildStore("1001", "Robinson"),
    buildStore("1009", "Lakehaven"),
    buildStore("1005", "Charlestown Square")
  ]
}), {
  status: 200,
  headers: { "Content-Type": "application/json" }
});
try {
  const stores = await getActiveStores();
  assertDeepEqual(
    stores,
    [
      { label: "Charlestown Square", value: "1005" },
      { label: "Lakehaven", value: "1009" },
      { label: "Robinson", value: "1001" }
    ],
    "\u5206\u5E97\u9009\u9879\u5E94\u8BE5\u6309\u7167\u540D\u79F0\u5347\u5E8F\u6392\u5217"
  );
  let requestedUrl = "";
  globalThis.fetch = async (input) => {
    requestedUrl = String(input);
    return new Response(JSON.stringify({
      success: true,
      data: {
        items: [],
        total: 0,
        page: 2,
        pageSize: 50
      }
    }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
  };
  await getStores({
    page: 2,
    pageSize: 50,
    brandName: "Hot Bargain",
    isActive: true,
    sortField: "brandName",
    sortOrder: "desc"
  });
  const requestUrl = new URL(requestedUrl, "http://localhost");
  assertDeepEqual(
    Array.from(requestUrl.searchParams.entries()),
    [
      ["page", "2"],
      ["pageSize", "50"],
      ["brandName", "Hot Bargain"],
      ["isActive", "true"],
      ["sortField", "brandName"],
      ["sortOrder", "desc"]
    ],
    "\u5206\u5E97\u5217\u8868\u67E5\u8BE2\u5E94\u8BE5\u900F\u4F20\u54C1\u724C\u3001\u72B6\u6001\u548C\u6392\u5E8F\u53C2\u6570"
  );
  let nextCodeUrl = "";
  globalThis.fetch = async (input) => {
    nextCodeUrl = String(input);
    return new Response(JSON.stringify({
      success: true,
      data: "1043"
    }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
  };
  const nextCode = await getNextStoreCode();
  assertDeepEqual(
    {
      path: new URL(nextCodeUrl, "http://localhost").pathname,
      nextCode
    },
    {
      path: "/api/stores/next-code",
      nextCode: "1043"
    },
    "\u83B7\u53D6\u4E0B\u4E00\u4E2A\u5206\u5E97\u7F16\u7801\u5E94\u8C03\u7528 next-code \u63A5\u53E3\u5E76\u8FD4\u56DE\u7F16\u7801\u5B57\u7B26\u4E32"
  );
  let capturedCreateUrl = "";
  let capturedCreateInit;
  globalThis.fetch = async (input, init) => {
    capturedCreateUrl = String(input);
    capturedCreateInit = init;
    return new Response(JSON.stringify({
      success: true,
      data: {
        ...buildStore("1999", "New Store"),
        isActive: false
      }
    }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
  };
  const createdStore = await createStore({
    storeName: "New Store",
    storeCode: "1999",
    brandName: "Hot Bargain",
    isActive: false
  });
  assertDeepEqual(
    {
      path: new URL(capturedCreateUrl, "http://localhost").pathname,
      method: capturedCreateInit?.method,
      body: JSON.parse(String(capturedCreateInit?.body)),
      isActive: createdStore.isActive
    },
    {
      path: "/api/stores",
      method: "POST",
      body: {
        storeName: "New Store",
        storeCode: "1999",
        brandName: "Hot Bargain",
        isActive: false
      },
      isActive: false
    },
    "\u521B\u5EFA\u5206\u5E97\u63A5\u53E3\u5E94\u4F7F\u7528 POST \u5E76\u539F\u6837\u63D0\u4EA4\u672A\u542F\u7528\u6536\u94F6\u7CFB\u7EDF\u72B6\u6001"
  );
  let capturedSyncUrl = "";
  let capturedSyncInit;
  globalThis.fetch = async (input, init) => {
    capturedSyncUrl = String(input);
    capturedSyncInit = init;
    return new Response(JSON.stringify({
      success: true,
      data: true
    }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
  };
  const syncResult = await syncStoreToHq("store-guid-1");
  assertDeepEqual(
    {
      path: new URL(capturedSyncUrl, "http://localhost").pathname,
      method: capturedSyncInit?.method,
      syncResult
    },
    {
      path: "/api/stores/guid/store-guid-1/sync-hq",
      method: "POST",
      syncResult: true
    },
    "\u540C\u6B65HQ\u5206\u5E97\u5E94\u8C03\u7528\u5F53\u524D\u5206\u5E97\u7684 sync-hq POST \u63A5\u53E3"
  );
  globalThis.fetch = async () => new Response(JSON.stringify({
    success: false,
    message: "\u540C\u6B65HQ\u5206\u5E97\u5931\u8D25",
    errorCode: "SYNC_STORE_TO_HQ_ERROR"
  }), {
    status: 200,
    headers: { "Content-Type": "application/json" }
  });
  let failed = false;
  try {
    await syncStoreToHq("store-guid-2");
  } catch (error) {
    failed = error instanceof Error && error.message === "\u540C\u6B65HQ\u5206\u5E97\u5931\u8D25";
  }
  assertDeepEqual(failed, true, "\u540C\u6B65HQ\u5206\u5E97\u4E1A\u52A1\u5931\u8D25\u65F6\u5E94\u629B\u51FA\u540E\u7AEF\u9519\u8BEF\u6D88\u606F");
} finally {
  globalThis.fetch = originalFetch;
}
