export interface ApplicationLogIngestItem {
  level: string
  message: string
  timestampUtc: string
  projectCode: string
  environment: string
  sourceType: string
  serviceName?: string
  instanceId?: string
  category?: string
  eventId?: string
  traceId?: string
  requestPath?: string
  requestMethod?: string
  statusCode?: number
  userId?: string
  userName?: string
  clientIp?: string
  exceptionType?: string
  exceptionMessage?: string
  stackTrace?: string
  properties?: Record<string, unknown>
}

export interface ApplicationLogIngestRequest {
  logs: ApplicationLogIngestItem[]
}

export interface ApplicationLogIngestResult {
  acceptedCount: number
  rejectedCount: number
}

export interface ApplicationLogQueryParams {
  projectCode?: string
  projectCodes?: string[]
  environment?: string
  sourceType?: string
  level?: string
  category?: string
  requestPath?: string
  traceId?: string
  userId?: string
  userName?: string
  keyword?: string
  startUtc?: string
  endUtc?: string
  pageNumber?: number
  pageSize?: number
  sortBy?: string
  sortDirection?: 'asc' | 'desc'
}

export interface ApplicationLogItem {
  id: string
  timestampUtc: string
  projectCode: string
  projectName?: string
  environment: string
  sourceType: string
  serviceName?: string
  level: string
  category?: string
  message: string
  exceptionType?: string
  exceptionMessage?: string
  stackTrace?: string
  requestPath?: string
  requestMethod?: string
  statusCode?: number
  traceId?: string
  userId?: string
  userName?: string
  clientIp?: string
  propertiesJson?: string
}

export interface ApplicationLogSummaryGroup {
  name: string
  count: number
}

export interface ApplicationLogSummary {
  total: number
  byProject: ApplicationLogSummaryGroup[]
  byLevel: ApplicationLogSummaryGroup[]
  byExceptionType: ApplicationLogSummaryGroup[]
  byRequestPath: ApplicationLogSummaryGroup[]
}
