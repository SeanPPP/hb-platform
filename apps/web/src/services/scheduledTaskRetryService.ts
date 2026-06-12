import type { ApiResponse, PagedResult } from '../types/api'
import type {
  RetryByTypeRequest,
  ScheduledTaskListQuery,
  ScheduledTaskLogItem,
} from '../types/scheduledTaskRetry'
import request, { unwrapApiData, unwrapPagedResult } from '../utils/request'

const API_BASE = '/api/ScheduledTaskRetry'

type ScheduledTaskApiItem = Record<string, unknown>

function readString(value: unknown) {
  return typeof value === 'string' && value.trim() ? value : undefined
}

function readParameters(value: unknown) {
  if (!value) {
    return undefined
  }
  if (typeof value === 'object') {
    return value as Record<string, unknown>
  }
  if (typeof value !== 'string' || !value.trim()) {
    return undefined
  }

  try {
    const parsed = JSON.parse(value) as unknown
    return parsed && typeof parsed === 'object'
      ? parsed as Record<string, unknown>
      : value
  } catch {
    return value
  }
}

function transformTask(raw: ScheduledTaskLogItem | ScheduledTaskApiItem): ScheduledTaskLogItem {
  const item = raw as ScheduledTaskApiItem
  const parameters = item.parameters ?? item.Parameters ?? item.taskParameters ?? item.TaskParameters

  return {
    id: readString(item.id ?? item.Id) ?? '',
    taskType: readString(item.taskType ?? item.TaskType) ?? '',
    status: readString(item.status ?? item.Status) ?? '',
    trigger: readString(item.trigger ?? item.Trigger),
    triggeredBy: readString(item.triggeredBy ?? item.TriggeredBy),
    startedAt: readString(item.startedAt ?? item.StartedAt),
    completedAt: readString(item.completedAt ?? item.CompletedAt),
    errorMessage: readString(item.errorMessage ?? item.ErrorMessage),
    parameters: readParameters(parameters),
    result: item.result && typeof item.result === 'object'
      ? item.result as Record<string, unknown>
      : readString(item.result ?? item.Result),
  }
}

function normalizeTaskList(
  payload: ApiResponse<PagedResult<ScheduledTaskApiItem>> | PagedResult<ScheduledTaskApiItem>,
) {
  const result = unwrapPagedResult(payload)
  return {
    ...result,
    items: result.items.map(transformTask),
  }
}

export async function getScheduledTaskList(query: ScheduledTaskListQuery = {}) {
  const response = await request.get<ApiResponse<PagedResult<ScheduledTaskApiItem>>>(
    `${API_BASE}/list`,
    { params: query as Record<string, unknown> },
  )
  return normalizeTaskList(response)
}

export async function getScheduledTaskDetail(id: string) {
  const response = await request.get<ApiResponse<ScheduledTaskApiItem>>(`${API_BASE}/${id}`)
  return transformTask(unwrapApiData(response))
}

export async function retryScheduledTask(id: string) {
  const response = await request.post<ApiResponse<unknown>>(`${API_BASE}/${id}`, {})
  return response
}

export async function retryFailedScheduledTasksByType(payload: RetryByTypeRequest) {
  const response = await request.post<ApiResponse<unknown>>(`${API_BASE}/retry-by-type`, payload)
  return response
}
