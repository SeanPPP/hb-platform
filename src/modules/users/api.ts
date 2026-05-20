import { apiClient } from "@/shared/api/client";
import type {
  StoreUserCreatePayload,
  StoreUserDetail,
  StoreUserGridParams,
  StoreUserListItem,
  StoreUserPasswordPayload,
  StoreUserStatusPayload,
  StoreUserUpdatePayload,
} from "@/modules/users/types";
import { STORE_STAFF_ROLE } from "@/modules/users/types";

type ApiRecord = Record<string, unknown>;

function asString(value: unknown): string | undefined {
  if (typeof value !== "string") {
    return undefined;
  }

  const normalized = value.trim();
  return normalized ? normalized : undefined;
}

function asNumber(value: unknown, fallback = 0): number {
  if (typeof value === "number" && Number.isFinite(value)) {
    return value;
  }

  if (typeof value === "string" && value.trim()) {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : fallback;
  }

  return fallback;
}

function asStringArray(value: unknown): string[] {
  if (!Array.isArray(value)) {
    return [];
  }

  return value
    .map((item) => (typeof item === "string" ? item.trim() : ""))
    .filter(Boolean);
}

function normalizeStoreUser(raw: ApiRecord): StoreUserListItem {
  return {
    userGUID: String(raw.userGUID ?? raw.userGuid ?? raw.UserGUID ?? raw.UserGuid ?? ""),
    username: String(raw.username ?? raw.userName ?? raw.UserName ?? raw.Username ?? ""),
    fullName: asString(raw.fullName ?? raw.FullName ?? raw.name ?? raw.Name),
    email: asString(raw.email ?? raw.Email),
    phone: asString(raw.phone ?? raw.Phone ?? raw.mobile ?? raw.Mobile),
    status: asNumber(raw.status ?? raw.Status, 1),
    storeCode: asString(raw.storeCode ?? raw.StoreCode),
    storeName: asString(raw.storeName ?? raw.StoreName),
    roleNames: asStringArray(raw.roleNames ?? raw.RoleNames).length
      ? asStringArray(raw.roleNames ?? raw.RoleNames)
      : [STORE_STAFF_ROLE],
    lastLoginTime: asString(raw.lastLoginTime ?? raw.LastLoginTime),
    createdAt: asString(raw.createdAt ?? raw.CreatedAt),
    updatedAt: asString(raw.updatedAt ?? raw.UpdatedAt),
  };
}

function normalizeStoreUserList(payload: unknown): StoreUserListItem[] {
  if (Array.isArray(payload)) {
    return payload
      .filter((item): item is ApiRecord => Boolean(item) && typeof item === "object")
      .map(normalizeStoreUser);
  }

  if (payload && typeof payload === "object") {
    const record = payload as ApiRecord;
    const items = record.items ?? record.Items ?? record.rows ?? record.Rows ?? record.list ?? record.List;
    if (Array.isArray(items)) {
      return items
        .filter((item): item is ApiRecord => Boolean(item) && typeof item === "object")
        .map(normalizeStoreUser);
    }
  }

  return [];
}

function normalizeStoreUserDetail(payload: unknown): StoreUserDetail {
  if (!payload || typeof payload !== "object") {
    throw new Error("Invalid user detail payload");
  }

  return normalizeStoreUser(payload as ApiRecord);
}

function sanitizeMutationPayload<T extends { storeCode: string; roleNames?: string[] }>(payload: T): T {
  return {
    ...payload,
    roleNames: [STORE_STAFF_ROLE],
    storeCode: payload.storeCode.trim(),
  };
}

export async function fetchStoreUsers(params: StoreUserGridParams): Promise<StoreUserListItem[]> {
  const response = await apiClient.post("/react/v1/store-users/grid", {
    storeCode: params.storeCode,
    keyword: params.keyword?.trim() || undefined,
  });

  return normalizeStoreUserList(response.data);
}

export async function fetchStoreUserDetail(userGuid: string, storeCode: string): Promise<StoreUserDetail> {
  const response = await apiClient.get("/react/v1/store-users/" + encodeURIComponent(userGuid), {
    params: { storeCode },
  });

  return normalizeStoreUserDetail(response.data);
}

export async function createStoreUser(payload: StoreUserCreatePayload): Promise<StoreUserDetail> {
  const response = await apiClient.post("/react/v1/store-users", sanitizeMutationPayload(payload));

  return normalizeStoreUserDetail(response.data);
}

export async function updateStoreUser(payload: StoreUserUpdatePayload): Promise<StoreUserDetail> {
  const response = await apiClient.put(
    "/react/v1/store-users/" + encodeURIComponent(payload.userGuid),
    sanitizeMutationPayload(payload)
  );

  return normalizeStoreUserDetail(response.data);
}

export async function updateStoreUserStatus(payload: StoreUserStatusPayload): Promise<void> {
  await apiClient.put("/react/v1/store-users/" + encodeURIComponent(payload.userGuid) + "/status", {
    storeCode: payload.storeCode,
    status: payload.status,
  });
}

export async function resetStoreUserPassword(payload: StoreUserPasswordPayload): Promise<void> {
  await apiClient.put(
    "/react/v1/store-users/" + encodeURIComponent(payload.userGuid) + "/password",
    {
      storeCode: payload.storeCode,
      newPassword: payload.newPassword,
    }
  );
}
