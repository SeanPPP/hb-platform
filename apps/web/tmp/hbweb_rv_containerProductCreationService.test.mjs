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

// src/services/containerProductCreationService.ts
var API_BASE = "/api/react/v1/container-products/create-new-products";
var SUBMIT_CONTAINER_API_BASE = "/api/react/v1/container-products/submit-container";
function assertApiSuccess(response, fallbackMessage) {
  if (response.success === false || response.isSuccess === false) {
    throw new RequestError(response.message || fallbackMessage, 200, response);
  }
}
function isRecord(value) {
  return typeof value === "object" && value !== null;
}
function readNumber(record, keys) {
  for (const key of keys) {
    const value = record[key];
    if (value !== void 0 && value !== null && value !== "") {
      const numericValue = Number(value);
      if (Number.isFinite(numericValue)) {
        return numericValue;
      }
    }
  }
  return 0;
}
function readArray(record, keys) {
  for (const key of keys) {
    const value = record[key];
    if (Array.isArray(value)) {
      return value;
    }
  }
  return [];
}
function readString(record, keys) {
  for (const key of keys) {
    const value = record[key];
    if (typeof value === "string") {
      return value;
    }
  }
  return void 0;
}
function normalizeResult(raw) {
  const nested = isRecord(raw.result) ? raw.result : isRecord(raw.Result) ? raw.Result : raw;
  const merged = { ...raw, ...nested };
  return {
    createdCount: readNumber(merged, ["createdCount", "CreatedCount", "created"]),
    updatedCount: readNumber(merged, ["updatedCount", "UpdatedCount", "updated"]),
    skippedCount: readNumber(merged, ["skippedCount", "SkippedCount", "skipped"]),
    failedCount: readNumber(merged, ["failedCount", "FailedCount", "failed", "Failed", "errorCount", "ErrorCount"]),
    containerCompleted: Boolean(merged.containerCompleted ?? merged.ContainerCompleted ?? false),
    created: readArray(merged, ["created", "Created"]),
    updated: readArray(merged, ["updated", "Updated"]),
    skipped: readArray(merged, ["skipped", "Skipped"]),
    errors: readArray(merged, ["errors", "Errors"])
  };
}
function normalizeJob(raw, fallbackJobId) {
  const record = isRecord(raw) ? raw : {};
  const status = readString(record, ["status", "Status"]) || "Queued";
  if (!["Queued", "Running", "Succeeded", "Failed"].includes(status)) {
    throw new RequestError(`\u672A\u77E5\u521B\u5EFA\u65B0\u5546\u54C1 job \u72B6\u6001\uFF1A${status}`, 200, raw);
  }
  const jobId = readString(record, ["jobId", "JobId"]) || fallbackJobId || "";
  if (!jobId) {
    throw new RequestError("\u521B\u5EFA\u65B0\u5546\u54C1 job \u7F3A\u5C11 jobId", 200, raw);
  }
  return {
    jobId,
    status,
    operationId: readString(record, ["operationId", "OperationId"]),
    message: readString(record, ["message", "Message"]),
    result: normalizeResult(record)
  };
}
function buildContainerCreateProductsOperationId(containerGuid, detailHguids) {
  const normalizedDetails = detailHguids.map((value) => value.trim()).filter(Boolean).sort();
  const detailPart = normalizedDetails.length ? normalizedDetails.join(",") : "empty";
  return `container-create-products:${containerGuid}:${detailPart}`;
}
function buildContainerSubmitOperationId(containerGuid) {
  return `submit-container:${containerGuid.trim()}`;
}
async function createContainerProductCreationJob(data) {
  const response = await request_default.post(`${API_BASE}/jobs`, data);
  assertApiSuccess(response, "\u521B\u5EFA\u65B0\u5546\u54C1 job \u5931\u8D25");
  return normalizeJob(unwrapApiData(response));
}
async function createContainerSubmitJob(data) {
  const response = await request_default.post(`${SUBMIT_CONTAINER_API_BASE}/jobs`, {
    ...data,
    detailHguids: [],
    submitContainer: true
  });
  assertApiSuccess(response, "\u63D0\u4EA4\u8D27\u67DC job \u5931\u8D25");
  return normalizeJob(unwrapApiData(response));
}
async function getContainerProductCreationJob(jobId) {
  const response = await request_default.get(`${API_BASE}/jobs/${encodeURIComponent(jobId)}`);
  assertApiSuccess(response, "\u67E5\u8BE2\u521B\u5EFA\u65B0\u5546\u54C1 job \u5931\u8D25");
  return normalizeJob(unwrapApiData(response), jobId);
}

