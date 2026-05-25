import type { DeviceStatus } from "@/modules/device-management/status";

export interface DeviceManagementQuery {
  pageNumber?: number;
  pageSize?: number;
  keyword?: string;
  storeCode?: string | null;
  status?: DeviceStatus | number | null;
}

export interface DeviceManagementDevice {
  id: string;
  hardwareId: string;
  systemDeviceNumber?: string;
  deviceNumber?: string;
  deviceType?: string;
  deviceSystem?: string;
  deviceName?: string;
  storeCode?: string;
  storeName?: string;
  status: DeviceStatus;
  appVersion?: string;
  platform?: string;
  lastSeenAt?: string;
  createdAt?: string;
  updatedAt?: string;
}

export interface DeviceManagementListItem extends DeviceManagementDevice {}

export interface DeviceManagementPagination {
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface DeviceManagementListResult {
  devices: DeviceManagementDevice[];
  pagination: DeviceManagementPagination;
}

export interface DeviceManagementListViewResult {
  items: DeviceManagementListItem[];
  total: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
}

export interface DeviceManagementActionPayload {
  id: string | number;
  hardwareId?: string;
  systemDeviceNumber?: string;
  storeCode?: string;
}
