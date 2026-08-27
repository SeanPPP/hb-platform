import type { StoreOption } from './deviceRegistration'

export type DeviceActivationSystem = 'Windows' | 'iPadOS' | 'Android' | 'iOS'
export type DeviceActivationStatus = 'Available' | 'Expired' | 'Revoked' | 'Consumed'
export type DeviceActivationConsumptionKind = 'Initial' | 'Rebind'

export interface DeviceActivationCodeSummary {
  grantId: string
  storeCode: string
  storeName?: string | null
  deviceSystem: DeviceActivationSystem
  status: DeviceActivationStatus
  createdAtUtc: string
  createdBy: string
  reason: string
  expiresAtUtc: string
  revokedAtUtc?: string | null
  revokedBy?: string | null
  revokeReason?: string | null
  consumedAtUtc?: string | null
  consumedHardwareId?: string | null
  consumedDeviceCode?: string | null
  consumptionKind?: DeviceActivationConsumptionKind | null
  previousStoreCode?: string | null
  previousDeviceCode?: string | null
}

export interface DeviceActivationCodePagedResult {
  items: DeviceActivationCodeSummary[]
  total: number
  page: number
  pageSize: number
  totalPages: number
}

export interface DeviceActivationCodeCreatePayload {
  storeCode: string
  deviceSystem: DeviceActivationSystem
  validForMinutes: 30 | 120 | 1440
  reason: string
}

export interface DeviceActivationCodeCreateResponse {
  grant: DeviceActivationCodeSummary
  activationCode: string
}

export type DeviceActivationManageableStore = StoreOption
