// <define:import.meta.env>
var define_import_meta_env_default = {};

// src/pages/Warehouse/Products/WarehouseProducts.hqSync.logic.test.ts
import { readFileSync } from "node:fs";
import path from "node:path";

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
function unwrapResponse(response, emptyData) {
  if (response && typeof response === "object") {
    if ("data" in response && response.data !== void 0) {
      return response.data;
    }
    return response;
  }
  return emptyData;
}
function ensureApiSuccess(success, message, fallback) {
  if (success === false) {
    throw new Error(message || fallback || "\u8BF7\u6C42\u5931\u8D25");
  }
}
async function syncWarehouseProductsFromHq() {
  const response = await request_default.post(`${API_BASE}/sync-from-hq`);
  const apiSuccess = response.success ?? response.isSuccess;
  ensureApiSuccess(apiSuccess, response.message, "\u4ECEHQ\u540C\u6B65 WarehouseProduct \u5931\u8D25");
  const syncResult = response.data;
  const syncSuccess = syncResult?.isSuccess ?? syncResult?.IsSuccess;
  ensureApiSuccess(syncSuccess, syncResult?.message ?? syncResult?.Message ?? response.message, "\u4ECEHQ\u540C\u6B65 WarehouseProduct \u5931\u8D25");
  return syncResult ?? {
    isSuccess: response.isSuccess ?? response.success,
    message: response.message
  };
}
async function createWarehouseProductHqSyncJob(payload) {
  const response = await request_default.post(
    `${API_BASE}/sync-from-hq/jobs`,
    payload
  );
  ensureApiSuccess(response.success ?? response.isSuccess, response.message, "\u521B\u5EFA\u4ED3\u5E93\u5546\u54C1 HQ \u540C\u6B65\u4EFB\u52A1\u5931\u8D25");
  return unwrapResponse(response.data, {
    jobId: "",
    status: "Failed",
    message: response.message
  });
}
async function getWarehouseProductHqSyncJob(jobId) {
  const response = await request_default.get(
    `${API_BASE}/sync-from-hq/jobs/${encodeURIComponent(jobId)}`
  );
  ensureApiSuccess(response.success ?? response.isSuccess, response.message, "\u67E5\u8BE2\u4ED3\u5E93\u5546\u54C1 HQ \u540C\u6B65\u4EFB\u52A1\u5931\u8D25");
  return unwrapResponse(response.data, {
    jobId,
    status: "Failed",
    message: response.message
  });
}

// src/types/permissions.ts
var P = {
  Users: {
    View: "Users.View",
    Create: "Users.Create",
    Edit: "Users.Edit",
    Delete: "Users.Delete",
    ManageRoles: "Users.ManageRoles",
    ManageStores: "Users.ManageStores",
    ManagePosTerminalPermissions: "Users.ManagePosTerminalPermissions",
    ResetPassword: "Users.ResetPassword"
  },
  Roles: {
    View: "Roles.View",
    Create: "Roles.Create",
    Edit: "Roles.Edit",
    Delete: "Roles.Delete",
    ManagePermissions: "Roles.ManagePermissions",
    ManageUsers: "Roles.ManageUsers"
  },
  Stores: {
    View: "Stores.View",
    Create: "Stores.Create",
    Edit: "Stores.Edit",
    Delete: "Stores.Delete",
    Sync: "Stores.Sync"
  },
  Products: {
    View: "Products.View",
    Create: "Products.Create",
    Edit: "Products.Edit",
    Delete: "Products.Delete"
  },
  Orders: {
    View: "Orders.View",
    Create: "Orders.Create",
    Edit: "Orders.Edit",
    Delete: "Orders.Delete"
  },
  Warehouse: {
    View: "Warehouse.View",
    Manage: "Warehouse.Manage",
    ManageProducts: "Warehouse.ManageProducts",
    ManageCategories: "Warehouse.ManageCategories",
    ManageLocations: "Warehouse.ManageLocations",
    ManageOrders: "Warehouse.ManageOrders"
  },
  Container: {
    View: "Container.View",
    Create: "Container.Create",
    Edit: "Container.Edit",
    Delete: "Container.Delete"
  },
  InstallmentOrders: {
    View: "InstallmentOrders.View"
  },
  StoreVouchers: {
    View: "StoreVouchers.View"
  },
  DomesticPurchase: {
    View: "DomesticPurchase.View",
    ManageSuppliers: "DomesticPurchase.ManageSuppliers",
    ManageProducts: "DomesticPurchase.ManageProducts",
    ManagePrefixCodes: "DomesticPurchase.ManagePrefixCodes"
  },
  Prices: {
    View: "Prices.View",
    Modify: "Prices.Modify",
    Delete: "Prices.Delete"
  },
  Reports: {
    View: "Reports.View",
    Export: "Reports.Export",
    ProductMovementView: "Reports.ProductMovement.View"
  },
  System: {
    ViewLogs: "System.ViewLogs",
    ManageScheduledTasks: "System.ManageScheduledTasks",
    ManageSettings: "System.ManageSettings",
    ViewAppDownloads: "System.ViewAppDownloads",
    ManageAppDownloads: "System.ManageAppDownloads"
  },
  DeviceRegistration: {
    View: "DeviceRegistration.View",
    Manage: "DeviceRegistration.Manage"
  },
  EmployeeProfiles: {
    View: "EmployeeProfiles.View",
    Edit: "EmployeeProfiles.Edit"
  },
  StoreProducts: {
    View: "StoreProducts.View",
    Create: "StoreProducts.Create",
    Edit: "StoreProducts.Edit"
  },
  Promotions: {
    View: "Promotions.View",
    Edit: "Promotions.Edit"
  },
  Advertisements: {
    View: "Advertisements.View",
    Edit: "Advertisements.Edit"
  },
  PricingStrategy: {
    View: "PricingStrategy.View",
    Edit: "PricingStrategy.Edit"
  },
  LocalPurchase: {
    View: "LocalPurchase.View",
    Edit: "LocalPurchase.Edit",
    PushToHq: "LocalPurchase.PushToHq"
  },
  AustralianSuppliers: {
    View: "AustralianSuppliers.View",
    Edit: "AustralianSuppliers.Edit"
  },
  Store: {
    ManageOperations: "Store.ManageOperations",
    ManageInfo: "Store.ManageInfo"
  },
  PosProducts: {
    View: "PosProducts.View",
    Manage: "PosProducts.Manage"
  },
  PosTerminal: {
    AuditView: "Permissions.PosTerminal.Audit.View"
  },
  Dashboard: {
    View: "Dashboard"
  },
  OrderFront: {
    View: "OrderFront"
  },
  Attendance: {
    ScheduleViewSelf: "Attendance.Schedule.ViewSelf",
    ScheduleViewStore: "Attendance.Schedule.ViewStore",
    ScheduleEditManagedStore: "Attendance.Schedule.EditManagedStore",
    AvailabilitySubmitSelf: "Attendance.Availability.SubmitSelf",
    AvailabilityViewManagedStore: "Attendance.Availability.ViewManagedStore",
    PunchSelf: "Attendance.Punch.Self",
    PunchViewManagedStore: "Attendance.Punch.ViewManagedStore",
    ApprovalViewManagedStore: "Attendance.Approval.ViewManagedStore",
    ApprovalReviewManagedStore: "Attendance.Approval.ReviewManagedStore",
    HolidayViewStore: "Attendance.Holiday.ViewStore",
    HolidayEditManagedStore: "Attendance.Holiday.EditManagedStore",
    LeaveApplySelf: "Attendance.Leave.ApplySelf",
    LeaveViewManagedStore: "Attendance.Leave.ViewManagedStore",
    LeaveReviewManagedStore: "Attendance.Leave.ReviewManagedStore",
    SettingsEdit: "Attendance.Settings.Edit",
    AdminView: "Attendance.Admin.View"
  }
};
var ALL_PERMISSIONS = Object.values(P).flatMap(
  (group) => Object.values(group)
);

// src/utils/webPortalAccess.ts
var ADMIN_ENTRY_RULES = [
  {
    defaultPath: "/dashboard",
    targetPrefixes: ["/dashboard"],
    canAccess: (access) => access.canAccessDashboard
  },
  {
    defaultPath: "/warehouse/store-orders",
    targetPrefixes: [
      "/warehouse/store-orders",
      "/warehouse/preorders",
      "/warehouse/store-order"
    ],
    canAccess: (access) => access.canManageWarehouseOrders
  },
  {
    defaultPath: "/warehouse/store-order-import-price-variance",
    targetPrefixes: ["/warehouse/store-order-import-price-variance"],
    canAccess: (access) => access.canManageStoreOrderImportPriceVariance
  },
  {
    defaultPath: "/warehouse/store-orders",
    // 旧 Warehouse.Manage 只覆盖其实际派生的仓库业务页，不能绕过价差或货柜叶子权限。
    targetPrefixes: [
      "/warehouse/products",
      "/warehouse/categories",
      "/warehouse/locations",
      "/warehouse/product-grade-management"
    ],
    canAccess: (access) => access.canManageWarehouse
  },
  {
    defaultPath: "/warehouse/containers",
    targetPrefixes: [
      "/warehouse/containers",
      "/warehouse/container/detail",
      "/warehouse/container/allocation-sales"
    ],
    canAccess: (access) => access.canViewContainers
  },
  {
    defaultPath: "/executive-sales-intelligence/product-movement-report",
    targetPrefixes: ["/executive-sales-intelligence/product-movement-report"],
    // Reports.View 兼容叶子页面访问，但后台导航入口与后端一致，仅认专用权限。
    canAccess: (access) => access.hasPermission(P.Reports.ProductMovementView)
  },
  {
    defaultPath: "/executive-sales-intelligence/purchase-amount-dashboard",
    targetPrefixes: [
      "/executive-sales-intelligence/purchase-amount-dashboard",
      "/pos-admin/local-supplier-invoices",
      "/pos-admin/local-supplier-purchase-sales-analysis",
      "/pos-admin/invoice-detail"
    ],
    canAccess: (access) => access.canManageLocalPurchase
  },
  {
    defaultPath: "/system/invoice-email-settings",
    targetPrefixes: [
      "/system/invoice-email-settings",
      "/system/payment-terminal-settings",
      "/system/emergency-login-keys"
    ],
    canAccess: (access) => access.canManageSystemSettings
  },
  {
    defaultPath: "/system/app-downloads",
    targetPrefixes: ["/system/app-downloads", "/system/wpf-versions"],
    canAccess: (access) => access.canViewAppDownloads
  },
  {
    defaultPath: "/pos-admin/operation-logs",
    targetPrefixes: ["/pos-admin/operation-logs"],
    canAccess: (access) => access.canViewOperationAudits
  }
];
function hasBackendNavigationAccess(access) {
  return access.isAdmin || ADMIN_ENTRY_RULES.some((rule) => rule.canAccess(access));
}

