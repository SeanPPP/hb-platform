import type { ApiResponse } from '../types/api'
import type {
  PosHandheldPlatform,
  PosHandheldPolicyLane,
  PosHandheldReleaseCandidate,
  PosHandheldReleaseKind,
  PosHandheldUpdatePolicy,
  PosHandheldUpdatePolicyRequest,
  PosHandheldUpdatePolicyRevision,
} from '../types/posHandheldUpdatePolicy'
import request, { unwrapApiData } from '../utils/request'

type TransportOptions = {
  params?: Record<string, unknown>
  signal?: AbortSignal
}

export interface PosHandheldUpdatePolicyTransport {
  get(url: string, options?: TransportOptions): Promise<unknown>
  put(url: string, payload?: unknown, options?: TransportOptions): Promise<unknown>
}

const defaultTransport: PosHandheldUpdatePolicyTransport = {
  get: (url, options) => request.get(url, options),
  put: (url, payload, options) => request.put(url, payload, options),
}

function asRecord(value: unknown): Record<string, unknown> {
  return value && typeof value === 'object' && !Array.isArray(value)
    ? value as Record<string, unknown>
    : {}
}

function text(raw: Record<string, unknown>, key: string) {
  const value = raw[key]
  if (typeof value === 'string') {
    return value
  }
  if (typeof value === 'number' && Number.isFinite(value)) {
    return String(value)
  }
  return ''
}

function nullableText(raw: Record<string, unknown>, key: string) {
  const value = text(raw, key).trim()
  return value || null
}

function firstNullableText(raw: Record<string, unknown>, ...keys: string[]) {
  for (const key of keys) {
    const value = nullableText(raw, key)
    if (value) {
      return value
    }
  }
  return null
}

function number(raw: Record<string, unknown>, key: string) {
  const value = Number(raw[key])
  return Number.isFinite(value) ? value : 0
}

function nullableInt32(raw: Record<string, unknown>, key: string) {
  const value = raw[key]
  if (value === null || value === undefined || value === '') {
    return null
  }
  const parsed = Number(value)
  return Number.isInteger(parsed) && parsed >= 0 && parsed <= 2_147_483_647
    ? parsed
    : null
}

function platform(value: unknown): PosHandheldPlatform {
  return String(value).trim().toLowerCase() === 'ios' ? 'ios' : 'android'
}

function kind(value: unknown): PosHandheldReleaseKind {
  return String(value).trim().toLowerCase() === 'ota' ? 'ota' : 'native'
}

function lane(value: unknown, fallbackPlatform = 'android', fallbackKind = 'native'):
PosHandheldPolicyLane {
  const normalized = String(value).trim().toLowerCase()
  if (
    normalized === 'android-native'
    || normalized === 'ios-native'
    || normalized === 'android-ota'
    || normalized === 'ios-ota'
  ) {
    return normalized
  }
  return `${platform(fallbackPlatform)}-${kind(fallbackKind)}`
}

function boolean(raw: Record<string, unknown>, key: string) {
  return raw[key] === true
}

function unwrap<T>(payload: unknown) {
  return unwrapApiData(payload as ApiResponse<T> | T)
}

function payloadItems(payload: unknown, key: string) {
  if (Array.isArray(payload)) {
    return payload
  }
  const raw = asRecord(payload)
  return Array.isArray(raw[key]) ? raw[key] as unknown[] : []
}

export function normalizePosHandheldReleaseCandidate(
  value: unknown,
): PosHandheldReleaseCandidate {
  const raw = asRecord(value)
  const normalizedPlatform = platform(raw.platform)
  const normalizedKind = kind(raw.kind)
  const artifactUrl = firstNullableText(raw, 'downloadUrl', 'artifactUrl')
  const releaseChannel = firstNullableText(raw, 'releaseChannel', 'channel')
  return {
    id: text(raw, 'id'),
    lane: lane(raw.lane, normalizedPlatform, normalizedKind),
    platform: normalizedPlatform,
    kind: normalizedKind,
    version: nullableText(raw, 'version'),
    buildNumber: nullableText(raw, 'buildNumber'),
    runtimeVersion: nullableText(raw, 'runtimeVersion'),
    channel: releaseChannel,
    clientChannel: firstNullableText(raw, 'clientChannel'),
    releaseChannel,
    releaseBatchId: nullableText(raw, 'releaseBatchId'),
    updateId: nullableText(raw, 'updateId'),
    updateGroupId: nullableText(raw, 'updateGroupId'),
    message: firstNullableText(raw, 'message', 'releaseMessage'),
    gitCommitHash: nullableText(raw, 'gitCommitHash'),
    dashboardUrl: nullableText(raw, 'dashboardUrl'),
    factFingerprint: nullableText(raw, 'factFingerprint'),
    legacy: boolean(raw, 'legacy'),
    isRollback: boolean(raw, 'isRollback'),
    rollbackOfReleaseId: nullableText(raw, 'rollbackOfReleaseId'),
    registrationSource: nullableText(raw, 'registrationSource'),
    downloadUrl: artifactUrl,
    appStoreUrl: nullableText(raw, 'appStoreUrl')
      ?? (normalizedPlatform === 'ios' && normalizedKind === 'native'
        ? artifactUrl
        : null),
    artifactSha256: firstNullableText(raw, 'artifactSha256', 'sha256'),
    createdAt: firstNullableText(raw, 'createdAt', 'publishedAtUtc') ?? '',
    createdBy: firstNullableText(raw, 'createdBy', 'registeredBy'),
    activatable: boolean(raw, 'activatable') || boolean(raw, 'isActivatable'),
    blockedReason: nullableText(raw, 'blockedReason'),
  }
}

