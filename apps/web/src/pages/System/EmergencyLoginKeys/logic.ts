import type { EmergencyLoginKey, EmergencyLoginKeyStatus } from '../../../types/emergencyLoginKey'
import { RequestError } from '../../../utils/request'

export interface EmergencyLoginKeyActionState {
  canActivate: boolean
  canForceActivate: boolean
  canDiscard: boolean
  canRetire: boolean
}

export function getShortEmergencyLoginKeyFingerprint(fingerprint: string) {
  const normalized = fingerprint.trim()
  return normalized.length > 16
    ? `${normalized.slice(0, 8)}...${normalized.slice(-6)}`
    : normalized || '-'
}

export function getEmergencyLoginKeyActionState(
  status: EmergencyLoginKeyStatus,
  coverageComplete: boolean,
): EmergencyLoginKeyActionState {
  const staged = status === 'Staged'
  return {
    // 关键状态转换：设备未全部确认时禁止普通激活，只开放显式强制激活流程。
    canActivate: staged && coverageComplete,
    canForceActivate: staged && !coverageComplete,
    canDiscard: staged,
    // Retiring 是否可最终退役仍由后端按有效授权状态做最终裁决。
    canRetire: status === 'Retiring',
  }
}

export function getLatestEmergencyLoginKeyOperator(key: EmergencyLoginKey) {
  return key.retiredBy || key.activatedBy || key.createdBy || '-'
}

function getEmergencyLoginKeyErrorCode(payload: unknown) {
  if (!payload || typeof payload !== 'object') {
    return undefined
  }
  const raw = payload as Record<string, unknown>
  const code = raw.code ?? raw.errorCode
  return typeof code === 'string' ? code : undefined
}

export function isEmergencyLoginKeyVersionConflict(error: unknown) {
  return error instanceof RequestError
    && (error.status === 409
      || getEmergencyLoginKeyErrorCode(error.payload) === 'EMERGENCY_KEY_VERSION_CONFLICT')
}

export function resolveEmergencyLoginKeyErrorMessage(
  error: unknown,
  conflictMessage: string,
  fallback: string,
  localizedMessages: Record<string, string> = {},
) {
  if (isEmergencyLoginKeyVersionConflict(error)) {
    return conflictMessage
  }
  if (error instanceof RequestError) {
    const code = getEmergencyLoginKeyErrorCode(error.payload)
    if (code && localizedMessages[code]) {
      return localizedMessages[code]
    }
  }
  // 后端文案及错误码不直接透传到界面，避免语言混用和内部实现细节泄漏。
  return fallback
}

export function getEmergencyLoginKeyDataProtectionStatusKey(status: string) {
  switch (status) {
    case 'Healthy':
    case 'StoredKeyDecryptFailed':
    case 'RoundTripFailed':
    case 'Unavailable':
      return status
    default:
      return 'Unknown'
  }
}

export function getEmergencyLoginKeyConflictRefreshFeedback(
  refreshSucceeded: boolean,
  refreshedMessage: string,
  refreshFailedMessage: string,
) {
  return refreshSucceeded
    ? { type: 'warning' as const, message: refreshedMessage }
    : { type: 'error' as const, message: refreshFailedMessage }
}
