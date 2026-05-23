import axios, { AxiosError, InternalAxiosRequestConfig } from "axios";
import { router } from "expo-router";
import { SecureStorage } from "@/shared/storage/secure";
import { DeviceStorage } from "@/modules/device/storage";
import { buildApiBaseUrl, DEFAULT_API_BASE_URL, getStoredApiHost } from "@/shared/api/config";
import { extractApiErrorMessage } from "@/shared/api/error-message";

function unwrapEnvelope<T>(payload: unknown): T {
  let current = payload;
  for (let depth = 0; depth < 3; depth++) {
    if (typeof current !== "object" || current === null || !("data" in current)) break;
    const keys = Object.keys(current);
    const isEnvelope =
      keys.includes("data") &&
      (keys.includes("success") || keys.includes("isSuccess") || keys.includes("message"));
    if (!isEnvelope) break;
    const envelope = current as Record<string, unknown>;
    const success = envelope.success ?? envelope.isSuccess;
    if (success === false) {
      throw new Error(extractApiErrorMessage(envelope, "Request failed"));
    }
    current = envelope.data;
  }
  return current as T;
}

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
let refreshQueue: Array<{
  resolve: (t: string) => void;
  reject: (e: Error) => void;
}> = [];

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
  (response) => {
    response.data = unwrapEnvelope(response.data);
    return response;
  },
  async (error: AxiosError) => {
    const original = error.config as InternalAxiosRequestConfig & { _retry?: boolean };
    if (error.response?.status === 401 && !original?._retry) {
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
        await SecureStorage.clearAll();
        router.replace("/(auth)/login");
        return Promise.reject(refreshErr);
      } finally {
        isRefreshing = false;
      }
    }
    return Promise.reject(new Error(extractApiErrorMessage(error, error.message)));
  }
);
