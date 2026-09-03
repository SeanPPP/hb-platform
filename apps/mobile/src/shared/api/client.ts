import axios, { AxiosError, InternalAxiosRequestConfig } from "axios";
import { router } from "expo-router";
import { SecureStorage } from "@/shared/storage/secure";
import { DeviceStorage } from "@/modules/device/storage";
import { emitUnauthenticatedSession } from "@/modules/auth/auth-session-events";
import { stopAttendanceLocationTracking } from "@/modules/attendance/location-tracking-control";
import { isUnauthenticatedApiPayload } from "@/shared/api/auth-error";
import { buildApiBaseUrl, DEFAULT_API_BASE_URL, getStoredApiHost } from "@/shared/api/config";
import { extractApiErrorMessage } from "@/shared/api/error-message";
import { unwrapApiEnvelope } from "@/shared/api/api-envelope";
import { preserveApiClientError } from "@/shared/api/client-error";
import { isLogCenterIngestUrl } from "@/shared/logging/log-center";
import { reportApplicationLog } from "@/shared/logging/log-center-runtime";
import {
  iosReviewAxiosAdapter,
} from "@/modules/ios-review/transport";
import { isIosReviewSessionActive } from "@/modules/ios-review/session";
import {
  clearAuthSessionMarker,
  getAuthSessionMarker,
} from "@/modules/device-activation/auth-session-marker";
import { DeviceAccountStorage } from "@/modules/device-activation/device-account-storage-runtime";
import { exchangeStoredDeviceAccountToken } from "@/modules/device-activation/device-account-token";
import {
  deriveEffectiveAuthSessionKind,
  isRelativeApiClientUrl,
  removeRequestHeader,
  resolveDeviceAccountRequestPolicy,
} from "@/modules/device-activation/device-account-request-policy";

export const apiClient = axios.create({
  baseURL: DEFAULT_API_BASE_URL,
  timeout: 30000,
  headers: { "Content-Type": "application/json" },
});

async function syncApiBaseUrl() {
  const host = await getStoredApiHost();
  const baseURL = buildApiBaseUrl(host);
  apiClient.defaults.baseURL = baseURL;
  return baseURL;
}

let isRefreshing = false;
let isRedirectingToLogin = false;
let refreshQueue: Array<{
  resolve: (t: string) => void;
  reject: (e: Error) => void;
}> = [];

function isAuthenticationRequest(config?: InternalAxiosRequestConfig | null) {
  const url = config?.url ?? "";
  return (
    url.includes("/auth/login") ||
    url.includes("/auth/refresh") ||
    url.includes("/mobile/v1/device-session/exchange")
  );
}

function shouldSkipAuthRedirect(config?: InternalAxiosRequestConfig | null) {
  if (!config) {
    return false;
  }

  const rawSkipHeader = config.headers?.["X-Skip-Auth-Redirect"];
  const skipHeaderValue = Array.isArray(rawSkipHeader) ? rawSkipHeader[0] : rawSkipHeader;
  return skipHeaderValue === "1";
}

function shouldSkipAuthRecovery(config?: InternalAxiosRequestConfig | null) {
  if (!config) {
    return false;
  }
  const rawValue = config.headers?.["X-Skip-Auth-Recovery"];
  const value = Array.isArray(rawValue) ? rawValue[0] : rawValue;
  return value === "1";
}

function shouldSkipCenterLog(config?: InternalAxiosRequestConfig | null) {
  if (!config) {
    return false;
  }

  const rawSkipHeader = config.headers?.["X-Skip-Center-Log"];
  const skipHeaderValue = Array.isArray(rawSkipHeader) ? rawSkipHeader[0] : rawSkipHeader;
  return skipHeaderValue === "1" || isLogCenterIngestUrl(resolveRequestUrl(config));
}

function resolveRequestUrl(config?: InternalAxiosRequestConfig | null) {
  if (!config?.url) {
    return "";
  }

  if (/^https?:\/\//i.test(config.url)) {
    return config.url;
  }

  const baseURL = config.baseURL ?? apiClient.defaults.baseURL ?? DEFAULT_API_BASE_URL;
  try {
    return new URL(config.url, baseURL).toString();
  } catch {
    return `${baseURL.replace(/\/+$/, "")}/${config.url.replace(/^\/+/, "")}`;
  }
}