export function normalizePosHandheldUpdatePolicy(value: unknown): PosHandheldUpdatePolicy {
  const raw = asRecord(value)
  const normalizedLane = lane(raw.lane)
  return {
    id: nullableText(raw, 'id'),
    lane: normalizedLane,
    managed: boolean(raw, 'managed') || raw.id !== null && raw.id !== undefined,
    enabled: boolean(raw, 'enabled'),
    required: boolean(raw, 'required'),
    policyVersion: number(raw, 'policyVersion'),
    candidateId: nullableText(raw, 'candidateId'),
    candidateValid: boolean(raw, 'candidateValid'),
    blockedReason: nullableText(raw, 'blockedReason'),
    candidate: raw.candidate && typeof raw.candidate === 'object'
      ? normalizePosHandheldReleaseCandidate(raw.candidate)
      : null,
    minimumSupportedVersion: nullableText(raw, 'minimumSupportedVersion'),
    minimumSupportedBuildNumber: nullableInt32(raw, 'minimumSupportedBuildNumber'),
    releaseMessage: nullableText(raw, 'releaseMessage'),
    updatedAt: nullableText(raw, 'updatedAt'),
    updatedBy: nullableText(raw, 'updatedBy'),
  }
}

export function normalizePosHandheldUpdatePolicyRevision(
  value: unknown,
): PosHandheldUpdatePolicyRevision {
  const raw = asRecord(value)
  return {
    id: text(raw, 'id'),
    lane: lane(raw.lane),
    policyVersion: number(raw, 'policyVersion'),
    operation: firstNullableText(raw, 'operation', 'action') ?? '',
    snapshotJson: nullableText(raw, 'snapshotJson')
      ?? JSON.stringify(raw.snapshot ?? {}),
    createdAt: text(raw, 'createdAt'),
    createdBy: nullableText(raw, 'createdBy'),
  }
}

export function createPosHandheldUpdatePolicyService(
  transport: PosHandheldUpdatePolicyTransport,
) {
  const basePath = '/api/app-update-policies/pos-handheld'
  return {
    async getPolicies(signal?: AbortSignal) {
      const response = await transport.get(basePath, { signal })
      return payloadItems(unwrap(response), 'policies').map(normalizePosHandheldUpdatePolicy)
    },

    async getNativeCandidates(targetPlatform: PosHandheldPlatform, signal?: AbortSignal) {
      const response = await transport.get(
        `${basePath}/candidates/native/${targetPlatform}`,
        { signal },
      )
      return payloadItems(unwrap(response), 'items').map(normalizePosHandheldReleaseCandidate)
    },

    async getOtaCandidates(targetPlatform: PosHandheldPlatform, signal?: AbortSignal) {
      const response = await transport.get(`${basePath}/candidates/ota`, {
        params: { platform: targetPlatform },
        signal,
      })
      return payloadItems(unwrap(response), 'items').map(normalizePosHandheldReleaseCandidate)
    },

    async savePolicy(
      targetLane: PosHandheldPolicyLane,
      payload: PosHandheldUpdatePolicyRequest,
      signal?: AbortSignal,
    ) {
      const response = await transport.put(`${basePath}/${targetLane}`, payload, { signal })
      return normalizePosHandheldUpdatePolicy(unwrap(response))
    },

    async getRevisions(targetLane: PosHandheldPolicyLane, signal?: AbortSignal) {
      const response = await transport.get(`${basePath}/revisions`, {
        params: { lane: targetLane },
        signal,
      })
      return payloadItems(unwrap(response), 'items')
        .map(normalizePosHandheldUpdatePolicyRevision)
    },
  }
}

export const posHandheldUpdatePolicyService = createPosHandheldUpdatePolicyService(
  defaultTransport,
)
