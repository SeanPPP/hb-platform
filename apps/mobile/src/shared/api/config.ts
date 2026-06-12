import { AppAsyncStorage } from "@/shared/storage/async-storage";

const API_HOST_STORAGE_KEY = "hbweb_api_host";
export const DEFAULT_API_HOST = normalizeApiHost(process.env.EXPO_PUBLIC_API_BASE_URL) || "hotbargain.vip";
export const API_PROTOCOL = "http";
export const API_PORT = "5002";
export const API_PATH = "/api";
const PRODUCTION_API_HOST = "hotbargain.vip";
export const DEFAULT_API_BASE_URL = buildApiBaseUrl(DEFAULT_API_HOST);
// 服务器设置弹窗使用的预设地址，线上地址必须放在首位作为发布默认选项。
export const API_HOST_PRESETS = [
  { key: "production", host: "hotbargain.vip", labelKey: "apiHost.presets.production" },
  { key: "local", host: "192.168.31.247", labelKey: "apiHost.presets.local" },
] as const;

let cachedApiHost = DEFAULT_API_HOST;

export function normalizeApiHost(input?: string | null) {
  const raw = input?.trim();
  if (!raw) {
    return "";
  }

  try {
    const candidate = raw.includes("://") ? raw : `${API_PROTOCOL}://${raw}`;
    const url = new URL(candidate);
    return url.hostname.trim();
  } catch {
    return raw
      .replace(/^https?:\/\//i, "")
      .split("/")[0]
      .split(":")[0]
      .trim();
  }
}

export function buildApiBaseUrl(host: string) {
  // 生产域名通过 Nginx HTTPS 代理进入 5002，移动端不再直连明文端口。
  if (host === PRODUCTION_API_HOST) {
    return `https://${host}${API_PATH}`;
  }
  return `${API_PROTOCOL}://${host}:${API_PORT}${API_PATH}`;
}

export function getCurrentApiHost() {
  return cachedApiHost;
}

export async function getStoredApiHost() {
  const storedHost = normalizeApiHost(await AppAsyncStorage.getString(API_HOST_STORAGE_KEY));
  cachedApiHost = storedHost || DEFAULT_API_HOST;
  return cachedApiHost;
}

export async function setStoredApiHost(input: string) {
  const host = normalizeApiHost(input) || DEFAULT_API_HOST;
  cachedApiHost = host;
  await AppAsyncStorage.setString(API_HOST_STORAGE_KEY, host);
  return host;
}
