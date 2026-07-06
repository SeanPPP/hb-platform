export interface DeviceRegistrationRequest {
  hardwareId: string
  deviceType: string
  deviceSystem: string
  storeCode?: string
}

export interface DeviceValidationRequest {
  hardwareId: string
  authCode: string
  systemDeviceNumber?: string
  deviceSystem?: string
  locationLatitude?: number
  locationLongitude?: number
  locationAccuracy?: number
  locationPermissionStatus?: string
  locationCapturedAtUtc?: string
}

export interface DeviceUnbindRequest {
  hardwareId: string
  authCode: string
}

export interface DeviceValidationResult {
  isValid: boolean
  newAuthCode?: string | null
  isDeviceSwitched?: boolean
  isCommonDevice?: boolean
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
  storeName?: string | null
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
