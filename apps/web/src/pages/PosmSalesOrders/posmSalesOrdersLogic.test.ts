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
  syncColumnFiltersToTopFilters,
  syncTopFiltersToColumnFilters,
  validatePosmSalesOrderNumberRanges,
} from './posmSalesOrdersLogic'

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
