import { readFileSync } from 'node:fs'
import dayjs from 'dayjs'
import {
  CENTER_LOG_PATH,
  DEFAULT_CENTER_LOG_PROJECT_CODE,
  DEFAULT_CENTER_LOG_PAGE_SIZE,
  buildCenterLogQueryParams,
  buildDefaultCenterLogQueryParams,
  shouldHydrateCenterLogQueryFromLocation,
} from './query'
import * as queryModule from './query'

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
    storeCode: ' S01 ',
    deviceCode: ' POS-01 ',
    appVersion: ' 1.2.3 ',
    instanceId: ' instance-01 ',
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
assertEqual(query.storeCode, 'S01', 'store code is trimmed')
assertEqual(query.deviceCode, 'POS-01', 'device code is trimmed')
assertEqual(query.appVersion, '1.2.3', 'app version is trimmed')
assertEqual(query.instanceId, 'instance-01', 'instance id is trimmed')
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

assertEqual(
  typeof (queryModule as Record<string, unknown>).buildCenterLogFormValuesFromSearchParams,
  'function',
  'center logs should expose URL query hydration for audit detail links',
)

const linkedValues = (
  queryModule as unknown as {
    buildCenterLogFormValuesFromSearchParams: (params: URLSearchParams) => {
      projectCodes?: string[]
      deviceCode?: string
      traceId?: string
      timeRange?: [dayjs.Dayjs, dayjs.Dayjs]
    }
  }
).buildCenterLogFormValuesFromSearchParams(
  new URLSearchParams({
    projectCode: 'hbpos_win',
    deviceCode: 'POS-01',
    traceId: 'trace-01',
    fromUtc: '2026-07-10T00:57:03.000Z',
    toUtc: '2026-07-10T01:07:03.000Z',
  }),
)

assertEqual(linkedValues.projectCodes?.join(','), 'hbpos_win', 'URL project should hydrate project selection')
assertEqual(linkedValues.deviceCode, 'POS-01', 'URL device should hydrate device filter')
assertEqual(linkedValues.traceId, 'trace-01', 'URL trace should hydrate trace filter')
assertEqual(linkedValues.timeRange?.[0].toISOString(), '2026-07-10T00:57:03.000Z', 'URL start should hydrate range')
assertEqual(linkedValues.timeRange?.[1].toISOString(), '2026-07-10T01:07:03.000Z', 'URL end should hydrate range')

assertEqual(
  shouldHydrateCenterLogQueryFromLocation(true, CENTER_LOG_PATH, '?traceId=new', 'next-key', 'old-key'),
  true,
  'active keep-alive page should hydrate changed audit link query',
)
assertEqual(
  shouldHydrateCenterLogQueryFromLocation(false, CENTER_LOG_PATH, '?traceId=new', 'next-key', 'old-key'),
  false,
  'hidden keep-alive page should ignore global location changes',
)
assertEqual(
  shouldHydrateCenterLogQueryFromLocation(true, '/pos-admin/operation-logs', '?traceId=new', 'next-key', 'old-key'),
  false,
  'other routes should not hydrate hidden center log form',
)
assertEqual(
  shouldHydrateCenterLogQueryFromLocation(true, CENTER_LOG_PATH, '?traceId=same', 'same-key', 'same-key'),
  false,
  'same navigation should not reload center logs',
)
assertEqual(
  shouldHydrateCenterLogQueryFromLocation(true, CENTER_LOG_PATH, '?traceId=same', 'second-key', 'first-key'),
  true,
  'same audit link should rehydrate on a new navigation',
)
assertEqual(
  shouldHydrateCenterLogQueryFromLocation(true, CENTER_LOG_PATH, '', 'tab-key', 'audit-link-key'),
  false,
  'plain keep-alive tab restore should preserve cached filters',
)

const centerLogsPageSource = readFileSync('src/pages/System/CenterLogs/index.tsx', 'utf8')
assertEqual(
  centerLogsPageSource.includes('const { active } = useKeepAliveContext()'),
  true,
  'center logs page should guard URL hydration with keep-alive active state',
)
assertEqual(
  centerLogsPageSource.includes('shouldHydrateCenterLogQueryFromLocation('),
  true,
  'center logs page should rehydrate changed audit-link query',
)

console.log('centerLogs.query.test: ok')
