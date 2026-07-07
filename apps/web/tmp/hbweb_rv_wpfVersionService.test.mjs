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

// src/services/wpfVersionService.ts
var WPF_APP_RELEASES_API = "/api/wpf-app-releases";
function asRecord(value) {
  return value && typeof value === "object" ? value : {};
}
function getString(raw, key) {
  const value = raw[key];
  return typeof value === "string" ? value : null;
}
function hasValue(value) {
  return typeof value === "string" && value.trim().length > 0;
}
function getBoolean(raw, key) {
  const value = raw[key];
  if (typeof value === "boolean") {
    return value;
  }
  if (typeof value === "string") {
    return value.trim().toLowerCase() === "true";
  }
  return false;
}
function getNullableBoolean(raw, key) {
  const value = raw[key];
  if (typeof value === "boolean") {
    return value;
  }
  if (typeof value === "string") {
    const normalizedValue = value.trim().toLowerCase();
    if (normalizedValue === "true") {
      return true;
    }
    if (normalizedValue === "false") {
      return false;
    }
  }
  return null;
}
function getNumber(raw, key) {
  const value = raw[key];
  if (typeof value === "number" && Number.isFinite(value)) {
    return value;
  }
  if (typeof value === "string") {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : null;
  }
  return null;
}
function normalizeWpfInstallerType(value) {
  if (typeof value !== "string") {
    return null;
  }
  const normalizedValue = value.trim().toLowerCase();
  if (normalizedValue === "exe" || normalizedValue === "msi") {
    return normalizedValue;
  }
  return null;
}
function getHeaders(raw) {
  const headers = asRecord(raw.headers ?? raw.uploadHeaders);
  return Object.fromEntries(
    Object.entries(headers).filter((entry) => typeof entry[1] === "string")
  );
}
function normalizePathSegment(value) {
  return value.trim().replace(/\\/g, "/").replace(/^\/+|\/+$/g, "");
}
function normalizeWpfVersion(value) {
  const trimmed = value.trim();
  const match = trimmed.match(/^v?(\d+)\.(\d+)\.(\d+)(?:\.(\d+))?$/i);
  if (!match) {
    return trimmed;
  }
  return match[4] ? `${match[1]}.${match[2]}.${match[3]}.${match[4]}` : `${match[1]}.${match[2]}.${match[3]}`;
}
function buildWpfResponseError(raw) {
  const code = getString(raw, "code") ?? getString(raw, "errorCode");
  const message = getString(raw, "message") ?? "WPF release request failed";
  return new Error(code ? `${code}: ${message}` : message);
}
function assertWpfResponseSucceeded(payload) {
  const raw = asRecord(payload);
  const success = getNullableBoolean(raw, "success") ?? getNullableBoolean(raw, "isSuccess");
  if (success === false) {
    throw buildWpfResponseError(raw);
  }
}
function unwrapWpfResponseData(payload) {
  assertWpfResponseSucceeded(payload);
  return unwrapApiData(payload);
}
function unwrapWpfReleaseListData(payload) {
  assertWpfResponseSucceeded(payload);
  const raw = asRecord(payload);
  if (Array.isArray(raw.data)) {
    return raw;
  }
  return unwrapApiData(payload);
}
function buildWpfReleaseObjectKey(input) {
  const channel = normalizePathSegment(input.channel).toLowerCase() || "production";
  const version = normalizePathSegment(normalizeWpfVersion(input.version));
  const fileName = normalizePathSegment(input.fileName).replace(/\s+/g, "-");
  return `wpf-releases/${channel}/${version}/${fileName}`;
}
function normalizeWpfAppRelease(raw) {
  return {
    id: getString(raw, "id") ?? getString(raw, "releaseId") ?? "",
    version: getString(raw, "version") ?? "",
    channel: getString(raw, "channel") ?? "production",
    fileName: getString(raw, "fileName") ?? "",
    fileSize: getNumber(raw, "fileSize"),
    sha256: getString(raw, "sha256"),
    installerType: normalizeWpfInstallerType(raw.installerType),
    installerArguments: getString(raw, "installerArguments"),
    downloadUrl: getString(raw, "downloadUrl"),
    objectKey: getString(raw, "objectKey") ?? getString(raw, "cosObjectKey"),
    releaseNotes: getString(raw, "releaseNotes"),
    isActive: getBoolean(raw, "isActive") || getBoolean(raw, "active"),
    isCurrent: getBoolean(raw, "isCurrent") || getBoolean(raw, "current"),
    isRollback: getBoolean(raw, "isRollback"),
    forceUpdate: getBoolean(raw, "forceUpdate"),
    minimumSupportedVersion: getString(raw, "minimumSupportedVersion"),
    targetVersion: getString(raw, "targetVersion"),
    createdAt: getString(raw, "createdAt"),
    updatedAt: getString(raw, "updatedAt") ?? getString(raw, "lastModifiedAt")
  };
}
function normalizeWpfReleaseUploadInitResult(raw) {
  const directUpload = asRecord(raw.directUpload);
  return {
    uploadUrl: getString(directUpload, "uploadUrl") ?? getString(directUpload, "signedUrl") ?? getString(directUpload, "url") ?? "",
    uploadMethod: getString(directUpload, "uploadMethod") ?? getString(directUpload, "method") ?? "PUT",
    objectKey: getString(asRecord(raw), "objectKey") ?? getString(directUpload, "objectKey") ?? getString(asRecord(raw), "cosObjectKey") ?? "",
    downloadUrl: getString(asRecord(raw), "downloadUrl") ?? getString(asRecord(raw), "publicUrl") ?? getString(directUpload, "publicUrl") ?? "",
    headers: getHeaders(directUpload)
  };
}
async function getWpfAppReleases(params) {
  const page = params?.page ?? 1;
  const pageSize = params?.pageSize ?? 10;
  const response = await request_default.get(WPF_APP_RELEASES_API, {
    params: {
      page,
      pageSize,
      channel: params?.channel,
      includeDisabled: params?.includeDisabled
    }
  });
  const raw = asRecord(unwrapWpfReleaseListData(response));
  const rawItems = Array.isArray(raw.items) ? raw.items : Array.isArray(raw.list) ? raw.list : Array.isArray(raw.data) ? raw.data : [];
  return {
    items: rawItems.map((item) => normalizeWpfAppRelease(asRecord(item))),
    total: Number(raw.total ?? raw.totalCount ?? rawItems.length),
    page: Number(raw.page ?? page),
    pageSize: Number(raw.pageSize ?? pageSize)
  };
}
async function initWpfReleaseUpload(input) {
  const payload = {
    ...input,
    channel: input.channel.trim().toLowerCase(),
    version: normalizeWpfVersion(input.version),
    fileName: input.fileName.trim(),
    sha256: input.sha256?.trim() || void 0,
    contentType: input.contentType || void 0,
    objectKey: buildWpfReleaseObjectKey(input)
  };
  const response = await request_default.post(
    `${WPF_APP_RELEASES_API}/upload/init`,
    payload
  );
  const uploadInit = normalizeWpfReleaseUploadInitResult(asRecord(unwrapWpfResponseData(response)));
  if (!hasValue(uploadInit.uploadUrl)) {
    throw new Error("WPF release upload init response is missing uploadUrl");
  }
  if (!hasValue(uploadInit.downloadUrl)) {
    throw new Error("WPF release upload init response is missing downloadUrl");
  }
  return uploadInit;
}
async function uploadWpfReleaseFile(file, upload) {
  if (!hasValue(upload.uploadUrl)) {
    throw new Error("WPF release uploadUrl is required before uploading file");
  }
  const response = await fetch(upload.uploadUrl, {
    method: upload.uploadMethod || "PUT",
    headers: upload.headers,
    body: file
  });
  if (!response.ok) {
    throw new Error(`COS upload failed (${response.status})`);
  }
}
async function createWpfAppRelease(input) {
  const payload = {
    ...input,
    version: normalizeWpfVersion(input.version),
    channel: input.channel.trim().toLowerCase(),
    fileName: input.fileName.trim(),
    sha256: input.sha256?.trim() || void 0,
    installerArguments: input.installerArguments?.trim() || void 0,
    releaseNotes: input.releaseNotes?.trim() || void 0,
    cosObjectKey: input.objectKey
  };
  const response = await request_default.post(
    WPF_APP_RELEASES_API,
    payload
  );
  return normalizeWpfAppRelease(asRecord(unwrapWpfResponseData(response)));
}
async function saveWpfReleasePolicy(input) {
  const payload = {
    channel: input.channel.trim().toLowerCase(),
    targetVersion: normalizeWpfVersion(input.targetVersion),
    minimumSupportedVersion: normalizeWpfVersion(input.minimumSupportedVersion),
    forceUpdate: input.forceUpdate,
    isRollback: input.isRollback,
    rollbackConfirmed: input.rollbackConfirmed
  };
  const response = await request_default.post(
    `${WPF_APP_RELEASES_API}/policy`,
    payload
  );
  return unwrapWpfResponseData(response);
}
async function updateWpfAppRelease(id, input) {
  const payload = {
    ...input,
    downloadUrl: input.downloadUrl?.trim() || void 0,
    sha256: input.sha256?.trim() || void 0,
    installerArguments: input.installerArguments?.trim() || void 0,
    releaseNotes: input.releaseNotes?.trim() || void 0
  };
  const response = await request_default.put(
    `${WPF_APP_RELEASES_API}/${id}`,
    payload
  );
  return normalizeWpfAppRelease(asRecord(unwrapWpfResponseData(response)));
}

