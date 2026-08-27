import type {
  AppOtaRelease,
  MobileOtaEnvironment,
  MobileOtaPlatform,
  MobileOtaPolicyRequest,
} from '../../../types/mobileOtaPolicy'

export interface MobileOtaPolicyFormValue {
  enabled: boolean
  required?: boolean
  targetReleaseId?: string | null
  releaseMessage?: string | null
}

function normalizeText(value?: string | null) {
  const normalized = value?.trim()
  return normalized || null
}

export function buildMobileOtaPolicyRequest(
  value: MobileOtaPolicyFormValue,
  expectedPolicyVersion: number,
): MobileOtaPolicyRequest {
  if (!value.enabled) {
    return {
      expectedPolicyVersion,
      enabled: false,
      required: false,
      targetReleaseId: null,
      releaseMessage: null,
    }
  }

  return {
    expectedPolicyVersion,
    enabled: true,
    required: Boolean(value.required),
    targetReleaseId: normalizeText(value.targetReleaseId),
    releaseMessage: normalizeText(value.releaseMessage),
  }
}

export function isMobileOtaReleaseCompatibleWithLane(
  release: AppOtaRelease,
  environment: MobileOtaEnvironment,
  platform: MobileOtaPlatform,
) {
  return release.appKey === 'mobile'
    && release.environment === environment
    && release.platform === platform
    && release.clientChannel === environment
}

export function formatMobileOtaReleaseLabel(release: AppOtaRelease) {
  return [
    release.runtimeVersion || '--',
    release.releaseChannel || '--',
    release.updateId ? release.updateId.slice(0, 8) : '--',
  ].join(' · ')
}

export function parseMobileOtaRevisionSnapshot(value: string) {
  try {
    const parsed = JSON.parse(value) as unknown
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed)
      ? parsed as Record<string, unknown>
      : {}
  } catch {
    // 审计快照属于展示辅助信息，旧数据损坏时保留时间线而不是让整条 lane 崩溃。
    return {}
  }
}
