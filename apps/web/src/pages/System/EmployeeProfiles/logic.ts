import { RequestError } from '../../../utils/request'
import type { EmployeeProfileSensitiveChangeSummaryDto } from '../../../types/employeeProfile'
import type { EmployeeProfileSensitiveField } from '../../../types/employeeProfile'

export const SENSITIVE_PROFILE_FIELDS = [
  'bankBsb',
  'bankAccountNumber',
  'superannuationCompanyName',
  'superannuationCompanyCode',
  'superannuationAccountNumber',
  'identityType',
  'identityId',
  'identityPhotoUrl',
] as const

export type SensitiveProfileField = EmployeeProfileSensitiveField

export type SensitiveProfileSnapshot = Partial<Record<SensitiveProfileField, unknown>>

function normalizeSensitiveValue(value: unknown) {
  return typeof value === 'string' ? value.trim() : value ?? null
}

export function getChangedSensitiveFields<
  TCurrent extends SensitiveProfileSnapshot,
  TNext extends SensitiveProfileSnapshot,
>(
  current: TCurrent,
  next: TNext,
): SensitiveProfileField[] {
  return SENSITIVE_PROFILE_FIELDS.filter(
    (field) => normalizeSensitiveValue(current[field]) !== normalizeSensitiveValue(next[field]),
  )
}

export function maskSensitiveSummary(value?: string | null) {
  const normalized = value?.trim()
  if (!normalized) {
    return '--'
  }

  return `****${normalized.slice(-4)}`
}

export function isRejectReasonValid(reason?: string | null) {
  return Boolean(reason?.trim())
}

export function shouldConfirmPendingSupersede<
  TCurrent extends SensitiveProfileSnapshot,
  TNext extends SensitiveProfileSnapshot,
>(
  pendingRequest: Pick<EmployeeProfileSensitiveChangeSummaryDto, 'status'> | null | undefined,
  current: TCurrent,
  next: TNext,
) {
  return pendingRequest?.status === 'Pending' && getChangedSensitiveFields(current, next).length > 0
}

export function shouldConfirmAdminSensitiveSupersede<TNext extends SensitiveProfileSnapshot>(
  pendingRequest: Pick<EmployeeProfileSensitiveChangeSummaryDto, 'status'> | null | undefined,
  current: SensitiveProfileSnapshot & { identityPhotoUrlExpiresAt?: unknown },
  next: TNext,
) {
  // 有 expiresAt 代表正式证件照来自托管对象，后端会忽略直接提交的 URL；legacy URL 才参与 PUT 比较。
  const comparableCurrent = current.identityPhotoUrlExpiresAt
    ? { ...current, identityPhotoUrl: next.identityPhotoUrl }
    : current
  return shouldConfirmPendingSupersede(pendingRequest, comparableCurrent, next)
}

function readErrorCode(payload: unknown) {
  if (!payload || typeof payload !== 'object') {
    return undefined
  }

  const candidate = payload as { code?: unknown; errorCode?: unknown }
  return typeof candidate.errorCode === 'string'
    ? candidate.errorCode
    : typeof candidate.code === 'string'
      ? candidate.code
      : undefined
}

export function isSensitiveVersionConflict(error: unknown) {
  return error instanceof RequestError
    && error.status === 409
    && readErrorCode(error.payload) === 'EMPLOYEE_PROFILE_SENSITIVE_VERSION_CONFLICT'
}

export async function handleSensitiveReviewFailure(
  error: unknown,
  refreshDetail: () => Promise<unknown>,
  refreshList: () => Promise<unknown>,
) {
  if (!isSensitiveVersionConflict(error)) {
    return false
  }

  // 409 表示正式资料已变化，详情与列表必须一起刷新，避免继续审核过期快照。
  await Promise.all([refreshDetail(), refreshList()])
  return true
}
