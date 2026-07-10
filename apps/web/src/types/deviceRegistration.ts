export interface DeviceRegistrationItem {
  id: number
  hardwareId: string
  systemDeviceNumber: string
  storeCode?: string | null
  storeName?: string | null
  deviceType: string
  deviceSystem: string
  status: number
  statusDescription: string
  remark?: string | null
  createdAt?: string
  lastModified?: string | null
  createdBy?: string | null
  lastModifiedBy?: string | null
  isOnline: boolean
  lastHeartbeatAt?: string | null
  currentCashierId?: string | null
  currentCashierName?: string | null
  cashierLoginAt?: string | null
}

export interface DeviceRegistrationDetail extends DeviceRegistrationItem {}

export interface UpdateDeviceRegistrationPayload {
  deviceType: string
  deviceSystem: string
  remark?: string | null
}

export interface UpdateDeviceRegistrationApiPayload {
  设备类型: string
  设备系统: string
  备注?: string | null
}

export interface DeviceRegistrationPagedResult {
  devices: DeviceRegistrationItem[]
  total: number
  page: number
  pageSize: number
  totalPages: number
}

export type AppDeviceOnlineState = 'all' | 'online' | 'offline'

export interface AppDeviceStatus {
  id: string
  hardwareId: string
  systemDeviceNumber?: string
  deviceSystem?: string
  platform?: string
  storeCode?: string
  appVersion?: string
  appBuildVersion?: string
  runtimeVersion?: string
  channel?: string
  updateId?: string
  updateSource?: string
  lastSeenAtUtc?: string
  isOnline: boolean
  lastAuthMode?: string
  lastSeenUserGuid?: string
  lastSeenUsername?: string
  lastSeenUserFullName?: string
  registeredDeviceId?: number
}

export interface AppDeviceStatusSummary {
  total: number
  online: number
  offline: number
  android: number
  ios: number
  unknownSystem: number
}

export interface AppDeviceStatusPagedResult {
  devices: AppDeviceStatus[]
  total: number
  page: number
  pageSize: number
  totalPages: number
}

export interface StoreOption {
  storeCode: string
  storeName: string
}
