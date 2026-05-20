import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  createStoreUser,
  fetchStoreUserDetail,
  fetchStoreUsers,
  resetStoreUserPassword,
  updateStoreUser,
  updateStoreUserStatus,
} from "@/modules/users/api";
import type {
  StoreUserCreatePayload,
  StoreUserPasswordPayload,
  StoreUserStatusPayload,
  StoreUserUpdatePayload,
} from "@/modules/users/types";

export function storeUsersQueryKey(storeCode?: string | null, keyword?: string) {
  return ["storeUsers", storeCode ?? "", keyword?.trim() ?? ""] as const;
}

export function useStoreUsers(storeCode?: string | null, keyword?: string) {
  return useQuery({
    queryKey: storeUsersQueryKey(storeCode, keyword),
    enabled: Boolean(storeCode),
    queryFn: () => fetchStoreUsers({ storeCode: storeCode!, keyword }),
  });
}

export function useStoreUserDetail(userGuid?: string | null, storeCode?: string | null) {
  return useQuery({
    queryKey: ["storeUserDetail", storeCode ?? "", userGuid ?? ""],
    enabled: Boolean(userGuid && storeCode),
    queryFn: () => fetchStoreUserDetail(userGuid!, storeCode!),
  });
}

export function useStoreUserMutations(storeCode?: string | null, keyword?: string) {
  const queryClient = useQueryClient();

  const invalidateUsers = async () => {
    if (!storeCode) {
      return;
    }

    await Promise.all([
      queryClient.invalidateQueries({ queryKey: storeUsersQueryKey(storeCode, keyword) }),
      queryClient.invalidateQueries({ queryKey: ["storeUsers", storeCode] }),
    ]);
  };

  const createMutation = useMutation({
    mutationFn: (payload: StoreUserCreatePayload) => createStoreUser(payload),
    onSuccess: invalidateUsers,
  });

  const updateMutation = useMutation({
    mutationFn: (payload: StoreUserUpdatePayload) => updateStoreUser(payload),
    onSuccess: async (_, variables) => {
      await Promise.all([
        invalidateUsers(),
        queryClient.invalidateQueries({
          queryKey: ["storeUserDetail", variables.storeCode, variables.userGuid],
        }),
      ]);
    },
  });

  const statusMutation = useMutation({
    mutationFn: (payload: StoreUserStatusPayload) => updateStoreUserStatus(payload),
    onSuccess: async (_, variables) => {
      await Promise.all([
        invalidateUsers(),
        queryClient.invalidateQueries({
          queryKey: ["storeUserDetail", variables.storeCode, variables.userGuid],
        }),
      ]);
    },
  });

  const passwordMutation = useMutation({
    mutationFn: (payload: StoreUserPasswordPayload) => resetStoreUserPassword(payload),
  });

  return {
    createMutation,
    updateMutation,
    statusMutation,
    passwordMutation,
  };
}