// src/services/wpfVersionService.test.ts
function assertEqual(actual, expected, message) {
  if (actual !== expected) {
    throw new Error(`${message}. Expected: ${String(expected)}, received: ${String(actual)}`);
  }
}
function assertDeepEqual(actual, expected, message) {
  const actualJson = JSON.stringify(actual);
  const expectedJson = JSON.stringify(expected);
  if (actualJson !== expectedJson) {
    throw new Error(`${message}. Expected: ${expectedJson}, received: ${actualJson}`);
  }
}
async function assertRejectsWithMessage(action, expectedParts, message) {
  try {
    await action();
  } catch (error) {
    if (!(error instanceof Error)) {
      throw new Error(`${message}. Expected Error instance, received: ${String(error)}`);
    }
    for (const part of expectedParts) {
      if (!error.message.includes(part)) {
        throw new Error(`${message}. Expected error message to include: ${part}, received: ${error.message}`);
      }
    }
    return;
  }
  throw new Error(`${message}. Expected promise to reject`);
}
var normalizedRelease = normalizeWpfAppRelease({
  id: "release-1",
  version: "1.2.3",
  channel: "production",
  fileName: "hbpos-1.2.3.msi",
  fileSize: "1048576",
  sha256: "ABCDEF",
  installerType: "msi",
  installerArguments: "/qn",
  downloadUrl: "https://cos.example/hbpos-1.2.3.msi",
  objectKey: "wpf-releases/production/1.2.3/hbpos-1.2.3.msi",
  releaseNotes: "Stable release",
  isCurrent: true,
  forceUpdate: "true",
  minimumSupportedVersion: "1.0.0",
  targetVersion: "1.2.3",
  createdAt: "2026-06-25T01:00:00Z",
  updatedAt: "2026-06-25T02:00:00Z"
});
assertEqual(normalizedRelease.fileSize, 1048576, "WPF release normalizer should parse fileSize as number");
assertEqual(normalizedRelease.forceUpdate, true, "WPF release normalizer should parse forceUpdate boolean");
assertEqual(normalizedRelease.sha256, "ABCDEF", "WPF release normalizer should keep sha256");
assertEqual(
  normalizeWpfAppRelease({
    id: "release-unsupported",
    version: "1.2.4",
    channel: "production",
    fileName: "hbpos-1.2.4.zip",
    installerType: "zip"
  }).installerType,
  null,
  "Unsupported installerType values should be normalized to null instead of leaking raw backend values"
);
assertEqual(
  normalizeWpfAppRelease({
    id: "release-unsupported-2",
    version: "1.2.5",
    channel: "production",
    fileName: "hbpos-1.2.5.bat",
    installerType: "bat"
  }).installerType,
  null,
  "Unsupported installerType values should consistently normalize to null"
);
assertEqual(
  buildWpfReleaseObjectKey({ channel: "Preview", version: " v1.2.3 ", fileName: " Hb POS Setup 1.2.3.msi " }),
  "wpf-releases/preview/1.2.3/Hb-POS-Setup-1.2.3.msi",
  "COS object key should follow the fixed wpf-releases/{channel}/{version}/{fileName} contract"
);
var nestedUploadInit = normalizeWpfReleaseUploadInitResult({
  objectKey: "wpf-releases/production/1.2.3/hbpos-1.2.3.msi",
  directUpload: {
    url: "https://cos-upload.example/upload",
    objectKey: "wpf-releases/production/1.2.3/hbpos-1.2.3.msi",
    headers: { "content-type": "application/x-msi" }
  }
});
assertEqual(
  nestedUploadInit.uploadUrl,
  "https://cos-upload.example/upload",
  "Upload init normalizer should read backend directUpload.url"
);
assertDeepEqual(
  nestedUploadInit.headers,
  { "content-type": "application/x-msi" },
  "Upload init normalizer should read backend directUpload.headers"
);
var originalFetch = globalThis.fetch;
var calls = [];
globalThis.fetch = async (input, init) => {
  calls.push({
    url: String(input),
    method: init?.method,
    body: init?.body ? JSON.parse(String(init.body)) : void 0
  });
  const data = calls.length === 1 ? {
    objectKey: "wpf-releases/production/1.2.3/hbpos-1.2.3.msi",
    downloadUrl: "https://cos.example/hbpos-1.2.3.msi",
    directUpload: {
      uploadUrl: "https://cos-upload.example/upload",
      headers: { "x-cos-meta": "wpf" }
    }
  } : calls.length === 2 ? normalizedRelease : calls.length === 3 ? {
    items: [normalizedRelease],
    total: 1,
    page: 2,
    pageSize: 20
  } : { success: true };
  return new Response(JSON.stringify({ success: true, data }), {
    status: 200,
    headers: { "Content-Type": "application/json" }
  });
};
try {
  const uploadInit = await initWpfReleaseUpload({
    channel: "production",
    version: "1.2.3",
    fileName: "hbpos-1.2.3.msi",
    fileSize: 1048576,
    sha256: "ABCDEF",
    contentType: "application/x-msi"
  });
  const release = await createWpfAppRelease({
    version: "1.2.3",
    channel: "production",
    fileName: "hbpos-1.2.3.msi",
    fileSize: 1048576,
    sha256: "ABCDEF",
    installerType: "msi",
    installerArguments: "/qn",
    objectKey: uploadInit.objectKey,
    downloadUrl: uploadInit.downloadUrl,
    releaseNotes: "Stable release"
  });
  const paged = await getWpfAppReleases({
    page: 2,
    pageSize: 20,
    channel: "preview",
    includeDisabled: true
  });
  await saveWpfReleasePolicy({
    channel: "production",
    targetVersion: "1.2.3",
    minimumSupportedVersion: "1.0.0",
    forceUpdate: true,
    isRollback: false
  });
  await updateWpfAppRelease("release-1", { isActive: false });
  assertEqual(calls[0]?.url, "/api/wpf-app-releases/upload/init", "Upload init should call fixed backend endpoint");
  assertEqual(calls[0]?.method, "POST", "Upload init should use POST");
  assertDeepEqual(
    calls[0]?.body,
    {
      channel: "production",
      version: "1.2.3",
      fileName: "hbpos-1.2.3.msi",
      fileSize: 1048576,
      sha256: "ABCDEF",
      contentType: "application/x-msi",
      objectKey: "wpf-releases/production/1.2.3/hbpos-1.2.3.msi"
    },
    "Upload init payload should include deterministic COS object key"
  );
  assertEqual(release.id, "release-1", "Create release should normalize response payload");
  assertEqual(paged.page, 2, "Release list should normalize page from backend payload");
  assertEqual(
    calls[1]?.body?.cosObjectKey,
    "wpf-releases/production/1.2.3/hbpos-1.2.3.msi",
    "Create release should submit backend cosObjectKey field"
  );
  assertEqual(
    calls[2]?.url,
    "/api/wpf-app-releases?page=2&pageSize=20&channel=preview&includeDisabled=true",
    "Release list query should include includeDisabled when requested"
  );
  assertDeepEqual(
    calls[3]?.body,
    {
      channel: "production",
      targetVersion: "1.2.3",
      minimumSupportedVersion: "1.0.0",
      forceUpdate: true,
      isRollback: false
    },
    "Policy save should submit rollback and force-update contract fields"
  );
  assertEqual(calls[4]?.url, "/api/wpf-app-releases/release-1", "Update release should use release id route");
  assertEqual(calls[4]?.method, "PUT", "Update release should use PUT");
  assertDeepEqual(
    calls[4]?.body,
    {
      isActive: false
    },
    "Update release should submit status mutation payload"
  );
} finally {
  globalThis.fetch = originalFetch;
}
var multipartOnlyCalls = [];
globalThis.fetch = async (input, init) => {
  multipartOnlyCalls.push({
    url: String(input),
    method: init?.method,
    body: init?.body ? JSON.parse(String(init.body)) : void 0
  });
  return new Response(JSON.stringify({
    success: true,
    data: {
      objectKey: "wpf-releases/production/1.2.3/hbpos-1.2.3.msi",
      multipartUpload: {
        uploadId: "multipart-1",
        partSize: 5242880
      }
    }
  }), {
    status: 200,
    headers: { "Content-Type": "application/json" }
  });
};
try {
  await assertRejectsWithMessage(
    () => initWpfReleaseUpload({
      channel: "production",
      version: "1.2.3",
      fileName: "hbpos-1.2.3.msi",
      fileSize: 1048576,
      sha256: "ABCDEF",
      contentType: "application/x-msi"
    }),
    ["uploadUrl"],
    "Upload init should reject multipart-like responses that do not include a direct uploadUrl"
  );
  assertEqual(
    multipartOnlyCalls.length,
    1,
    "Upload init should stop after the invalid init response instead of continuing as a successful upload init"
  );
} finally {
  globalThis.fetch = originalFetch;
}
var multipartUploadUrlCalls = [];
globalThis.fetch = async (input, init) => {
  multipartUploadUrlCalls.push({
    url: String(input),
    method: init?.method,
    body: init?.body ? JSON.parse(String(init.body)) : void 0
  });
  return new Response(JSON.stringify({
    success: true,
    data: {
      objectKey: "wpf-releases/production/1.2.3/hbpos-1.2.3.msi",
      downloadUrl: "https://cos.example/hbpos-1.2.3.msi",
      multipartUpload: {
        uploadUrl: "https://cos-upload.example/multipart-only",
        uploadMethod: "PUT",
        headers: { "x-cos-meta": "multipart-only" }
      }
    }
  }), {
    status: 200,
    headers: { "Content-Type": "application/json" }
  });
};
try {
  await assertRejectsWithMessage(
    () => initWpfReleaseUpload({
      channel: "production",
      version: "1.2.3",
      fileName: "hbpos-1.2.3.msi",
      fileSize: 1048576,
      sha256: "ABCDEF",
      contentType: "application/x-msi"
    }),
    ["uploadUrl"],
    "Upload init should reject multipart-only responses even when multipartUpload exposes uploadUrl and downloadUrl"
  );
  assertEqual(
    multipartUploadUrlCalls.length,
    1,
    "Upload init should treat multipart-only responses as invalid and stop immediately"
  );
} finally {
  globalThis.fetch = originalFetch;
}
var missingDownloadUrlCalls = [];
globalThis.fetch = async (input, init) => {
  missingDownloadUrlCalls.push({
    url: String(input),
    method: init?.method,
    body: init?.body ? JSON.parse(String(init.body)) : void 0
  });
  return new Response(JSON.stringify({
    success: true,
    data: {
      objectKey: "wpf-releases/production/1.2.3/hbpos-1.2.3.msi",
      directUpload: {
        url: "https://cos-upload.example/upload",
        headers: { "content-type": "application/x-msi" }
      }
    }
  }), {
    status: 200,
    headers: { "Content-Type": "application/json" }
  });
};
try {
  await assertRejectsWithMessage(
    () => initWpfReleaseUpload({
      channel: "production",
      version: "1.2.3",
      fileName: "hbpos-1.2.3.msi",
      fileSize: 1048576,
      sha256: "ABCDEF",
      contentType: "application/x-msi"
    }),
    ["downloadUrl"],
    "Upload init should reject responses that do not include a downloadable release URL"
  );
  assertEqual(
    missingDownloadUrlCalls.length,
    1,
    "Upload init should stop after missing downloadUrl instead of continuing as a successful release init"
  );
} finally {
  globalThis.fetch = originalFetch;
}
var emptyUploadUrlCalls = [];
globalThis.fetch = async (input) => {
  emptyUploadUrlCalls.push(String(input));
  return new Response(null, { status: 200 });
};
try {
  await assertRejectsWithMessage(
    () => uploadWpfReleaseFile(
      new File(["installer"], "hbpos-1.2.3.msi", { type: "application/x-msi" }),
      {
        uploadUrl: "",
        uploadMethod: "PUT",
        objectKey: "wpf-releases/production/1.2.3/hbpos-1.2.3.msi",
        downloadUrl: "https://cos.example/hbpos-1.2.3.msi",
        headers: {}
      }
    ),
    ["uploadUrl"],
    "Upload file should reject empty uploadUrl before calling fetch"
  );
  assertDeepEqual(emptyUploadUrlCalls, [], "Upload file should not call fetch with an empty uploadUrl");
} finally {
  globalThis.fetch = originalFetch;
}
var uploadHeaderCalls = [];
globalThis.fetch = async (input, init) => {
  uploadHeaderCalls.push({
    url: String(input),
    method: init?.method,
    headers: init?.headers,
    body: init?.body
  });
  return new Response(null, { status: 200 });
};
try {
  await uploadWpfReleaseFile(
    new File(["installer"], "hbpos-1.2.3.msi", { type: "application/x-msi" }),
    {
      uploadUrl: "https://cos-upload.example/upload",
      uploadMethod: "PUT",
      objectKey: "wpf-releases/production/1.2.3/hbpos-1.2.3.msi",
      downloadUrl: "https://cos.example/hbpos-1.2.3.msi",
      headers: {
        "Content-Type": "application/x-msi",
        "x-cos-meta-sha256": "ABCDEF"
      }
    }
  );
  assertEqual(uploadHeaderCalls.length, 1, "Upload file should issue exactly one direct upload request");
  assertEqual(uploadHeaderCalls[0]?.url, "https://cos-upload.example/upload", "Upload file should use direct upload URL");
  assertEqual(uploadHeaderCalls[0]?.method, "PUT", "Upload file should preserve direct upload method");
  assertDeepEqual(
    uploadHeaderCalls[0]?.headers,
    {
      "Content-Type": "application/x-msi",
      "x-cos-meta-sha256": "ABCDEF"
    },
    "Upload file should pass direct upload headers to fetch without dropping metadata headers"
  );
} finally {
  globalThis.fetch = originalFetch;
}
globalThis.fetch = async () => {
  return new Response(JSON.stringify({
    data: [
      {
        ...normalizedRelease,
        id: "release-top-level-data",
        version: "1.2.4",
        isCurrent: false
      }
    ],
    total: 9,
    page: 3,
    pageSize: 5
  }), {
    status: 200,
    headers: { "Content-Type": "application/json" }
  });
};
try {
  const topLevelPaged = await getWpfAppReleases({
    page: 3,
    pageSize: 5
  });
  assertEqual(
    topLevelPaged.items.length,
    1,
    "Release list should keep top-level data array items when pagination metadata sits beside data"
  );
  assertEqual(topLevelPaged.items[0]?.id, "release-top-level-data", "Top-level data release should be normalized");
  assertEqual(topLevelPaged.total, 9, "Top-level data release list should keep sibling total");
  assertEqual(topLevelPaged.page, 3, "Top-level data release list should keep sibling page");
  assertEqual(topLevelPaged.pageSize, 5, "Top-level data release list should keep sibling pageSize");
} finally {
  globalThis.fetch = originalFetch;
}
var listFailureCalls = [];
globalThis.fetch = async (input, init) => {
  listFailureCalls.push({
    url: String(input),
    method: init?.method,
    body: init?.body ? JSON.parse(String(init.body)) : void 0
  });
  return new Response(JSON.stringify({
    isSuccess: false,
    errorCode: "WPF_RELEASE_LIST_REJECTED",
    message: "Release list is temporarily unavailable"
  }), {
    status: 200,
    headers: { "Content-Type": "application/json" }
  });
};
try {
  await assertRejectsWithMessage(
    () => getWpfAppReleases({
      page: 1,
      pageSize: 10,
      channel: "production",
      includeDisabled: true
    }),
    ["WPF_RELEASE_LIST_REJECTED", "Release list is temporarily unavailable"],
    "Release list should throw backend code and message when ApiResponse.success is false"
  );
  assertEqual(
    listFailureCalls[0]?.url,
    "/api/wpf-app-releases?page=1&pageSize=10&channel=production&includeDisabled=true",
    "Release list failure request should still use the expected query contract"
  );
} finally {
  globalThis.fetch = originalFetch;
}
var failureCalls = [];
globalThis.fetch = async (input, init) => {
  failureCalls.push({
    url: String(input),
    method: init?.method,
    body: init?.body ? JSON.parse(String(init.body)) : void 0
  });
  let payload;
  if (failureCalls.length === 1) {
    payload = {
      success: false,
      code: "WPF_UPLOAD_INIT_REJECTED",
      message: "Upload init denied by release policy"
    };
  } else if (failureCalls.length === 2) {
    payload = {
      success: false,
      code: "WPF_RELEASE_CREATE_REJECTED",
      message: "Release version already exists"
    };
  } else if (failureCalls.length === 3) {
    payload = {
      success: false,
      code: "WPF_RELEASE_UPDATE_REJECTED",
      message: "Release is referenced by active policy"
    };
  } else {
    payload = {
      success: false,
      code: "WPF_POLICY_SAVE_REJECTED",
      message: "Rollback confirmation required"
    };
  }
  return new Response(JSON.stringify(payload), {
    status: 200,
    headers: { "Content-Type": "application/json" }
  });
};
try {
  await assertRejectsWithMessage(
    () => initWpfReleaseUpload({
      channel: "production",
      version: "1.2.3",
      fileName: "hbpos-1.2.3.msi",
      fileSize: 1048576,
      sha256: "ABCDEF",
      contentType: "application/x-msi"
    }),
    ["WPF_UPLOAD_INIT_REJECTED", "Upload init denied by release policy"],
    "Upload init should throw backend code and message when ApiResponse.success is false"
  );
  await assertRejectsWithMessage(
    () => createWpfAppRelease({
      version: "1.2.3",
      channel: "production",
      fileName: "hbpos-1.2.3.msi",
      fileSize: 1048576,
      sha256: "ABCDEF",
      installerType: "msi",
      installerArguments: "/qn",
      objectKey: "wpf-releases/production/1.2.3/hbpos-1.2.3.msi",
      downloadUrl: "https://cos.example/hbpos-1.2.3.msi",
      releaseNotes: "Stable release"
    }),
    ["WPF_RELEASE_CREATE_REJECTED", "Release version already exists"],
    "Create release should throw backend code and message when ApiResponse.success is false"
  );
  await assertRejectsWithMessage(
    () => updateWpfAppRelease("release-1", {
      isActive: false
    }),
    ["WPF_RELEASE_UPDATE_REJECTED", "Release is referenced by active policy"],
    "Update release should throw backend code and message when ApiResponse.success is false"
  );
  await assertRejectsWithMessage(
    () => saveWpfReleasePolicy({
      channel: "production",
      targetVersion: "1.2.3",
      minimumSupportedVersion: "1.0.0",
      forceUpdate: true,
      isRollback: true,
      rollbackConfirmed: false
    }),
    ["WPF_POLICY_SAVE_REJECTED", "Rollback confirmation required"],
    "Save policy should throw backend code and message when ApiResponse.success is false"
  );
} finally {
  globalThis.fetch = originalFetch;
}
console.log("wpfVersionService.test: ok");
