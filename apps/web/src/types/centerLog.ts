export interface ApplicationLogIngestItem {
  clientEventId?: string
  level: string
  message: string
  timestampUtc: string
  projectCode: string
  environment: string
  sourceType: string
  serviceName?: string
  instanceId?: string
  storeCode?: string
  deviceCode?: string
  appVersion?: string
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
  duplicateCount?: number
  results?: Array<{
    clientEventId?: string
    status: 'accepted' | 'duplicate' | 'rejected'
    errorCode?: string
  }>
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
  storeCode?: string
  deviceCode?: string
  appVersion?: string
  instanceId?: string
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
  clientEventId?: string
  eventId?: string
  storeCode?: string
  deviceCode?: string
  appVersion?: string
  instanceId?: string
  createdAtUtc: string
}

export interface ApplicationLogSummaryGroup {
  name: string
  count: number
}

export type ApplicationLogConfigurationState = 'Ready' | 'Disabled' | 'MissingCredential'

export interface ApplicationLogProjectStatus {
  projectCode: string
  displayName: string
  mode: string
  explicitlyConfigured: boolean
  enabled: boolean
  credentialConfigured: boolean | null
  configurationState: ApplicationLogConfigurationState
  effectiveRetentionDays: number
  lastReceivedAtUtc: string | null
}

export interface ApplicationLogStatus {
  backendCaptureEnabled: boolean
  backendMinimumLevel: string
  defaultProjectCode: string
  defaultEnvironment: string
  serviceName: string
  projects: ApplicationLogProjectStatus[]
}

export interface ApplicationLogPipelineStatus {
  droppedOldestCount: number
  enqueueFailureCount: number
  failedFlushBatchCount: number
  failedFlushLogCount: number
  lastFailedFlushBatchSize: number
  lastFailedFlushReason?: string | null
}

export interface ApplicationLogSummary {
  total: number
  byProject: ApplicationLogSummaryGroup[]
  byLevel: ApplicationLogSummaryGroup[]
  byExceptionType: ApplicationLogSummaryGroup[]
  byRequestPath: ApplicationLogSummaryGroup[]
  status?: ApplicationLogStatus
  pipeline?: ApplicationLogPipelineStatus
}
