import { AppAsyncStorage } from "@/shared/storage/async-storage";

export type PersistedAuthSessionKind = "account" | "deviceAccount";

const AUTH_SESSION_KIND_KEY = "hbmobile.auth-session-kind.v1";

export async function getAuthSessionMarker(): Promise<PersistedAuthSessionKind | null> {
  const value = await AppAsyncStorage.getString(AUTH_SESSION_KIND_KEY);
  return value === "account" || value === "deviceAccount" ? value : null;
}

export function setAuthSessionMarker(kind: PersistedAuthSessionKind) {
  return AppAsyncStorage.setString(AUTH_SESSION_KIND_KEY, kind);
}

export function clearAuthSessionMarker() {
  return AppAsyncStorage.removeItem(AUTH_SESSION_KIND_KEY);
}