// src/utils/access.ts
var PERMISSION_ALIAS_GROUPS = [
  {
    canonicalCode: P.LocalPurchase.View,
    aliasCodes: ["LocalInvocie.View"]
  },
  {
    canonicalCode: P.LocalPurchase.Edit,
    aliasCodes: ["LocalInvocie.Edit"]
  }
];
var permissionAliasMap = /* @__PURE__ */ new Map();
PERMISSION_ALIAS_GROUPS.forEach(({ canonicalCode, aliasCodes }) => {
  const codes = [canonicalCode, ...aliasCodes];
  const uniqueCodes = Array.from(new Set(codes.map((code) => code.toLowerCase())));
  codes.forEach((code) => {
    permissionAliasMap.set(code.toLowerCase(), uniqueCodes);
  });
});
function getEquivalentPermissionCodes(permission) {
  const normalizedPermission = permission.toLowerCase();
  return permissionAliasMap.get(normalizedPermission) ?? [normalizedPermission];
}
function createEmptyAccess() {
  const alwaysFalse = () => false;
  return {
    isAdmin: false,
    isManager: false,
    isUser: false,
    isWarehouseStaff: false,
    isWarehouseStaffOnly: false,
    isWarehouseManager: false,
    isStoreStaff: false,
    isStoreManager: false,
    isStoreLevelManager: false,
    onlyOrder: false,
    canReadOrder: false,
    canWriteOrder: false,
    canDeleteOrder: false,
    canReadProduct: false,
    canWriteProduct: false,
    canDeleteProduct: false,
    canReadUser: false,
    canWriteUser: false,
    canDeleteUser: false,
    canReadRole: false,
    canWriteRole: false,
    canDeleteRole: false,
    canReadStore: false,
    canWriteStore: false,
    canDeleteStore: false,
    canManageWarehouse: false,
    canManageStore: false,
    canViewReports: false,
    canViewSalesIntelligence: false,
    canViewProductMovementReport: false,
    canExportData: false,
    canModifyPrice: false,
    canDeletePrice: false,
    // 新细粒度权限
    canManageWarehouseProducts: false,
    canManageWarehouseOrders: false,
    canManageStoreOrderImportPriceVariance: false,
    canManageWarehouseCategories: false,
    canManageWarehouseLocations: false,
    canViewContainers: false,
    canCreateContainer: false,
    canEditContainer: false,
    canDeleteContainer: false,
    canManageStoreProducts: false,
    canEditStoreProducts: false,
    canCreateStoreProducts: false,
    canManageStoreOps: false,
    canManageLocalPurchase: false,
    canEditLocalPurchase: false,
    canPushLocalPurchaseToHq: false,
    canManagePricing: false,
    canEditPricing: false,
    canManagePromotions: false,
    canEditPromotions: false,
    canManageAdvertisements: false,
    canEditAdvertisements: false,
    canViewAustralianSuppliers: false,
    canEditAustralianSuppliers: false,
    canManageDomesticSuppliers: false,
    canManageDomesticProducts: false,
    canManageDomesticPrefixCodes: false,
    canViewAttendanceSchedule: false,
    canEditAttendanceSchedule: false,
    canViewAttendanceAvailability: false,
    canViewAttendancePunches: false,
    canReviewAttendance: false,
    canEditAttendanceHoliday: false,
    canEditAttendanceSettings: false,
    canViewEmployeeProfiles: false,
    canViewSystemLogs: false,
    canViewOperationAudits: false,
    canManageSystemSettings: false,
    canManageScheduledTasks: false,
    canViewAppDownloads: false,
    canManageAppDownloads: false,
    canViewDeviceRegistration: false,
    canManageDeviceRegistration: false,
    canViewPosProducts: false,
    canManagePosProducts: false,
    canAccessAdminShell: false,
    canAccessDashboard: false,
    canAccessOrderFront: false,
    hasPermission: alwaysFalse,
    hasRole: alwaysFalse,
    onlyRole: alwaysFalse,
    hasAnyRole: alwaysFalse,
    hasAllRoles: alwaysFalse,
    managedStoreCodes: () => null,
    visibleStoreCodes: () => null
  };
}
function buildAccess(currentUser) {
  if (!currentUser) {
    return createEmptyAccess();
  }
  const hasRole = (role) => currentUser.roleNames?.some((item) => item.toLowerCase() === role.toLowerCase()) ?? false;
  const isAdmin = hasRole("Admin") || hasRole("\u7BA1\u7406\u5458") || hasRole("SuperAdmin") || hasRole("\u8D85\u7EA7\u7BA1\u7406\u5458");
  const isWarehouseManager = hasRole("WarehouseManager") || hasRole("\u4ED3\u5E93\u7ECF\u7406");
  const currentPermissionSet = new Set((currentUser.permissions ?? []).map((item) => item.toLowerCase()));
  const hasPermission = (permission) => {
    if (isAdmin) return true;
    return getEquivalentPermissionCodes(permission).some((code) => currentPermissionSet.has(code));
  };
  const onlyRole = (role) => {
    if (!currentUser.roleNames?.length) {
      return false;
    }
    return hasRole(role) && currentUser.roleNames.length === 1;
  };
  const hasAnyRole = (roles) => roles.some((role) => hasRole(role));
  const hasAllRoles = (roles) => roles.every((role) => hasRole(role));
  const isStoreManager = hasRole("StoreManager") || hasRole("\u5E97\u957F") || hasRole("\u7ECF\u7406");
  const isManager = isStoreManager || isWarehouseManager;
  const isUser = hasRole("User") || hasRole("\u7528\u6237");
  const isWarehouseStaff = isAdmin || hasRole("WarehouseStaff") || hasRole("\u4ED3\u5E93\u5458\u5DE5") || hasRole("WarehouseManager");
  const isWarehouseStaffOnly = isWarehouseStaff && !isAdmin && !isWarehouseManager && (hasRole("WarehouseStaff") || hasRole("\u4ED3\u5E93\u5458\u5DE5"));
  const isStoreStaff = hasRole("StoreStaff") || hasRole("\u5E97\u94FA\u5458\u5DE5");
  const isStoreLevelManager = isStoreManager && !isAdmin && !isWarehouseManager;
  const onlyOrder = onlyRole("Order") || hasRole("\u8BA2\u8D27\u5458");
  const managedStoreCodes = () => {
    if (isAdmin || isWarehouseManager) {
      return null;
    }
    if (currentUser.stores?.length) {
      return currentUser.stores.filter((item) => item.isManageable).map((item) => item.storeCode).filter(Boolean);
    }
    return [];
  };
  const visibleStoreCodes = () => {
    if (isAdmin || isWarehouseManager) {
      return null;
    }
    if (currentUser.stores?.length) {
      return currentUser.stores.map((item) => item.storeCode).filter(Boolean);
    }
    return [];
  };
  const canReadUser = isAdmin || hasPermission(P.Users.View);
  const canWriteUser = isAdmin || hasPermission(P.Users.Create) || hasPermission(P.Users.Edit);
  const canDeleteUser = isAdmin || hasPermission(P.Users.Delete);
  const canReadRole = isAdmin || hasPermission(P.Roles.View);
  const canWriteRole = isAdmin || hasPermission(P.Roles.Create) || hasPermission(P.Roles.Edit);
  const canDeleteRole = isAdmin || hasPermission(P.Roles.Delete);
  const canReadStore = isAdmin || hasPermission(P.Stores.View);
  const canWriteStore = isAdmin || hasPermission(P.Stores.Create) || hasPermission(P.Stores.Edit);
  const canDeleteStore = isAdmin || hasPermission(P.Stores.Delete);
  const canManageWarehouse = isAdmin || hasPermission(P.Warehouse.Manage);
  const canManageStore = isAdmin || hasPermission(P.Stores.Edit) || hasPermission(P.Warehouse.Manage);
  const canReadOrder = isAdmin || hasPermission(P.Orders.View);
  const canWriteOrder = isAdmin || hasPermission(P.Orders.Create) || hasPermission(P.Orders.Edit);
  const canDeleteOrder = isAdmin || hasPermission(P.Orders.Delete);
  const canReadProduct = isAdmin || hasPermission(P.Products.View);
  const canWriteProduct = isAdmin || hasPermission(P.Products.Create) || hasPermission(P.Products.Edit);
  const canDeleteProduct = isAdmin || hasPermission(P.Products.Delete);
  const canViewReports = isAdmin || hasPermission(P.Reports.View);
  const canViewProductMovementReport = isAdmin || hasPermission(P.Reports.ProductMovementView) || hasPermission(P.Reports.View);
  const canViewSalesIntelligence = canViewReports || canViewProductMovementReport || hasPermission(P.LocalPurchase.View);
  const canExportData = isAdmin || hasPermission(P.Reports.Export);
  const canModifyPrice = isAdmin || hasPermission(P.Prices.Modify);
  const canDeletePrice = isAdmin || hasPermission(P.Prices.Delete);
  const canManageWarehouseProducts = isAdmin || hasPermission(P.Warehouse.ManageProducts) || hasPermission(P.Warehouse.Manage);
  const canManageWarehouseOrders = isAdmin || hasPermission(P.Warehouse.ManageOrders) || hasPermission(P.Warehouse.Manage);
  const canManageStoreOrderImportPriceVariance = canManageWarehouseOrders && !isWarehouseStaffOnly;
  const canManageWarehouseCategories = isAdmin || hasPermission(P.Warehouse.ManageCategories) || hasPermission(P.Warehouse.Manage);
  const canManageWarehouseLocations = isAdmin || hasPermission(P.Warehouse.ManageLocations) || hasPermission(P.Warehouse.Manage);
  const canViewContainers = isAdmin || hasPermission(P.Container.View);
  const canCreateContainer = isAdmin || hasPermission(P.Container.Create);
  const canEditContainer = isAdmin || hasPermission(P.Container.Edit);
  const canDeleteContainer = isAdmin || hasPermission(P.Container.Delete);
  const canManageStoreProducts = isAdmin || hasPermission(P.StoreProducts.View);
  const canEditStoreProducts = isAdmin || hasPermission(P.StoreProducts.Edit);
  const canCreateStoreProducts = isAdmin || hasPermission(P.StoreProducts.Create);
  const canManageStoreOps = isAdmin || hasPermission(P.Store.ManageOperations);
  const canManageLocalPurchase = isAdmin || hasPermission(P.LocalPurchase.View);
  const canEditLocalPurchase = isAdmin || hasPermission(P.LocalPurchase.Edit);
  const canPushLocalPurchaseToHq = isAdmin || hasPermission(P.LocalPurchase.PushToHq);
  const canManagePricing = isAdmin || hasPermission(P.PricingStrategy.View);
  const canEditPricing = isAdmin || hasPermission(P.PricingStrategy.Edit);
  const canManagePromotions = isAdmin || hasPermission(P.Promotions.View);
  const canEditPromotions = isAdmin || hasPermission(P.Promotions.Edit);
  const canManageAdvertisements = isAdmin || hasPermission(P.Advertisements.View);
  const canEditAdvertisements = isAdmin || hasPermission(P.Advertisements.Edit);
  const canViewAustralianSuppliers = isAdmin || hasPermission(P.AustralianSuppliers.View);
  const canEditAustralianSuppliers = isAdmin || hasPermission(P.AustralianSuppliers.Edit);
  const canManageDomesticSuppliers = isAdmin || hasPermission(P.DomesticPurchase.ManageSuppliers);
  const canManageDomesticProducts = isAdmin || hasPermission(P.DomesticPurchase.ManageProducts);
  const canManageDomesticPrefixCodes = isAdmin || hasPermission(P.DomesticPurchase.ManagePrefixCodes);
  const canViewAttendanceSchedule = isAdmin || hasPermission(P.Attendance.AdminView) || hasPermission(P.Attendance.ScheduleViewStore);
  const canEditAttendanceSchedule = isAdmin || hasPermission(P.Attendance.AdminView) || hasPermission(P.Attendance.ScheduleEditManagedStore);
  const canViewAttendanceAvailability = isAdmin || hasPermission(P.Attendance.AdminView) || hasPermission(P.Attendance.AvailabilityViewManagedStore);
  const canViewAttendancePunches = isAdmin || hasPermission(P.Attendance.AdminView) || hasPermission(P.Attendance.PunchViewManagedStore);
  const canReviewAttendance = isAdmin || hasPermission(P.Attendance.AdminView) || hasPermission(P.Attendance.ApprovalReviewManagedStore) || hasPermission(P.Attendance.LeaveReviewManagedStore);
  const canEditAttendanceHoliday = isAdmin || hasPermission(P.Attendance.AdminView) || hasPermission(P.Attendance.HolidayEditManagedStore);
  const canEditAttendanceSettings = isAdmin || hasPermission(P.Attendance.SettingsEdit);
  const canViewEmployeeProfiles = isAdmin || hasPermission(P.EmployeeProfiles.View);
  const canViewSystemLogs = isAdmin || hasPermission(P.System.ViewLogs);
  const canViewOperationAudits = isAdmin || hasPermission(P.PosTerminal.AuditView);
  const canManageScheduledTasks = isAdmin || hasPermission(P.System.ManageScheduledTasks);
  const canManageSystemSettings = isAdmin || hasPermission(P.System.ManageSettings);
  const canManageAppDownloads = isAdmin || hasPermission(P.System.ManageAppDownloads);
  const canViewAppDownloads = isAdmin || canManageAppDownloads || hasPermission(P.System.ViewAppDownloads);
  const canManageDeviceRegistration = isAdmin || hasPermission(P.DeviceRegistration.Manage);
  const canViewDeviceRegistration = canManageDeviceRegistration || isAdmin || hasPermission(P.DeviceRegistration.View);
  const canViewPosProducts = isAdmin || hasPermission(P.PosProducts.View) || hasPermission(P.PosProducts.Manage);
  const canManagePosProducts = isAdmin || hasPermission(P.PosProducts.Manage);
  const canAccessDashboard = isAdmin || hasPermission(P.Dashboard.View);
  const canAccessAdminShell = hasBackendNavigationAccess({
    isAdmin,
    canAccessDashboard,
    canManageWarehouse,
    canManageWarehouseOrders,
    canManageStoreOrderImportPriceVariance,
    canViewContainers,
    canViewProductMovementReport,
    canManageLocalPurchase,
    canEditLocalPurchase,
    canManageSystemSettings,
    canViewAppDownloads,
    canViewOperationAudits,
    hasPermission
  });
  const canAccessOrderFront = isAdmin || hasPermission(P.OrderFront.View) || isWarehouseStaffOnly && hasPermission(P.Orders.Create);
  return {
    isAdmin,
    isManager,
    isUser,
    isWarehouseStaff,
    isWarehouseStaffOnly,
    isWarehouseManager,
    isStoreStaff,
    isStoreManager,
    isStoreLevelManager,
    onlyOrder,
    canReadOrder,
    canWriteOrder,
    canDeleteOrder,
    canReadProduct,
    canWriteProduct,
    canDeleteProduct,
    canReadUser,
    canWriteUser,
    canDeleteUser,
    canReadRole,
    canWriteRole,
    canDeleteRole,
    canReadStore,
    canWriteStore,
    canDeleteStore,
    canManageWarehouse,
    canManageStore,
    canViewReports,
    canViewSalesIntelligence,
    canViewProductMovementReport,
    canExportData,
    canModifyPrice,
    canDeletePrice,
    // 新细粒度
    canManageWarehouseProducts,
    canManageWarehouseOrders,
    canManageStoreOrderImportPriceVariance,
    canManageWarehouseCategories,
    canManageWarehouseLocations,
    canViewContainers,
    canCreateContainer,
    canEditContainer,
    canDeleteContainer,
    canManageStoreProducts,
    canEditStoreProducts,
    canCreateStoreProducts,
    canManageStoreOps,
    canManageLocalPurchase,
    canEditLocalPurchase,
    canPushLocalPurchaseToHq,
    canManagePricing,
    canEditPricing,
    canManagePromotions,
    canEditPromotions,
    canManageAdvertisements,
    canEditAdvertisements,
    canViewAustralianSuppliers,
    canEditAustralianSuppliers,
    canManageDomesticSuppliers,
    canManageDomesticProducts,
    canManageDomesticPrefixCodes,
    canViewAttendanceSchedule,
    canEditAttendanceSchedule,
    canViewAttendanceAvailability,
    canViewAttendancePunches,
    canReviewAttendance,
    canEditAttendanceHoliday,
    canEditAttendanceSettings,
    canViewEmployeeProfiles,
    canViewSystemLogs,
    canViewOperationAudits,
    canManageScheduledTasks,
    canManageSystemSettings,
    canViewAppDownloads,
    canManageAppDownloads,
    canViewDeviceRegistration,
    canManageDeviceRegistration,
    canViewPosProducts,
    canManagePosProducts,
    canAccessAdminShell,
    canAccessDashboard,
    canAccessOrderFront,
    hasPermission,
    hasRole,
    onlyRole,
    hasAnyRole,
    hasAllRoles,
    managedStoreCodes,
    visibleStoreCodes
  };
}

// src/pages/Warehouse/Categories/categoryProductFilters.ts
var ALL_PRODUCTS_FILTER_KEY = "__ALL_PRODUCTS__";
var UNCATEGORIZED_PRODUCTS_FILTER_KEY = "__UNCATEGORIZED_PRODUCTS__";
function resolveCategoryProductFilterMode(value) {
  if (value === ALL_PRODUCTS_FILTER_KEY) {
    return { type: "all" };
  }
  if (!value || value === UNCATEGORIZED_PRODUCTS_FILTER_KEY) {
    return { type: "uncategorized" };
  }
  return { type: "category", categoryGuid: value };
}

