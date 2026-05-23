export interface DeviceRegistrationRequest {
  hardwareId: string
  deviceType: string
  deviceSystem: string
  storeCode?: string
}

export interface DeviceValidationRequest {
  hardwareId: string
  authCode: string
}

export interface DeviceUnbindRequest {
  hardwareId: string
  authCode: string
}

export interface DeviceValidationResult {
  isValid: boolean
  newAuthCode?: string | null
}

export interface DeviceProfile {
  id: number
  hardwareId: string
  systemDeviceNumber: string
  authCode: string
  status: number
  statusDescription: string
  deviceType: string
  deviceSystem: string
  storeCode?: string | null
  resolvedFromExisting?: boolean
}

export interface PersistedDeviceSession {
  hardwareId: string
  authCode: string
  storeCode: string
  storeName?: string | null
  systemDeviceNumber?: string | null
  status?: number
  statusDescription?: string | null
  resolvedFromExisting?: boolean
}
