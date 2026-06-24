import type { ApiResponse, PagedResult } from '../types/api'
import type {
  MobileAppBuild,
  MobileAppBuildPagedResult,
  MobileAppOtaRollbackCommand,
  MobileAppOtaUpdate,
  MobileAppOtaUpdatePagedResult,
} from '../types/mobileAppBuild'
import request, { unwrapApiData } from '../utils/request'

const MOBILE_APP_BUILDS_API = '/api/mobile-app-builds'

function asRecord(value: unknown): Record<string, unknown> {
  return value && typeof value === 'object' ? (value as Record<string, unknown>) : {}
}

function getString(raw: Record<string, unknown>, key: string) {
  const value = raw[key]
  return typeof value === 'string' ? value : null
}

function getBoolean(raw: Record<string, unknown>, key: string) {
  const value = raw[key]
  if (typeof value === 'boolean') {
    return value
  }
  if (typeof value === 'string') {
    return value.trim().toLowerCase() === 'true'
  }
  return false
}

function getNumber(raw: Record<string, unknown>, key: string) {
  const value = raw[key]
  if (typeof value === 'number' && Number.isFinite(value)) {
    return value
  }
  if (typeof value === 'string') {
    const parsed = Number(value)
    return Number.isFinite(parsed) ? parsed : null
  }
  return null
}

export function normalizeMobileAppBuild(raw: Record<string, unknown>): MobileAppBuild {
  return {
    id: getString(raw, 'id') ?? '',
    easBuildId: getString(raw, 'easBuildId'),
    appName: getString(raw, 'appName'),
    platform: getString(raw, 'platform'),
    status: getString(raw, 'status'),
    buildProfile: getString(raw, 'buildProfile'),
    distribution: getString(raw, 'distribution'),
    channel: getString(raw, 'channel'),
    runtimeVersion: getString(raw, 'runtimeVersion'),
    appVersion: getString(raw, 'appVersion'),
    appBuildVersion: getString(raw, 'appBuildVersion'),
    artifactUrl: getString(raw, 'artifactUrl'),
    originalArtifactUrl: getString(raw, 'originalArtifactUrl'),
    cosArtifactUrl: getString(raw, 'cosArtifactUrl'),
    cosObjectKey: getString(raw, 'cosObjectKey'),
    cosMirroredAt: getString(raw, 'cosMirroredAt'),
    cosMirrorError: getString(raw, 'cosMirrorError'),
    cosMirrorStatus: getString(raw, 'cosMirrorStatus'),
    cosMirrorAttempts: getNumber(raw, 'cosMirrorAttempts'),
    cosMirrorLastAttemptAtUtc: getString(raw, 'cosMirrorLastAttemptAtUtc'),
    buildDetailsPageUrl: getString(raw, 'buildDetailsPageUrl'),
    gitCommitHash: getString(raw, 'gitCommitHash'),
    gitCommitMessage: getString(raw, 'gitCommitMessage'),
    createdAt: getString(raw, 'createdAt'),
    completedAt: getString(raw, 'completedAt'),
    expirationDate: getString(raw, 'expirationDate'),
    receivedAt: getString(raw, 'receivedAt'),
  }
}

export function normalizeMobileAppOtaUpdate(raw: Record<string, unknown>): MobileAppOtaUpdate {
  return {
    id: getString(raw, 'id') ?? '',
    updateGroupId: getString(raw, 'updateGroupId'),
    androidUpdateId: getString(raw, 'androidUpdateId'),
    channel: getString(raw, 'channel'),
    branch: getString(raw, 'branch'),
    platform: getString(raw, 'platform'),
    runtimeVersion: getString(raw, 'runtimeVersion'),
    message: getString(raw, 'message'),
    gitCommitHash: getString(raw, 'gitCommitHash'),
    dashboardUrl: getString(raw, 'dashboardUrl'),
    publishedAt: getString(raw, 'publishedAt'),
    isRollback: getBoolean(raw, 'isRollback'),
    rollbackOfGroupId: getString(raw, 'rollbackOfGroupId'),
    createdAt: getString(raw, 'createdAt'),
    updatedAt: getString(raw, 'updatedAt'),
  }
}

