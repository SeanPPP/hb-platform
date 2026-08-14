import { isAxiosError } from "axios";
import { apiClient } from "@/shared/api/client";
import type {
  StoreUserCreatePayload,
  StoreUserDetail,
  StoreUserGridParams,
  StoreUserListItem,
  StoreUserPasswordPayload,
  StoreUserProfile,
  StoreUserStatusPayload,
  StoreUserUpdatePayload,
} from "@/modules/users/types";
import {
  normalizeStoreUserDetail,
  normalizeStoreUserList,
  normalizeStoreUserProfile,
} from "@/modules/users/profile-normalization";
import { STORE_STAFF_ROLE } from "@/modules/users/types";

function sanitizeMutationPayload<T extends { storeCode: string; roleNames?: string[] }>(payload: T): T {
  return {
    ...payload,
    roleNames: [STORE_STAFF_ROLE],
    storeCode: payload.storeCode.trim(),
  };
}

export interface SafeStoreUserErrorLogMetadata {
  name: string;
  code?: string;
  status?: number;
}

export function toSafeStoreUserErrorLog(error: unknown): SafeStoreUserErrorLogMetadata {
  if (!isAxiosError(error)) {
    return { name: error instanceof Error ? error.name : "UnknownError" };
  }

  return {
    name: "AxiosError",
    ...(typeof error.code === "string" ? { code: error.code } : {}),
    ...(typeof error.response?.status === "number" ? { status: error.response.status } : {}),
  };
}

export async function fetchStoreUsers(params: StoreUserGridParams): Promise<StoreUserListItem[]> {
  const response = await apiClient.post("/react/v1/store-users/grid", {
    storeCode: params.storeCode?.trim() || undefined,
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

export async function fetchStoreUserProfile(
  userGuid: string,
  storeCode: string
): Promise<StoreUserProfile> {
  const response = await apiClient.get(
    "/react/v1/store-users/" + encodeURIComponent(userGuid) + "/profile",
    {
      params: { storeCode },
    }
  );

  return normalizeStoreUserProfile(response.data);
}

export async function updateStoreUser(payload: StoreUserUpdatePayload): Promise<StoreUserDetail> {
  const response = await apiClient.put(
    "/react/v1/store-users/" + encodeURIComponent(payload.userGuid),
    sanitizeMutationPayload(payload)
  );

  return normalizeStoreUserDetail(response.data);
}

export async function createStoreUser(payload: StoreUserCreatePayload): Promise<StoreUserDetail> {
  // 创建入口只允许生成固定的店员账号，避免调用方意外提升角色或改变员工类型。
  const response = await apiClient.post("/react/v1/store-users", {
    ...sanitizeMutationPayload(payload),
    employmentType: "casual",
    passwordFormat: "raw",
  });

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
      passwordFormat: payload.passwordFormat,
    }
  );
}
