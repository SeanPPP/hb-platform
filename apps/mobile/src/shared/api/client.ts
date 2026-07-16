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

function isLoginRequest(config?: InternalAxiosRequestConfig | null) {
  return Boolean(config?.url?.includes("/auth/login"));
}

function shouldSkipAuthRedirect(config?: InternalAxiosRequestConfig | null) {
  if (!config) {
    return false;
  }

  const rawSkipHeader = config.headers?.["X-Skip-Auth-Redirect"];
  const skipHeaderValue = Array.isArray(rawSkipHeader) ? rawSkipHeader[0] : rawSkipHeader;
  return skipHeaderValue === "1";
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
    config.baseURL = await syncApiBaseUrl();
    const token = await SecureStorage.getToken();
    if (token && config.headers) {
      config.headers.Authorization = `Bearer ${token}`;
      return config;
    }

    const deviceSession = await DeviceStorage.getSession();
    if (deviceSession?.hardwareId && deviceSession.authCode && config.headers) {
      config.headers["X-Device-Id"] = deviceSession.hardwareId;
      config.headers["X-Auth-Code"] = deviceSession.authCode;
    }
    return config;
  },
  (error) => Promise.reject(error)
);

apiClient.interceptors.response.use(
  async (response) => {
    if (
      isUnauthenticatedApiPayload(response.data) &&
      !isLoginRequest(response.config as InternalAxiosRequestConfig)
    ) {
      const message = extractApiErrorMessage(response.data, "Unauthorized");
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

    if (error.response?.status === 401 && original?._retry && !isLoginRequest(original)) {
      const message = extractApiErrorMessage(error, error.message);
      if (!skipAuthRedirect) {
        await redirectToLoginAfterUnauthenticated(message);
      } else {
        await invalidateLocalSession(message);
      }
      error.message = message;
      return Promise.reject(preserveApiClientError(error));
    }

    if (error.response?.status === 401 && !original?._retry && !isLoginRequest(original)) {
      if (isRefreshing) {
        return new Promise((resolve, reject) => {
          refreshQueue.push({
            resolve: (t) => {
              original.headers.Authorization = `Bearer ${t}`;
              resolve(apiClient(original));
            },
            reject,
          });
        });
      }
      original._retry = true;
      isRefreshing = true;
      try {
        const baseURL = await syncApiBaseUrl();
        const rt = await SecureStorage.getRefreshToken();
        if (!rt) throw new Error("No refresh token");
        const res = await axios.post(`${baseURL}/auth/refresh`, {
          refreshToken: rt,
        });
        const { accessToken, refreshToken: newRt } = res.data.data ?? res.data;
        await SecureStorage.setToken(accessToken);
        await SecureStorage.setRefreshToken(newRt);
        refreshQueue.forEach((cb) => cb.resolve(accessToken));
        refreshQueue = [];
        original.headers.Authorization = `Bearer ${accessToken}`;
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
