import { apiClient } from "@/shared/api/client";
import type {
  LoginRequest,
  TokenResponse,
  CurrentUser,
  UserStoreDto,
} from "@/modules/auth/types";

function normalizeUserStores(payload: unknown): UserStoreDto[] {
  if (!Array.isArray(payload)) {
    return [];
  }

  return payload
    .filter((item): item is Record<string, unknown> => Boolean(item) && typeof item === "object")
    .map((item) => ({
      storeCode:
        (typeof item.storeCode === "string" && item.storeCode) ||
        (typeof item.StoreCode === "string" && item.StoreCode) ||
        "",
      storeName:
        (typeof item.storeName === "string" && item.storeName) ||
        (typeof item.StoreName === "string" && item.StoreName) ||
        (typeof item.storeCode === "string" && item.storeCode) ||
        (typeof item.StoreCode === "string" && item.StoreCode) ||
        "",
    }))
    .filter((item) => Boolean(item.storeCode));
}

function normalizeCurrentUser(payload: unknown): CurrentUser {
  const data = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;

  return {
    userGUID:
      (typeof data.userGUID === "string" && data.userGUID) ||
      (typeof data.userGuid === "string" && data.userGuid) ||
      (typeof data.UserGUID === "string" && data.UserGUID) ||
      (typeof data.UserGuid === "string" && data.UserGuid) ||
      "",
    username:
      (typeof data.username === "string" && data.username) ||
      (typeof data.userName === "string" && data.userName) ||
      (typeof data.UserName === "string" && data.UserName) ||
      "",
    email:
      (typeof data.email === "string" && data.email) ||
      (typeof data.Email === "string" && data.Email) ||
      "",
    fullName:
      (typeof data.fullName === "string" && data.fullName) ||
      (typeof data.FullName === "string" && data.FullName) ||
      undefined,
    phone:
      (typeof data.phone === "string" && data.phone) ||
      (typeof data.Phone === "string" && data.Phone) ||
      undefined,
    permissions: Array.isArray(data.permissions)
      ? (data.permissions as string[])
      : Array.isArray(data.Permissions)
        ? (data.Permissions as string[])
        : [],
    roleNames: Array.isArray(data.roleNames)
      ? (data.roleNames as string[])
      : Array.isArray(data.RoleNames)
        ? (data.RoleNames as string[])
        : [],
    storeNames: Array.isArray(data.storeNames)
      ? (data.storeNames as string[])
      : Array.isArray(data.StoreNames)
        ? (data.StoreNames as string[])
        : [],
    stores: normalizeUserStores(data.stores ?? data.Stores),
  };
}

export async function loginApi(payload: LoginRequest): Promise<TokenResponse> {
  const res = await apiClient.post("/auth/login", payload);
  return res.data as TokenResponse;
}

export async function refreshTokenApi(
  refreshToken: string
): Promise<TokenResponse> {
  const res = await apiClient.post("/auth/refresh", { refreshToken });
  return res.data as TokenResponse;
}

export async function getCurrentUserApi(): Promise<CurrentUser> {
  const res = await apiClient.get("/auth/current");
  return normalizeCurrentUser(res.data);
}

export async function getUserStoresApi(userGuid: string): Promise<UserStoreDto[]> {
  const res = await apiClient.get(`/Users/guid/${encodeURIComponent(userGuid)}/stores`);
  return Array.isArray(res.data) ? (res.data as UserStoreDto[]) : [];
}

export async function logoutApi(refreshToken: string): Promise<void> {
  await apiClient.post("/auth/logout", { refreshToken });
}
