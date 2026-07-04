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

export interface StoreOption {
  storeCode: string
  storeName: string
}
