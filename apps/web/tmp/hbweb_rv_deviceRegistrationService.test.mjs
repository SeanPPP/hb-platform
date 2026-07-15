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

// src/services/deviceRegistrationService.ts
var DEVICE_API_BASE = "/api";
var EMERGENCY_LOGIN_GRANT_API_BASE = "/api/react/v1/emergency-login-grants";
var DEVICE_RUNTIME_ONLINE_STALE_MS = 45e3;
var APP_DEVICE_API_BASE = "/api/mobile/app-device-status";
function getString(raw, ...keys) {
  for (const key of keys) {
    const value = raw[key];
    if (typeof value === "string") {
      return value;
    }
  }
  return void 0;
}
function getNullableString(raw, ...keys) {
  for (const key of keys) {
    const value = raw[key];
    if (typeof value === "string") {
      return value;
    }
    if (value === null) {
      return null;
    }
  }
  return null;
}
function getBoolean(raw, ...keys) {
  for (const key of keys) {
    const value = raw[key];
    if (typeof value === "boolean") {
      return value;
    }
  }
  return false;
}
function isDeviceRuntimeOnline(item, now = Date.now()) {
  if (!item.isOnline || !item.lastHeartbeatAt) {
    return false;
  }
  const heartbeatTime = Date.parse(item.lastHeartbeatAt);
  return !Number.isNaN(heartbeatTime) && now - heartbeatTime <= DEVICE_RUNTIME_ONLINE_STALE_MS;
}
function pick(raw, ...keys) {
  for (const key of keys) {
    if (raw[key] !== void 0 && raw[key] !== null) {
      return raw[key];
    }
  }
  return void 0;
}
function asRecord(value) {
  return value && typeof value === "object" && !Array.isArray(value) ? value : null;
}
function asString(value) {
  if (typeof value === "string") {
    const trimmed = value.trim();
    return trimmed || void 0;
  }
  if (typeof value === "number" && Number.isFinite(value)) {
    return String(value);
  }
  return void 0;
}
function asNumber(value, fallback) {
  const numericValue = typeof value === "string" && value.trim() ? Number(value) : value;
  return typeof numericValue === "number" && Number.isFinite(numericValue) ? numericValue : fallback;
}
function asOptionalNumber(value) {
  const numericValue = typeof value === "string" && value.trim() ? Number(value) : value;
  return typeof numericValue === "number" && Number.isFinite(numericValue) ? numericValue : void 0;
}
function asBoolean(value, fallback = false) {
  if (typeof value === "boolean") {
    return value;
  }
  if (typeof value === "string") {
    const normalized = value.trim().toLowerCase();
    if (["true", "1", "yes"].includes(normalized)) {
      return true;
    }
    if (["false", "0", "no"].includes(normalized)) {
      return false;
    }
  }
  return fallback;
}
function normalizeItem(raw) {
  return {
    id: Number(raw.id ?? raw.Id ?? 0),
    hardwareId: String(raw.hardwareId ?? raw.HardwareId ?? ""),
    systemDeviceNumber: String(raw.systemDeviceNumber ?? raw.SystemDeviceNumber ?? ""),
    storeCode: typeof raw.storeCode === "string" ? raw.storeCode : typeof raw.StoreCode === "string" ? raw.StoreCode : null,
    storeName: getNullableString(raw, "storeName", "StoreName"),
    deviceType: String(raw.deviceType ?? raw.DeviceType ?? ""),
    deviceSystem: String(raw.deviceSystem ?? raw.DeviceSystem ?? ""),
    status: Number(raw.status ?? raw.Status ?? -1),
    statusDescription: String(raw.statusDescription ?? raw.StatusDescription ?? ""),
    remark: getNullableString(raw, "remark", "remarks", "Remark", "Remarks"),
    createdAt: typeof raw.createdAt === "string" ? raw.createdAt : typeof raw.CreatedAt === "string" ? raw.CreatedAt : void 0,
    lastModified: typeof raw.lastModified === "string" ? raw.lastModified : typeof raw.LastModified === "string" ? raw.LastModified : null,
    createdBy: typeof raw.createdBy === "string" ? raw.createdBy : typeof raw.CreatedBy === "string" ? raw.CreatedBy : null,
    lastModifiedBy: typeof raw.lastModifiedBy === "string" ? raw.lastModifiedBy : typeof raw.LastModifiedBy === "string" ? raw.LastModifiedBy : null,
    isOnline: getBoolean(raw, "isOnline", "IsOnline", "\u662F\u5426\u5728\u7EBF"),
    lastHeartbeatAt: getNullableString(raw, "lastHeartbeatAt", "LastHeartbeatAt", "\u6700\u540E\u5FC3\u8DF3\u65F6\u95F4"),
    currentCashierId: getNullableString(raw, "currentCashierId", "CurrentCashierId", "\u5F53\u524D\u6536\u94F6\u5458ID"),
    currentCashierName: getNullableString(
      raw,
      "currentCashierName",
      "CurrentCashierName",
      "\u5F53\u524D\u6536\u94F6\u5458\u59D3\u540D"
    ),
    cashierLoginAt: getNullableString(raw, "cashierLoginAt", "CashierLoginAt", "\u6536\u94F6\u5458\u767B\u5F55\u65F6\u95F4")
  };
}
function normalizeAppDeviceStatus(raw) {
  const record = asRecord(raw);
  if (!record) {
    return null;
  }
  const hardwareId = asString(pick(record, "hardwareId", "HardwareId"));
  const id = asString(pick(record, "id", "Id")) ?? hardwareId;
  if (!id || !hardwareId) {
    return null;
  }
  return {
    id,
    hardwareId,
    systemDeviceNumber: asString(pick(record, "systemDeviceNumber", "SystemDeviceNumber")),
    deviceSystem: asString(pick(record, "deviceSystem", "DeviceSystem")),
    platform: asString(pick(record, "platform", "Platform")),
    storeCode: asString(pick(record, "storeCode", "StoreCode")),
    appVersion: asString(pick(record, "appVersion", "AppVersion")),
    appBuildVersion: asString(pick(record, "appBuildVersion", "AppBuildVersion")),
    runtimeVersion: asString(pick(record, "runtimeVersion", "RuntimeVersion")),
    channel: asString(pick(record, "channel", "Channel")),
    updateId: asString(pick(record, "updateId", "UpdateId")),
    updateSource: asString(pick(record, "updateSource", "UpdateSource")),
    lastSeenAtUtc: asString(pick(record, "lastSeenAtUtc", "LastSeenAtUtc", "lastSeenAt", "LastSeenAt")),
    isOnline: asBoolean(pick(record, "isOnline", "IsOnline")),
    lastAuthMode: asString(pick(record, "lastAuthMode", "LastAuthMode")),
    lastSeenUserGuid: asString(pick(record, "lastSeenUserGuid", "LastSeenUserGuid")),
    lastSeenUsername: asString(pick(record, "lastSeenUsername", "LastSeenUsername")),
    lastSeenUserFullName: asString(pick(record, "lastSeenUserFullName", "LastSeenUserFullName")),
    registeredDeviceId: asOptionalNumber(pick(record, "registeredDeviceId", "RegisteredDeviceId"))
  };
}
function normalizeAppDeviceStatusListResponse(payload) {
  const data = unwrapApiData(payload);
  const record = asRecord(data) ?? {};
  const itemsPayload = pick(record, "items", "Items", "devices", "Devices", "data", "Data");
  const total = asNumber(pick(record, "total", "Total", "totalCount", "TotalCount"), 0);
  const page = asNumber(pick(record, "page", "Page", "pageIndex", "PageIndex"), 1);
  const pageSize = asNumber(pick(record, "pageSize", "PageSize"), 20);
  return {
    devices: Array.isArray(itemsPayload) ? itemsPayload.map(normalizeAppDeviceStatus).filter((item) => Boolean(item)) : [],
    total,
    page,
    pageSize,
    totalPages: asNumber(
      pick(record, "totalPages", "TotalPages"),
      pageSize > 0 ? Math.ceil(total / pageSize) : 0
    )
  };
}
function normalizeAppDeviceStatusSummary(payload) {
  const data = unwrapApiData(payload);
  const record = asRecord(data) ?? {};
  return {
    total: asNumber(pick(record, "total", "Total"), 0),
    online: asNumber(pick(record, "online", "Online"), 0),
    offline: asNumber(pick(record, "offline", "Offline"), 0),
    android: asNumber(pick(record, "android", "Android"), 0),
    ios: asNumber(pick(record, "ios", "Ios", "iOS", "IOS"), 0),
    unknownSystem: asNumber(pick(record, "unknownSystem", "UnknownSystem"), 0)
  };
}
function buildAppDeviceStatusParams(params) {
  return {
    page: params?.page,
    pageSize: params?.pageSize,
    storeCode: params?.storeCode,
    deviceSystem: params?.deviceSystem,
    onlineState: params?.onlineState && params.onlineState !== "all" ? params.onlineState : void 0,
    keyword: params?.keyword?.trim() || void 0
  };
}
function normalizeDeviceRegistrationDetail(raw) {
  return {
    id: Number(raw.id ?? raw.ID ?? raw.Id ?? 0),
    hardwareId: String(raw.hardwareId ?? raw.\u8BBE\u5907\u786C\u4EF6\u8BC6\u522B\u7801 ?? raw.HardwareId ?? ""),
    systemDeviceNumber: String(
      raw.systemDeviceNumber ?? raw.\u7CFB\u7EDF\u8BBE\u5907\u7F16\u53F7 ?? raw.SystemDeviceNumber ?? ""
    ),
    storeCode: getNullableString(raw, "storeCode", "\u5206\u5E97\u4EE3\u7801", "StoreCode"),
    storeName: getNullableString(raw, "storeName", "\u5206\u5E97\u540D\u79F0", "StoreName"),
    deviceType: String(raw.deviceType ?? raw.\u8BBE\u5907\u7C7B\u578B ?? raw.DeviceType ?? ""),
    deviceSystem: String(raw.deviceSystem ?? raw.\u8BBE\u5907\u7CFB\u7EDF ?? raw.DeviceSystem ?? ""),
    status: Number(raw.status ?? raw.\u8BBE\u5907\u72B6\u6001 ?? raw.Status ?? -1),
    statusDescription: String(
      raw.statusDescription ?? raw.\u8BBE\u5907\u72B6\u6001\u63CF\u8FF0 ?? raw.StatusDescription ?? ""
    ),
    remark: getNullableString(raw, "remark", "remarks", "\u5907\u6CE8", "Remark", "Remarks"),
    createdAt: getString(raw, "createdAt", "\u521B\u5EFA\u65F6\u95F4", "CreatedAt"),
    lastModified: getNullableString(raw, "lastModified", "\u6700\u540E\u4FEE\u6539\u65F6\u95F4", "LastModified"),
    createdBy: getNullableString(raw, "createdBy", "\u521B\u5EFA\u4EBA", "CreatedBy"),
    lastModifiedBy: getNullableString(raw, "lastModifiedBy", "\u6700\u540E\u4FEE\u6539\u4EBA", "LastModifiedBy"),
    isOnline: getBoolean(raw, "isOnline", "\u662F\u5426\u5728\u7EBF", "IsOnline"),
    lastHeartbeatAt: getNullableString(raw, "lastHeartbeatAt", "\u6700\u540E\u5FC3\u8DF3\u65F6\u95F4", "LastHeartbeatAt"),
    currentCashierId: getNullableString(raw, "currentCashierId", "\u5F53\u524D\u6536\u94F6\u5458ID", "CurrentCashierId"),
    currentCashierName: getNullableString(
      raw,
      "currentCashierName",
      "\u5F53\u524D\u6536\u94F6\u5458\u59D3\u540D",
      "CurrentCashierName"
    ),
    cashierLoginAt: getNullableString(raw, "cashierLoginAt", "\u6536\u94F6\u5458\u767B\u5F55\u65F6\u95F4", "CashierLoginAt")
  };
}
function buildUpdateDeviceRegistrationPayload(payload) {
  return {
    \u8BBE\u5907\u7C7B\u578B: payload.deviceType,
    \u8BBE\u5907\u7CFB\u7EDF: payload.deviceSystem,
    \u5907\u6CE8: payload.remark ?? ""
  };
}
async function getDeviceRegistrations(params) {
  const response = await request_default.get(`${DEVICE_API_BASE}/paged`, {
    params: {
      page: params?.page ?? 1,
      pageSize: params?.pageSize ?? 50,
      storeCode: params?.storeCode,
      deviceType: params?.deviceType,
      deviceSystem: params?.deviceSystem
    }
  });
  const data = response.data ?? {};
  const pagination = data.pagination ?? {};
  return {
    devices: Array.isArray(data.devices) ? data.devices.map(normalizeItem) : [],
    total: Number(pagination.total ?? 0),
    page: Number(pagination.page ?? params?.page ?? 1),
    pageSize: Number(pagination.pageSize ?? params?.pageSize ?? 50),
    totalPages: Number(pagination.totalPages ?? 1)
  };
}
async function getAppDeviceStatuses(params) {
  const response = await request_default.get(`${APP_DEVICE_API_BASE}/paged`, {
    params: buildAppDeviceStatusParams(params)
  });
  return normalizeAppDeviceStatusListResponse(response);
}
async function getAppDeviceStatusSummary(params) {
  const response = await request_default.get(`${APP_DEVICE_API_BASE}/summary`, {
    params: {
      storeCode: params?.storeCode,
      deviceSystem: params?.deviceSystem,
      keyword: params?.keyword?.trim() || void 0
    }
  });
  return normalizeAppDeviceStatusSummary(response);
}
async function activateDevice(id) {
  return request_default.post(`${DEVICE_API_BASE}/${id}/activate`, {});
}
async function disableDevice(id) {
  return request_default.post(`${DEVICE_API_BASE}/${id}/disable`, {});
}
async function lockDevice(id) {
  return request_default.post(`${DEVICE_API_BASE}/${id}/lock`, {});
}
function normalizeEmergencyLoginGrant(value) {
  const raw = asRecord(value);
  if (!raw) {
    return null;
  }
  const grantId = asString(pick(raw, "grantId", "GrantId"));
  const storeCode = asString(pick(raw, "storeCode", "StoreCode"));
  if (!grantId || !storeCode) {
    return null;
  }
  const rawStatus = asString(pick(raw, "status", "Status"))?.toLowerCase();
  const status = rawStatus === "active" ? "Active" : rawStatus === "revoked" ? "Revoked" : "Expired";
  return {
    grantId,
    storeCode,
    businessDate: asString(pick(raw, "businessDate", "BusinessDate")) ?? "",
    keyId: asString(pick(raw, "keyId", "KeyId")) ?? "",
    permissionProfile: "AllPosTerminal",
    issuedBy: asString(pick(raw, "issuedBy", "IssuedBy")) ?? "",
    reason: asString(pick(raw, "reason", "Reason", "issuedReason", "IssuedReason")) ?? "",
    issuedAtUtc: asString(pick(raw, "issuedAtUtc", "IssuedAtUtc")) ?? "",
    expiresAtUtc: asString(pick(raw, "expiresAtUtc", "ExpiresAtUtc")) ?? "",
    revokedBy: asString(pick(raw, "revokedBy", "RevokedBy")) ?? null,
    revokedAtUtc: asString(pick(raw, "revokedAtUtc", "RevokedAtUtc")) ?? null,
    revokeReason: asString(
      pick(raw, "revokeReason", "RevokeReason", "revokedReason", "RevokedReason")
    ) ?? null,
    status
  };
}
async function getEmergencyLoginGrant(storeCode) {
  const response = await request_default.get(EMERGENCY_LOGIN_GRANT_API_BASE, {
    params: { storeCode }
  });
  const data = unwrapApiData(response);
  if (Array.isArray(data)) {
    const grants = data.map(normalizeEmergencyLoginGrant).filter((grant) => Boolean(grant));
    return grants.find((grant) => grant.status === "Active") ?? grants[0] ?? null;
  }
  return normalizeEmergencyLoginGrant(data);
}
async function createEmergencyLoginGrant(storeCode, reason) {
  const response = await request_default.post(EMERGENCY_LOGIN_GRANT_API_BASE, {
    storeCode,
    reason: reason.trim()
  });
  const data = asRecord(unwrapApiData(response));
  const grant = normalizeEmergencyLoginGrant(data ? pick(data, "grant", "Grant") : null);
  const token = data ? asString(pick(data, "token", "Token")) : void 0;
  if (!grant || !token) {
    throw new Error("\u7D27\u6025\u767B\u5F55\u6388\u6743\u54CD\u5E94\u65E0\u6548");
  }
  return { grant, token };
}
async function revokeEmergencyLoginGrant(grantId, reason) {
  const response = await request_default.post(
    `${EMERGENCY_LOGIN_GRANT_API_BASE}/${encodeURIComponent(grantId)}/revoke`,
    { reason: reason.trim() }
  );
  const grant = normalizeEmergencyLoginGrant(unwrapApiData(response));
  if (!grant) {
    throw new Error("\u7D27\u6025\u767B\u5F55\u6388\u6743\u54CD\u5E94\u65E0\u6548");
  }
  return grant;
}

