import type {
  AppUpdateTargetScope,
  NativeUpdatePolicyRequest,
  PosIpadNativeUpdatePolicyRequest,
  PosIpadOtaRolloutRequest,
} from '../../../types/appUpdatePolicy'

export interface NativeUpdatePolicyFormValue {
  enabled: boolean
  releaseId?: string | null
  minimumSupportedVersion?: string | null
  minimumSupportedBuildNumber?: number | null
  releaseMessage?: string | null
  targetScope?: AppUpdateTargetScope
  targetStoreGuids?: string[]
}

export interface OtaRolloutFormValue {
  enabled: boolean
  releaseId?: string | null
  forceUpdate?: boolean
  targetScope?: AppUpdateTargetScope
  targetStoreGuids?: string[]
  releaseMessage?: string | null
}

export interface AppStoreReleaseRegistrationFormValue {
  appStoreId: string
  buildNumber: string
  storefront: string
}

export interface AppStoreReleaseRegistrationSummary {
  appStoreId: string
  buildNumber: string
  storefront: string
}

export interface AppUpdatePolicyConfirmationSummary {
  releaseId: string | null
  targetScope: AppUpdateTargetScope
  targetStoreGuids: string[]
  updateMode: 'optional' | 'required'
  minimumSupportedVersion: string | null
  minimumSupportedBuildNumber: number | null
}

const INT32_MAX_VALUE = 2_147_483_647
const POLICY_VERSION_ERROR_CODES = new Set([
  'APP_UPDATE_POLICY_VERSION_REQUIRED',
  'APP_UPDATE_POLICY_VERSION_CONFLICT',
])

function normalizeText(value?: string | null) {
  const normalized = value?.trim()
  return normalized || null
}

function normalizeStoreGuids(values?: string[]) {
  const seen = new Set<string>()
  return (values ?? []).reduce<string[]>((result, value) => {
    const normalized = value.trim()
    if (normalized && !seen.has(normalized)) {
      seen.add(normalized)
      result.push(normalized)
    }
    return result
  }, [])
}

function normalizeMinimumSupportedBuildNumber(value?: number | null) {
  return Number.isInteger(value)
    && Number(value) >= 0
    && Number(value) <= INT32_MAX_VALUE
    ? Number(value)
    : null
}

function readPolicyMutationErrorCode(value: unknown): string | null {
  if (!value || typeof value !== 'object') {
    return null
  }

  const raw = value as Record<string, unknown>
  const code = raw.errorCode ?? raw.code
  if (typeof code === 'string') {
    return code
  }
  return readPolicyMutationErrorCode(raw.data)
}

export function isValidPosIpadBuildNumber(value?: string | null) {
  const normalized = value?.trim() ?? ''
  if (!/^\d+$/.test(normalized)) {
    return false
  }

  const parsed = Number(normalized)
  return Number.isInteger(parsed) && parsed >= 0 && parsed <= INT32_MAX_VALUE
}

export function validateMinimumSupportedBuildNumber(
  minimumSupportedVersion?: string | null,
  minimumSupportedBuildNumber?: number | null,
) {
  if (minimumSupportedBuildNumber === null || minimumSupportedBuildNumber === undefined) {
    return true
  }

  const normalizedBuildNumber = normalizeMinimumSupportedBuildNumber(
    minimumSupportedBuildNumber,
  )
  return normalizedBuildNumber !== null && normalizeText(minimumSupportedVersion) !== null
}

export function isAppUpdatePolicyVersionConflict(error: unknown) {
  if (!error || typeof error !== 'object') {
    return false
  }

  const candidate = error as { status?: unknown; payload?: unknown }
  return candidate.status === 409
    && POLICY_VERSION_ERROR_CODES.has(readPolicyMutationErrorCode(candidate.payload) ?? '')
}

export function buildAppStoreReleaseRegistrationSummary(
  value: AppStoreReleaseRegistrationFormValue,
): AppStoreReleaseRegistrationSummary {
  return {
    appStoreId: value.appStoreId.trim(),
    buildNumber: value.buildNumber.trim(),
    storefront: value.storefront.trim().toLowerCase(),
  }
}

export function validateTargetStores(
  targetScope: AppUpdateTargetScope,
  targetStoreGuids?: string[],
) {
  return targetScope === 'all' || normalizeStoreGuids(targetStoreGuids).length > 0
}

