import type { PagedResult } from './api'

export interface ScheduledTaskLogItem {
  id: string
  taskType: string
  status: string
  trigger?: string
  triggeredBy?: string
  startedAt?: string
  completedAt?: string
  errorMessage?: string
  parameters?: Record<string, unknown> | string
  result?: Record<string, unknown> | string
}

export interface ScheduledTaskListQuery {
  taskType?: string
  status?: string
  triggeredBy?: string
  startDate?: string
  endDate?: string
  pageNumber?: number
  pageSize?: number
  sortBy?: string
  sortDirection?: string
}

export interface RetryByTypeRequest {
  taskType?: string
  startDate?: string
  endDate?: string
}

export type ScheduledTaskListResult = PagedResult<ScheduledTaskLogItem>
