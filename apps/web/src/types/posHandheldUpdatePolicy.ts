export type PosHandheldPlatform = 'android' | 'ios'
export type PosHandheldReleaseKind = 'native' | 'ota'
export type PosHandheldPolicyLane =
  | 'android-native'
  | 'ios-native'
  | 'android-ota'
  | 'ios-ota'

export interface PosHandheldReleaseCandidate {
  id: string
  lane: PosHandheldPolicyLane
  platform: PosHandheldPlatform
  kind: PosHandheldReleaseKind
  version: string | null
  buildNumber: string | null
  runtimeVersion: string | null
  channel: string | null
  clientChannel?: string | null
  releaseChannel?: string | null
  releaseBatchId?: string | null
  updateId: string | null
  updateGroupId: string | null
  message?: string | null
  gitCommitHash?: string | null
  dashboardUrl?: string | null
  factFingerprint?: string | null
  legacy?: boolean
  isRollback?: boolean
  rollbackOfReleaseId?: string | null
  registrationSource?: string | null
  downloadUrl: string | null
  appStoreUrl: string | null
  artifactSha256: string | null
  createdAt: string
  createdBy: string | null
  activatable: boolean
  blockedReason: string | null
}

export interface PosHandheldUpdatePolicy {
  id: string | null
  lane: PosHandheldPolicyLane
  managed: boolean
  enabled: boolean
  required: boolean
  policyVersion: number
  candidateId: string | null
  candidateValid: boolean
  blockedReason: string | null
  candidate: PosHandheldReleaseCandidate | null
  minimumSupportedVersion: string | null
  minimumSupportedBuildNumber: number | null
  releaseMessage: string | null
  updatedAt: string | null
  updatedBy: string | null
}

export interface PosHandheldUpdatePolicyRequest {
  expectedPolicyVersion: number
  enabled: boolean
  required: boolean
  candidateId: string | null
  minimumSupportedVersion: string | null
  minimumSupportedBuildNumber: number | null
  releaseMessage: string | null
}

export interface PosHandheldUpdatePolicyRevision {
  id: string
  lane: PosHandheldPolicyLane
  policyVersion: number
  operation: string
  snapshotJson: string
  createdAt: string
  createdBy: string | null
}
