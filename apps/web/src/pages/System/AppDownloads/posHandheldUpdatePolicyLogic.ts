import type {
  PosHandheldPlatform,
  PosHandheldPolicyLane,
  PosHandheldReleaseCandidate,
  PosHandheldReleaseKind,
  PosHandheldUpdatePolicy,
  PosHandheldUpdatePolicyRequest,
} from '../../../types/posHandheldUpdatePolicy'

export interface PosHandheldPolicyFormValue {
  enabled: boolean
  required?: boolean
  candidateId?: string | null
  minimumSupportedVersion?: string | null
  minimumSupportedBuildNumber?: number | null
  releaseMessage?: string | null
}

export interface PosHandheldCandidateFilters {
  platform: PosHandheldPlatform | 'all'
  kind: PosHandheldReleaseKind | 'all'
  status: 'all' | 'activatable' | 'active' | 'blocked'
  keyword: string
}

export interface PosHandheldPolicyConfirmationSummary {
  lane: PosHandheldPolicyLane
  enabled: boolean
  updateMode: 'optional' | 'required'
  candidateId: string | null
  candidateLabel: string
  minimumSupportedVersion: string | null
  minimumSupportedBuildNumber: number | null
  releaseMessage: string | null
}

export type PosHandheldCandidateEffectiveStatus =
  | 'active'
  | 'activatable'
  | 'blocked'

export type PosHandheldPolicySelectionState =
  | 'ready'
  | 'refreshable'
  | 'blocked'

const INT32_MAX_VALUE = 2_147_483_647
const CANDIDATE_FINGERPRINT_MISMATCH =
  'POS_HANDHELD_UPDATE_CANDIDATE_FINGERPRINT_MISMATCH'

function normalizeText(value?: string | null) {
  const normalized = value?.trim()
  return normalized || null
}

function normalizeMinimumBuild(value?: number | null) {
  return Number.isInteger(value)
    && Number(value) > 0
    && Number(value) <= INT32_MAX_VALUE
    ? Number(value)
    : null
}

export function buildPosHandheldPolicyRequest(
  value: PosHandheldPolicyFormValue,
  lane: PosHandheldPolicyLane,
  expectedPolicyVersion: number,
): PosHandheldUpdatePolicyRequest {
  if (!value.enabled) {
    return {
      expectedPolicyVersion,
      enabled: false,
      required: false,
      candidateId: null,
      minimumSupportedVersion: null,
      minimumSupportedBuildNumber: null,
      releaseMessage: null,
    }
  }

  const native = lane.endsWith('-native')
  const minimumSupportedVersion = native
    ? normalizeText(value.minimumSupportedVersion)
    : null
  return {
    expectedPolicyVersion,
    enabled: true,
    required: Boolean(value.required),
    candidateId: normalizeText(value.candidateId),
    minimumSupportedVersion,
    minimumSupportedBuildNumber: native
      ? normalizeMinimumBuild(value.minimumSupportedBuildNumber)
      : null,
    releaseMessage: normalizeText(value.releaseMessage),
  }
}

export function getPosHandheldCandidateLabel(candidate: PosHandheldReleaseCandidate) {
  if (candidate.kind === 'native') {
    return `${candidate.version || '--'} (${candidate.buildNumber || '--'})`
  }
  return [
    candidate.runtimeVersion || '--',
    candidate.channel || '--',
    candidate.updateId || '--',
  ].join(' · ')
}

export function getPosHandheldCandidateKey(candidate: PosHandheldReleaseCandidate) {
  return `${candidate.lane}:${candidate.id}`
}

export function isPosHandheldPolicyCandidateActive(policy: PosHandheldUpdatePolicy) {
  return policy.enabled && Boolean(policy.candidateId) && policy.candidateValid
}

