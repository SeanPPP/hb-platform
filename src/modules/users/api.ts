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