// src/services/deviceRegistrationService.test.ts
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
var normalizedChineseDetail = normalizeDeviceRegistrationDetail({
  ID: 12,
  \u8BBE\u5907\u786C\u4EF6\u8BC6\u522B\u7801: "HW-CN-01",
  \u7CFB\u7EDF\u8BBE\u5907\u7F16\u53F7: "POS-CN-01",
  \u5206\u5E97\u4EE3\u7801: "S001",
  \u5206\u5E97\u540D\u79F0: "\u793A\u4F8B\u5206\u5E97",
  \u8BBE\u5907\u7C7B\u578B: "POS",
  \u8BBE\u5907\u7CFB\u7EDF: "Windows",
  \u8BBE\u5907\u72B6\u6001: 1,
  \u8BBE\u5907\u72B6\u6001\u63CF\u8FF0: "\u5DF2\u542F\u7528",
  \u5907\u6CE8: "\u4E2D\u6587\u5B57\u6BB5",
  \u521B\u5EFA\u65F6\u95F4: "2026-05-01T10:00:00Z",
  \u6700\u540E\u4FEE\u6539\u65F6\u95F4: "2026-05-02T11:00:00Z",
  \u6700\u540E\u4FEE\u6539\u4EBA: "editor-cn",
  \u521B\u5EFA\u4EBA: "creator-cn"
});
assertEqual(normalizedChineseDetail.id, 12, "Should normalize Chinese ID field");
assertEqual(
  normalizedChineseDetail.hardwareId,
  "HW-CN-01",
  "Should normalize Chinese hardware identifier field"
);
assertEqual(
  normalizedChineseDetail.storeName,
  "\u793A\u4F8B\u5206\u5E97",
  "Should normalize Chinese store name field"
);
assertEqual(
  normalizedChineseDetail.remark,
  "\u4E2D\u6587\u5B57\u6BB5",
  "Should normalize Chinese remark field"
);
assertEqual(
  normalizedChineseDetail.lastModifiedBy,
  "editor-cn",
  "Should normalize Chinese last modified by field"
);
var normalizedCamelDetail = normalizeDeviceRegistrationDetail({
  id: 13,
  hardwareId: "HW-EN-01",
  systemDeviceNumber: "POS-EN-01",
  storeCode: "S002",
  storeName: "Example Store",
  deviceType: "Admin",
  deviceSystem: "Mac",
  status: 3,
  statusDescription: "Locked",
  remarks: "camel field",
  createdAt: "2026-05-03T10:00:00Z",
  lastModified: "2026-05-04T11:00:00Z",
  lastModifiedBy: "editor-en",
  createdBy: "creator-en"
});
assertEqual(normalizedCamelDetail.id, 13, "Should normalize camelCase ID field");
assertEqual(
  normalizedCamelDetail.statusDescription,
  "Locked",
  "Should normalize camelCase status description field"
);
assertEqual(
  normalizedCamelDetail.remark,
  "camel field",
  "Should normalize camelCase remark field"
);
assertEqual(
  normalizedCamelDetail.isOnline,
  false,
  "Should normalize missing runtime online field as false"
);
var normalizedRuntimeDetail = normalizeDeviceRegistrationDetail({
  id: 14,
  hardwareId: "HW-RUN-01",
  systemDeviceNumber: "POS-RUN-01",
  deviceType: "POS",
  deviceSystem: "Windows",
  status: 1,
  isOnline: true,
  lastHeartbeatAt: "2026-07-01T10:00:00Z",
  currentCashierId: "CASHIER-1",
  currentCashierName: "Alice",
  cashierLoginAt: "2026-07-01T09:55:00Z"
});
assertEqual(normalizedRuntimeDetail.isOnline, true, "Should normalize runtime online status");
assertEqual(
  normalizedRuntimeDetail.lastHeartbeatAt,
  "2026-07-01T10:00:00Z",
  "Should normalize last heartbeat time"
);
assertEqual(
  normalizedRuntimeDetail.currentCashierName,
  "Alice",
  "Should normalize current cashier name"
);
assertEqual(
  isDeviceRuntimeOnline(
    normalizedRuntimeDetail,
    Date.parse("2026-07-01T10:00:44Z")
  ),
  true,
  "Runtime status should stay online inside the 45 second heartbeat window"
);
assertEqual(
  isDeviceRuntimeOnline(
    normalizedRuntimeDetail,
    Date.parse("2026-07-01T10:00:46Z")
  ),
  false,
  "Runtime status should become offline after the 45 second heartbeat window"
);
var updateValuesWithRuntimeExtras = {
  deviceType: "Mobile",
  deviceSystem: "Android",
  remark: "updated",
  status: 2,
  statusDescription: "Disabled"
};
assertDeepEqual(
  buildUpdateDeviceRegistrationPayload(updateValuesWithRuntimeExtras),
  {
    \u8BBE\u5907\u7C7B\u578B: "Mobile",
    \u8BBE\u5907\u7CFB\u7EDF: "Android",
    \u5907\u6CE8: "updated"
  },
  "Update payload should only include editable Chinese DTO fields"
);
var normalizedAppDevices = normalizeAppDeviceStatusListResponse({
  success: true,
  data: {
    items: [{
      Id: "F53AF6E9-A4C6-4C31-9B19-83E93B120D93",
      HardwareId: "HW-APP-01",
      SystemDeviceNumber: "DEV202607080001",
      DeviceSystem: "Android",
      AppVersion: "1.2.3",
      AppBuildVersion: "45",
      RuntimeVersion: "1.2.3",
      Channel: "production",
      UpdateId: "12345678-90ab-cdef-1234-567890abcdef",
      UpdateSource: "ota",
      LastSeenAtUtc: "2026-07-08T09:00:00Z",
      IsOnline: "true",
      LastSeenUsername: "ada"
    }],
    Total: 1,
    Page: 1,
    PageSize: 20
  }
});
assertEqual(normalizedAppDevices.total, 1, "Should normalize App device total");
assertEqual(normalizedAppDevices.devices[0]?.hardwareId, "HW-APP-01", "Should normalize App hardware ID");
assertEqual(normalizedAppDevices.devices[0]?.isOnline, true, "Should normalize App online state");
assertEqual(normalizedAppDevices.devices[0]?.appVersion, "1.2.3", "Should normalize App package version");
assertEqual(normalizedAppDevices.devices[0]?.appBuildVersion, "45", "Should normalize App build version");
assertEqual(normalizedAppDevices.devices[0]?.runtimeVersion, "1.2.3", "Should normalize App runtime version");
assertEqual(normalizedAppDevices.devices[0]?.channel, "production", "Should normalize App channel");
assertEqual(
  normalizedAppDevices.devices[0]?.updateId,
  "12345678-90ab-cdef-1234-567890abcdef",
  "Should preserve full App update ID"
);
assertEqual(normalizedAppDevices.devices[0]?.updateSource, "ota", "Should normalize App update source");
assertEqual(normalizedAppDevices.devices[0]?.lastSeenUsername, "ada", "Should normalize App recent user");
assertDeepEqual(
  normalizeAppDeviceStatusSummary({
    success: true,
    data: {
      Total: 3,
      Online: 1,
      Offline: 2,
      Android: 2,
      Ios: 1,
      UnknownSystem: 0
    }
  }),
  {
    total: 3,
    online: 1,
    offline: 2,
    android: 2,
    ios: 1,
    unknownSystem: 0
  },
  "Should normalize App device summary"
);
var originalFetch = globalThis.fetch;
var calls = [];
globalThis.fetch = async (input, init) => {
  const url = String(input);
  calls.push({
    url,
    method: init?.method,
    body: typeof init?.body === "string" ? init.body : void 0
  });
  if (url.includes("/api/mobile/app-device-status/paged")) {
    return new Response(JSON.stringify({
      success: true,
      data: {
        items: [{ id: "app-1", hardwareId: "HW-APP-URL", isOnline: true }],
        total: 1,
        page: 1,
        pageSize: 20
      }
    }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
  }
  if (url.includes("/api/mobile/app-device-status/summary")) {
    return new Response(JSON.stringify({
      success: true,
      data: {
        total: 1,
        online: 1,
        offline: 0,
        android: 1,
        ios: 0,
        unknownSystem: 0
      }
    }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
  }
  if (url.includes("/api/react/v1/emergency-login-grants")) {
    const revoked = url.endsWith("/revoke");
    const body = init?.body ? JSON.parse(String(init.body)) : {};
    const grant = {
      grantId: "grant-1",
      storeCode: "S01",
      businessDate: "2026-07-14",
      keyId: "KEY1",
      permissionProfile: "AllPosTerminal",
      issuedBy: "admin",
      reason: String(body.reason ?? "outage"),
      issuedAtUtc: "2026-07-14T01:00:00Z",
      expiresAtUtc: "2026-07-14T14:00:00Z",
      status: revoked ? "Revoked" : "Active"
    };
    const data = init?.method === "POST" && !revoked ? { grant, token: "HBPOSE1-KEY1-PAYLOAD-SIGNATURE" } : grant;
    return new Response(JSON.stringify({ success: true, data }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
  }
  return new Response(JSON.stringify({
    success: true,
    data: {
      devices: [],
      pagination: {
        page: 2,
        pageSize: 30,
        total: 0,
        totalPages: 1
      }
    }
  }), {
    status: 200,
    headers: { "Content-Type": "application/json" }
  });
};
try {
  await getDeviceRegistrations({
    page: 2,
    pageSize: 30,
    storeCode: "S01",
    deviceType: "POS",
    deviceSystem: "Windows"
  });
  await activateDevice(12);
  await disableDevice(12);
  await lockDevice(12);
  await getAppDeviceStatuses({
    page: 1,
    pageSize: 20,
    storeCode: "S01",
    deviceSystem: "Android",
    onlineState: "online",
    keyword: "Ada"
  });
  await getAppDeviceStatusSummary({
    storeCode: "S01",
    deviceSystem: "Android",
    keyword: "Ada"
  });
  const grant = await getEmergencyLoginGrant("S01");
  const createdGrant = await createEmergencyLoginGrant("S01", " network outage ");
  const revokedGrant = await revokeEmergencyLoginGrant("grant-1", " resolved ");
  assertEqual(
    calls[0]?.url,
    "/api/paged?page=2&pageSize=30&storeCode=S01&deviceType=POS&deviceSystem=Windows",
    "Device registration list should use legacy device API base path"
  );
  assertEqual(calls[0]?.method, "GET", "Device registration list should use GET");
  assertEqual(
    calls[1]?.url,
    "/api/12/activate",
    "Device activation should use legacy device API base path"
  );
  assertEqual(calls[1]?.method, "POST", "Device activation should use POST");
  assertEqual(
    calls[2]?.url,
    "/api/12/disable",
    "Device disable should use legacy device API base path"
  );
  assertEqual(
    calls[3]?.url,
    "/api/12/lock",
    "Device lock should use legacy device API base path"
  );
  assertEqual(
    calls[4]?.url,
    "/api/mobile/app-device-status/paged?page=1&pageSize=20&storeCode=S01&deviceSystem=Android&onlineState=online&keyword=Ada",
    "App device list should use mobile app-device-status paged API"
  );
  assertEqual(calls[4]?.method, "GET", "App device list should use GET");
  assertEqual(
    calls[5]?.url,
    "/api/mobile/app-device-status/summary?storeCode=S01&deviceSystem=Android&keyword=Ada",
    "App device summary should use mobile app-device-status summary API"
  );
  assertEqual(calls[5]?.method, "GET", "App device summary should use GET");
  assertEqual(
    calls[6]?.url,
    "/api/react/v1/emergency-login-grants?storeCode=S01",
    "Emergency grant summary should use the store query route"
  );
  assertEqual(grant?.status, "Active", "Emergency grant summary should be normalized");
  assertEqual(calls[7]?.method, "POST", "Emergency grant creation should use POST");
  assertEqual(
    calls[7]?.body,
    JSON.stringify({ storeCode: "S01", reason: "network outage" }),
    "Emergency grant creation should trim its reason"
  );
  assertEqual(
    createdGrant.token,
    "HBPOSE1-KEY1-PAYLOAD-SIGNATURE",
    "Emergency grant creation should return the one-time token"
  );
  assertEqual(
    calls[8]?.url,
    "/api/react/v1/emergency-login-grants/grant-1/revoke",
    "Emergency grant revocation should use the grant route"
  );
  assertEqual(revokedGrant.status, "Revoked", "Emergency grant revocation should return its status");
} finally {
  globalThis.fetch = originalFetch;
}
console.log("deviceRegistrationService.test: ok");
