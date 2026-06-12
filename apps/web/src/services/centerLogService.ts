import type { ApiResponse, PagedResult } from '../types/api'
import type {
  ApplicationLogItem,
  ApplicationLogQueryParams,
  ApplicationLogSummary,
} from '../types/centerLog'
import request, { unwrapApiData } from '../utils/request'

function normalizePagedLogs(payload: ApiResponse<PagedResult<ApplicationLogItem>> | PagedResult<ApplicationLogItem>) {
  const result = unwrapApiData(payload)
  return {
    items: result.items ?? [],
    total: result.total ?? result.totalCount ?? 0,
    pageNumber: result.page ?? result.pageIndex ?? 1,
    pageSize: result.pageSize ?? 50,
  }
}

export async function getCenterLogs(params: ApplicationLogQueryParams) {
  const response = await request.get<ApiResponse<PagedResult<ApplicationLogItem>>>('/api/system/logs', {
    params: params as Record<string, unknown>,
  })
  return normalizePagedLogs(response)
}

export async function getCenterLogDetail(id: string) {
  const response = await request.get<ApiResponse<ApplicationLogItem>>(`/api/system/logs/${id}`)
  return unwrapApiData(response)
}

export async function getCenterLogSummary(params: ApplicationLogQueryParams) {
  const response = await request.get<ApiResponse<ApplicationLogSummary>>('/api/system/logs/summary', {
    params: params as Record<string, unknown>,
  })
  return unwrapApiData(response)
}
