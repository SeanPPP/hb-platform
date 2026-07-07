// <define:import.meta.env>
var define_import_meta_env_default = {};

// src/utils/centerLogClient.ts
var importMetaEnv = define_import_meta_env_default ?? {};
var API_BASE_URL = (importMetaEnv.VITE_API_BASE_URL || "").trim();
var CENTER_LOG_PROJECT = (importMetaEnv.VITE_CENTER_LOG_PROJECT || "hbweb_rv").trim();
var CENTER_LOG_KEY = (importMetaEnv.VITE_CENTER_LOG_KEY || "").trim();
var CENTER_LOG_ENVIRONMENT = (importMetaEnv.VITE_CENTER_LOG_ENVIRONMENT || importMetaEnv.MODE || "development").trim();
var CENTER_LOG_SERVICE_NAME = (importMetaEnv.VITE_CENTER_LOG_SERVICE_NAME || "hbweb_rv-web").trim();
var MAX_PROPERTY_LENGTH = 1e3;
var EXTERNAL_SENSITIVE_PROPERTY_PATTERN = /(token|password|authorization|credential|signature|sig|code)/i;
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
function sanitizeExternalUrl(value) {
  try {
    const resolved = new URL(value, typeof window !== "undefined" ? window.location.origin : "http://localhost");
    if (resolved.origin === "null") {
      return value.split("?")[0]?.split("#")[0]?.trim() || void 0;
    }
    return `${resolved.origin}${resolved.pathname}`;
  } catch {
    return value.split("?")[0]?.split("#")[0]?.trim() || void 0;
  }
}
function extractUriTail(value) {
  const sanitized = sanitizeExternalUrl(value);
  if (!sanitized) {
    return void 0;
  }
  const segments = sanitized.split("/").filter(Boolean);
  return segments[segments.length - 1] || void 0;
}
function sanitizeExternalProperties(properties) {
  if (!properties) {
    return void 0;
  }
  const sanitizedEntries = [];
  Object.entries(properties).forEach(([key, value]) => {
    if (value === void 0 || value === null || value === "") {
      return;
    }
    if (EXTERNAL_SENSITIVE_PROPERTY_PATTERN.test(key)) {
      return;
    }
    if (typeof value === "string" && /url$/i.test(key)) {
      const sanitizedUrl = sanitizeExternalUrl(value);
      if (sanitizedUrl) {
        sanitizedEntries.push([key, sanitizedUrl]);
      }
      return;
    }
    if (typeof value === "string" && /uri$/i.test(key)) {
      const uriTail = extractUriTail(value);
      if (uriTail) {
        sanitizedEntries.push([`${key}Tail`, uriTail]);
      }
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
function summarizeExternalResponsePayloadForLog(payload) {
  if (payload === void 0 || payload === null || payload === "") {
    return void 0;
  }
  if (typeof payload === "string") {
    return { type: "string", length: payload.length };
  }
  if (typeof payload !== "object") {
    return {
      type: typeof payload,
      message: trimText(String(payload), MAX_PROPERTY_LENGTH)
    };
  }
  return summarizeResponsePayloadForLog(payload);
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
function buildExternalFetchErrorLog(input) {
  const normalizedError = normalizeUnknownError(input.error);
  const sanitizedProperties = sanitizeExternalProperties(input.properties);
  return {
    level: input.statusCode && input.statusCode < 500 ? "Warning" : "Error",
    sourceType: "frontend-external-request",
    message: normalizedError.message,
    exceptionType: normalizedError.exceptionType,
    exceptionMessage: normalizedError.message,
    stackTrace: normalizedError.stackTrace,
    // 外部 URL 常带签名 query，这里只保留 path，避免把临时凭证写进中心日志。
    requestPath: getRequestPath(input.url, { stripQuery: true }),
    requestMethod: input.method,
    statusCode: input.statusCode,
    traceId: input.traceId,
    properties: {
      ...sanitizedProperties,
      responsePayload: summarizeExternalResponsePayloadForLog(input.responsePayload)
    }
  };
}

// src/utils/centerLogClient.test.ts
function assertDeepEqual(actual, expected, label) {
  const actualJson = JSON.stringify(actual);
  const expectedJson = JSON.stringify(expected);
  if (actualJson !== expectedJson) {
    throw new Error(`${label}. Expected: ${expectedJson}, received: ${actualJson}`);
  }
}
assertDeepEqual(
  summarizeResponsePayloadForLog({
    success: false,
    message: "\u4FDD\u5B58\u5931\u8D25",
    code: "VALIDATION_ERROR",
    errorCode: "PRODUCT_INVALID",
    details: { password: "secret", token: "secret-token" },
    data: { customerEmail: "customer@example.test" }
  }),
  {
    success: false,
    message: "\u4FDD\u5B58\u5931\u8D25",
    code: "VALIDATION_ERROR",
    errorCode: "PRODUCT_INVALID"
  },
  "\u65E5\u5FD7\u53EA\u5E94\u4FDD\u7559\u4E1A\u52A1\u5931\u8D25\u6458\u8981\uFF0C\u4E0D\u80FD\u4E0A\u62A5\u5B8C\u6574\u54CD\u5E94\u4F53"
);
assertDeepEqual(
  summarizeResponsePayloadForLog("raw backend error"),
  { message: "raw backend error" },
  "\u5B57\u7B26\u4E32\u54CD\u5E94\u4F53\u53EA\u5E94\u4F5C\u4E3A message \u6458\u8981\u4E0A\u62A5"
);
var externalFetchLog = buildExternalFetchErrorLog({
  url: "https://cdn.example.com/upload/banner.png?token=secret-token&signature=abc123",
  method: "PUT",
  statusCode: 403,
  error: new Error("Upload failed: 403"),
  responsePayload: {
    success: false,
    message: "signature expired",
    code: "SignatureExpired",
    token: "should-not-log",
    detail: {
      presignedUrl: "https://cdn.example.com/upload/banner.png?token=secret-token"
    }
  },
  properties: {
    uploadUrl: "https://cdn.example.com/upload/banner.png?token=secret-token&signature=abc123",
    authorizationCode: "auth-code-should-not-log",
    fileUri: "file:///tmp/banner.png?token=secret-token",
    objectKey: "upload/banner.png"
  }
});
assertDeepEqual(
  externalFetchLog,
  {
    level: "Warning",
    sourceType: "frontend-external-request",
    message: "Upload failed: 403",
    exceptionType: "Error",
    exceptionMessage: "Upload failed: 403",
    stackTrace: externalFetchLog.stackTrace,
    requestPath: "/upload/banner.png",
    requestMethod: "PUT",
    statusCode: 403,
    properties: {
      uploadUrl: "https://cdn.example.com/upload/banner.png",
      fileUriTail: "banner.png",
      objectKey: "upload/banner.png",
      responsePayload: {
        success: false,
        message: "signature expired",
        code: "SignatureExpired"
      }
    }
  },
  "\u5916\u90E8 fetch \u5931\u8D25\u65E5\u5FD7\u53EA\u80FD\u8BB0\u5F55\u8131\u654F\u540E\u7684\u8DEF\u5F84\u4E0E\u54CD\u5E94\u6458\u8981"
);
if (externalFetchLog.requestPath?.includes("token=") || externalFetchLog.requestPath?.includes("signature=")) {
  throw new Error("\u5916\u90E8 fetch \u5931\u8D25\u65E5\u5FD7\u4E0D\u5E94\u6CC4\u9732 URL query");
}
var serializedExternalFetchLog = JSON.stringify(externalFetchLog);
if (serializedExternalFetchLog.includes("secret-token") || serializedExternalFetchLog.includes("auth-code-should-not-log")) {
  throw new Error("\u5916\u90E8 fetch \u5931\u8D25\u65E5\u5FD7 properties \u4E0D\u5E94\u6CC4\u9732\u7B7E\u540D token \u6216\u6388\u6743\u7801");
}
assertDeepEqual(
  summarizeExternalResponsePayloadForLog("token=secret-token&raw=full-response-body"),
  {
    type: "string",
    length: 41
  },
  "\u5916\u90E8\u5B57\u7B26\u4E32\u54CD\u5E94\u4F53\u53EA\u80FD\u8BB0\u5F55\u957F\u5EA6\u6458\u8981\uFF0C\u4E0D\u80FD\u76F4\u63A5\u4E0A\u62A5\u6B63\u6587"
);
console.log("centerLogClient.test: ok");