export function getPosHandheldPolicySelectionState(
  enabled: boolean,
  selectedCandidateId: string | null | undefined,
  policy: PosHandheldUpdatePolicy,
  selectedCandidate?: PosHandheldReleaseCandidate | null,
): PosHandheldPolicySelectionState {
  if (!enabled || !selectedCandidateId) {
    return 'ready'
  }

  if (selectedCandidateId !== policy.candidateId) {
    return selectedCandidate?.activatable ? 'ready' : 'blocked'
  }
  if (policy.candidateValid) {
    return 'ready'
  }

  // 仅候选事实的指纹发生变化、且当前事实仍可发布时，允许管理员复核后刷新快照。
  return policy.blockedReason === CANDIDATE_FINGERPRINT_MISMATCH
    && selectedCandidate?.activatable
    ? 'refreshable'
    : 'blocked'
}

export function mergePosHandheldPolicyCandidates(
  candidates: PosHandheldReleaseCandidate[],
  policies: PosHandheldUpdatePolicy[],
) {
  const merged = new Map(
    candidates.map((candidate) => [getPosHandheldCandidateKey(candidate), candidate]),
  )
  for (const policy of policies) {
    const candidate = policy.candidate
    if (
      candidate
      && candidate.id === policy.candidateId
      && candidate.lane === policy.lane
    ) {
      const key = getPosHandheldCandidateKey(candidate)
      if (!merged.has(key)) {
        merged.set(key, candidate)
      }
    }
  }
  return [...merged.values()]
}

export function getPosHandheldCandidateEffectiveStatus(
  candidate: PosHandheldReleaseCandidate,
  activeCandidateIds: ReadonlySet<string>,
  blockedCandidateIds: ReadonlySet<string>,
): PosHandheldCandidateEffectiveStatus {
  const key = getPosHandheldCandidateKey(candidate)
  if (blockedCandidateIds.has(key) || !candidate.activatable) {
    return 'blocked'
  }
  return activeCandidateIds.has(key) ? 'active' : 'activatable'
}

export function filterPosHandheldCandidates(
  candidates: PosHandheldReleaseCandidate[],
  filters: PosHandheldCandidateFilters,
  activeCandidateIds: ReadonlySet<string>,
  blockedCandidateIds: ReadonlySet<string> = new Set(),
) {
  const keyword = filters.keyword.trim().toLowerCase()
  return candidates.filter((candidate) => {
    if (filters.platform !== 'all' && candidate.platform !== filters.platform) {
      return false
    }
    if (filters.kind !== 'all' && candidate.kind !== filters.kind) {
      return false
    }
    const effectiveStatus = getPosHandheldCandidateEffectiveStatus(
      candidate,
      activeCandidateIds,
      blockedCandidateIds,
    )
    if (filters.status === 'activatable' && effectiveStatus === 'blocked') {
      return false
    }
    if (filters.status === 'blocked' && effectiveStatus !== 'blocked') {
      return false
    }
    if (filters.status === 'active' && effectiveStatus !== 'active') {
      return false
    }
    if (!keyword) {
      return true
    }
    return [
      candidate.id,
      candidate.version,
      candidate.buildNumber,
      candidate.runtimeVersion,
      candidate.channel,
      candidate.updateId,
      candidate.updateGroupId,
    ].some((value) => value?.toLowerCase().includes(keyword))
  })
}

export function buildPosHandheldPolicyConfirmationSummary(
  value: PosHandheldPolicyFormValue,
  lane: PosHandheldPolicyLane,
  candidate?: PosHandheldReleaseCandidate,
): PosHandheldPolicyConfirmationSummary {
  const request = buildPosHandheldPolicyRequest(value, lane, 0)
  return {
    lane,
    enabled: request.enabled,
    updateMode: request.required ? 'required' : 'optional',
    candidateId: request.candidateId,
    candidateLabel: candidate ? getPosHandheldCandidateLabel(candidate) : '--',
    minimumSupportedVersion: request.minimumSupportedVersion,
    minimumSupportedBuildNumber: request.minimumSupportedBuildNumber,
    releaseMessage: request.releaseMessage,
  }
}
