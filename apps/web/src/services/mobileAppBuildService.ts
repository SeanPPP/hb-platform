import type { ApiResponse, PagedResult } from '../types/api'
import type { MobileAppBuild, MobileAppBuildPagedResult } from '../types/mobileAppBuild'
import request, { unwrapApiData } from '../utils/request'

const MOBILE_APP_BUILDS_API = '/api/mobile-app-builds'

function asRecord(value: unknown): Record<string, unknown> {
  return value && typeof value === 'object' ? (value as Record<string, unknown>) : {}
}

function getString(raw: Record<string, unknown>, key: string) {
  const value = raw[key]
  return typeof value === 'string' ? value : null
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
    buildDetailsPageUrl: getString(raw, 'buildDetailsPageUrl'),
    gitCommitHash: getString(raw, 'gitCommitHash'),
    gitCommitMessage: getString(raw, 'gitCommitMessage'),
    createdAt: getString(raw, 'createdAt'),
    completedAt: getString(raw, 'completedAt'),
    expirationDate: getString(raw, 'expirationDate'),
    receivedAt: getString(raw, 'receivedAt'),
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