export function buildNativeUpdatePolicyRequest(
  value: NativeUpdatePolicyFormValue,
  targeted: false,
  expectedPolicyVersion: number,
): NativeUpdatePolicyRequest
export function buildNativeUpdatePolicyRequest(
  value: NativeUpdatePolicyFormValue,
  targeted: true,
  expectedPolicyVersion: number,
): PosIpadNativeUpdatePolicyRequest
export function buildNativeUpdatePolicyRequest(
  value: NativeUpdatePolicyFormValue,
  targeted: boolean,
  expectedPolicyVersion: number,
): NativeUpdatePolicyRequest | PosIpadNativeUpdatePolicyRequest {
  if (!value.enabled) {
    return targeted
      ? {
          expectedPolicyVersion,
          enabled: false,
          releaseId: null,
          minimumSupportedVersion: null,
          minimumSupportedBuildNumber: null,
          releaseMessage: null,
          targetScope: 'all',
          targetStoreGuids: [],
        }
      : {
          expectedPolicyVersion,
          enabled: false,
          releaseId: null,
          minimumSupportedVersion: null,
          releaseMessage: null,
        }
  }

  const base: NativeUpdatePolicyRequest = {
    expectedPolicyVersion,
    enabled: true,
    releaseId: normalizeText(value.releaseId),
    minimumSupportedVersion: normalizeText(value.minimumSupportedVersion),
    releaseMessage: normalizeText(value.releaseMessage),
  }
  if (!targeted) {
    return base
  }

  const targetScope = value.targetScope === 'stores' ? 'stores' : 'all'
  return {
    ...base,
    minimumSupportedBuildNumber: base.minimumSupportedVersion
      ? normalizeMinimumSupportedBuildNumber(value.minimumSupportedBuildNumber)
      : null,
    targetScope,
    targetStoreGuids: targetScope === 'stores'
      ? normalizeStoreGuids(value.targetStoreGuids)
      : [],
  }
}

export function buildOtaRolloutRequest(
  value: OtaRolloutFormValue,
  expectedPolicyVersion: number,
): PosIpadOtaRolloutRequest {
  if (!value.enabled) {
    return {
      expectedPolicyVersion,
      enabled: false,
      releaseId: null,
      forceUpdate: false,
      targetScope: 'all',
      targetStoreGuids: [],
      releaseMessage: null,
    }
  }

  const targetScope = value.targetScope === 'stores' ? 'stores' : 'all'
  return {
    expectedPolicyVersion,
    enabled: true,
    releaseId: normalizeText(value.releaseId),
    forceUpdate: Boolean(value.forceUpdate),
    targetScope,
    targetStoreGuids: targetScope === 'stores'
      ? normalizeStoreGuids(value.targetStoreGuids)
      : [],
    releaseMessage: normalizeText(value.releaseMessage),
  }
}

export function buildNativePolicyConfirmationSummary(
  value: NativeUpdatePolicyFormValue,
  targeted: boolean,
): AppUpdatePolicyConfirmationSummary {
  if (targeted) {
    const request = buildNativeUpdatePolicyRequest(value, true, 0)
    return {
      releaseId: request.releaseId,
      targetScope: request.targetScope,
      targetStoreGuids: request.targetStoreGuids,
      updateMode: request.minimumSupportedVersion ? 'required' : 'optional',
      minimumSupportedVersion: request.minimumSupportedVersion,
      minimumSupportedBuildNumber: request.minimumSupportedBuildNumber,
    }
  }

  const request = buildNativeUpdatePolicyRequest(value, false, 0)
  return {
    releaseId: request.releaseId,
    targetScope: 'all',
    targetStoreGuids: [],
    updateMode: request.minimumSupportedVersion ? 'required' : 'optional',
    minimumSupportedVersion: request.minimumSupportedVersion,
    minimumSupportedBuildNumber: null,
  }
}

export function buildOtaPolicyConfirmationSummary(
  value: OtaRolloutFormValue,
): AppUpdatePolicyConfirmationSummary {
  const request = buildOtaRolloutRequest(value, 0)
  return {
    releaseId: request.releaseId,
    targetScope: request.targetScope,
    targetStoreGuids: request.targetStoreGuids,
    updateMode: request.forceUpdate ? 'required' : 'optional',
    minimumSupportedVersion: null,
    minimumSupportedBuildNumber: null,
  }
}

export function resolveNativeReleaseStatus(
  releaseId: string,
  activeReleaseId: string | null | undefined,
  policyEnabled: boolean,
): 'active' | 'verified' {
  return policyEnabled && releaseId === activeReleaseId ? 'active' : 'verified'
}

export function resolveOtaReleaseStatus(
  releaseId: string,
  activeReleaseId: string | null | undefined,
  rolloutEnabled: boolean,
): 'active' | 'registered' {
  return rolloutEnabled && releaseId === activeReleaseId ? 'active' : 'registered'
}
