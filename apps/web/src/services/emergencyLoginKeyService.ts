import type { ApiResponse } from '../types/api'
import type {
  ActivateEmergencyLoginKeyRequest,
  EmergencyLoginKey,
  EmergencyLoginKeyList,
  EmergencyLoginKeyMissingDevice,
  EmergencyLoginKeyMutationResult,
  GenerateEmergencyLoginKeyRequest,
  RetireEmergencyLoginKeyRequest,
} from '../types/emergencyLoginKey'
import request, { unwrapApiData } from '../utils/request'

const API_BASE = '/api/react/v1/emergency-login-keys'

function asRecord(value: unknown): Record<string, unknown> {
  return value && typeof value === 'object' ? value as Record<string, unknown> : {}
}

function asString(value: unknown, fallback = '') {
  return typeof value === 'string' ? value : fallback
}

function asNullableString(value: unknown) {
  return typeof value === 'string' ? value : null
}

function asNumber(value: unknown, fallback = 0) {
  return typeof value === 'number' && Number.isFinite(value) ? value : fallback
}

function normalizeEmergencyLoginKey(value: unknown): EmergencyLoginKey {
  const raw = asRecord(value)

  // 安全边界：只白名单映射页面所需公钥元数据，任何私钥或密文私钥字段都不会进入前端状态。
  return {
    keyId: asString(raw.keyId),
    status: asString(raw.status),
    publicKeyFingerprint: asString(raw.publicKeyFingerprint),
    createdAtUtc: asNullableString(raw.createdAtUtc),
    createdBy: asString(raw.createdBy),
    createdReason: asString(raw.createdReason),
    activatedAtUtc: asNullableString(raw.activatedAtUtc),
    activatedBy: asNullableString(raw.activatedBy),
    retiredAtUtc: asNullableString(raw.retiredAtUtc),
    retiredBy: asNullableString(raw.retiredBy),
  }
}

function normalizeMissingDevice(value: unknown): EmergencyLoginKeyMissingDevice {
  const raw = asRecord(value)
  return {
    deviceRegistrationId: asNumber(raw.deviceRegistrationId),
    storeCode: asNullableString(raw.storeCode),
    deviceNumber: asString(raw.deviceNumber),
    hardwareId: asString(raw.hardwareId),
    lastOnlineAtUtc: asNullableString(raw.lastOnlineAtUtc),
    lastSyncAtUtc: asNullableString(raw.lastSyncAtUtc),
  }
}

function normalizeList(value: unknown): EmergencyLoginKeyList {
  const raw = asRecord(value)
  const coverage = asRecord(raw.coverage)
  return {
    activeKeyId: asNullableString(raw.activeKeyId),
    coverageKeyId: asNullableString(raw.coverageKeyId),
    version: asNumber(raw.version),
    dataProtectionHealthy: raw.dataProtectionHealthy === true,
    dataProtectionStatus: asString(raw.dataProtectionStatus),
    keys: Array.isArray(raw.keys) ? raw.keys.map(normalizeEmergencyLoginKey) : [],
    coverage: {
      totalDevices: asNumber(coverage.totalDevices),
      acknowledgedDevices: asNumber(coverage.acknowledgedDevices),
      percentage: asNumber(coverage.percentage),
    },
    missingDevices: Array.isArray(raw.missingDevices)
      ? raw.missingDevices.map(normalizeMissingDevice)
      : [],
  }
}

function normalizeMutation(value: unknown): EmergencyLoginKeyMutationResult {
  const raw = asRecord(value)
  return {
    version: asNumber(raw.version),
    activeKeyId: asNullableString(raw.activeKeyId),
    key: normalizeEmergencyLoginKey(raw.key),
  }
}

export async function getEmergencyLoginKeys() {
  const response = await request.get<ApiResponse<unknown> | unknown>(API_BASE)
  return normalizeList(unwrapApiData(response))
}

export async function generateEmergencyLoginKey(payload: GenerateEmergencyLoginKeyRequest) {
  const response = await request.post<ApiResponse<unknown> | unknown>(`${API_BASE}/generate`, payload)
  return normalizeMutation(unwrapApiData(response))
}

export async function activateEmergencyLoginKey(
  keyId: string,
  payload: ActivateEmergencyLoginKeyRequest,
) {
  const encodedKeyId = encodeURIComponent(keyId)
  const response = await request.post<ApiResponse<unknown> | unknown>(
    `${API_BASE}/${encodedKeyId}/activate`,
    payload,
  )
  return normalizeMutation(unwrapApiData(response))
}

export async function retireEmergencyLoginKey(
  keyId: string,
  payload: RetireEmergencyLoginKeyRequest,
) {
  const encodedKeyId = encodeURIComponent(keyId)
  const response = await request.post<ApiResponse<unknown> | unknown>(
    `${API_BASE}/${encodedKeyId}/retire`,
    payload,
  )
  return normalizeMutation(unwrapApiData(response))
}
