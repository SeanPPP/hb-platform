import { RequestError } from '../../../utils/request'
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

export function createLatestRequestGuard() {
  let version = 0
  return {
    begin: () => {
      version += 1
      return version
    },
    isCurrent: (token: number) => token === version,
    invalidate: () => {
      version += 1
    },
  }
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

export function isPendingChangeConfirmationRequired(error: unknown) {
  return error instanceof RequestError
    && error.status === 409
    && readErrorCode(error.payload) === 'EMPLOYEE_PROFILE_PENDING_CHANGE_CONFIRMATION_REQUIRED'
}

export async function saveAdminProfileWithPendingConfirmation<
  TPayload extends object,
  TResult,
>(
  payload: TPayload,
  save: (
    nextPayload: TPayload & { confirmSupersedePendingSensitiveChangeRequest?: boolean }
  ) => Promise<TResult>,
  confirm: () => Promise<boolean>,
): Promise<{ status: 'saved'; data: TResult } | { status: 'cancelled' }> {
  try {
    return { status: 'saved', data: await save(payload) }
  } catch (error) {
    if (!isPendingChangeConfirmationRequired(error)) {
      throw error
    }
  }

  if (!await confirm()) {
    return { status: 'cancelled' }
  }

  // 确认标志只在服务端明确报告 Pending 冲突后发送，最终判断仍由事务内检查完成。
  const confirmedPayload = {
    ...payload,
    confirmSupersedePendingSensitiveChangeRequest: true,
  }
  return { status: 'saved', data: await save(confirmedPayload) }
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
