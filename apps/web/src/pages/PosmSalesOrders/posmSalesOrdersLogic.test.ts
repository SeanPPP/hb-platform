import { OrderType } from '../../types/posmSalesOrder'
import {
  applyPosmSalesOrderQueryChange,
  applyPosmSalesOrderColumnFilterDraft,
  applyPosmSalesOrderTopFilterDraft,
  buildPosmSalesOrderListQuery,
  createResetPosmSalesOrderState,
  createPosmSalesOrderColumnFilterDraft,
  mapPosmSalesOrderSortState,
  normalizePosmSalesOrderFilterNumber,
  isLatestPosmSalesOrderRequest,
  resolvePosmSalesOrderClientUtcOffsetMinutes,
  syncColumnFiltersToTopFilters,
  syncTopFiltersToColumnFilters,
  validatePosmSalesOrderNumberRanges,
} from './posmSalesOrdersLogic'
import { formatPosmSalesOrderLocalTime, normalizePosmSalesOrderUtcTime } from './time'

// 固定业务测试时区，避免 CI 机器默认 UTC 导致跨日与 offset 断言不稳定。
process.env.TZ = 'Australia/Brisbane'

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)
  if (actualJson !== expectedJson) {
    throw new Error(`${label}. Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

function assert(condition: boolean, label: string) {
  if (!condition) throw new Error(label)
}

const currentState = {
  startDate: '2026-06-10',
  endDate: '2026-06-11',
  branchCode: 'S01',
  orderType: OrderType.Paid,
  keyword: '  invoice  ',
  page: 3,
  pageSize: 50,
  columnFilters: {
    orderGuidKeyword: '  ORDER-01  ',
    deviceCodeKeyword: '  POS-2  ',
    timeStart: '08:30:00',
    timeEnd: '18:15:59',
    skuCountMin: 1,
    skuCountMax: 10,
    itemCountMin: 2,
    itemCountMax: 20,
    totalAmountMin: 10.5,
    totalAmountMax: 200.75,
    discountAmountMin: 0,
    discountAmountMax: 30,
    actualPayMin: 9,
    actualPayMax: 180,
  },
  sort: { field: 'actualPay' as const, direction: 'desc' as const },
}

const appliedState = {
  ...currentState,
  startDate: '2026-06-10',
  endDate: '2026-06-11',
  branchCode: 'S01',
  columnFilters: {
    ...currentState.columnFilters,
    startDate: '2026-06-10',
    endDate: '2026-06-11',
    branchCode: 'S01',
  },
}
const pendingTopDraft = {
  startDate: '2026-07-01',
  endDate: '2026-07-02',
  branchCode: 'S09',
  orderType: OrderType.Refunded,
  keyword: 'new keyword',
}
const queryBeforeTopSearch = buildPosmSalesOrderListQuery(appliedState)
assert(
  queryBeforeTopSearch.startDate === '2026-06-10' &&
    queryBeforeTopSearch.endDate === '2026-06-11' &&
    queryBeforeTopSearch.branchCode === 'S01',
  '顶部 draft 改变后，已应用查询仍应保持不变',
)
const searchedState = applyPosmSalesOrderTopFilterDraft(appliedState, pendingTopDraft)
const searchedQuery = buildPosmSalesOrderListQuery(searchedState)
assert(
  searchedState.page === 1 &&
    searchedQuery.startDate === '2026-07-01' &&
    searchedQuery.endDate === '2026-07-02' &&
    searchedQuery.branchCode === 'S09' &&
    searchedQuery.orderType === OrderType.Refunded &&
    searchedQuery.keyword === 'new keyword',
  '点击查询后才应提交顶部 draft 并回到第 1 页',
)

const sortedBeforeTopSearch = applyPosmSalesOrderQueryChange(appliedState, {
  sort: { field: 'totalAmount', direction: 'desc' },
})
const sortedBeforeTopSearchQuery = buildPosmSalesOrderListQuery(sortedBeforeTopSearch)
assert(
  sortedBeforeTopSearchQuery.startDate === '2026-06-10' &&
    sortedBeforeTopSearchQuery.branchCode === 'S01' &&
    sortedBeforeTopSearchQuery.sortField === 'totalAmount',
  '顶部 draft 未提交时排序必须继续使用上次 applied 条件',
)

const filteredFromPageThree = applyPosmSalesOrderColumnFilterDraft(appliedState, {
  ...appliedState.columnFilters,
  deviceCodeKeyword: 'POS-NEW',
})
assert(
  filteredFromPageThree.page === 1 &&
    buildPosmSalesOrderListQuery(filteredFromPageThree).deviceCodeKeyword === 'POS-NEW',
  '非第一页应用列过滤只能生成 page=1 查询',
)

let requestView = { data: 'initial', loading: true, error: '' }
const oldRequestId = 1
const latestRequestId = 2
if (isLatestPosmSalesOrderRequest(oldRequestId, latestRequestId)) {
  requestView = { data: 'stale', loading: false, error: 'stale error' }
}
assertDeepEqual(
  requestView,
  { data: 'initial', loading: true, error: '' },
  '旧请求不得更新 data、loading 或 error',
)
if (isLatestPosmSalesOrderRequest(latestRequestId, latestRequestId)) {
  requestView = { data: 'latest', loading: false, error: '' }
}
assertDeepEqual(
  requestView,
  { data: 'latest', loading: false, error: '' },
  '最新请求可以提交 data 和 loading',
)

const appliedFilters = { orderGuidKeyword: 'APPLIED', skuCountMin: 2 }
const draftFilters = createPosmSalesOrderColumnFilterDraft(appliedFilters, {
  orderGuidKeyword: 'DRAFT',
})
assertDeepEqual(
  appliedFilters,
  { orderGuidKeyword: 'APPLIED', skuCountMin: 2 },
  '编辑草稿不得修改已应用列过滤',
)
assertDeepEqual(
  draftFilters,
  { orderGuidKeyword: 'DRAFT', skuCountMin: 2 },
  '草稿应从已应用条件复制并独立更新',
)
assert(
  normalizePosmSalesOrderFilterNumber(3.6, true) === 4,
  'SKU数和件数的小数应规范化为整数',
)
assert(
  normalizePosmSalesOrderFilterNumber('12.25', false) === 12.25,
  '金额过滤应保留小数',
)
assert(
  normalizePosmSalesOrderFilterNumber('', true) === undefined,
  '空数值应省略',
)

assertDeepEqual(
  buildPosmSalesOrderListQuery(currentState, { page: 1 }),
  {
    startDate: '2026-06-10',
    endDate: '2026-06-11',
    branchCode: 'S01',
    orderType: OrderType.Paid,
    keyword: 'invoice',
    orderGuidKeyword: 'ORDER-01',
    deviceCodeKeyword: 'POS-2',
    timeStart: '08:30:00',
    timeEnd: '18:15:59',
    clientUtcOffsetMinutes: 600,
    skuCountMin: 1,
    skuCountMax: 10,
    itemCountMin: 2,
    itemCountMax: 20,
    totalAmountMin: 10.5,
    totalAmountMax: 200.75,
    discountAmountMin: 0,
    discountAmountMax: 30,
    actualPayMin: 9,
    actualPayMax: 180,
    sortField: 'actualPay',
    sortDirection: 'desc',
    pageNumber: 1,
    pageSize: 50,
  },
  '搜索时应映射所有条件并使用显式 page=1',
)

assertDeepEqual(
  buildPosmSalesOrderListQuery(currentState, {
    startDate: '2026-06-12',
    endDate: '2026-06-12',
    branchCode: '',
    orderType: OrderType.All,
    keyword: '   ',
    page: 1,
    columnFilters: {
      ...currentState.columnFilters,
      orderGuidKeyword: '  ',
      deviceCodeKeyword: '',
    },
  }),
  {
    startDate: '2026-06-12',
    endDate: '2026-06-12',
    branchCode: undefined,
    orderType: OrderType.All,
    keyword: undefined,
    orderGuidKeyword: undefined,
    deviceCodeKeyword: undefined,
    timeStart: '08:30:00',
    timeEnd: '18:15:59',
    clientUtcOffsetMinutes: 600,
    skuCountMin: 1,
    skuCountMax: 10,
    itemCountMin: 2,
    itemCountMax: 20,
    totalAmountMin: 10.5,
    totalAmountMax: 200.75,
    discountAmountMin: 0,
    discountAmountMax: 30,
    actualPayMin: 9,
    actualPayMax: 180,
    sortField: 'actualPay',
    sortDirection: 'desc',
    pageNumber: 1,
    pageSize: 50,
  },
  '文本条件应 trim 且空白省略，同时 overrides 应合并列过滤',
)

const partialOverrideQuery = buildPosmSalesOrderListQuery(currentState, {
  columnFilters: { deviceCodeKeyword: ' D9 ' },
})
assert(
  partialOverrideQuery.orderGuidKeyword === 'ORDER-01' &&
    partialOverrideQuery.deviceCodeKeyword === 'D9',
  '局部列过滤 override 应与已有列过滤合并',
)

const sortMappings = [
  ['orderGuid', 'orderGuid'],
  ['branchName', 'branchCode'],
  ['deviceCode', 'deviceCode'],
  ['date', 'orderTime'],
  ['time', 'orderTime'],
  ['skuCount', 'skuCount'],
  ['itemCount', 'itemCount'],
  ['totalAmount', 'totalAmount'],
  ['discountAmount', 'discountAmount'],
  ['actualAmount', 'actualPay'],
] as const
sortMappings.forEach(([columnKey, field]) => {
  assertDeepEqual(
    mapPosmSalesOrderSortState(columnKey, 'descend'),
    { field, direction: 'desc' },
    `${columnKey} 应映射到 ${field}`,
  )
})
assertDeepEqual(
  mapPosmSalesOrderSortState('unknown', undefined),
  { field: 'orderTime', direction: 'asc' },
  '未知排序应回退默认值',
)

const syncedColumns = syncTopFiltersToColumnFilters(
  { deviceCodeKeyword: 'D1', startDate: 'old', endDate: 'old', branchCode: 'OLD' },
  { startDate: '2026-07-01', endDate: '2026-07-02', branchCode: 'S02' },
)
assertDeepEqual(
  syncedColumns,
  { deviceCodeKeyword: 'D1', startDate: '2026-07-01', endDate: '2026-07-02', branchCode: 'S02' },
  '顶部日期和分店应同步到列过滤且保留其他条件',
)
assertDeepEqual(
  syncColumnFiltersToTopFilters(syncedColumns),
  { startDate: '2026-07-01', endDate: '2026-07-02', branchCode: 'S02' },
  '列头日期和分店应同步回顶部',
)

assertDeepEqual(
  createResetPosmSalesOrderState('2026-07-14', 50),
  {
    startDate: '2026-07-14',
    endDate: '2026-07-14',
    branchCode: '',
    orderType: OrderType.All,
    keyword: '',
    page: 1,
    pageSize: 50,
    columnFilters: { startDate: '2026-07-14', endDate: '2026-07-14', branchCode: '' },
    sort: { field: 'orderTime', direction: 'asc' },
  },
  '总重置应恢复今日、全部、空关键词和默认排序',
)
const resetQuery = buildPosmSalesOrderListQuery(
  currentState,
  createResetPosmSalesOrderState('2026-07-14', 50),
)
assert(
  resetQuery.orderGuidKeyword === undefined &&
    resetQuery.skuCountMin === undefined &&
    resetQuery.actualPayMax === undefined &&
    resetQuery.sortField === 'orderTime' &&
    resetQuery.sortDirection === 'asc',
  '总重置 override 应真正清空旧列过滤并恢复默认排序',
)

assertDeepEqual(
  validatePosmSalesOrderNumberRanges({ skuCountMin: 5, skuCountMax: 4 }),
  { isValid: false, range: 'skuCount' },
  'min 大于 max 时应返回可识别的区间校验失败',
)
assert(
  validatePosmSalesOrderNumberRanges({ actualPayMin: 0, actualPayMax: 0 }).isValid,
  '相等边界应通过校验',
)

function formatLocalDate(date: Date): string {
  const pad = (value: number) => String(value).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

function formatLocalTime(date: Date): string {
  const pad = (value: number) => String(value).padStart(2, '0')
  return `${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`
}

assert(
  resolvePosmSalesOrderClientUtcOffsetMinutes('2026-06-10', -123) === 600,
  '客户端偏移应按选中日期的本地零点计算',
)
assert(
  resolvePosmSalesOrderClientUtcOffsetMinutes('', 345) === 345 &&
    resolvePosmSalesOrderClientUtcOffsetMinutes('2026-02-30', 345) === 345,
  '空值或非法选中日期应回退当前客户端偏移',
)

assert(
  normalizePosmSalesOrderUtcTime('2026-07-17T00:15:01') ===
    '2026-07-17T00:15:01Z',
  '无时区后缀的订单时间应补充 Z',
)
assert(
  normalizePosmSalesOrderUtcTime('2026-07-17 00:15:01') ===
    '2026-07-17 00:15:01Z',
  '空格分隔的无后缀订单时间也应只补充 Z',
)
for (const timestamp of [
  '2026-07-17T00:15:01Z',
  '2026-07-17T00:15:01+10:00',
  '2026-07-17T00:15:01+1000',
]) {
  assert(
    normalizePosmSalesOrderUtcTime(timestamp) === timestamp,
    `已有时区后缀的订单时间应保持不变: ${timestamp}`,
  )
}

const utcWithoutSuffix = '2026-07-17T00:15:01'
const utcWithoutSuffixLocal = new Date(`${utcWithoutSuffix}Z`)
assert(
  formatPosmSalesOrderLocalTime(utcWithoutSuffix, 'YYYY-MM-DD') ===
    formatLocalDate(utcWithoutSuffixLocal),
  '无时区后缀的订单 UTC 日期应转换为浏览器本地日期',
)
assert(
  formatPosmSalesOrderLocalTime(utcWithoutSuffix, 'HH:mm:ss') ===
    formatLocalTime(utcWithoutSuffixLocal),
  '无时区后缀的订单 UTC 时间应转换为浏览器本地时间',
)

const explicitUtc = '2026-07-17T00:15:01Z'
assert(
  formatPosmSalesOrderLocalTime(explicitUtc, 'HH:mm:ss') ===
    formatLocalTime(new Date(explicitUtc)),
  '带 Z 的订单时间应按显式 UTC 转换为浏览器本地时间',
)

const explicitOffset = '2026-07-17T00:15:01+02:00'
assert(
  formatPosmSalesOrderLocalTime(explicitOffset, 'HH:mm:ss') ===
    formatLocalTime(new Date(explicitOffset)),
  '带 offset 的订单时间应保留原时区语义并转换为浏览器本地时间',
)

assert(
  formatPosmSalesOrderLocalTime('2026-07-16T14:30:00Z', 'YYYY-MM-DD HH:mm:ss') ===
    '2026-07-17 00:30:00',
  'UTC 订单时间跨日后应显示 Brisbane 当地日期和时间',
)

for (const [timestamp, expected] of [
  ['2026-07-17 00:15:01.1234567', '2026-07-17 10:15:01'],
  ['2026-07-17T00:15:01+10:00', '2026-07-17 00:15:01'],
  ['2026-07-17T00:15:01+1000', '2026-07-17 00:15:01'],
]) {
  assert(
    formatPosmSalesOrderLocalTime(timestamp, 'YYYY-MM-DD HH:mm:ss') === expected,
    `.NET/ISO 合法时间格式应被接受: ${timestamp}`,
  )
}

assert(
  formatPosmSalesOrderLocalTime('not-a-date', 'HH:mm:ss') === 'not-a-date',
  '非法订单时间应保留原文，避免隐藏后端异常数据',
)
assert(
  formatPosmSalesOrderLocalTime('2026-02-30T00:00:00Z', 'YYYY-MM-DD') ===
    '2026-02-30T00:00:00Z',
  '不存在的日历日期不得被 dayjs 正常化',
)
for (const invalidTimestamp of [
  '0000-01-01T00:00:00Z',
  '2026-13-01T00:00:00Z',
  '2026-07-17T24:00:00Z',
  '2026-07-17T00:60:00Z',
  '2026-07-17T00:00:60Z',
  '2026-07-17T00:00:00.12345678Z',
  '2026-07-17T00:00:00+14:01',
  '2026-07-17T00:00:00+1060',
]) {
  assert(
    formatPosmSalesOrderLocalTime(invalidTimestamp, 'YYYY-MM-DD') === invalidTimestamp,
    `越界或不符合 .NET/ISO 格式的订单时间应保留原文: ${invalidTimestamp}`,
  )
}
assert(
  formatPosmSalesOrderLocalTime('', 'HH:mm:ss') === '-' &&
    formatPosmSalesOrderLocalTime(null, 'HH:mm:ss') === '-' &&
    formatPosmSalesOrderLocalTime(undefined, 'HH:mm:ss') === '-',
  '空订单时间应显示占位符',
)

const changedState = applyPosmSalesOrderQueryChange(currentState, {
  columnFilters: { ...currentState.columnFilters, deviceCodeKeyword: 'D9' },
})
assert(changedState.page === 1, '更换条件应回到第 1 页')
assert(
  changedState.pageSize === 50 && changedState.sort.field === 'actualPay',
  '更换条件应保留页大小和排序',
)
assertDeepEqual(
  buildPosmSalesOrderListQuery(changedState, { page: 4 }),
  { ...buildPosmSalesOrderListQuery(changedState), pageNumber: 4 },
  '翻页应保留全部筛选与排序条件',
)

console.log('posmSalesOrdersLogic.test: ok')
