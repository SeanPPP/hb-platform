import type { ApiResponse } from '../types/api'
import type {
  ScheduledTaskRuntimeControlStatus,
  ScheduledTaskRuntimeControlUpdate,
} from '../types/scheduledTaskRuntimeControl'
import request, { unwrapApiData } from '../utils/request'

const API_BASE = '/api/scheduled-task/runtime-control'

export async function getScheduledTaskRuntimeControl() {
  const response = await request.get<ApiResponse<ScheduledTaskRuntimeControlStatus>>(API_BASE)
  return unwrapApiData(response)
}

export async function updateScheduledTaskRuntimeControl(
  payload: ScheduledTaskRuntimeControlUpdate,
) {
  const response = await request.post<ApiResponse<ScheduledTaskRuntimeControlStatus>>(API_BASE, payload)
  return unwrapApiData(response)
}
