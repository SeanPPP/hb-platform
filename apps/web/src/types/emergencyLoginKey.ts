export type EmergencyLoginKeyStatus = 'Staged' | 'Active' | 'Retiring' | 'Retired' | string

export interface EmergencyLoginKey {
  keyId: string
  status: EmergencyLoginKeyStatus
  publicKeyFingerprint: string
  createdAtUtc: string | null
  createdBy: string
  createdReason: string
  activatedAtUtc?: string | null
  activatedBy?: string | null
  retiredAtUtc?: string | null
  retiredBy?: string | null
}

export interface EmergencyLoginKeyCoverage {
  totalDevices: number
  acknowledgedDevices: number
  percentage: number
}

export interface EmergencyLoginKeyMissingDevice {
  deviceRegistrationId: number
  storeCode: string | null
  deviceNumber: string
  hardwareId: string
  lastOnlineAtUtc: string | null
  lastSyncAtUtc: string | null
}

export interface EmergencyLoginKeyList {
  activeKeyId: string | null
  coverageKeyId: string | null
  version: number
  dataProtectionHealthy: boolean
  dataProtectionStatus: string
  keys: EmergencyLoginKey[]
  coverage: EmergencyLoginKeyCoverage
  missingDevices: EmergencyLoginKeyMissingDevice[]
}

export interface EmergencyLoginKeyMutationResult {
  version: number
  activeKeyId: string | null
  key: EmergencyLoginKey
}

export interface GenerateEmergencyLoginKeyRequest {
  reason: string
  expectedVersion: number
}

export interface ActivateEmergencyLoginKeyRequest extends GenerateEmergencyLoginKeyRequest {
  force: boolean
}

export type RetireEmergencyLoginKeyRequest = GenerateEmergencyLoginKeyRequest
