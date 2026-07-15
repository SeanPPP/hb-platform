import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  fetchStoreUserPosTerminalPermissions,
  restoreStoreUserPosTerminalPermissions,
  updateStoreUserPosTerminalPermissions,
} from "@/modules/users/pos-terminal-permissions-api";
import type {
  StoreUserPosTerminalPermissionTarget,
  UpdateStoreUserPosTerminalPermissionsPayload,
} from "@/modules/users/types";

export function storeUserPosTerminalPermissionsQueryKey(
  storeGuid?: string | null,
  userGuid?: string | null
) {
  return [
    "storeUserPosTerminalPermissions",
    storeGuid ?? "",
    userGuid ?? "",
  ] as const;
}

export function useStoreUserPosTerminalPermissions(
  userGuid?: string | null,
  storeGuid?: string | null,
  enabled = true
) {
  return useQuery({
    queryKey: storeUserPosTerminalPermissionsQueryKey(storeGuid, userGuid),
    enabled: Boolean(enabled && userGuid && storeGuid),
    queryFn: () =>
      fetchStoreUserPosTerminalPermissions(userGuid!, storeGuid!),
    retry: false,
  });
}

export function useUpdateStoreUserPosTerminalPermissions() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (payload: UpdateStoreUserPosTerminalPermissionsPayload) =>
      updateStoreUserPosTerminalPermissions(payload),
    onSuccess: (permissions, payload) => {
      // 直接采用服务端权威响应，避免额外刷新用户列表或权限详情。
      queryClient.setQueryData(
        storeUserPosTerminalPermissionsQueryKey(
          payload.storeGuid,
          payload.userGuid
        ),
        permissions
      );
    },
  });
}

export function useRestoreStoreUserPosTerminalPermissions() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ userGuid, storeGuid }: StoreUserPosTerminalPermissionTarget) =>
      restoreStoreUserPosTerminalPermissions(userGuid, storeGuid),
    onSuccess: (permissions, target) => {
      // 恢复继承后同样以服务端计算出的 effective 权限覆盖缓存。
      queryClient.setQueryData(
        storeUserPosTerminalPermissionsQueryKey(
          target.storeGuid,
          target.userGuid
        ),
        permissions
      );
    },
  });
}