// src/services/containerProductCreationService.test.ts
function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}
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
async function assertRejectsRequestError(execute, expectedMessage) {
  try {
    await execute();
  } catch (error) {
    assert(error instanceof RequestError, "\u4E1A\u52A1\u5931\u8D25\u5E94\u629B\u51FA RequestError");
    assert(error.message.includes(expectedMessage), `\u9519\u8BEF\u4FE1\u606F\u5E94\u5305\u542B ${expectedMessage}`);
    return;
  }
  throw new Error("\u9884\u671F\u8BF7\u6C42\u5931\u8D25\uFF0C\u4F46\u5B9E\u9645\u6210\u529F");
}
async function main() {
  const failures = [];
  const operationIdFailure = await runTest("\u8D27\u67DC\u521B\u5EFA\u65B0\u5546\u54C1 operationId \u5E94\u7531\u8D27\u67DC\u548C\u660E\u7EC6\u7A33\u5B9A\u751F\u6210", () => {
    assertEqual(
      buildContainerCreateProductsOperationId("container-1", ["detail-b", "detail-a"]),
      buildContainerCreateProductsOperationId("container-1", ["detail-a", "detail-b"]),
      "\u76F8\u540C\u660E\u7EC6\u96C6\u5408\u5E94\u751F\u6210\u540C\u4E00\u4E2A operationId"
    );
    assertEqual(
      buildContainerCreateProductsOperationId("container-1", []),
      "container-create-products:container-1:empty",
      "\u7A7A\u660E\u7EC6\u5E94\u4F7F\u7528 empty \u54E8\u5175\uFF0C\u907F\u514D\u7A7A\u5B57\u7B26\u4E32\u6B67\u4E49"
    );
  });
  if (operationIdFailure) failures.push(operationIdFailure);
  const submitOperationIdFailure = await runTest("\u6574\u67DC\u63D0\u4EA4 operationId \u5E94\u53EA\u7531\u8D27\u67DC\u7A33\u5B9A\u751F\u6210", () => {
    assertEqual(
      buildContainerSubmitOperationId(" container-1 "),
      "submit-container:container-1",
      "\u6574\u67DC\u63D0\u4EA4 operationId \u5E94\u53BB\u9664\u8D27\u67DC GUID \u524D\u540E\u7A7A\u683C"
    );
  });
  if (submitOperationIdFailure) failures.push(submitOperationIdFailure);
  const submitJobFailure = await runTest("\u6574\u67DC\u63D0\u4EA4\u5E94\u521B\u5EFA\u540E\u53F0 job \u5E76\u53EA\u643A\u5E26\u5F53\u524D\u8D27\u67DC GUID", async () => {
    const captured = await captureFetch(
      {
        success: true,
        data: {
          jobId: "container-submit-job-1",
          status: "Queued",
          operationId: "submit-container:container-1",
          result: {
            createdCount: 0,
            updatedCount: 0,
            skippedCount: 0,
            failedCount: 0,
            containerCompleted: false
          }
        }
      },
      () => createContainerSubmitJob({
        operationId: "submit-container:container-1",
        containerGuid: "container-1"
      })
    );
    assertDeepEqual(
      {
        url: captured.capturedUrl,
        method: captured.capturedMethod,
        body: captured.capturedBody,
        job: captured.result
      },
      {
        url: "/api/react/v1/container-products/submit-container/jobs",
        method: "POST",
        body: {
          operationId: "submit-container:container-1",
          containerGuid: "container-1",
          detailHguids: [],
          submitContainer: true
        },
        job: {
          jobId: "container-submit-job-1",
          status: "Queued",
          operationId: "submit-container:container-1",
          result: {
            createdCount: 0,
            updatedCount: 0,
            skippedCount: 0,
            failedCount: 0,
            containerCompleted: false,
            created: [],
            updated: [],
            skipped: [],
            errors: []
          }
        }
      },
      "\u6574\u67DC\u63D0\u4EA4 job \u8BF7\u6C42\u548C\u5F52\u4E00\u5316\u7ED3\u679C\u4E0D\u7B26\u5408\u9884\u671F"
    );
  });
  if (submitJobFailure) failures.push(submitJobFailure);
  const createJobFailure = await runTest("\u8D27\u67DC\u521B\u5EFA\u65B0\u5546\u54C1\u5E94\u521B\u5EFA\u540E\u53F0 job \u5E76\u643A\u5E26 operationId \u548C\u660E\u7EC6 GUID", async () => {
    const captured = await captureFetch(
      {
        success: true,
        data: {
          jobId: "container-product-job-1",
          status: "Queued",
          operationId: "op-1",
          result: {
            createdCount: 0,
            skippedCount: 0,
            failedCount: 0
          }
        }
      },
      () => createContainerProductCreationJob({
        operationId: "op-1",
        containerGuid: "container-1",
        detailHguids: ["detail-1", "detail-2"]
      })
    );
    assertDeepEqual(
      {
        url: captured.capturedUrl,
        method: captured.capturedMethod,
        body: captured.capturedBody,
        job: captured.result
      },
      {
        url: "/api/react/v1/container-products/create-new-products/jobs",
        method: "POST",
        body: {
          operationId: "op-1",
          containerGuid: "container-1",
          detailHguids: ["detail-1", "detail-2"]
        },
        job: {
          jobId: "container-product-job-1",
          status: "Queued",
          operationId: "op-1",
          result: {
            createdCount: 0,
            updatedCount: 0,
            skippedCount: 0,
            failedCount: 0,
            containerCompleted: false,
            created: [],
            updated: [],
            skipped: [],
            errors: []
          }
        }
      },
      "\u521B\u5EFA job \u8BF7\u6C42\u548C\u5F52\u4E00\u5316\u7ED3\u679C\u4E0D\u7B26\u5408\u9884\u671F"
    );
  });
  if (createJobFailure) failures.push(createJobFailure);
  const queryJobFailure = await runTest("\u8D27\u67DC\u521B\u5EFA\u65B0\u5546\u54C1 job \u67E5\u8BE2\u5E94\u5F52\u4E00\u5316\u9876\u5C42\u7EDF\u8BA1\u548C\u9519\u8BEF\u6570\u7EC4", async () => {
    const captured = await captureFetch(
      {
        success: true,
        data: {
          jobId: "container-product-job-2",
          status: "Succeeded",
          createdCount: 2,
          skippedCount: 1,
          failedCount: 1,
          errors: [{ productCode: "P-3", reasonCode: "PRICE_INVALID", message: "\u4EF7\u683C\u5F02\u5E38" }]
        }
      },
      () => getContainerProductCreationJob("container-product-job-2")
    );
    assertEqual(
      captured.capturedUrl,
      "/api/react/v1/container-products/create-new-products/jobs/container-product-job-2",
      "\u67E5\u8BE2 job \u5E94\u547D\u4E2D\u65B0\u63A5\u53E3"
    );
    assertDeepEqual(
      captured.result.result,
      {
        createdCount: 2,
        updatedCount: 0,
        skippedCount: 1,
        failedCount: 1,
        containerCompleted: false,
        created: [],
        updated: [],
        skipped: [],
        errors: [{ productCode: "P-3", reasonCode: "PRICE_INVALID", message: "\u4EF7\u683C\u5F02\u5E38" }]
      },
      "\u67E5\u8BE2 job \u5E94\u5F52\u4E00\u5316\u9876\u5C42\u7EDF\u8BA1"
    );
  });
  if (queryJobFailure) failures.push(queryJobFailure);
  const pascalCaseResultFailure = await runTest("\u8D27\u67DC\u521B\u5EFA\u65B0\u5546\u54C1 job \u5E94\u517C\u5BB9 PascalCase \u5931\u8D25\u7ED3\u679C", async () => {
    const captured = await captureFetch(
      {
        success: true,
        data: {
          JobId: "container-product-job-pascal",
          Status: "Succeeded",
          Result: {
            CreatedCount: 1,
            SkippedCount: 0,
            FailedCount: 1,
            Created: [{ productCode: "P-1" }],
            Errors: [{ productCode: "P-2", reasonCode: "DUPLICATE_CODE", message: "\u5546\u54C1\u5DF2\u5B58\u5728" }]
          }
        }
      },
      () => getContainerProductCreationJob("container-product-job-pascal")
    );
    assertEqual(captured.result.jobId, "container-product-job-pascal", "PascalCase JobId \u5E94\u88AB\u8BC6\u522B");
    assertEqual(captured.result.status, "Succeeded", "PascalCase Status \u5E94\u88AB\u8BC6\u522B");
    assertDeepEqual(
      captured.result.result,
      {
        createdCount: 1,
        updatedCount: 0,
        skippedCount: 0,
        failedCount: 1,
        containerCompleted: false,
        created: [{ productCode: "P-1" }],
        updated: [],
        skipped: [],
        errors: [{ productCode: "P-2", reasonCode: "DUPLICATE_CODE", message: "\u5546\u54C1\u5DF2\u5B58\u5728" }]
      },
      "PascalCase \u5931\u8D25\u7ED3\u679C\u5E94\u5F52\u4E00\u5316\u5230\u7EDF\u4E00\u5B57\u6BB5\uFF0C\u907F\u514D\u9875\u9762\u8BEF\u62A5\u7EAF\u6210\u529F"
    );
  });
  if (pascalCaseResultFailure) failures.push(pascalCaseResultFailure);
  const submitPascalCaseResultFailure = await runTest("\u6574\u67DC\u63D0\u4EA4 job \u5E94\u517C\u5BB9 PascalCase \u66F4\u65B0\u7EDF\u8BA1\u548C\u5B8C\u6210\u72B6\u6001", async () => {
    const captured = await captureFetch(
      {
        success: true,
        data: {
          JobId: "container-submit-job-pascal",
          Status: "Succeeded",
          Result: {
            CreatedCount: 2,
            UpdatedCount: 3,
            SkippedCount: 1,
            FailedCount: 0,
            ContainerCompleted: true,
            Updated: [{ productCode: "P-UPDATED", message: "\u4EF7\u683C\u5DF2\u66F4\u65B0" }]
          }
        }
      },
      () => getContainerProductCreationJob("container-submit-job-pascal")
    );
    assertDeepEqual(
      captured.result.result,
      {
        createdCount: 2,
        updatedCount: 3,
        skippedCount: 1,
        failedCount: 0,
        containerCompleted: true,
        created: [],
        updated: [{ productCode: "P-UPDATED", message: "\u4EF7\u683C\u5DF2\u66F4\u65B0" }],
        skipped: [],
        errors: []
      },
      "\u6574\u67DC\u63D0\u4EA4 PascalCase \u7ED3\u679C\u5E94\u5F52\u4E00\u5316\u5230\u7EDF\u4E00\u5B57\u6BB5"
    );
  });
  if (submitPascalCaseResultFailure) failures.push(submitPascalCaseResultFailure);
  const missingStatusFailure = await runTest("\u521B\u5EFA job \u54CD\u5E94\u7F3A\u5C11 status \u65F6\u4E0D\u5F97\u5F52\u4E00\u4E3A\u6210\u529F", async () => {
    const captured = await captureFetch(
      {
        success: true,
        data: {
          jobId: "container-product-job-missing-status",
          operationId: "op-missing-status"
        }
      },
      () => createContainerProductCreationJob({
        operationId: "op-missing-status",
        containerGuid: "container-1",
        detailHguids: ["detail-1"]
      })
    );
    assertEqual(captured.result.status, "Queued", "\u7F3A\u5C11 status \u7684\u521B\u5EFA\u54CD\u5E94\u5E94\u6309\u5F85\u8F6E\u8BE2 job \u5904\u7406");
  });
  if (missingStatusFailure) failures.push(missingStatusFailure);
  const missingJobIdFailure = await runTest("\u521B\u5EFA job \u54CD\u5E94\u7F3A\u5C11 jobId \u5E94\u629B\u51FA\u4E1A\u52A1\u9519\u8BEF", async () => {
    await assertRejectsRequestError(
      () => captureFetch(
        {
          success: true,
          data: {
            status: "Queued"
          }
        },
        () => createContainerProductCreationJob({
          operationId: "op-missing-job",
          containerGuid: "container-1",
          detailHguids: ["detail-1"]
        })
      ).then(({ result }) => result),
      "\u521B\u5EFA\u65B0\u5546\u54C1 job \u7F3A\u5C11 jobId"
    );
  });
  if (missingJobIdFailure) failures.push(missingJobIdFailure);
  const businessFailure = await runTest("\u8D27\u67DC\u521B\u5EFA\u65B0\u5546\u54C1 job \u63A5\u53E3 success false \u5E94\u629B\u51FA\u4E1A\u52A1\u9519\u8BEF", async () => {
    await assertRejectsRequestError(
      () => captureFetch(
        {
          success: false,
          message: "\u521B\u5EFA\u65B0\u5546\u54C1 job \u5931\u8D25"
        },
        () => createContainerProductCreationJob({
          operationId: "op-failed",
          containerGuid: "container-1",
          detailHguids: ["detail-1"]
        })
      ).then(({ result }) => result),
      "\u521B\u5EFA\u65B0\u5546\u54C1 job \u5931\u8D25"
    );
  });
  if (businessFailure) failures.push(businessFailure);
  if (failures.length) {
    throw new Error(failures.join("\n"));
  }
}
main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
