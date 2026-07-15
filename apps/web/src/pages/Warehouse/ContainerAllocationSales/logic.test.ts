import dayjs from 'dayjs'
import {
  buildBranchQuery,
  buildContainerAllocationSalesQuery,
  buildQuickRangeQuery,
  buildRangeByWeeks,
  formatAustralianCurrency,
  getContainerAllocationSalesViewState,
  getGrossMarginDisplay,
  isCustomEndDateDisabled,
  mapTableChangeToQuery,
  shouldLoadContainerAllocationSales,
} from './logic'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

const fourWeeks = buildRangeByWeeks('2026-06-01', 4, dayjs('2026-07-15'))
assertEqual(fourWeeks.startDate, '2026-06-01', '快捷区间起点应固定为到货日')
assertEqual(fourWeeks.endDate, '2026-06-28', '4 周区间应包含到货日起连续 28 天')

const clipped = buildRangeByWeeks('2026-07-01', 4, dayjs('2026-07-15'))
assertEqual(clipped.endDate, '2026-07-15', '快捷区间结束日不得晚于今天')

const quickQuery = buildQuickRangeQuery('2026-06-01', 2, dayjs('2026-07-15'))
assertEqual(JSON.stringify(quickQuery), JSON.stringify({ startDate: '2026-06-01', endDate: '2026-06-14', pageNumber: 1 }), '快捷周数应构造页面实际使用的查询参数')

const branchQuery = buildBranchQuery('P-001', '2026-06-01', '2026-06-28')
assertEqual(JSON.stringify(branchQuery), JSON.stringify({ productCode: 'P-001', startDate: '2026-06-01', endDate: '2026-06-28' }), '分店懒加载应构造商品和当前日期区间')

assertEqual(
  isCustomEndDateDisabled(dayjs('2026-05-31'), '2026-06-01', dayjs('2026-07-15')),
  true,
  '自定义结束日不得早于到货日',
)
assertEqual(
  isCustomEndDateDisabled(dayjs('2026-07-16'), '2026-06-01', dayjs('2026-07-15')),
  true,
  '自定义结束日不得晚于今天',
)
assertEqual(
  isCustomEndDateDisabled(dayjs('2026-06-20'), '2026-06-01', dayjs('2026-07-15')),
  false,
  '有效自定义结束日应允许选择',
)

const mapped = mapTableChangeToQuery(
  { current: 3, pageSize: 50 },
  { field: 'salesAmount', order: 'descend' },
)
assertEqual(mapped.pageNumber, 3, '分页页码应映射到服务端请求')
assertEqual(mapped.pageSize, 50, '分页大小应映射到服务端请求')
assertEqual(mapped.sortBy, 'salesAmount', '排序字段应映射到服务端请求')
assertEqual(mapped.sortDirection, 'desc', '降序应映射为后端 desc')

assertEqual(getGrossMarginDisplay({ grossMarginRate: 0.25, isGrossMarginComplete: true }), '25.00%', '完整毛利率应按百分比显示')
assertEqual(getGrossMarginDisplay({ grossMarginRate: null, isGrossMarginComplete: false }), '成本缺失', '成本不完整时不得显示误导毛利率')
assertEqual(getGrossMarginDisplay({ grossMarginRate: null, isGrossMarginComplete: null }), '-', '无销售数据时毛利率应显示短横线')

const freshEmptyStatistics = getContainerAllocationSalesViewState({
  canQuery: true,
  total: 2,
  statisticStatus: 'Fresh',
  allocationQuantity: 0,
  salesQuantity: 0,
  search: '',
})
assertEqual(freshEmptyStatistics.showNoStatistics, true, 'Fresh 且配货销售均为零时应显示无统计数据')
assertEqual(freshEmptyStatistics.showTable, true, '有商品时应继续显示表格')

const staleStatistics = getContainerAllocationSalesViewState({
  canQuery: true,
  total: 2,
  statisticStatus: 'Stale',
  allocationQuantity: 0,
  salesQuantity: null,
  search: '',
})
assertEqual(staleStatistics.showNoStatistics, false, '统计非 Fresh 时不得误报暂无配货或销售数据')

const unavailable = getContainerAllocationSalesViewState({
  canQuery: false,
  total: 0,
  statisticStatus: 'Missing',
  allocationQuantity: 0,
  salesQuantity: null,
  search: '',
})
assertEqual(unavailable.showTable, false, '不可查询时不应显示表格')
assertEqual(unavailable.emptyDescription, null, '不可查询时不应误显示暂无货柜商品')

const emptyProducts = getContainerAllocationSalesViewState({
  canQuery: true,
  total: 0,
  statisticStatus: 'Fresh',
  allocationQuantity: 0,
  salesQuantity: 0,
  search: '',
})
assertEqual(emptyProducts.emptyDescription, '暂无货柜商品', '可查询但无商品时应显示货柜商品空态')

assertEqual(formatAustralianCurrency(1234.5), '$1,234.50', '金额应使用澳洲业务页面的美元符号口径')
assertEqual(formatAustralianCurrency(0), '$0.00', '零金额应明确显示 $0.00')
assertEqual(formatAustralianCurrency(null), '-', '空金额应显示短横线')

const pagedWithDraftUntouched = buildContainerAllocationSalesQuery(
  {
    startDate: '2026-06-01',
    endDate: '2026-06-28',
    search: '已提交词',
    pageNumber: 1,
    pageSize: 20,
    sortBy: 'productCode',
    sortDirection: 'asc',
  },
  { pageNumber: 2 },
)
assertEqual(pagedWithDraftUntouched.search, '已提交词', '分页排序应继续使用已提交搜索词，不得读取输入框 draft')
const explicitSearchQuery = buildContainerAllocationSalesQuery(pagedWithDraftUntouched, { search: '新查询词', pageNumber: 1 })
assertEqual(explicitSearchQuery.search, '新查询词', '只有显式搜索请求才应覆盖已提交搜索词')
assertEqual(explicitSearchQuery.pageNumber, 1, '显式搜索应重置第一页')

assertEqual(
  shouldLoadContainerAllocationSales({ active: false, requestedContainerGuid: 'C-1', loadedContainerGuid: null }),
  false,
  '隐藏 KeepAlive 页面不得自动请求',
)
assertEqual(
  shouldLoadContainerAllocationSales({ active: true, requestedContainerGuid: 'C-1', loadedContainerGuid: 'C-1' }),
  false,
  '同一货柜 Tab 切回时应复用已有内容',
)
assertEqual(
  shouldLoadContainerAllocationSales({ active: true, requestedContainerGuid: 'C-2', loadedContainerGuid: 'C-1' }),
  true,
  '激活新货柜且未加载时应发起请求',
)

console.log('containerAllocationSales.logic.test: ok')