// src/pages/Warehouse/Products/columnFilters.ts
var FILTER_TOKEN_PREFIXES = ["contains", "eq", "starts", "ends", "gte", "lte"];
var FILTER_TOKEN_NAMESPACE = "__filter";
function setFilterValues(filters, key, values) {
  const normalizedValues = (values ?? []).map((value) => value === void 0 || value === null ? "" : String(value).trim()).filter(Boolean);
  if (!normalizedValues.length) {
    if (!(key in filters)) {
      return filters;
    }
    const nextFilters = { ...filters };
    delete nextFilters[key];
    return nextFilters;
  }
  return {
    ...filters,
    [key]: normalizedValues
  };
}
function buildRangeFilterTokens(min, max) {
  const tokens = [];
  if (min !== void 0 && min !== null && String(min).trim()) {
    tokens.push(`gte:${String(min).trim()}`);
  }
  if (max !== void 0 && max !== null && String(max).trim()) {
    tokens.push(`lte:${String(max).trim()}`);
  }
  return tokens;
}
function findFilterTokenValue(values, prefix) {
  return values?.find((value) => value.startsWith(prefix))?.slice(prefix.length) ?? "";
}
function buildModeToken(mode, value) {
  const normalizedValue = value === void 0 || value === null ? "" : String(value).trim();
  return normalizedValue ? `${FILTER_TOKEN_NAMESPACE}:${mode}:${normalizedValue}` : void 0;
}
function splitFilterToken(value) {
  const normalizedValue = value?.trim() ?? "";
  const namespacePrefix = `${FILTER_TOKEN_NAMESPACE}:`;
  if (!normalizedValue.startsWith(namespacePrefix)) {
    return { mode: void 0, value: normalizedValue };
  }
  const tokenBody = normalizedValue.slice(namespacePrefix.length);
  const separatorIndex = tokenBody.indexOf(":");
  if (separatorIndex <= 0) {
    return { mode: void 0, value: normalizedValue };
  }
  const rawMode = tokenBody.slice(0, separatorIndex);
  const tokenValue = tokenBody.slice(separatorIndex + 1).trim();
  const mode = FILTER_TOKEN_PREFIXES.find((item) => item === rawMode);
  if (!mode) {
    return { mode: void 0, value: normalizedValue };
  }
  return { mode, value: tokenValue };
}
function buildTextFilterTokens(mode, value) {
  const token = buildModeToken(mode, value);
  return token ? [token] : [];
}
function parseTextFilterTokens(values) {
  const firstValue = values?.find((value) => value.trim()) ?? "";
  const parsed = splitFilterToken(firstValue);
  if (parsed.mode === "eq" || parsed.mode === "starts" || parsed.mode === "ends") {
    return { mode: parsed.mode, value: parsed.value };
  }
  return {
    // 兼容旧列头筛选：没有模式前缀的文本值继续按 contains 处理。
    mode: "contains",
    value: parsed.mode === "contains" ? parsed.value : firstValue.trim()
  };
}
function buildComparableFilterTokens(mode, values) {
  if (mode === "range") {
    return buildRangeFilterTokens(values.min, values.max);
  }
  if (mode === "gte") {
    return buildRangeFilterTokens(values.value, void 0);
  }
  if (mode === "lte") {
    return buildRangeFilterTokens(void 0, values.value);
  }
  const token = buildModeToken(mode, values.value);
  return token ? [token] : [];
}
function parseComparableFilterTokens(values) {
  const normalizedValues = values?.map((value) => value.trim()).filter(Boolean) ?? [];
  const gteValue = findFilterTokenValue(normalizedValues, "gte:");
  const lteValue = findFilterTokenValue(normalizedValues, "lte:");
  if (gteValue && lteValue) {
    return { mode: "range", min: gteValue, max: lteValue, value: "" };
  }
  if (gteValue) {
    return { mode: "gte", value: gteValue, min: "", max: "" };
  }
  if (lteValue) {
    return { mode: "lte", value: lteValue, min: "", max: "" };
  }
  const parsed = splitFilterToken(normalizedValues[0]);
  if (parsed.mode === "eq") {
    return { mode: "eq", value: parsed.value, min: "", max: "" };
  }
  return { mode: "eq", value: normalizedValues[0] ?? "", min: "", max: "" };
}
function normalizeTableFilters(filters) {
  const filterKeyMap = {
    name: "productName",
    labelPrice: "oemPrice"
  };
  return Object.entries(filters).reduce((current, [key, value]) => {
    if (key === "categoryName" || !value?.length) {
      return current;
    }
    const mappedFilterKey = filterKeyMap[key] ?? key;
    const normalizedValues = value.map((item) => String(item).trim());
    return setFilterValues(current, mappedFilterKey, normalizedValues);
  }, {});
}
function resolveCategoryFilterValueFromTableFilters(filters) {
  const categoryValues = filters.categoryName?.map((value) => String(value).trim()).filter(Boolean) ?? [];
  return categoryValues[0] || ALL_PRODUCTS_FILTER_KEY;
}
function buildCategoryQueryValue(categoryValue) {
  const filterMode = resolveCategoryProductFilterMode(categoryValue);
  if (filterMode.type === "category") {
    return { categoryGuid: filterMode.categoryGuid, uncategorizedOnly: false };
  }
  if (filterMode.type === "uncategorized") {
    return { categoryGuid: void 0, uncategorizedOnly: true };
  }
  return { categoryGuid: void 0, uncategorizedOnly: false };
}
function getSingleFilterValue(values) {
  return values?.length === 1 ? values[0] : void 0;
}

