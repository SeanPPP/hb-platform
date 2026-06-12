import dayjs from 'dayjs'
import {
  DEFAULT_CENTER_LOG_PROJECT_CODE,
  DEFAULT_CENTER_LOG_PAGE_SIZE,
  buildCenterLogQueryParams,
  buildDefaultCenterLogQueryParams,
} from './query'

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`)
  }
}

const start = dayjs('2026-06-05T00:00:00.000Z')
const end = dayjs('2026-06-05T02:30:00.000Z')

const query = buildCenterLogQueryParams(
  {
    projectCodes: [' hbweb_rv ', '', 'HbwebExpo', 'hbweb_rv'],
    level: 'Error',
    sourceType: 'Web',
    category: ' frontend-request ',
    requestPath: ' /api/system/users ',
    traceId: ' trace-001 ',
    keyword: ' timeout ',
    timeRange: [start, end],
  },
  3,
  50,
)

assertEqual(Array.isArray(query.projectCodes), true, 'project codes are returned as an array')
assertEqual(query.projectCodes?.join(','), 'hbweb_rv,HbwebExpo', 'project codes are trimmed and deduplicated')
assertEqual(query.projectCode, 'hbweb_rv', 'legacy project code keeps first selected project')
assertEqual(query.level, 'Error', 'level is preserved')
assertEqual(query.sourceType, 'Web', 'source type is preserved')
assertEqual(query.category, 'frontend-request', 'category is trimmed')
assertEqual(query.requestPath, '/api/system/users', 'request path is trimmed')
assertEqual(query.traceId, 'trace-001', 'trace id is trimmed')
assertEqual(query.keyword, 'timeout', 'keyword is trimmed')
assertEqual(query.startUtc, start.toISOString(), 'start time is serialized')
assertEqual(query.endUtc, end.toISOString(), 'end time is serialized')
assertEqual(query.pageNumber, 3, 'page number is preserved')
assertEqual(query.pageSize, 50, 'page size is preserved')
assertEqual(query.sortBy, 'TimestampUtc', 'sort field defaults to timestamp')
assertEqual(query.sortDirection, 'desc', 'sort direction defaults to descending')

const defaultQuery = buildDefaultCenterLogQueryParams()
assertEqual(defaultQuery.projectCodes?.join(','), DEFAULT_CENTER_LOG_PROJECT_CODE, 'default query keeps default project')
assertEqual(defaultQuery.projectCode, DEFAULT_CENTER_LOG_PROJECT_CODE, 'default query keeps legacy project code fallback')
assertEqual(defaultQuery.pageNumber, 1, 'default query resets page number')
assertEqual(defaultQuery.pageSize, DEFAULT_CENTER_LOG_PAGE_SIZE, 'default query keeps default page size')
assertEqual(defaultQuery.level, undefined, 'default query clears level')
assertEqual(defaultQuery.sourceType, undefined, 'default query clears source type')
assertEqual(defaultQuery.category, undefined, 'default query clears category')
assertEqual(defaultQuery.requestPath, undefined, 'default query clears request path')
assertEqual(defaultQuery.traceId, undefined, 'default query clears trace id')
assertEqual(defaultQuery.keyword, undefined, 'default query clears keyword')

console.log('centerLogs.query.test: ok')
