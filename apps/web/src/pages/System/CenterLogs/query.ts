import dayjs, { type Dayjs } from 'dayjs'
import type { ApplicationLogQueryParams } from '../../../types/centerLog'

export interface CenterLogQueryFormValues {
  projectCodes?: string[]
  level?: string
  sourceType?: string
  category?: string
  requestPath?: string
  traceId?: string
  storeCode?: string
  deviceCode?: string
  appVersion?: string
  instanceId?: string
  keyword?: string
  timeRange?: [Dayjs, Dayjs]
}

export const DEFAULT_CENTER_LOG_PROJECT_CODE = 'hbweb_rv'
export const DEFAULT_CENTER_LOG_PAGE_SIZE = 20
export const CENTER_LOG_PATH = '/system/center-logs'

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
    storeCode: values.storeCode?.trim() || undefined,
    deviceCode: values.deviceCode?.trim() || undefined,
    appVersion: values.appVersion?.trim() || undefined,
    instanceId: values.instanceId?.trim() || undefined,
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

export function buildCenterLogFormValuesFromSearchParams(
  searchParams: URLSearchParams,
): CenterLogQueryFormValues {
  const projectCodes = normalizeProjectCodes([
    ...searchParams.getAll('projectCodes'),
    searchParams.get('projectCode') ?? '',
  ]) ?? [DEFAULT_CENTER_LOG_PROJECT_CODE]
  const fromUtc = searchParams.get('fromUtc')
  const toUtc = searchParams.get('toUtc')
  const from = fromUtc ? dayjs(fromUtc) : null
  const to = toUtc ? dayjs(toUtc) : null
  const read = (key: string) => searchParams.get(key)?.trim() || undefined

  return {
    projectCodes,
    level: read('level'),
    sourceType: read('sourceType'),
    category: read('category'),
    requestPath: read('requestPath'),
    traceId: read('traceId'),
    storeCode: read('storeCode'),
    deviceCode: read('deviceCode'),
    appVersion: read('appVersion'),
    instanceId: read('instanceId'),
    keyword: read('keyword'),
    timeRange: from?.isValid() && to?.isValid() ? [from, to] : undefined,
  }
}

export function shouldHydrateCenterLogQueryFromLocation(
  active: boolean,
  pathname: string,
  nextSearch: string,
  locationKey: string,
  hydratedLocationKey: string,
) {
  return active &&
    pathname === CENTER_LOG_PATH &&
    nextSearch.length > 1 &&
    locationKey !== hydratedLocationKey
}