function reportApiErrorLog(
  error: unknown,
  config?: InternalAxiosRequestConfig | null,
  options?: {
    responseStatus?: number;
    responseData?: unknown;
    message?: string;
  }
) {
  if (shouldSkipCenterLog(config)) {
    return;
  }

  const responseStatus = options?.responseStatus;
  const retryableConfig = config as (InternalAxiosRequestConfig & { _retry?: boolean }) | null | undefined;
  if (responseStatus === 401 && !retryableConfig?._retry) {
    return;
  }

  const normalizedError = error instanceof Error ? error : new Error(String(error));
  const requestUrl = resolveRequestUrl(config);
  let requestPath = config?.url ?? "";

  if (requestUrl) {
    try {
      requestPath = new URL(requestUrl).pathname;
    } catch {
      requestPath = config?.url ?? requestUrl;
    }
  }

  reportApplicationLog({
    level: responseStatus && responseStatus >= 500 ? "Error" : "Warning",
    message: options?.message ?? "移动端 API 请求失败",
    sourceType: "mobile.api",
    requestPath: requestPath || undefined,
    requestMethod: config?.method?.toUpperCase(),
    statusCode: responseStatus,
    exceptionType: normalizedError.name,
    exceptionMessage: normalizedError.message,
    stackTrace: normalizedError.stack,
    properties: {
      url: requestUrl || undefined,
      axiosCode: (error as { code?: unknown } | undefined)?.code,
      responseData: options?.responseData,
      hasResponse: responseStatus != null,
    },
  });
}

async function invalidateLocalSession(message?: string) {
  // 401/会话失效时必须直接停掉班中后台定位；不能只依赖 UI store 订阅者。
  await stopAttendanceLocationTracking().catch((error) => {
    console.warn("[attendance-location] 会话失效时停止后台定位失败", error);
  });
  await SecureStorage.clearAll();
  await clearAuthSessionMarker();
  emitUnauthenticatedSession({ message });
}

async function redirectToLoginAfterUnauthenticated(message?: string) {
  if (isRedirectingToLogin) {
    return;
  }

  isRedirectingToLogin = true;
  try {
    await invalidateLocalSession(message);
    router.replace("/(auth)/login");
  } finally {
    isRedirectingToLogin = false;
  }
}

apiClient.interceptors.request.use(
  async (config: InternalAxiosRequestConfig) => {
    if (isIosReviewSessionActive()) {
      // 关键位置：审核会话在任何 base URL、token、设备认证读取之前强制切到本地 adapter。
      config.adapter = iosReviewAxiosAdapter;
      config.baseURL = undefined;
      if (config.headers) {
        removeRequestHeader(config.headers, "Authorization");
        removeRequestHeader(config.headers, "X-Device-Id");
        removeRequestHeader(config.headers, "X-Auth-Code");
      }
      return config;
    }

    if (!isRelativeApiClientUrl(config.url)) {
      throw new Error("ABSOLUTE_API_URL_NOT_ALLOWED");
    }

    const rawApiHost = config.headers?.["X-Client-Api-Host"];
    const apiHost = Array.isArray(rawApiHost) ? rawApiHost[0] : rawApiHost;
    const rawSkipAuthentication = config.headers?.["X-Client-Skip-Authentication"];
    const skipAuthentication =
      (Array.isArray(rawSkipAuthentication)
        ? rawSkipAuthentication[0]
        : rawSkipAuthentication) === "1";
    if (config.headers) {
      removeRequestHeader(config.headers, "X-Client-Api-Host");
      removeRequestHeader(config.headers, "X-Client-Skip-Authentication");
    }
    const requestedApiHost =
      typeof apiHost === "string" && apiHost
        ? apiHost
        : await getStoredApiHost();
    if (skipAuthentication) {
      const requestPolicy = resolveDeviceAccountRequestPolicy({
        requestedApiHost,
        bindingApiHost: null,
        sessionKind: null,
        skipAuthentication: true,
      });
      config.baseURL = buildApiBaseUrl(requestPolicy.apiHost);
      if (config.headers) {
        removeRequestHeader(config.headers, "Authorization");
        removeRequestHeader(config.headers, "X-Device-Id");
        removeRequestHeader(config.headers, "X-Auth-Code");
      }
      return config;
    }

    const [token, refreshToken, persistedSessionKind, accountBinding] = await Promise.all([
      SecureStorage.getToken(),
      SecureStorage.getRefreshToken(),
      getAuthSessionMarker(),
      DeviceAccountStorage.loadBinding().catch(() => null),
    ]);
    const sessionKind = deriveEffectiveAuthSessionKind({
      persistedKind: persistedSessionKind,
      hasAccessToken: Boolean(token),
      hasRefreshToken: Boolean(refreshToken),
      hasBinding: Boolean(accountBinding),
    });
    const requestPolicy = resolveDeviceAccountRequestPolicy({
      requestedApiHost,
      bindingApiHost: accountBinding?.apiHost,
      sessionKind,
      skipAuthentication: false,
    });
    config.baseURL = buildApiBaseUrl(requestPolicy.apiHost);
    if (!apiHost && requestPolicy.apiHost === requestedApiHost) {
      apiClient.defaults.baseURL = config.baseURL;
    }

    if (!requestPolicy.allowDeviceHeaders && config.headers) {
      // 即使调用方显式传入，绑定凭据也不能跨 host 发送。
      removeRequestHeader(config.headers, "X-Device-Id");
      removeRequestHeader(config.headers, "X-Auth-Code");
    }
    if (!requestPolicy.allowBearerToken) {
      if (config.headers) {
        removeRequestHeader(config.headers, "Authorization");
      }
      throw new Error("DEVICE_ACCOUNT_BINDING_NOT_FOUND");
    }

    if (token && config.headers) {
      if (!config.headers.has("Authorization")) {
        config.headers.set("Authorization", `Bearer ${token}`);
      }
      if (sessionKind !== "deviceAccount") {
        return config;
      }
    }

    const deviceSession = requestPolicy.allowDeviceHeaders
      ? await DeviceStorage.getSession()
      : null;
    if (deviceSession?.hardwareId && deviceSession.authCode && config.headers) {
      config.headers.set("X-Device-Id", deviceSession.hardwareId);
      config.headers.set("X-Auth-Code", deviceSession.authCode);
    }
    return config;
  },
  (error) => Promise.reject(error)
);