export function normalizeMobileAppOtaRollbackCommand(
  raw: Record<string, unknown>,
): MobileAppOtaRollbackCommand {
  return {
    updateGroupId: getString(raw, 'updateGroupId') ?? '',
    command: getString(raw, 'command') ?? '',
    warning: getString(raw, 'warning'),
  }
}

export async function getLatestMobileAppBuild(profile = 'production') {
  const response = await request.get<ApiResponse<Record<string, unknown> | null> | Record<string, unknown> | null>(
    `${MOBILE_APP_BUILDS_API}/latest`,
    { params: { profile } },
  )
  const payload = unwrapApiData(response)
  return payload ? normalizeMobileAppBuild(asRecord(payload)) : null
}

export async function getMobileAppBuilds(params?: {
  page?: number
  pageSize?: number
  profile?: string
}): Promise<MobileAppBuildPagedResult> {
  const page = params?.page ?? 1
  const pageSize = params?.pageSize ?? 10
  const response = await request.get<
    | ApiResponse<PagedResult<Record<string, unknown>>>
    | PagedResult<Record<string, unknown>>
    | {
        data?: Record<string, unknown>[]
        list?: Record<string, unknown>[]
        items?: Record<string, unknown>[]
        total?: number
        totalCount?: number
        page?: number
        pageSize?: number
      }
  >(MOBILE_APP_BUILDS_API, {
    params: {
      page,
      pageSize,
      profile: params?.profile ?? 'production',
    },
  })

  const payload = unwrapApiData(response)
  const raw = asRecord(payload)
  // 后端分页字段仍在并行实现中，这里兼容 items/list/data 三种常见返回形态。
  const rawItems = Array.isArray(raw.items)
    ? raw.items
    : Array.isArray(raw.list)
      ? raw.list
      : Array.isArray(raw.data)
        ? raw.data
        : []

  return {
    items: rawItems.map((item) => normalizeMobileAppBuild(asRecord(item))),
    total: Number(raw.total ?? raw.totalCount ?? rawItems.length),
    page: Number(raw.page ?? page),
    pageSize: Number(raw.pageSize ?? pageSize),
  }
}

export async function getMobileAppOtaUpdates(params?: {
  channel?: string
  runtimeVersion?: string
  page?: number
  pageSize?: number
}): Promise<MobileAppOtaUpdatePagedResult> {
  const page = params?.page ?? 1
  const pageSize = params?.pageSize ?? 10
  const runtimeVersion = params?.runtimeVersion?.trim()
  const response = await request.get<
    | ApiResponse<PagedResult<Record<string, unknown>>>
    | PagedResult<Record<string, unknown>>
    | {
        data?: Record<string, unknown>[]
        list?: Record<string, unknown>[]
        items?: Record<string, unknown>[]
        total?: number
        totalCount?: number
        page?: number
        pageSize?: number
      }
  >(`${MOBILE_APP_BUILDS_API}/ota-updates`, {
    params: {
      channel: params?.channel ?? 'production',
      runtimeVersion: runtimeVersion || undefined,
      page,
      pageSize,
    },
  })

  const payload = unwrapApiData(response)
  const raw = asRecord(payload)
  // OTA 后端会返回标准 PagedResult；这里保留兼容分支，避免联调期字段名轻微差异导致空表。
  const rawItems = Array.isArray(raw.items)
    ? raw.items
    : Array.isArray(raw.list)
      ? raw.list
      : Array.isArray(raw.data)
        ? raw.data
        : []

  return {
    items: rawItems.map((item) => normalizeMobileAppOtaUpdate(asRecord(item))),
    total: Number(raw.total ?? raw.totalCount ?? rawItems.length),
    page: Number(raw.page ?? page),
    pageSize: Number(raw.pageSize ?? pageSize),
  }
}

export async function createMobileAppOtaRollbackCommand(
  updateGroupId: string,
  params?: { message?: string },
): Promise<MobileAppOtaRollbackCommand> {
  const normalizedGroupId = updateGroupId.trim()
  const message = params?.message?.trim()
  const response = await request.post<
    ApiResponse<Record<string, unknown>> | Record<string, unknown>
  >(
    `${MOBILE_APP_BUILDS_API}/ota-updates/${encodeURIComponent(normalizedGroupId)}/rollback-command`,
    message ? { message } : {},
  )
  return normalizeMobileAppOtaRollbackCommand(asRecord(unwrapApiData(response)))
}