// src/pages/Warehouse/Products/WarehouseProducts.hqSync.logic.test.ts
function createCurrentUser(overrides = {}) {
  return {
    userGUID: "test-user-guid",
    username: "tester",
    email: "tester@example.com",
    permissions: [],
    roleNames: [],
    storeNames: [],
    ...overrides
  };
}
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
function assertDeepEqual(actual, expected, message) {
  const actualText = JSON.stringify(actual);
  const expectedText = JSON.stringify(expected);
  if (actualText !== expectedText) {
    throw new Error(`${message}\u3002Expected: ${expectedText}, received: ${actualText}`);
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
  throw new Error(`${label}\u3002Expected promise to reject`);
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
function extractSection(source, startText, endText) {
  const startIndex = source.indexOf(startText);
  assert(startIndex >= 0, `\u672A\u627E\u5230\u4EE3\u7801\u7247\u6BB5\uFF1A${startText}`);
  const endIndex = source.indexOf(endText, startIndex);
  assert(endIndex >= 0, `\u672A\u627E\u5230\u7ED3\u675F\u7247\u6BB5\uFF1A${endText}`);
  return source.slice(startIndex, endIndex);
}
var pageFile = path.resolve(process.cwd(), "src/pages/Warehouse/Products/index.tsx");
var pageSource = readFileSync(pageFile, "utf8");
var columnFiltersFile = path.resolve(process.cwd(), "src/pages/Warehouse/Products/columnFilters.ts");
var columnFiltersSource = readFileSync(columnFiltersFile, "utf8");
var categoryTreePickerFile = path.resolve(process.cwd(), "src/pages/Warehouse/Products/CategoryTreePicker.tsx");
var categoryTreePickerSource = readFileSync(categoryTreePickerFile, "utf8");
async function main() {
  const failures = [];
  const adminAccessFailure = await runTest("Admin \u6743\u9650\u5224\u65AD\u6210\u7ACB", () => {
    const access = buildAccess(
      createCurrentUser({
        roleNames: ["Admin"]
      })
    );
    assertEqual(access.isAdmin, true, "Admin \u5E94\u88AB\u8BC6\u522B\u4E3A\u7BA1\u7406\u5458");
  });
  if (adminAccessFailure) failures.push(adminAccessFailure);
  const nonAdminAccessFailure = await runTest("\u975E Admin \u6743\u9650\u4E0D\u4F1A\u663E\u793A\u540C\u6B65\u6309\u94AE", () => {
    const access = buildAccess(
      createCurrentUser({
        roleNames: ["WarehouseStaff"]
      })
    );
    assertEqual(access.isAdmin, false, "WarehouseStaff \u4E0D\u5E94\u88AB\u8BC6\u522B\u4E3A\u7BA1\u7406\u5458");
  });
  if (nonAdminAccessFailure) failures.push(nonAdminAccessFailure);
  const shelfStatusTextFailure = await runTest("\u4ED3\u5E93\u5546\u54C1\u72B6\u6001\u6587\u6848\u5E94\u4F7F\u7528\u4E0A\u67B6\u548C\u4E0B\u67B6", () => {
    assert(
      pageSource.includes("function getShelfStatusLabel(isActive: boolean") && pageSource.includes("t('warehouse.onShelf', '\u4E0A\u67B6')") && pageSource.includes("t('warehouse.offShelf', '\u4E0B\u67B6')"),
      "\u9875\u9762\u5E94\u901A\u8FC7 getShelfStatusLabel \u7EDF\u4E00\u4ED3\u5E93\u5546\u54C1\u4E0A\u4E0B\u67B6\u6587\u6848"
    );
    const formModalSection = extractSection(
      pageSource,
      "function ProductFormModal",
      "function SetItemsModal"
    );
    assert(
      formModalSection.includes("label={t('warehouse.isListed')}") && formModalSection.includes("checkedChildren={getShelfStatusLabel(true, t)}") && formModalSection.includes("unCheckedChildren={getShelfStatusLabel(false, t)}"),
      "\u7F16\u8F91\u5F39\u7A97\u72B6\u6001\u5B57\u6BB5\u5E94\u663E\u793A\u662F\u5426\u4E0A\u67B6\u548C\u4E0A\u67B6/\u4E0B\u67B6 Switch \u6587\u6848"
    );
    const columnsSection = extractSection(
      pageSource,
      "const baseColumns = useMemo",
      "return (<>"
    );
    assert(
      columnsSection.includes("checkedChildren={getShelfStatusLabel(true, t)}") && columnsSection.includes("unCheckedChildren={getShelfStatusLabel(false, t)}") && !columnsSection.includes("t('warehouse.active')") && !columnsSection.includes("t('warehouse.inactive')"),
      "\u4E3B\u8868\u72B6\u6001\u5217\u5E94\u663E\u793A\u4E0A\u67B6/\u4E0B\u67B6\uFF0C\u4E0D\u80FD\u7EE7\u7EED\u4F7F\u7528\u542F\u7528/\u505C\u7528\u6587\u6848"
    );
    const batchSection = extractSection(
      pageSource,
      "const handleBatchToggleActive = async",
      "const handleToggleSingleActive"
    );
    const singleSection = extractSection(
      pageSource,
      "const handleToggleSingleActive = async",
      "const handleOpenSetItems"
    );
    assert(
      batchSection.includes("status: getShelfStatusLabel(nextIsActive, t)") && singleSection.includes("status: getShelfStatusLabel(nextIsActive, t)"),
      "\u6279\u91CF\u548C\u5355\u6761\u72B6\u6001\u6210\u529F\u63D0\u793A\u5E94\u7EDF\u4E00\u4F7F\u7528\u4E0A\u67B6/\u4E0B\u67B6\u6587\u6848"
    );
  });
  if (shelfStatusTextFailure) failures.push(shelfStatusTextFailure);
  const productTypeAndActionFailure = await runTest("\u4ED3\u5E93\u5546\u54C1\u7C7B\u578B\u5217\u548C\u64CD\u4F5C\u5165\u53E3\u5E94\u533A\u5206\u666E\u901A\u5957\u88C5\u591A\u7801", () => {
    assert(
      pageSource.includes("function getProductTypeTagColor(value: ProductType)") && pageSource.includes("if (value === ProductType.SET) return") && pageSource.includes("if (value === ProductType.MULTICODE) return") && pageSource.includes("function canManageProductDetails(productType: ProductType)"),
      "\u9875\u9762\u5E94\u58F0\u660E\u5546\u54C1\u7C7B\u578B\u989C\u8272\u548C\u53EF\u7BA1\u7406\u7C7B\u578B\u5224\u65AD"
    );
    const columnsSection = extractSection(
      pageSource,
      "const baseColumns = useMemo",
      "return (<>"
    );
    assert(
      columnsSection.includes("title: t('column.productType')") && columnsSection.includes("dataIndex: 'productType'") && columnsSection.includes("<Tag color={getProductTypeTagColor(value)}>{getProductTypeLabel(value, t)}</Tag>"),
      "\u5546\u54C1\u7C7B\u578B\u5217\u5E94\u4EE5 Tag \u663E\u793A\u666E\u901A\u3001\u5957\u88C5\u548C\u591A\u7801"
    );
    assert(
      columnsSection.includes("canManageProductDetails(record.productType)") && columnsSection.includes("getProductDetailsActionLabel(record.productType, t)") && columnsSection.includes("getProductDetailsDisabledHint(t)") && !columnsSection.includes("record.productType === 1 ?"),
      "\u64CD\u4F5C\u5217\u5E94\u5141\u8BB8\u5957\u88C5\u548C\u591A\u7801\u8FDB\u5165\u7BA1\u7406\u5165\u53E3\uFF0C\u4E0D\u80FD\u518D\u53EA\u5224\u65AD productType === 1"
    );
    assert(
      pageSource.includes("t('warehouse.multiCodeManagement', '\u591A\u7801\u7BA1\u7406')") && pageSource.includes("t('warehouse.normalProductNoDetails', '\u666E\u901A\u5546\u54C1\u6CA1\u6709\u5957\u88C5\u6216\u591A\u7801\u660E\u7EC6')"),
      "\u591A\u7801\u5546\u54C1\u548C\u666E\u901A\u5546\u54C1\u5E94\u6709\u660E\u786E\u64CD\u4F5C\u6587\u6848"
    );
  });
  if (productTypeAndActionFailure) failures.push(productTypeAndActionFailure);
  const productDetailsModalFailure = await runTest("\u5957\u88C5\u548C\u591A\u7801\u5E94\u590D\u7528\u660E\u7EC6\u5F39\u7A97\u4F46\u6309\u7C7B\u578B\u663E\u793A\u6807\u9898\u548C\u63D0\u793A", () => {
    const modalSection = extractSection(
      pageSource,
      "function SetItemsModal",
      "export default function WarehouseProductsPage"
    );
    assert(
      modalSection.includes("title={getProductDetailsModalTitle(product, t)}") && modalSection.includes("getProductDetailsHint(product?.productType, t)") && modalSection.includes("t('warehouse.addMultiCodeDetail', '\u65B0\u589E\u591A\u7801')"),
      "\u660E\u7EC6\u5F39\u7A97\u5E94\u6309\u5546\u54C1\u7C7B\u578B\u5C55\u793A\u5957\u88C5\u6216\u591A\u7801\u6807\u9898\u3001\u63D0\u793A\u548C\u65B0\u589E\u6309\u94AE"
    );
    assert(
      pageSource.includes("t('warehouse.multiCodeDetailsTitle', '\u591A\u7801\u7BA1\u7406 - {{name}}'") && pageSource.includes("t('warehouse.multiCodeEditHint', '\u591A\u7801\u5546\u54C1\u53EF\u7EF4\u62A4\u591A\u7801\u6761\u7801\u3001\u4EF7\u683C\u548C\u5206\u5E97\u540C\u6B65\u4F7F\u7528\u7684\u660E\u7EC6\u3002')"),
      "\u591A\u7801\u660E\u7EC6\u5F39\u7A97\u5E94\u6709\u72EC\u7ACB\u6807\u9898\u548C\u8BF4\u660E\u6587\u6848"
    );
  });
  if (productDetailsModalFailure) failures.push(productDetailsModalFailure);
  const warehouseProductSetCodesFailure = await runTest("\u4ED3\u5E93\u5957\u88C5\u660E\u7EC6\u5F39\u7A97\u5E94\u8BFB\u53D6\u5E76\u4FDD\u5B58 product-set-codes \u660E\u7EC6", () => {
    assert(
      pageSource.includes("from '../../../services/multiCodeSetService'") && pageSource.includes("getGridData as getSetCodeGridData") && pageSource.includes("batchCreateSetCodes") && pageSource.includes("batchUpdateBarcodes as batchUpdateSetBarcodes") && pageSource.includes("batchUpdatePrices as batchUpdateSetPrices") && pageSource.includes("batchDelete as batchDeleteSetCodes"),
      "\u4ED3\u5E93\u5546\u54C1\u9875\u5E94\u4F7F\u7528 product-set-codes \u670D\u52A1\u7EF4\u62A4\u5957\u88C5/\u591A\u7801\u660E\u7EC6"
    );
    assert(
      !pageSource.includes("getDomesticProductSetItems") && !pageSource.includes("updateDomesticProductSetItems") && !pageSource.includes("DomesticProductSetItem"),
      "\u4ED3\u5E93\u5546\u54C1\u9875\u4E0D\u80FD\u7EE7\u7EED\u5F15\u7528\u56FD\u5185\u91C7\u8D2D set-items \u670D\u52A1\u548C\u7C7B\u578B"
    );
    const modalSection = extractSection(
      pageSource,
      "function SetItemsModal",
      "export default function WarehouseProductsPage"
    );
    assert(
      modalSection.includes("items: MulticodeSetItem[]") && modalSection.includes("dataIndex: 'setItemNumber'") && modalSection.includes("dataIndex: 'setBarcode'") && modalSection.includes("dataIndex: 'setPurchasePrice'") && modalSection.includes("dataIndex: 'setRetailPrice'") && modalSection.includes("dataIndex: 'isActive'"),
      "\u5F39\u7A97\u5217\u5E94\u4F7F\u7528 product-set-codes \u7684\u8D27\u53F7\u3001\u6761\u7801\u3001\u8FDB\u8D27\u4EF7\u3001\u96F6\u552E\u4EF7\u548C\u72B6\u6001\u5B57\u6BB5"
    );
    const openSection = extractSection(
      pageSource,
      "const handleOpenSetItems = async (record: WarehouseProductListItem) => {",
      "const handleSaveSetItems = async () => {"
    );
    assert(
      openSection.includes("getSetCodeGridData({ productCode: record.productCode") && openSection.includes("setSetItemsDraft(result.items ?? [])"),
      "\u6253\u5F00\u4ED3\u5E93\u5957\u88C5\u5F39\u7A97\u65F6\u5E94\u6309\u5F53\u524D\u4ED3\u5E93\u5546\u54C1 productCode \u8BFB\u53D6 product-set-codes grid"
    );
    const saveSection = extractSection(
      pageSource,
      "const handleSaveSetItems = async () => {",
      "const handleExport = async () => {"
    );
    assert(
      saveSection.includes("batchCreateSetCodes({") && saveSection.includes("batchUpdateSetBarcodes({") && saveSection.includes("batchUpdateSetPrices({") && saveSection.includes("batchDeleteSetCodes({ ids: deletedSetCodeIds })"),
      "\u4FDD\u5B58\u4ED3\u5E93\u5957\u88C5\u5F39\u7A97\u65F6\u5E94\u5206\u522B\u5904\u7406\u65B0\u589E\u3001\u5DF2\u6709\u66F4\u65B0\u548C\u5220\u9664\u7684 product-set-codes \u660E\u7EC6"
    );
    assert(
      saveSection.includes("invalidSetCodeItem") && saveSection.includes("message.error(t('warehouse.invalidSetCodeDetail'") && saveSection.includes("item.setBarcode?.trim()") && saveSection.includes("item.setPurchasePrice === undefined") && saveSection.includes("item.setRetailPrice === undefined"),
      "\u4FDD\u5B58\u524D\u5E94\u6821\u9A8C\u5957\u88C5\u660E\u7EC6\u6761\u7801\u3001\u8FDB\u8D27\u4EF7\u548C\u96F6\u552E\u4EF7\uFF0C\u907F\u514D\u7A7A\u65B0\u589E\u6216\u6E05\u7A7A\u5B57\u6BB5\u9759\u9ED8\u5931\u8D25"
    );
    assert(
      saveSection.indexOf("batchDeleteSetCodes({ ids: deletedSetCodeIds })") > saveSection.indexOf("batchUpdateSetStatus({"),
      "\u5220\u9664\u5DF2\u6709\u660E\u7EC6\u5FC5\u987B\u653E\u5728\u65B0\u589E\u548C\u66F4\u65B0\u4E4B\u540E\uFF0C\u907F\u514D\u540E\u7EED\u63A5\u53E3\u5931\u8D25\u65F6\u5148\u5220\u6570\u636E"
    );
  });
  if (warehouseProductSetCodesFailure) failures.push(warehouseProductSetCodesFailure);
  const minOrderQuantityColumnFailure = await runTest("\u4ED3\u5E93\u5546\u54C1\u5217\u8868\u5E94\u4EE5 MinOrderQuantity \u4F5C\u4E3A\u4E2D\u5305\u6570\u5217\u6765\u6E90", () => {
    const columnsSection = extractSection(
      pageSource,
      "const baseColumns = useMemo",
      "return (<>"
    );
    assert(
      columnsSection.includes("title: t('warehouse.middlePackQuantity', '\u4E2D\u5305\u6570')") && columnsSection.includes("dataIndex: 'minOrderQuantity'"),
      "\u4E3B\u8868\u5E94\u65B0\u589E\u4E2D\u5305\u6570\u5217\uFF0C\u5E76\u7ED1\u5B9A WarehouseProduct.MinOrderQuantity \u5F52\u4E00\u540E\u7684 minOrderQuantity"
    );
    assert(
      !columnsSection.includes("dataIndex: 'middlePackQty'"),
      "\u4E3B\u8868\u4E2D\u5305\u6570\u5217\u4E0D\u80FD\u7ED1\u5B9A middlePackQty\uFF0C\u907F\u514D\u4E0E MiddlePackQuantity \u6765\u6E90\u6DF7\u6DC6"
    );
    const packingColumnSection = extractSection(
      columnsSection,
      "key: 'packingQty'",
      "key: 'volume'"
    );
    assert(
      packingColumnSection.includes(`record.isPackingQtyFallback ? <Tag color="green">{t('warehouse.warehouse')}</Tag> : <Tag color="gold">{t('warehouse.domestic')}</Tag>`),
      "\u88C5\u7BB1\u6570\u4F7F\u7528\u4ED3\u5E93\u56DE\u9000\u503C\u65F6\u5E94\u663E\u793A\u4ED3\u5E93\u6765\u6E90\uFF0C\u5426\u5219\u663E\u793A\u56FD\u5185\u6765\u6E90"
    );
  });
  if (minOrderQuantityColumnFailure) failures.push(minOrderQuantityColumnFailure);
  const batchEditFailure = await runTest("\u4ED3\u5E93\u5546\u54C1\u9875\u5E94\u652F\u6301\u6309\u9009\u4E2D\u5546\u54C1\u6279\u91CF\u4FEE\u6539\u5E38\u7528\u5B57\u6BB5", () => {
    assert(
      pageSource.includes("batchUpdateWarehouseProducts"),
      "\u9875\u9762\u5E94\u5F15\u5165\u4ED3\u5E93\u5546\u54C1\u6279\u91CF\u66F4\u65B0\u670D\u52A1"
    );
    assert(
      pageSource.includes("interface BatchEditFormValues") && pageSource.includes("minOrderQuantity?: number"),
      "\u9875\u9762\u5E94\u58F0\u660E\u6279\u91CF\u7F16\u8F91\u8868\u5355\uFF0C\u5E76\u4F7F\u7528 minOrderQuantity \u8868\u793A\u4E2D\u5305\u6570"
    );
    const saveSection = extractSection(
      pageSource,
      "const handleBatchEditSave = async () => {",
      "const handleToggleSingleActive"
    );
    assert(
      saveSection.includes("MinOrderQuantity: values.minOrderQuantity") && saveSection.includes("PackingQuantity: values.packingQuantity") && saveSection.includes("batchUpdateWarehouseProducts(items)"),
      "\u6279\u91CF\u4FDD\u5B58\u5E94\u628A\u4E2D\u5305\u6570\u63D0\u4EA4\u4E3A MinOrderQuantity\uFF0C\u5E76\u590D\u7528\u4ED3\u5E93\u5546\u54C1\u6279\u91CF\u66F4\u65B0\u670D\u52A1"
    );
    assert(
      saveSection.includes("\u53EA\u4F20\u7528\u6237\u586B\u5199\u7684\u5B57\u6BB5") && saveSection.includes("WarehouseProduct.MinOrderQuantity"),
      "\u6279\u91CF payload \u6784\u9020\u5904\u5E94\u6709\u4E2D\u6587\u6CE8\u91CA\u8BF4\u660E\u4E2D\u5305\u6570\u5B57\u6BB5\u6765\u6E90\u548C\u907F\u514D\u8BEF\u8986\u76D6"
    );
    const toolbarSection = extractSection(
      pageSource,
      "<PageContainer title={t('warehouse.productManagement')}",
      "<Card>"
    );
    assert(
      toolbarSection.includes("t('warehouse.batchEdit', '\u6279\u91CF\u4FEE\u6539')") && toolbarSection.includes("onClick={openBatchEdit}"),
      "\u5DE5\u5177\u680F\u5E94\u63D0\u4F9B\u6279\u91CF\u4FEE\u6539\u6309\u94AE"
    );
    const modalSection = extractSection(
      pageSource,
      "<Modal title={t('warehouse.batchEditTitle'",
      "<ImportFromDomesticModal"
    );
    assert(
      modalSection.includes('name="domesticPrice"') && modalSection.includes('name="oemPrice"') && modalSection.includes('name="importPrice"') && modalSection.includes('name="packingQuantity"') && modalSection.includes('name="minOrderQuantity"') && modalSection.includes('name="unitVolume"') && modalSection.includes('name="isActive"'),
      "\u6279\u91CF\u4FEE\u6539\u5F39\u7A97\u5E94\u5305\u542B\u4EF7\u683C\u3001\u88C5\u7BB1\u6570\u3001\u4E2D\u5305\u6570\u3001\u4F53\u79EF\u548C\u4E0A\u4E0B\u67B6\u5B57\u6BB5"
    );
  });
  if (batchEditFailure) failures.push(batchEditFailure);
  const draggableColumnsFailure = await runTest("\u4ED3\u5E93\u5546\u54C1\u8868\u683C\u5E94\u652F\u6301\u62D6\u62FD\u5217\u5934\u5E76\u6301\u4E45\u5316\u5217\u987A\u5E8F", () => {
    assert(
      pageSource.includes("DndContext") && pageSource.includes("SortableContext") && pageSource.includes("useSortable") && pageSource.includes("horizontalListSortingStrategy"),
      "\u5546\u54C1\u7BA1\u7406\u8868\u5934\u5217\u62D6\u62FD\u5E94\u590D\u7528 @dnd-kit \u6A2A\u5411\u6392\u5E8F\u80FD\u529B"
    );
    assert(
      pageSource.includes("const WAREHOUSE_PRODUCT_COLUMN_ORDER_STORAGE_KEY = 'hbweb_rv.warehouseProducts.columnOrder.v1'") && pageSource.includes("localStorage.setItem(WAREHOUSE_PRODUCT_COLUMN_ORDER_STORAGE_KEY") && pageSource.includes("mergeWarehouseProductColumnOrder("),
      "\u5546\u54C1\u7BA1\u7406\u5217\u987A\u5E8F\u5E94\u4FDD\u5B58\u5230\u72EC\u7ACB localStorage key\uFF0C\u5E76\u517C\u5BB9\u5217\u589E\u5220"
    );
    assert(
      pageSource.includes("components={{ header: { cell: DraggableHeaderCell } }}") && pageSource.includes("<SortableContext items={columnOrder} strategy={horizontalListSortingStrategy}>") && pageSource.includes("<DndContext sensors={columnDragSensors} collisionDetection={closestCenter} onDragEnd={handleColumnDragEnd}>"),
      "\u5546\u54C1\u7BA1\u7406\u8868\u683C\u5E94\u63A5\u5165\u53EF\u62D6\u62FD\u8868\u5934 cell \u4E0E\u6A2A\u5411 SortableContext"
    );
    assert(
      pageSource.includes("const draggableColumnKeys = [...WAREHOUSE_PRODUCT_DEFAULT_COLUMN_ORDER]") && pageSource.includes("rowSelection={{") && !pageSource.includes("columnOrder.includes('selection')"),
      "\u5546\u54C1\u7BA1\u7406\u9009\u62E9\u5217\u4ECD\u5E94\u7531 rowSelection \u7BA1\u7406\uFF0C\u4E0D\u80FD\u8FDB\u5165\u4E1A\u52A1\u5217\u62D6\u62FD\u987A\u5E8F"
    );
  });
  if (draggableColumnsFailure) failures.push(draggableColumnsFailure);
  const defaultColumnOrderFailure = await runTest("\u4ED3\u5E93\u5546\u54C1\u8868\u683C\u9ED8\u8BA4\u5217\u987A\u5E8F\u5E94\u6309\u622A\u56FE\u5E76\u652F\u6301\u91CD\u7F6E\u5217", () => {
    const defaultOrderSection = extractSection(
      pageSource,
      "const WAREHOUSE_PRODUCT_DEFAULT_COLUMN_ORDER",
      "] as const"
    );
    const expectedOrder = [
      "'rowNumber'",
      "'itemNumber'",
      "'productImage'",
      "'domesticSupplierCode'",
      "'categoryName'",
      "'nameEn'",
      "'minOrderQuantity'",
      "'domesticPrice'",
      "'importPrice'",
      "'labelPrice'",
      "'isActive'",
      "'productType'",
      "'barcode'",
      "'locationCodes'",
      "'name'",
      "'packingQty'",
      "'volume'",
      "'localSupplierCode'",
      "'updatedAt'",
      "'updatedBy'",
      "'action'"
    ];
    let lastIndex = -1;
    for (const key of expectedOrder) {
      const nextIndex = defaultOrderSection.indexOf(key);
      assert(nextIndex > lastIndex, `\u9ED8\u8BA4\u5217\u987A\u5E8F\u5E94\u5305\u542B\u5E76\u6309\u622A\u56FE\u6392\u5217 ${key}`);
      lastIndex = nextIndex;
    }
    assert(
      !defaultOrderSection.includes("'selection'"),
      "\u9ED8\u8BA4\u5217\u987A\u5E8F\u4E0D\u80FD\u5305\u542B selection\uFF0C\u9009\u62E9\u5217\u4ECD\u7531 rowSelection \u7BA1\u7406"
    );
    const columnsSection = extractSection(
      pageSource,
      "const baseColumns = useMemo",
      "const draggableColumnKeys"
    );
    assert(
      columnsSection.indexOf("key: 'domesticSupplierCode'") < columnsSection.indexOf("key: 'categoryName'") && columnsSection.indexOf("key: 'categoryName'") < columnsSection.indexOf("key: 'nameEn'") && columnsSection.indexOf("key: 'nameEn'") < columnsSection.indexOf("key: 'minOrderQuantity'") && columnsSection.indexOf("key: 'minOrderQuantity'") < columnsSection.indexOf("key: 'domesticPrice'") && columnsSection.indexOf("key: 'barcode'") < columnsSection.indexOf("key: 'locationCodes'") && columnsSection.indexOf("key: 'locationCodes'") < columnsSection.indexOf("key: 'name'"),
      "baseColumns \u5E94\u6309\u622A\u56FE\u9ED8\u8BA4\u987A\u5E8F\u6392\u5217\uFF0C\u907F\u514D\u9ED8\u8BA4\u987A\u5E8F\u4F9D\u8D56\u5386\u53F2\u4EE3\u7801\u987A\u5E8F"
    );
    assert(
      pageSource.includes("const draggableColumnKeys = [...WAREHOUSE_PRODUCT_DEFAULT_COLUMN_ORDER]") && pageSource.includes("mergeWarehouseProductColumnOrder(current.length ? current : savedOrder, WAREHOUSE_PRODUCT_DEFAULT_COLUMN_ORDER)"),
      "\u5217\u987A\u5E8F\u521D\u59CB\u5316\u5E94\u4EE5\u663E\u5F0F\u9ED8\u8BA4\u987A\u5E8F\u4E3A\u51C6\uFF0C\u5E76\u517C\u5BB9 localStorage \u65E7\u7F13\u5B58"
    );
    const resetSection = extractSection(
      pageSource,
      "const handleResetColumnOrder = () => {",
      "const orderedColumns = useMemo"
    );
    assert(
      resetSection.includes("localStorage.removeItem(WAREHOUSE_PRODUCT_COLUMN_ORDER_STORAGE_KEY)") && resetSection.includes("setColumnOrder([...WAREHOUSE_PRODUCT_DEFAULT_COLUMN_ORDER])") && resetSection.includes("\u9009\u62E9\u5217\u4ECD\u7531 Ant Design rowSelection \u7BA1\u7406"),
      "\u91CD\u7F6E\u5217\u903B\u8F91\u5E94\u6E05\u9664\u5217\u987A\u5E8F\u7F13\u5B58\uFF0C\u6062\u590D\u9ED8\u8BA4\u4E1A\u52A1\u5217\u987A\u5E8F\uFF0C\u5E76\u4FDD\u7559\u4E2D\u6587\u6CE8\u91CA\u8BF4\u660E\u9009\u62E9\u5217\u8FB9\u754C"
    );
    assert(
      pageSource.includes("t('warehouse.resetColumns', '\u91CD\u7F6E\u5217')") && pageSource.includes("onClick={handleResetColumnOrder}") && pageSource.includes("disabled={!isColumnOrderCustomized}"),
      "\u7B5B\u9009\u5DE5\u5177\u680F\u5E94\u63D0\u4F9B\u91CD\u7F6E\u5217\u6309\u94AE\uFF0C\u5E76\u4EC5\u5728\u5217\u987A\u5E8F\u81EA\u5B9A\u4E49\u540E\u542F\u7528"
    );
    assert(
      pageSource.includes("rowSelection={{") && !pageSource.includes("WAREHOUSE_PRODUCT_DEFAULT_COLUMN_ORDER = ['selection'"),
      "\u91CD\u7F6E\u5217\u529F\u80FD\u4E0D\u80FD\u6539\u53D8 rowSelection \u7BA1\u7406\u9009\u62E9\u5217\u7684\u65B9\u5F0F"
    );
  });
  if (defaultColumnOrderFailure) failures.push(defaultColumnOrderFailure);
  const supplierColumnDisplayFailure = await runTest("\u4ED3\u5E93\u5546\u54C1\u4F9B\u5E94\u5546\u5217\u5E94\u533A\u5206\u56FD\u5185\u4F9B\u5E94\u5546\u548C\u6FB3\u6D32\u4F9B\u5E94\u5546\u540D\u79F0\u663E\u793A", () => {
    const columnsSection = extractSection(
      pageSource,
      "const baseColumns = useMemo",
      "const draggableColumnKeys"
    );
    const domesticSupplierSection = extractSection(
      columnsSection,
      "key: 'domesticSupplierCode'",
      "key: 'categoryName'"
    );
    const australianSupplierSection = extractSection(
      columnsSection,
      "key: 'localSupplierCode'",
      "key: 'updatedAt'"
    );
    assert(
      domesticSupplierSection.includes("title: t('warehouse.domesticSupplier', '\u56FD\u5185\u4F9B\u5E94\u5546')") && domesticSupplierSection.includes("dataIndex: 'domesticSupplierCode'") && domesticSupplierSection.includes("sorter: true"),
      "\u56FD\u5185\u4F9B\u5E94\u5546\u5217\u5E94\u7ED1\u5B9A domesticSupplierCode\uFF0C\u4E0D\u80FD\u663E\u793A\u6FB3\u6D32\u4F9B\u5E94\u5546\u5B57\u6BB5"
    );
    assert(
      australianSupplierSection.includes("title: t('column.australianSupplier', '\u6FB3\u6D32\u4F9B\u5E94\u5546')") && australianSupplierSection.includes("dataIndex: 'localSupplierCode'") && australianSupplierSection.includes("sorter: true"),
      "\u6FB3\u6D32\u4F9B\u5E94\u5546\u5217\u5E94\u7ED1\u5B9A localSupplierCode\uFF0C\u4E0D\u80FD\u663E\u793A\u56FD\u5185\u4F9B\u5E94\u5546\u5B57\u6BB5"
    );
    assert(
      domesticSupplierSection.includes("record.domesticSupplierName || record.domesticSupplierCode") && australianSupplierSection.includes("record.localSupplierName || localSupplierNameMap[record.localSupplierCode || ''] || record.localSupplierCode"),
      "\u56FD\u5185\u4F9B\u5E94\u5546\u5217\u5E94\u4F18\u5148\u663E\u793A\u540D\u79F0\uFF1B\u6FB3\u6D32\u4F9B\u5E94\u5546\u5217\u5E94\u4F18\u5148\u663E\u793A\u540D\u79F0\uFF0C\u5E76\u5728\u884C\u6570\u636E\u7F3A\u540D\u79F0\u65F6\u7528\u6D3B\u8DC3\u4F9B\u5E94\u5546\u6620\u5C04\u515C\u5E95"
    );
  });
  if (supplierColumnDisplayFailure) failures.push(supplierColumnDisplayFailure);
  const localSupplierFallbackFailure = await runTest("\u4ED3\u5E93\u5546\u54C1\u6FB3\u6D32\u4F9B\u5E94\u5546\u5217\u5E94\u4F7F\u7528\u6D3B\u8DC3\u4F9B\u5E94\u5546\u540D\u79F0\u515C\u5E95", () => {
    assert(
      pageSource.includes("import { getActiveLocalSuppliers as getActiveAustralianSuppliers } from '../../../services/localSupplierService'"),
      "\u9875\u9762\u5E94\u4ECE\u6FB3\u6D32\u4F9B\u5E94\u5546\u670D\u52A1\u5BFC\u5165\u6D3B\u8DC3\u4F9B\u5E94\u5546\u5217\u8868\uFF0C\u5E76\u4F7F\u7528\u522B\u540D\u907F\u514D\u548C\u56FD\u5185\u4F9B\u5E94\u5546\u6DF7\u6DC6"
    );
    assert(
      pageSource.includes("const [localSupplierNameMap, setLocalSupplierNameMap] = useState<Record<string, string>>({})"),
      "\u9875\u9762\u5E94\u7EF4\u62A4\u6FB3\u6D32\u4F9B\u5E94\u5546\u4EE3\u7801\u5230\u540D\u79F0\u7684\u515C\u5E95\u6620\u5C04"
    );
    assert(
      pageSource.includes("getActiveAustralianSuppliers()") && pageSource.includes("setLocalSupplierNameMap(") && pageSource.includes("map[item.localSupplierCode] = item.name"),
      "\u9875\u9762\u52A0\u8F7D\u65F6\u5E94\u8BFB\u53D6\u6D3B\u8DC3\u6FB3\u6D32\u4F9B\u5E94\u5546\u5E76\u5EFA\u7ACB\u4EE3\u7801\u5230\u540D\u79F0\u6620\u5C04"
    );
    const columnsSection = extractSection(
      pageSource,
      "const baseColumns = useMemo",
      "const draggableColumnKeys"
    );
    const australianSupplierSection = extractSection(
      columnsSection,
      "key: 'localSupplierCode'",
      "key: 'updatedAt'"
    );
    assert(
      australianSupplierSection.includes("record.localSupplierName || localSupplierNameMap[record.localSupplierCode || ''] || record.localSupplierCode"),
      "\u6FB3\u6D32\u4F9B\u5E94\u5546\u5217\u5E94\u6309\u884C\u5185\u540D\u79F0\u3001\u6D3B\u8DC3\u4F9B\u5E94\u5546\u540D\u79F0\u6620\u5C04\u3001\u4F9B\u5E94\u5546\u4EE3\u7801\u7684\u987A\u5E8F\u663E\u793A"
    );
    assert(
      pageSource.includes("\u8868\u683C\u63A5\u53E3\u53EA\u8FD4\u56DE\u6FB3\u6D32\u4F9B\u5E94\u5546\u4EE3\u7801\u65F6\uFF0C\u7528\u6D3B\u8DC3\u4F9B\u5E94\u5546\u5217\u8868\u8865\u9F50\u540D\u79F0"),
      "\u515C\u5E95\u903B\u8F91\u5E94\u6709\u4E2D\u6587\u6CE8\u91CA\u8BF4\u660E\u539F\u56E0"
    );
  });
  if (localSupplierFallbackFailure) failures.push(localSupplierFallbackFailure);
  const categoryColumnFailure = await runTest("\u4ED3\u5E93\u5546\u54C1\u8868\u683C\u5E94\u663E\u793A\u5206\u7C7B\u540D\u79F0\u5E76\u60AC\u6D6E\u5C55\u793A\u5B8C\u6574\u8DEF\u5F84", () => {
    const columnsSection = extractSection(
      pageSource,
      "const baseColumns = useMemo",
      "const draggableColumnKeys"
    );
    const categoryColumn = extractSection(
      columnsSection,
      "key: 'categoryName'",
      "key: 'minOrderQuantity'"
    );
    assert(categoryColumn.includes("title: t('column.category')"), "\u5206\u7C7B\u5217\u5E94\u4F7F\u7528 column.category \u6587\u6848");
    assert(categoryColumn.includes("dataIndex: 'categoryName'"), "\u5206\u7C7B\u5217\u5E94\u7ED1\u5B9A categoryName");
    assert(categoryColumn.includes("renderWarehouseProductCategoryCell(record, categoryLookup, i18n.language)"), "\u5206\u7C7B\u5217\u5E94\u4F7F\u7528\u5206\u7C7B\u5C55\u793A helper");
    assert(pageSource.includes("function renderWarehouseProductCategoryCell"), "\u9875\u9762\u5E94\u63D0\u4F9B\u5206\u7C7B\u5355\u5143\u683C helper");
    assert(pageSource.includes("getWarehouseProductCategoryTooltip(record, categoryLookup, language)"), "\u5206\u7C7B Tooltip \u5E94\u4F18\u5148\u8BFB\u53D6\u5B8C\u6574\u8DEF\u5F84 helper");
    assert(pageSource.includes("const categoryLookup = useMemo(() => buildWarehouseCategoryLookup(categories), [categories])"), "\u9875\u9762\u5E94\u57FA\u4E8E\u5206\u7C7B\u6811\u5EFA\u7ACB GUID \u548C\u5206\u7C7B\u540D\u5230\u5B8C\u6574\u8DEF\u5F84\u7684\u6620\u5C04");
    assert(pageSource.includes("buildWarehouseCategoryLookup") && pageSource.includes("WarehouseCategoryLookup"), "\u9875\u9762\u5E94\u590D\u7528\u53EF\u6D4B\u8BD5\u7684\u5206\u7C7B\u8DEF\u5F84 lookup helper");
    assert(categoryTreePickerSource.includes("formatWarehouseCategoryNodeName(node, language)"), "\u5206\u7C7B\u6811\u8282\u70B9\u5E94\u6309\u5F53\u524D\u8BED\u8A00\u663E\u793A\u540D\u79F0");
    assert(pageSource.includes("buildFilterCategoryOptions(categories, t, i18n.language)"), "\u5206\u7C7B\u7B5B\u9009\u4E0B\u62C9\u5E94\u4F7F\u7528\u5F53\u524D\u8BED\u8A00\u663E\u793A\u5206\u7C7B\u540D");
    assert(pageSource.includes("import CategoryTreePicker from './CategoryTreePicker'"), "\u6279\u91CF\u5206\u7C7B\u5F39\u7A97\u5E94\u590D\u7528\u5E26\u67E5\u8BE2\u7684\u5206\u7C7B\u6811\u7EC4\u4EF6");
    assert(pageSource.includes("setCategoryExpandedKeys(collectCategoryExpandedKeys(categories, 1));"), "\u6279\u91CF\u5206\u7C7B\u5F39\u7A97\u6BCF\u6B21\u6253\u5F00\u5E94\u9ED8\u8BA4\u5C55\u5F00\u5230\u4E00\u7EA7\u5206\u7C7B");
    assert(pageSource.includes("<CategoryTreePicker categories={categories}") && pageSource.includes("maxHeight={420}"), "\u6279\u91CF\u5206\u7C7B\u6811\u5E94\u4F7F\u7528\u5F53\u524D\u8BED\u8A00\u67E5\u8BE2\u7EC4\u4EF6\u6784\u5EFA");
    assert(pageSource.includes("selectedTargetCategoryPath || formatWarehouseCategoryNodeName(selectedTargetCategory, i18n.language)"), "\u6279\u91CF\u5206\u7C7B\u76EE\u6807\u63D0\u793A\u5E94\u663E\u793A\u5F53\u524D\u8BED\u8A00\u5B8C\u6574\u8DEF\u5F84");
    assert(pageSource.includes("<Tooltip title={tooltipTitle}"), "\u5206\u7C7B\u540D\u79F0\u5E94\u901A\u8FC7 Tooltip \u5C55\u793A\u5B8C\u6574\u8DEF\u5F84");
    assert(pageSource.includes('className="warehouse-products-category-cell"'), "\u5206\u7C7B\u540D\u79F0\u5E94\u6302\u8F7D\u7D27\u51D1\u6837\u5F0F class");
    assert(pageSource.includes("record.categoryName ||") && pageSource.includes("'--'"), "\u5206\u7C7B\u5217\u7F3A\u5931\u540D\u79F0\u65F6\u5E94\u663E\u793A --");
    const batchCategorySaveSection = extractSection(
      pageSource,
      "const handleBatchCategorySave = async () => {",
      "const handleBatchEditSave = async () => {"
    );
    assert(
      batchCategorySaveSection.includes("await batchAssignProducts(targetCategoryGuid, selectedProductCodes)") && batchCategorySaveSection.includes("setData((items) =>") && batchCategorySaveSection.includes("selectedProductCodeSet.has(item.productCode)") && batchCategorySaveSection.includes("warehouseCategoryGUID: targetCategoryGuid") && batchCategorySaveSection.includes("categoryName: selectedTargetCategory") && batchCategorySaveSection.includes("formatWarehouseCategoryNodeName(selectedTargetCategory, i18n.language)") && !batchCategorySaveSection.includes("void loadData({ page })"),
      "\u4ED3\u5E93\u5546\u54C1\u6279\u91CF\u5206\u7C7B\u4FDD\u5B58\u6210\u529F\u540E\u5E94\u672C\u5730\u66F4\u65B0\u5F53\u524D\u9875\u5206\u7C7B\uFF0C\u4E0D\u5E94\u91CD\u65B0\u67E5\u8BE2\u5546\u54C1\u8868\u683C"
    );
    assert(
      categoryTreePickerSource.includes("function filterCategoryTree") && categoryTreePickerSource.includes("buildSearchText(node, language, parentPath).includes(keyword)") && categoryTreePickerSource.includes("children: childResult.nodes") && categoryTreePickerSource.includes("const visibleExpandedKeys = keyword ? searchResult.expandedKeys : expandedKeys") && categoryTreePickerSource.includes("placeholder={t('warehouse.categories.searchPlaceholder'"),
      "\u5171\u4EAB\u5206\u7C7B\u6811\u7EC4\u4EF6\u5E94\u652F\u6301\u67E5\u8BE2\u5206\u7C7B\u540D\u548C\u7236\u7EA7\u8DEF\u5F84\uFF0C\u5E76\u5728\u641C\u7D22\u65F6\u81EA\u52A8\u5C55\u5F00\u547D\u4E2D\u8DEF\u5F84"
    );
  });
  if (categoryColumnFailure) failures.push(categoryColumnFailure);
  const compactTableFailure = await runTest("\u4ED3\u5E93\u5546\u54C1\u4E3B\u8868\u5E94\u4F7F\u7528\u7D27\u51D1\u884C\u9AD8\u3001\u5A92\u4F53\u5C3A\u5BF8\u548C\u5217\u5BBD", () => {
    assert(
      pageSource.includes("const WAREHOUSE_TABLE_ROW_MAX_HEIGHT = 60"),
      "\u5546\u54C1\u7BA1\u7406\u4E3B\u8868\u884C\u9AD8\u5E94\u538B\u7F29\u5230\u7D27\u51D1\u503C 60px"
    );
    assert(
      pageSource.includes(".warehouse-products-table .ant-table-thead > tr > th,") && pageSource.includes("padding: 4px 6px !important") && pageSource.includes(".warehouse-products-table .ant-table-column-title") && pageSource.includes("-webkit-line-clamp: 2") && pageSource.includes(".warehouse-products-table .ant-table-filter-column") && pageSource.includes(".warehouse-products-table .ant-table-filter-trigger"),
      "\u5546\u54C1\u7BA1\u7406\u4E3B\u8868\u5E94\u4F7F\u7528\u7D27\u51D1\u5355\u5143\u683C padding\uFF0C\u4E14\u8868\u5934\u6807\u9898\u3001\u6392\u5E8F\u548C\u7B5B\u9009\u56FE\u6807\u5E94\u7A33\u5B9A\u6392\u5217"
    );
    assert(
      pageSource.includes("min-height: 48px") && pageSource.includes("max-height: 48px") && pageSource.includes("width: 36px") && pageSource.includes("height: 36px") && pageSource.includes("max-height: 42px !important"),
      "\u5546\u54C1\u56FE\u7247\u548C\u6761\u7801\u9884\u89C8\u5E94\u4F7F\u7528\u7D27\u51D1\u5C3A\u5BF8\uFF0C\u51CF\u5C11\u884C\u5185\u5360\u7528\u7A7A\u95F4"
    );
    const columnsSection = extractSection(
      pageSource,
      "const baseColumns = useMemo",
      "return (<>"
    );
    assert(
      columnsSection.includes("key: 'productImage'") && columnsSection.includes("width: 64") && columnsSection.includes('<Image src={value} alt="" width={36} height={36}') && columnsSection.includes("key: 'itemNumber'") && columnsSection.includes("width: 122") && columnsSection.includes("key: 'isActive'") && columnsSection.includes("width: 104") && columnsSection.includes("key: 'productType'") && columnsSection.includes("key: 'domesticPrice'") && columnsSection.includes("width: 96") && columnsSection.includes("key: 'packingQty'") && columnsSection.includes("width: 108") && columnsSection.includes("key: 'minOrderQuantity'") && columnsSection.includes("dataIndex: 'minOrderQuantity'") && columnsSection.includes("width: 96") && columnsSection.includes("key: 'updatedAt'") && columnsSection.includes("width: 164"),
      "\u56FE\u7247\u3001\u72B6\u6001\u3001\u5546\u54C1\u7C7B\u578B\u3001\u4EF7\u683C\u3001\u88C5\u7BB1\u6570\u3001\u4E2D\u5305\u6570\u548C\u66F4\u65B0\u65F6\u95F4\u7B49\u5173\u952E\u5217\u5E94\u4F7F\u7528\u7B5B\u9009\u53CB\u597D\u5217\u5BBD\uFF0C\u4E14\u4E2D\u5305\u6570\u4ECD\u7ED1\u5B9A minOrderQuantity"
    );
    assert(
      columnsSection.includes("BarcodePreview value={value} textMaxWidth={150} compactCopy") && pageSource.includes("scroll={{ x: 2390, y: 620 }}"),
      "\u6761\u7801\u5217\u548C\u8868\u683C\u6A2A\u5411\u6EDA\u52A8\u5BBD\u5EA6\u5E94\u6309\u7D27\u51D1\u5E03\u5C40\u66F4\u65B0"
    );
    assert(
      pageSource.includes("components={{ header: { cell: DraggableHeaderCell } }}") && pageSource.includes("rowSelection={{") && pageSource.includes("const orderedColumns = useMemo"),
      "\u7D27\u51D1\u663E\u793A\u4E0D\u80FD\u79FB\u9664\u62D6\u62FD\u5217\u5934\u3001rowSelection \u6216 orderedColumns"
    );
  });
  if (compactTableFailure) failures.push(compactTableFailure);
  const mainTablePaginationFailure = await runTest("\u4ED3\u5E93\u5546\u54C1\u4E3B\u8868\u9ED8\u8BA4\u6BCF\u9875 100 \u4E14\u4EC5\u63D0\u4F9B\u5927\u5206\u9875\u9009\u9879", () => {
    const tableSection = extractSection(
      pageSource,
      "pagination={{",
      "}} onChange={(pagination: TablePaginationConfig"
    );
    assert(
      pageSource.includes("const WAREHOUSE_PRODUCTS_DEFAULT_PAGE_SIZE = 100") && pageSource.includes("const WAREHOUSE_PRODUCTS_PAGE_SIZE_OPTIONS = ['50', '100', '200', '500', '1000']"),
      "\u4ED3\u5E93\u5546\u54C1\u4E3B\u8868\u5E94\u96C6\u4E2D\u58F0\u660E\u9ED8\u8BA4\u5206\u9875 100 \u548C 50/100/200/500/1000 \u5206\u9875\u9009\u9879"
    );
    assert(
      pageSource.includes("const [pageSize, setPageSize] = useState(WAREHOUSE_PRODUCTS_DEFAULT_PAGE_SIZE);"),
      "\u4ED3\u5E93\u5546\u54C1\u4E3B\u8868 pageSize \u521D\u59CB\u503C\u5E94\u4F7F\u7528\u9ED8\u8BA4\u5206\u9875\u5E38\u91CF 100"
    );
    assert(
      tableSection.includes("pageSizeOptions: WAREHOUSE_PRODUCTS_PAGE_SIZE_OPTIONS") && tableSection.includes("showSizeChanger: true,"),
      "\u4ED3\u5E93\u5546\u54C1\u4E3B\u8868\u5206\u9875\u4E0B\u62C9\u5E94\u53EA\u4F7F\u7528 50/100/200/500/1000 \u8FD9\u4E9B\u9009\u9879\uFF0C\u5E76\u4FDD\u7559\u5207\u6362\u5165\u53E3"
    );
    assert(
      pageSource.includes("virtual") && pageSource.includes("scroll={{ x: 2390, y: 620 }}") && pageSource.includes("const result = await getWarehouseProductsTable(query);"),
      "\u5206\u9875\u8C03\u6574\u5E94\u4FDD\u7559\u73B0\u6709\u865A\u62DF\u8868\u683C\u3001\u56FA\u5B9A\u6EDA\u52A8\u9AD8\u5EA6\u548C\u5F02\u6B65\u670D\u52A1\u7AEF\u5206\u9875\u8BF7\u6C42"
    );
  });
  if (mainTablePaginationFailure) failures.push(mainTablePaginationFailure);
  const adminOnlyButtonFailure = await runTest("\u9875\u9762\u5E94\u4EC5\u5BF9 Admin \u6E32\u67D3\u4ECE HQ \u540C\u6B65\u6309\u94AE", () => {
    assert(
      pageSource.includes("CloudSyncOutlined"),
      "\u9875\u9762\u5E94\u5F15\u5165 CloudSyncOutlined \u56FE\u6807"
    );
    assert(
      pageSource.includes("access.isAdmin") && pageSource.includes("t('warehouse.hqSync', '\u4ECEHQ\u540C\u6B65\u5E93\u5B58')"),
      "\u9875\u9762\u5E94\u57FA\u4E8E access.isAdmin \u63A7\u5236\u201C\u4ECEHQ\u540C\u6B65\u5E93\u5B58\u201D\u6309\u94AE\u53EF\u89C1\u6027"
    );
  });
  if (adminOnlyButtonFailure) failures.push(adminOnlyButtonFailure);
  const modalConfirmFailure = await runTest("\u70B9\u51FB\u540C\u6B65\u6309\u94AE\u524D\u5E94\u5F39\u51FA\u660E\u786E\u63D0\u793A\u6309\u5546\u54C1\u7F16\u7801\u65B0\u589E\u66F4\u65B0\u7684\u786E\u8BA4\u6846", () => {
    const syncSection = extractSection(
      pageSource,
      "const handleSyncWarehouseProductsFromHq = () => {",
      "const baseColumns = useMemo"
    );
    assert(
      syncSection.includes("Modal.confirm({") && syncSection.includes("t('warehouse.hqSyncTitle', '\u4ECEHQ\u540C\u6B65\u5E93\u5B58')") && syncSection.includes("\u6309\u5546\u54C1\u7F16\u7801\u5339\u914D") && syncSection.includes("\u4E0D\u4F1A\u5220\u9664\u672C\u5730\u7F3A\u5931\u5546\u54C1"),
      "\u540C\u6B65\u524D\u5E94\u5F39\u51FA\u660E\u786E\u63D0\u793A\u201C\u6309\u5546\u54C1\u7F16\u7801\u5339\u914D\u65B0\u589E/\u66F4\u65B0\u4E14\u4E0D\u5220\u9664\u672C\u5730\u7F3A\u5931\u5546\u54C1\u201D\u7684\u786E\u8BA4\u6846"
    );
  });
  if (modalConfirmFailure) failures.push(modalConfirmFailure);
  const loadingFailure = await runTest("\u540C\u6B65\u6309\u94AE\u5E94\u5728\u540E\u53F0\u4EFB\u52A1\u63D0\u4EA4\u4E2D\u6216\u8FD0\u884C\u4E2D\u5C55\u793A loading\uFF0C\u63D0\u4EA4\u8BF7\u6C42\u4E2D disabled", () => {
    assert(
      pageSource.includes("loading={syncingFromHq || Boolean(activeHqSyncJob)}") && pageSource.includes("disabled={syncingFromHq}"),
      "\u540C\u6B65\u6309\u94AE\u5E94\u7ED1\u5B9A\u63D0\u4EA4\u4E2D\u548C\u540E\u53F0\u8FD0\u884C\u4E2D\u72B6\u6001\uFF0C\u5E76\u5141\u8BB8\u8FD0\u884C\u4E2D\u70B9\u51FB\u67E5\u770B\u72B6\u6001"
    );
  });
  if (loadingFailure) failures.push(loadingFailure);
  const jobApiFailure = await runTest("\u9875\u9762\u5E94\u63D0\u4EA4\u540E\u53F0 job \u5E76\u8F6E\u8BE2\u67E5\u8BE2 job \u72B6\u6001", () => {
    const syncSection = extractSection(
      pageSource,
      "const handleSyncWarehouseProductsFromHq = () => {",
      "const baseColumns = useMemo"
    );
    assert(
      pageSource.includes("createWarehouseProductHqSyncJob") && pageSource.includes("getWarehouseProductHqSyncJob") && pageSource.includes("createWarehouseProductHqSyncJobPoller"),
      "\u9875\u9762\u5E94\u4F7F\u7528\u540E\u53F0 job \u521B\u5EFA\u63A5\u53E3\u3001\u67E5\u8BE2\u63A5\u53E3\u548C\u8F6E\u8BE2\u5668"
    );
    assert(
      syncSection.includes("createWarehouseProductHqSyncJob") && !syncSection.includes("syncWarehouseProductsFromHq()"),
      "\u6309\u94AE\u786E\u8BA4\u540E\u4E0D\u5E94\u518D\u76F4\u63A5\u7B49\u5F85\u65E7\u540C\u6B65\u63A5\u53E3\u5B8C\u6210"
    );
  });
  if (jobApiFailure) failures.push(jobApiFailure);
  const notificationFailure = await runTest("\u540C\u6B65\u63D0\u4EA4\u548C\u5B8C\u6210\u7ED3\u679C\u5E94\u901A\u8FC7\u53F3\u4E0A\u89D2 notification \u8FD4\u56DE", () => {
    const syncSection = extractSection(
      pageSource,
      "const handleSyncWarehouseProductsFromHq = () => {",
      "const baseColumns = useMemo"
    );
    assert(
      pageSource.includes("notification") && pageSource.includes("notification.info") && pageSource.includes("notification.success") && pageSource.includes("notification.error") && pageSource.includes("notification.warning"),
      "\u9875\u9762\u5E94\u4F7F\u7528 notification \u5C55\u793A\u63D0\u4EA4\u3001\u6210\u529F\u3001\u5931\u8D25\u548C\u8D85\u65F6\u4FE1\u606F"
    );
    assert(
      syncSection.includes("t('warehouse.hqSyncJobSubmitted") && syncSection.includes("startHqSyncJobPolling"),
      "\u63D0\u4EA4\u6210\u529F\u540E\u5E94\u63D0\u793A\u540E\u53F0\u6267\u884C\u5E76\u542F\u52A8\u8F6E\u8BE2"
    );
  });
  if (notificationFailure) failures.push(notificationFailure);
  const successRefreshFailure = await runTest("\u540E\u53F0\u540C\u6B65\u6210\u529F\u540E\u53F3\u4E0A\u89D2\u63D0\u793A\u7ED3\u679C\u5E76\u5237\u65B0\u7B2C\u4E00\u9875", () => {
    const descriptionSection = extractSection(
      pageSource,
      "const buildHqSyncResultDescription",
      "const showHqSyncJobResult"
    );
    const resultSection = extractSection(
      pageSource,
      "const showHqSyncJobResult",
      "const startHqSyncJobPolling"
    );
    const refreshSection = extractSection(
      pageSource,
      "const refreshCurrentList",
      "const stopHqSyncJobPolling"
    );
    assert(
      resultSection.includes("notification.success") && descriptionSection.includes("addedCount") && descriptionSection.includes("updatedCount") && descriptionSection.includes("errorCount") && resultSection.includes("void refreshCurrentList({ page: 1 })") && refreshSection.includes("if (!isMountedRef.current) {") && refreshSection.includes("loadDataRef.current?.(overrides)"),
      "\u540E\u53F0\u540C\u6B65\u6210\u529F\u5E94\u5C55\u793A\u7ED3\u679C\uFF0C\u5E76\u5728 mounted gate \u540E\u901A\u8FC7 current loader \u5237\u65B0\u7B2C\u4E00\u9875"
    );
  });
  if (successRefreshFailure) failures.push(successRefreshFailure);
  const failureNoRefreshFailure = await runTest("\u540E\u53F0\u540C\u6B65\u5931\u8D25\u65F6\u53EA\u63D0\u793A\u5931\u8D25\u4E14\u4E0D\u5237\u65B0\u7B2C\u4E00\u9875", () => {
    const resultSection = extractSection(
      pageSource,
      "const showHqSyncJobResult",
      "const startHqSyncJobPolling"
    );
    assert(
      resultSection.includes("notification.error"),
      "\u540E\u53F0\u540C\u6B65\u5931\u8D25\u65F6\u5E94\u4F7F\u7528 notification.error"
    );
    assert(
      !extractSection(resultSection, "if (!success) {", "const errorCount").includes("refreshCurrentList("),
      "\u540E\u53F0\u540C\u6B65\u5931\u8D25\u5206\u652F\u4E0D\u5E94\u5237\u65B0\u7B2C\u4E00\u9875"
    );
  });
  if (failureNoRefreshFailure) failures.push(failureNoRefreshFailure);
  const serviceUrlFailure = await runTest("\u540C\u6B65\u670D\u52A1\u5E94\u4F7F\u7528\u6B63\u786E\u7684 URL\u3001POST \u65B9\u6CD5\uFF0C\u5E76\u5728\u540E\u7AEF\u8FD4\u56DE\u5931\u8D25\u65F6\u629B\u51FA message", async () => {
    const originalFetch = globalThis.fetch;
    let capturedUrl = "";
    let capturedInit;
    try {
      globalThis.fetch = async (input, init) => {
        capturedUrl = String(input);
        capturedInit = init;
        return new Response(JSON.stringify({
          success: true,
          data: {
            isSuccess: true,
            message: "\u540C\u6B65\u5B8C\u6210"
          }
        }), {
          status: 200,
          headers: { "Content-Type": "application/json" }
        });
      };
      await syncWarehouseProductsFromHq();
      assertEqual(capturedUrl, "/api/react/v1/product-warehouse/sync-from-hq", "\u540C\u6B65\u670D\u52A1\u5E94\u547D\u4E2D\u65E2\u5B9A\u63A5\u53E3\u5730\u5740");
      assertEqual(capturedInit?.method, "POST", "\u540C\u6B65\u670D\u52A1\u5E94\u4F7F\u7528 POST \u65B9\u6CD5");
      globalThis.fetch = async () => new Response(JSON.stringify({
        success: false,
        message: "\u540E\u7AEF\u8FD4\u56DE\u540C\u6B65\u5931\u8D25"
      }), {
        status: 200,
        headers: { "Content-Type": "application/json" }
      });
      await assertRejects(
        () => syncWarehouseProductsFromHq(),
        "\u540E\u7AEF\u8FD4\u56DE\u540C\u6B65\u5931\u8D25",
        "\u540E\u7AEF success=false \u65F6\u5E94\u629B\u51FA\u540E\u7AEF message"
      );
      globalThis.fetch = async () => new Response(JSON.stringify({
        success: true,
        message: "\u5916\u5C42\u6210\u529F\u4F46\u540C\u6B65\u5931\u8D25",
        data: {
          isSuccess: false,
          message: "\u5185\u5C42\u540C\u6B65\u4E8B\u52A1\u5931\u8D25"
        }
      }), {
        status: 200,
        headers: { "Content-Type": "application/json" }
      });
      await assertRejects(
        () => syncWarehouseProductsFromHq(),
        "\u5185\u5C42\u540C\u6B65\u4E8B\u52A1\u5931\u8D25",
        "\u5916\u5C42 success=true \u4F46 data.isSuccess=false \u65F6\u5E94\u629B\u51FA\u5185\u5C42 message"
      );
    } finally {
      globalThis.fetch = originalFetch;
    }
  });
  if (serviceUrlFailure) failures.push(serviceUrlFailure);
  const jobServiceFailure = await runTest("\u540E\u53F0 job \u670D\u52A1\u5E94\u4F7F\u7528\u521B\u5EFA\u548C\u67E5\u8BE2 URL", async () => {
    const originalFetch = globalThis.fetch;
    const capturedUrls = [];
    const capturedMethods = [];
    try {
      globalThis.fetch = async (input, init) => {
        capturedUrls.push(String(input));
        capturedMethods.push(init?.method);
        return new Response(JSON.stringify({
          success: true,
          data: {
            jobId: "warehouse-job-1",
            status: "Running",
            createdAt: "2026-06-04T00:00:00Z"
          }
        }), {
          status: 200,
          headers: { "Content-Type": "application/json" }
        });
      };
      await createWarehouseProductHqSyncJob({ operationId: "warehouse-products-hq-sync" });
      await getWarehouseProductHqSyncJob("warehouse-job-1");
      assertEqual(
        capturedUrls[0],
        "/api/react/v1/product-warehouse/sync-from-hq/jobs",
        "\u521B\u5EFA\u540E\u53F0 job \u5E94\u547D\u4E2D\u65B0\u63A5\u53E3\u5730\u5740"
      );
      assertEqual(capturedMethods[0], "POST", "\u521B\u5EFA\u540E\u53F0 job \u5E94\u4F7F\u7528 POST \u65B9\u6CD5");
      assertEqual(
        capturedUrls[1],
        "/api/react/v1/product-warehouse/sync-from-hq/jobs/warehouse-job-1",
        "\u67E5\u8BE2\u540E\u53F0 job \u5E94\u547D\u4E2D job \u67E5\u8BE2\u63A5\u53E3\u5730\u5740"
      );
      assertEqual(capturedMethods[1], "GET", "\u67E5\u8BE2\u540E\u53F0 job \u5E94\u4F7F\u7528 GET \u65B9\u6CD5");
    } finally {
      globalThis.fetch = originalFetch;
    }
  });
  if (jobServiceFailure) failures.push(jobServiceFailure);
  const columnFilterHelperFailure = await runTest("\u4ED3\u5E93\u5546\u54C1\u5217\u5934\u7B5B\u9009 helper \u5E94\u4FDD\u6301\u8FD0\u884C\u65F6\u8BED\u4E49", () => {
    assertDeepEqual(
      setFilterValues({ domesticSupplierCode: ["CN-001"] }, "domesticSupplierCode", ["  ", void 0]),
      {},
      "\u7A7A\u503C\u5E94\u79FB\u9664\u5BF9\u5E94\u5217\u5934\u7B5B\u9009"
    );
    assertDeepEqual(
      buildRangeFilterTokens(" 5 ", 10),
      ["gte:5", "lte:10"],
      "\u6570\u5B57\u8303\u56F4\u5E94\u751F\u6210\u540E\u7AEF\u8BC6\u522B\u7684 gte/lte token"
    );
    assertDeepEqual(buildTextFilterTokens("contains", "Clock"), ["__filter:contains:Clock"], "\u6587\u672C\u5305\u542B\u5E94\u751F\u6210\u547D\u540D\u7A7A\u95F4 contains token");
    assertDeepEqual(buildTextFilterTokens("eq", "HB001"), ["__filter:eq:HB001"], "\u6587\u672C\u7B49\u4E8E\u5E94\u751F\u6210\u547D\u540D\u7A7A\u95F4 eq token");
    assertDeepEqual(buildTextFilterTokens("starts", "HB"), ["__filter:starts:HB"], "\u6587\u672C\u5F00\u5934\u662F\u5E94\u751F\u6210\u547D\u540D\u7A7A\u95F4 starts token");
    assertDeepEqual(buildTextFilterTokens("ends", "001"), ["__filter:ends:001"], "\u6587\u672C\u7ED3\u5C3E\u662F\u5E94\u751F\u6210\u547D\u540D\u7A7A\u95F4 ends token");
    assertDeepEqual(parseTextFilterTokens(["Clock"]), { mode: "contains", value: "Clock" }, "\u65E7\u6587\u672C\u88F8\u503C\u5E94\u517C\u5BB9\u4E3A contains");
    assertDeepEqual(parseTextFilterTokens(["starts:HB"]), { mode: "contains", value: "starts:HB" }, "\u65E7\u6587\u672C\u4FDD\u7559\u524D\u7F00\u5B57\u9762\u503C");
    assertDeepEqual(parseTextFilterTokens(["__filter:starts:HB"]), { mode: "starts", value: "HB" }, "\u6587\u672C token \u5E94\u80FD\u8FD8\u539F\u6A21\u5F0F\u548C\u503C");
    assertDeepEqual(buildComparableFilterTokens("eq", { value: 12 }), ["__filter:eq:12"], "\u6570\u5B57\u7B49\u4E8E\u5E94\u751F\u6210\u547D\u540D\u7A7A\u95F4 eq token");
    assertDeepEqual(buildComparableFilterTokens("range", { min: 5, max: 10 }), ["gte:5", "lte:10"], "\u6570\u5B57\u8303\u56F4\u5E94\u751F\u6210 gte/lte token");
    assertDeepEqual(buildComparableFilterTokens("gte", { value: 8 }), ["gte:8"], "\u6570\u5B57\u5927\u4E8E\u7B49\u4E8E\u5E94\u751F\u6210 gte token");
    assertDeepEqual(buildComparableFilterTokens("lte", { value: 9 }), ["lte:9"], "\u6570\u5B57\u5C0F\u4E8E\u7B49\u4E8E\u5E94\u751F\u6210 lte token");
    assertDeepEqual(parseComparableFilterTokens(["18"]), { mode: "eq", value: "18", min: "", max: "" }, "\u65E7\u6570\u5B57\u88F8\u503C\u5E94\u517C\u5BB9\u4E3A eq");
    assertDeepEqual(parseComparableFilterTokens(["__filter:eq:18"]), { mode: "eq", value: "18", min: "", max: "" }, "\u6570\u5B57 eq token \u5E94\u80FD\u8FD8\u539F\u6A21\u5F0F\u548C\u503C");
    assertDeepEqual(parseComparableFilterTokens(["gte:2026-06-01", "lte:2026-06-16"]), {
      mode: "range",
      min: "2026-06-01",
      max: "2026-06-16",
      value: ""
    }, "\u65E5\u671F\u8303\u56F4 token \u5E94\u80FD\u8FD8\u539F\u4E3A range \u6A21\u5F0F");
    assertDeepEqual(
      normalizeTableFilters({
        name: [" Clock "],
        labelPrice: ["gte:2", "lte:9"],
        categoryName: [UNCATEGORIZED_PRODUCTS_FILTER_KEY],
        domesticSupplierCode: ["CN-001"]
      }),
      {
        productName: ["Clock"],
        oemPrice: ["gte:2", "lte:9"],
        domesticSupplierCode: ["CN-001"]
      },
      "\u666E\u901A\u5217\u5934\u7B5B\u9009\u5E94\u6620\u5C04\u540E\u7AEF key\uFF0C\u5206\u7C7B\u4E0D\u5E94\u6DF7\u5165\u666E\u901A Filters"
    );
    assertEqual(
      resolveCategoryFilterValueFromTableFilters({ categoryName: [UNCATEGORIZED_PRODUCTS_FILTER_KEY] }),
      UNCATEGORIZED_PRODUCTS_FILTER_KEY,
      "\u5206\u7C7B\u5217\u5934\u503C\u5E94\u5355\u72EC\u89E3\u6790"
    );
    assertDeepEqual(
      buildCategoryQueryValue(UNCATEGORIZED_PRODUCTS_FILTER_KEY),
      { categoryGuid: void 0, uncategorizedOnly: true },
      "\u672A\u5206\u7C7B\u5217\u5934\u5E94\u8F6C\u6210\u9876\u5C42 UncategorizedOnly"
    );
    assertDeepEqual(
      buildCategoryQueryValue("cat-runtime-001"),
      { categoryGuid: "cat-runtime-001", uncategorizedOnly: false },
      "\u5177\u4F53\u5206\u7C7B\u5217\u5934\u5E94\u8F6C\u6210\u9876\u5C42 CategoryGuids \u67E5\u8BE2\u503C"
    );
    assertDeepEqual(
      buildCategoryQueryValue(ALL_PRODUCTS_FILTER_KEY),
      { categoryGuid: void 0, uncategorizedOnly: false },
      "\u5168\u90E8\u5546\u54C1\u5217\u5934\u5E94\u6E05\u7A7A\u5206\u7C7B\u9876\u5C42\u5B57\u6BB5"
    );
    assertEqual(getSingleFilterValue(["true"]), "true", "\u5355\u9009\u7B5B\u9009\u5E94\u80FD\u540C\u6B65\u56DE\u9876\u90E8\u7B5B\u9009");
    assertEqual(getSingleFilterValue(["true", "false"]), void 0, "\u591A\u9009\u7B5B\u9009\u4E0D\u5E94\u5F3A\u884C\u540C\u6B65\u4E3A\u9876\u90E8\u5355\u503C");
  });
  if (columnFilterHelperFailure) failures.push(columnFilterHelperFailure);
  const columnFilterStateFailure = await runTest("\u4ED3\u5E93\u5546\u54C1\u9875\u5E94\u7EF4\u62A4\u5217\u5934\u540E\u7AEF\u7B5B\u9009\u72B6\u6001\u5E76\u533A\u5206\u5206\u7C7B\u9876\u5C42\u5B57\u6BB5", () => {
    assert(
      pageSource.includes("const [columnFilters, setColumnFilters] = useState<WarehouseProductColumnFilters>({})") && pageSource.includes("const mergedFilters = overrides.filters ?? columnFilters;") && pageSource.includes("filters: Object.keys(mergedFilters).length ? mergedFilters : undefined") && pageSource.includes("\u5217\u5934\u7B5B\u9009\u8D70\u540E\u7AEF Filters\uFF0C\u5206\u7C7B\u4ECD\u8D70\u9876\u5C42\u5B57\u6BB5"),
      "\u9875\u9762\u5E94\u7EF4\u62A4 columnFilters \u72B6\u6001\uFF0C\u5E76\u5728 buildGridQuery \u4E2D\u628A\u666E\u901A\u5217\u5934\u7B5B\u9009\u53D1\u5230\u540E\u7AEF Filters"
    );
    assert(
      pageSource.includes("setColumnFilters((current) => setFilterValues(current, 'domesticSupplierCode'") && pageSource.includes("setColumnFilters((current) => setFilterValues(current, 'productType'") && pageSource.includes("setColumnFilters((current) => setFilterValues(current, 'isActive'"),
      "\u9876\u90E8\u4F9B\u5E94\u5546\u3001\u5546\u54C1\u7C7B\u578B\u548C\u72B6\u6001\u7B5B\u9009\u53D8\u5316\u65F6\u5E94\u540C\u6B65 columnFilters\uFF0C\u907F\u514D\u65E7\u5217\u5934\u503C\u6B8B\u7559"
    );
  });
  if (columnFilterStateFailure) failures.push(columnFilterStateFailure);
  const topCategoryTreeFilterFailure = await runTest("\u4ED3\u5E93\u5546\u54C1\u9875\u9876\u90E8\u5206\u7C7B\u7B5B\u9009\u5E94\u4F7F\u7528\u53EF\u6298\u53E0\u5206\u7C7B\u6811", () => {
    const topFilterSection = extractSection(
      pageSource,
      "<Input value={searchText}",
      "<Select value={productType}"
    );
    assert(
      pageSource.includes("TreeSelect") && pageSource.includes("buildFilterCategoryTreeOptions") && pageSource.includes("const [categoryFilterExpandedKeys, setCategoryFilterExpandedKeys] = useState<string[]>([])") && pageSource.includes("const [categoryFilterSearchText, setCategoryFilterSearchText] = useState('')") && pageSource.includes("const hasCategoryFilterSearchText = categoryFilterSearchText.trim().length > 0;") && pageSource.includes("const categoryFilterTreeOptions = useMemo(() => buildFilterCategoryTreeOptions(categories, t, i18n.language)"),
      "\u9876\u90E8\u5206\u7C7B\u7B5B\u9009\u5E94\u5F15\u5165 TreeSelect\uFF0C\u5E76\u4F7F\u7528\u6811\u5F62\u5206\u7C7B options\u3001\u641C\u7D22\u72B6\u6001\u4E0E\u72EC\u7ACB\u5C55\u5F00\u72B6\u6001"
    );
    assert(
      pageSource.includes("const firstLevelExpandedKeys = collectCategoryExpandedKeys(tree, 1);") && pageSource.includes("setCategoryExpandedKeys(firstLevelExpandedKeys);") && pageSource.includes("setCategoryFilterExpandedKeys(firstLevelExpandedKeys);"),
      "\u52A0\u8F7D\u5206\u7C7B\u6811\u540E\u5E94\u540C\u65F6\u521D\u59CB\u5316\u6279\u91CF\u5206\u7C7B\u6811\u548C\u9876\u90E8\u7B5B\u9009\u6811\u7684\u4E00\u7EA7\u5C55\u5F00\u72B6\u6001"
    );
    assert(
      topFilterSection.includes("<TreeSelect") && topFilterSection.includes("treeData={categoryFilterTreeOptions}") && topFilterSection.includes("searchValue={categoryFilterSearchText}") && topFilterSection.includes("onSearch={setCategoryFilterSearchText}") && topFilterSection.includes("treeExpandedKeys={hasCategoryFilterSearchText ? undefined : categoryFilterExpandedKeys}") && topFilterSection.includes("if (!hasCategoryFilterSearchText)") && topFilterSection.includes('treeNodeFilterProp="searchText"'),
      "\u9876\u90E8\u5206\u7C7B\u63A7\u4EF6\u5E94\u7ED1\u5B9A treeData\u3001\u641C\u7D22\u5B57\u6BB5\uFF0C\u5E76\u5728\u641C\u7D22\u65F6\u8BA9 TreeSelect \u81EA\u52A8\u5C55\u5F00\u547D\u4E2D\u8DEF\u5F84"
    );
    assert(
      topFilterSection.includes("allowClear") && topFilterSection.includes("setCategoryFilterValue(value || ALL_PRODUCTS_FILTER_KEY);") && topFilterSection.includes("setCategoryFilterSearchText('');") && pageSource.includes("setCategoryFilterValue(UNCATEGORIZED_PRODUCTS_FILTER_KEY);") && pageSource.includes("uncategorizedOnly: true,"),
      "\u9876\u90E8\u5206\u7C7B\u6811\u6E05\u7A7A\u540E\u5E94\u56DE\u5230\u5168\u90E8\u5546\u54C1\u5E76\u6E05\u7A7A\u641C\u7D22\u8BCD\uFF0C\u672A\u5206\u7C7B\u5FEB\u6377\u6309\u94AE\u4ECD\u5E94\u67E5\u8BE2 UncategorizedOnly"
    );
  });
  if (topCategoryTreeFilterFailure) failures.push(topCategoryTreeFilterFailure);
  const tableChangeColumnFilterFailure = await runTest("\u8868\u683C onChange \u5E94\u8BFB\u53D6\u5217\u5934 filters \u5E76\u91CD\u67E5\u7B2C\u4E00\u9875", () => {
    const tableSection = extractSection(
      pageSource,
      "onChange={(pagination: TablePaginationConfig, filters: Record<string, FilterValue | null>, sorter:",
      "}/>"
    );
    assert(
      tableSection.includes("const nextColumnFilters = normalizeTableFilters(filters);") && tableSection.includes("const nextCategoryFilterValue = resolveCategoryFilterValueFromTableFilters(filters);") && tableSection.includes("setColumnFilters(nextColumnFilters);"),
      "\u8868\u683C onChange \u5E94\u63A5\u6536 AntD filters\uFF0C\u5E76\u8F6C\u6362\u540E\u56DE\u5199 columnFilters"
    );
    assert(
      tableSection.includes("page: extra.action === 'paginate' ? pagination.current || 1 : 1,") && tableSection.includes("filters: nextColumnFilters,") && tableSection.includes("...categoryQuery,"),
      "\u5217\u5934\u7B5B\u9009\u6216\u6392\u5E8F\u53D8\u5316\u540E\u5E94\u5E26 filters \u91CD\u67E5\u6570\u636E\uFF0C\u5E76\u5728\u975E\u5206\u9875\u573A\u666F\u56DE\u5230\u7B2C\u4E00\u9875"
    );
    assert(
      tableSection.includes("const categoryQuery = buildCategoryQueryValue(nextCategoryFilterValue);") && tableSection.includes("setCategoryFilterValue(nextCategoryFilterValue);"),
      "\u5206\u7C7B\u5217\u5934\u53D8\u5316\u65F6\u5E94\u8F6C\u6210\u9876\u5C42\u5206\u7C7B\u67E5\u8BE2\u5B57\u6BB5\uFF0C\u800C\u4E0D\u662F\u6DF7\u5165\u666E\u901A Filters"
    );
  });
  if (tableChangeColumnFilterFailure) failures.push(tableChangeColumnFilterFailure);
  const columnFilterUiFailure = await runTest("\u4ED3\u5E93\u5546\u54C1\u8868\u683C\u5E94\u4E3A\u6587\u672C\u6570\u5B57\u65E5\u671F\u679A\u4E3E\u5217\u63A5\u5165\u5217\u5934\u8FC7\u6EE4 UI", () => {
    const columnsSection = extractSection(
      pageSource,
      "const baseColumns = useMemo",
      "const draggableColumnKeys"
    );
    assert(
      pageSource.includes("const renderColumnFilterPanel = (content: ReactNode, onApply: () => void, onReset: () => void) =>") && pageSource.includes("\u7EDF\u4E00\u5217\u5934\u7B5B\u9009\u9762\u677F\u9AA8\u67B6") && pageSource.includes("warehouse-products-column-filter-panel") && pageSource.includes("warehouse-products-column-filter-body") && pageSource.includes("warehouse-products-column-filter-actions") && !pageSource.includes("style={{ width: 112 }}"),
      "\u6587\u672C\u3001\u6570\u5B57\u548C\u65E5\u671F\u5217\u5934\u7B5B\u9009\u5E94\u590D\u7528\u7EDF\u4E00\u9762\u677F\uFF0C\u4E0D\u80FD\u56DE\u9000\u5230\u7A84 Select \u4E0B\u62C9"
    );
    assert(
      pageSource.includes("const buildTextFilterDropdown = (filterKey: string, placeholder: string) =>") && pageSource.includes("const buildNumberRangeFilterDropdown = (filterKey: string) =>") && pageSource.includes("const buildDateRangeFilterDropdown = (filterKey: string) =>") && pageSource.includes("textFilterModeOptions") && pageSource.includes("comparableFilterModeOptions"),
      "\u9875\u9762\u5E94\u63D0\u4F9B\u6587\u672C\u3001\u6570\u5B57\u548C\u65E5\u671F\u5217\u5934\u7B5B\u9009 helper\uFF0C\u5E76\u663E\u793A\u5339\u914D\u65B9\u5F0F\u9009\u62E9"
    );
    assert(
      columnFiltersSource.includes("const filterKeyMap: Record<string, string> = {") && columnFiltersSource.includes("name: 'productName'") && columnFiltersSource.includes("labelPrice: 'oemPrice'"),
      "normalizeTableFilters \u5E94\u663E\u5F0F\u7EF4\u62A4\u5217 key \u5230\u540E\u7AEF filter key \u7684\u6620\u5C04"
    );
    assert(
      columnsSection.includes("...textFilterProps('itemNumber'") && columnsSection.includes("...textFilterProps('productName'") && columnsSection.includes("...textFilterProps('nameEn'") && columnsSection.includes("...textFilterProps('barcode'") && columnsSection.includes("...textFilterProps('locationCodes'"),
      "\u8D27\u53F7\u3001\u5546\u54C1\u540D\u3001\u82F1\u6587\u540D\u3001\u6761\u7801\u548C\u8D27\u4F4D\u5217\u5E94\u63A5\u5165\u6587\u672C\u5217\u5934\u7B5B\u9009"
    );
    assert(
      columnsSection.includes("...numberRangeFilterProps('minOrderQuantity')") && columnsSection.includes("...numberRangeFilterProps('domesticPrice')") && columnsSection.includes("...numberRangeFilterProps('importPrice')") && columnsSection.includes("...numberRangeFilterProps('oemPrice')") && columnsSection.includes("...numberRangeFilterProps('packingQty')") && columnsSection.includes("...numberRangeFilterProps('volume')") && columnsSection.includes("...dateRangeFilterProps('updatedAt')"),
      "\u4E2D\u5305\u6570\u3001\u4EF7\u683C\u3001\u88C5\u7BB1\u6570\u3001\u4F53\u79EF\u548C\u66F4\u65B0\u65F6\u95F4\u5217\u5E94\u63A5\u5165\u6570\u5B57/\u65E5\u671F\u5217\u5934\u7B5B\u9009"
    );
    assert(
      columnsSection.includes("...enumFilterProps('domesticSupplierCode'") && columnsSection.includes("...enumFilterProps('localSupplierCode'") && columnsSection.includes("...enumFilterProps('isActive'") && columnsSection.includes("...enumFilterProps('productType'") && columnsSection.includes("filters: categoryColumnFilterOptions") && columnsSection.includes("filteredValue: categoryFilterValue === ALL_PRODUCTS_FILTER_KEY ? null : [categoryFilterValue]"),
      "\u4F9B\u5E94\u5546\u3001\u72B6\u6001\u3001\u5546\u54C1\u7C7B\u578B\u548C\u5206\u7C7B\u5217\u5E94\u66B4\u9732 filters / filteredValue \u5F62\u5F0F\u7684\u5217\u5934\u8FC7\u6EE4 UI"
    );
    assert(
      columnsSection.includes("key: 'name'") && columnsSection.includes("dataIndex: 'name'") && columnsSection.includes("...textFilterProps('productName'") && columnsSection.includes("key: 'labelPrice'") && columnsSection.includes("dataIndex: 'labelPrice'") && columnsSection.includes("...numberRangeFilterProps('oemPrice')"),
      "\u5546\u54C1\u540D\u548C OEM \u5217\u5E94\u4FDD\u7559\u539F\u5217 key\uFF0C\u540C\u65F6\u7EE7\u7EED\u4F7F\u7528\u540E\u7AEF productName / oemPrice filter key"
    );
    assert(
      columnsSection.includes("key: 'locationCodes'") && columnsSection.includes("dataIndex: 'locationCodes'") && columnsSection.includes("t('location.location', '\u8D27\u4F4D')"),
      "\u8D27\u4F4D\u5217\u5E94\u4F7F\u7528 locationCodes \u4F5C\u4E3A\u5217 key/dataIndex\uFF0C\u5E76\u590D\u7528\u8D27\u4F4D\u7FFB\u8BD1\u6587\u6848"
    );
  });
  if (columnFilterUiFailure) failures.push(columnFilterUiFailure);
  const resetColumnFilterFailure = await runTest("\u91CD\u7F6E\u67E5\u8BE2\u5E94\u6E05\u7A7A\u5217\u5934\u7B5B\u9009\u72B6\u6001", () => {
    const resetSection = extractSection(
      pageSource,
      "<Button icon={<ReloadOutlined />} onClick={() => {",
      "{t('common.reset')}"
    );
    assert(
      resetSection.includes("setColumnFilters({});") && resetSection.includes("filters: {},"),
      "\u70B9\u51FB\u91CD\u7F6E\u65F6\u5E94\u6E05\u7A7A columnFilters\uFF0C\u5E76\u6309\u7A7A Filters \u91CD\u67E5\u5217\u8868"
    );
  });
  if (resetColumnFilterFailure) failures.push(resetColumnFilterFailure);
  if (failures.length > 0) {
    throw new Error(`\u5171\u6709 ${failures.length} \u4E2A\u6D4B\u8BD5\u5931\u8D25
- ${failures.join("\n- ")}`);
  }
  console.log("WarehouseProducts.hqSync.logic.test: ok");
}
await main();