apiClient.interceptors.response.use(
  async (response) => {
    if (
      isUnauthenticatedApiPayload(response.data) &&
      !isAuthenticationRequest(response.config as InternalAxiosRequestConfig)
    ) {
      const message = extractApiErrorMessage(response.data, "Unauthorized");
      if (shouldSkipAuthRecovery(response.config as InternalAxiosRequestConfig)) {
        throw new Error(message);
      }
      if (shouldSkipAuthRedirect(response.config as InternalAxiosRequestConfig)) {
        await invalidateLocalSession(message);
      } else {
        await redirectToLoginAfterUnauthenticated(message);
      }
      throw new Error(message);
    }

    try {
      response.data = unwrapApiEnvelope(response.data);
    } catch (error) {
      reportApiErrorLog(error, response.config as InternalAxiosRequestConfig, {
        responseStatus: response.status,
        responseData: response.data,
        message: "移动端 API 返回业务失败响应",
      });
      throw error;
    }
    return response;
  },
  async (error: AxiosError) => {
    const original = error.config as InternalAxiosRequestConfig & { _retry?: boolean };
    const skipAuthRedirect = shouldSkipAuthRedirect(original);
    const skipAuthRecovery = shouldSkipAuthRecovery(original);

    if (error.response?.status === 401 && skipAuthRecovery) {
      return Promise.reject(preserveApiClientError(error));
    }

    if (error.response?.status === 401 && original?._retry && !isAuthenticationRequest(original)) {
      const message = extractApiErrorMessage(error, error.message);
      if (!skipAuthRedirect) {
        await redirectToLoginAfterUnauthenticated(message);
      } else {
        await invalidateLocalSession(message);
      }
      error.message = message;
      return Promise.reject(preserveApiClientError(error));
    }

    if (error.response?.status === 401 && !original?._retry && !isAuthenticationRequest(original)) {
      if (isRefreshing) {
        return new Promise((resolve, reject) => {
          refreshQueue.push({
            resolve: (t) => {
              original.headers.set("Authorization", `Bearer ${t}`);
              resolve(apiClient(original));
            },
            reject,
          });
        });
      }
      original._retry = true;
      isRefreshing = true;
      try {
        const [persistedSessionKind, accountBinding, currentToken, refreshToken] =
          await Promise.all([
            getAuthSessionMarker(),
            DeviceAccountStorage.loadBinding().catch(() => null),
            SecureStorage.getToken(),
            SecureStorage.getRefreshToken(),
          ]);
        const sessionKind = deriveEffectiveAuthSessionKind({
          persistedKind: persistedSessionKind,
          hasAccessToken: Boolean(currentToken),
          hasRefreshToken: Boolean(refreshToken),
          hasBinding: Boolean(accountBinding),
        });
        let accessToken: string;
        if (sessionKind === "deviceAccount") {
          const exchanged = await exchangeStoredDeviceAccountToken();
          accessToken = exchanged.accessToken;
          await SecureStorage.setToken(accessToken);
          await SecureStorage.removeRefreshToken();
        } else {
          const baseURL = await syncApiBaseUrl();
          const rt = await SecureStorage.getRefreshToken();
          if (!rt) throw new Error("No refresh token");
          const res = await axios.post(`${baseURL}/auth/refresh`, {
            refreshToken: rt,
          });
          const refreshed = res.data.data ?? res.data;
          accessToken = refreshed.accessToken;
          await SecureStorage.setToken(accessToken);
          await SecureStorage.setRefreshToken(refreshed.refreshToken);
        }
        refreshQueue.forEach((cb) => cb.resolve(accessToken));
        refreshQueue = [];
        original.headers.set("Authorization", `Bearer ${accessToken}`);
        return apiClient(original);
      } catch (refreshErr) {
        refreshQueue.forEach((cb) => cb.reject(refreshErr as Error));
        refreshQueue = [];
        if (!skipAuthRedirect) {
          await redirectToLoginAfterUnauthenticated(
            refreshErr instanceof Error ? refreshErr.message : undefined
          );
        } else {
          await invalidateLocalSession(
            refreshErr instanceof Error ? refreshErr.message : undefined
          );
        }
        return Promise.reject(refreshErr);
      } finally {
        isRefreshing = false;
      }
    }
    reportApiErrorLog(error, original, {
      responseStatus: error.response?.status,
      responseData: error.response?.data,
    });
    return Promise.reject(preserveApiClientError(error));
  }
);
