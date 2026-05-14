export interface DeviceRegistrationItem {
  id: number
  hardwareId: string
  systemDeviceNumber: string
  storeCode?: string | null
  deviceType: string
  deviceSystem: string
  status: number
  statusDescription: string
  createdAt?: string
  lastModified?: string | null
  createdBy?: string | null
  lastModifiedBy?: string | null
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
