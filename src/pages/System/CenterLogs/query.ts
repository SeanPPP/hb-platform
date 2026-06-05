import type { Dayjs } from 'dayjs'
import type { ApplicationLogQueryParams } from '../../../types/centerLog'

export interface CenterLogQueryFormValues {
  projectCodes?: string[]
  level?: string
  sourceType?: string
  category?: string
  requestPath?: string
  traceId?: string
  keyword?: string
  timeRange?: [Dayjs, Dayjs]
}

export const DEFAULT_CENTER_LOG_PROJECT_CODE = 'hbweb_rv'
export const DEFAULT_CENTER_LOG_PAGE_SIZE = 20

export const CENTER_LOG_PROJECT_OPTIONS = ['HBBBackend', 'hbweb_rv', 'HbwebExpo', 'hbpos_win']
export const CENTER_LOG_LEVEL_OPTIONS = ['Trace', 'Debug', 'Information', 'Warning', 'Error', 'Critical']
export const CENTER_LOG_SOURCE_TYPE_OPTIONS = ['Backend', 'Web', 'Mobile', 'POS']

function normalizeProjectCodes(projectCodes?: string[]) {
  const normalized = projectCodes
    ?.map((item) => item.trim())
    .filter((item) => item.length > 0)

  return normalized?.length ? Array.from(new Set(normalized)) : undefined
}

export function buildCenterLogQueryParams(
  values: CenterLogQueryFormValues,
  pageNumber = 1,
  pageSize = DEFAULT_CENTER_LOG_PAGE_SIZE,
): ApplicationLogQueryParams {
  const projectCodes = normalizeProjectCodes(values.projectCodes)

  return {
    // 新后端使用 projectCodes 多选；projectCode 兜底兼容前后端非同步发布。
    projectCode: projectCodes?.[0],
    projectCodes,
    level: values.level || undefined,
    sourceType: values.sourceType || undefined,
    category: values.category?.trim() || undefined,
    requestPath: values.requestPath?.trim() || undefined,
    traceId: values.traceId?.trim() || undefined,
    keyword: values.keyword?.trim() || undefined,
    startUtc: values.timeRange?.[0]?.toISOString(),
    endUtc: values.timeRange?.[1]?.toISOString(),
    pageNumber,
    pageSize,
    sortBy: 'TimestampUtc',
    sortDirection: 'desc',
  }
}

export function buildDefaultCenterLogQueryParams(pageSize = DEFAULT_CENTER_LOG_PAGE_SIZE) {
  return buildCenterLogQueryParams(
    { projectCodes: [DEFAULT_CENTER_LOG_PROJECT_CODE] },
    1,
    pageSize,
  )
}
