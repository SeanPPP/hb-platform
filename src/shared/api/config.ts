import { AppAsyncStorage } from "@/shared/storage/async-storage";

const API_HOST_STORAGE_KEY = "hbweb_api_host";
export const DEFAULT_API_HOST = normalizeApiHost(process.env.EXPO_PUBLIC_API_BASE_URL) || "192.168.31.247";
export const API_PROTOCOL = "http";
export const API_PORT = "5001";
export const API_PATH = "/api";
export const DEFAULT_API_BASE_URL = buildApiBaseUrl(DEFAULT_API_HOST);

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

