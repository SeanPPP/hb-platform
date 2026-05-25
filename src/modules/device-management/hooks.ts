import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  activateDevice,
  disableDevice,
  fetchDeviceManagementDevices,
  lockDevice,
} from "@/modules/device-management/api";
import type {
  DeviceManagementActionPayload,
  DeviceManagementListViewResult,
  DeviceManagementQuery,
} from "@/modules/device-management/types";

export function deviceManagementQueryKey(query: DeviceManagementQuery = {}) {
  return ["deviceManagement", query] as const;
}

export function useDeviceManagementDevices(query: DeviceManagementQuery = {}) {
  return useQuery({
    queryKey: deviceManagementQueryKey(query),
    queryFn: () => fetchDeviceManagementDevices(query),
  });
}

export function useDeviceManagementList(query: DeviceManagementQuery = {}, enabled = true) {
  return useQuery({
    queryKey: deviceManagementQueryKey(query),
    enabled,
    queryFn: async (): Promise<DeviceManagementListViewResult> => {
      const result = await fetchDeviceManagementDevices(query);
      return {
        items: result.devices,
        total: result.pagination.totalCount,
        pageNumber: result.pagination.pageNumber,
        pageSize: result.pagination.pageSize,
        totalPages: result.pagination.totalPages,
      };
    },
  });
}

export function useDeviceManagementMutations(_query?: DeviceManagementQuery) {
  const queryClient = useQueryClient();
  const invalidateDeviceManagement = () =>
    queryClient.invalidateQueries({ queryKey: ["deviceManagement"] });

  const activateMutation = useMutation({
    mutationFn: ({ id }: DeviceManagementActionPayload) => activateDevice(id),
    onSuccess: invalidateDeviceManagement,
  });

  const disableMutation = useMutation({
    mutationFn: ({ id }: DeviceManagementActionPayload) => disableDevice(id),
    onSuccess: invalidateDeviceManagement,
  });

  const lockMutation = useMutation({
    mutationFn: ({ id }: DeviceManagementActionPayload) => lockDevice(id),
    onSuccess: invalidateDeviceManagement,
  });

  return {
    activateMutation,
    disableMutation,
    lockMutation,
  };
}
