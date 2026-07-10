import type { DeviceStatus } from "@/modules/device-management/status";

export interface DeviceManagementQuery {
  pageNumber?: number;
  pageSize?: number;
  keyword?: string;
  storeCode?: string | null;
  status?: DeviceStatus | number | null;
  deviceSystem?: string | null;
  deviceType?: string | null;
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

export type AppDeviceOnlineState = "all" | "online" | "offline";

export interface AppDeviceStatusQuery {
  pageNumber?: number;
  pageSize?: number;
  keyword?: string;
  storeCode?: string | null;
  deviceSystem?: string | null;
  onlineState?: AppDeviceOnlineState | null;
}

export interface AppDeviceStatus {
  id: string;
  hardwareId: string;
  systemDeviceNumber?: string;
  deviceSystem?: string;
  platform?: string;
  storeCode?: string;
  appVersion?: string;
  appBuildVersion?: string;
  runtimeVersion?: string;
  channel?: string;
  updateId?: string;
  updateSource?: string;
  lastSeenAtUtc?: string;
  isOnline: boolean;
  lastAuthMode?: string;
  lastSeenUserGuid?: string;
  lastSeenUsername?: string;
  lastSeenUserFullName?: string;
  registeredDeviceId?: number;
}

export interface AppDeviceStatusSummary {
  total: number;
  online: number;
  offline: number;
  android: number;
  ios: number;
  unknownSystem: number;
}

export interface AppDeviceStatusListResult {
  devices: AppDeviceStatus[];
  pagination: DeviceManagementPagination;
}

export interface AppDeviceHeartbeatPayload {
  hardwareId: string;
  systemDeviceNumber?: string;
  deviceSystem?: string;
  platform?: string;
  storeCode?: string;
  appVersion?: string;
  appBuildVersion?: string;
  runtimeVersion?: string;
  channel?: string;
  updateId?: string;
  updateSource?: string;
}
