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
}

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
): NativeUpdatePolicyRequest
export function buildNativeUpdatePolicyRequest(
  value: NativeUpdatePolicyFormValue,
  targeted: true,
): PosIpadNativeUpdatePolicyRequest
export function buildNativeUpdatePolicyRequest(
  value: NativeUpdatePolicyFormValue,
  targeted: boolean,
): NativeUpdatePolicyRequest | PosIpadNativeUpdatePolicyRequest {
  if (!value.enabled) {
    return targeted
      ? {
          enabled: false,
          releaseId: null,
          minimumSupportedVersion: null,
          releaseMessage: null,
          targetScope: 'all',
          targetStoreGuids: [],
        }
      : {
          enabled: false,
          releaseId: null,
          minimumSupportedVersion: null,
          releaseMessage: null,
        }
  }

  const base: NativeUpdatePolicyRequest = {
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
    targetScope,
    targetStoreGuids: targetScope === 'stores'
      ? normalizeStoreGuids(value.targetStoreGuids)
      : [],
  }
}

export function buildOtaRolloutRequest(
  value: OtaRolloutFormValue,
): PosIpadOtaRolloutRequest {
  if (!value.enabled) {
    return {
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
    const request = buildNativeUpdatePolicyRequest(value, true)
    return {
      releaseId: request.releaseId,
      targetScope: request.targetScope,
      targetStoreGuids: request.targetStoreGuids,
      updateMode: request.minimumSupportedVersion ? 'required' : 'optional',
      minimumSupportedVersion: request.minimumSupportedVersion,
    }
  }

  const request = buildNativeUpdatePolicyRequest(value, false)
  return {
    releaseId: request.releaseId,
    targetScope: 'all',
    targetStoreGuids: [],
    updateMode: request.minimumSupportedVersion ? 'required' : 'optional',
    minimumSupportedVersion: request.minimumSupportedVersion,
  }
}

export function buildOtaPolicyConfirmationSummary(
  value: OtaRolloutFormValue,
): AppUpdatePolicyConfirmationSummary {
  const request = buildOtaRolloutRequest(value)
  return {
    releaseId: request.releaseId,
    targetScope: request.targetScope,
    targetStoreGuids: request.targetStoreGuids,
    updateMode: request.forceUpdate ? 'required' : 'optional',
    minimumSupportedVersion: null,
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
