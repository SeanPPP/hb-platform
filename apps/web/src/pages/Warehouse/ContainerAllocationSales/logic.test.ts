import dayjs from 'dayjs'
import {
  buildBranchQuery,
  buildContainerAllocationSalesQuery,
  buildQuickRangeQuery,
  buildRangeByWeeks,
  compareContainerAllocationSalesBranches,
  formatAustralianCurrency,
  formatStatisticMessageAmounts,
  getContainerAllocationSalesViewState,
  getGrossMarginDisplay,
  getPaginatedRowNumber,
  isCustomEndDateDisabled,
  mapTableChangeToQuery,
  shouldTriggerTableRowClick,
  shouldLoadContainerAllocationSales,
} from './logic'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message)
}

const lowerBranch: import('../../../types/containerAllocationSales').ContainerAllocationSalesBranch = {
  branchCode: 'S2',
  branchName: 'Alpha 分店',
  isActive: false,
  allocationQuantity: 1,
  allocationImportAmount: 10,
  salesQuantity: 2,
  salesAmount: 20,
  averageSalesPrice: 10,
  grossProfit: -1,
  grossMarginRate: -0.05,
  isGrossMarginComplete: true,
}
const higherBranch: import('../../../types/containerAllocationSales').ContainerAllocationSalesBranch = {
  branchCode: 'S10',
  branchName: 'Beta 分店',
  isActive: true,
  allocationQuantity: 3,
  allocationImportAmount: 30,
  salesQuantity: 4,
  salesAmount: 40,
  averageSalesPrice: 20,
  grossProfit: 8,
  grossMarginRate: 0.2,
  isGrossMarginComplete: true,
}
const branchSortableFields = [
  'branchCode',
  'branchName',
  'isActive',
  'allocationQuantity',
  'allocationImportAmount',
  'salesQuantity',
  'salesAmount',
  'averageSalesPrice',
  'grossMarginRate',
] as const
for (const field of branchSortableFields) {
  assert(
    compareContainerAllocationSalesBranches(lowerBranch, higherBranch, field) < 0,
    `分店明细 ${field} 应支持本地升序比较`,
  )
}
assert(
  compareContainerAllocationSalesBranches({ ...lowerBranch, salesAmount: null }, higherBranch, 'salesAmount') > 0,
  '空数值升序时应稳定排在有效数值之后',
)
const compareBranchesWithOrder = compareContainerAllocationSalesBranches as unknown as (
  left: typeof lowerBranch,
  right: typeof higherBranch,
  field: 'salesAmount',
  sortOrder: 'ascend' | 'descend' | null,
) => number
assert(
  compareBranchesWithOrder({ ...lowerBranch, salesAmount: null }, higherBranch, 'salesAmount', 'descend') < 0,
  '空数值降序时应抵消 Ant Design 的结果翻转并继续排在有效数值之后',
)

const tableRowTarget = { kind: 'row' }
for (const interactiveKind of ['button', 'a', 'input']) {
  const interactiveTarget = { kind: interactiveKind }
  assertEqual(
    shouldTriggerTableRowClick({
      closest: (selector: string) => selector.split(',').includes(interactiveKind) ? interactiveTarget : null,
    }, tableRowTarget),
    false,
    `点击行内 ${interactiveKind} 控件时不得重复触发行点击`,
  )
}
assertEqual(
  shouldTriggerTableRowClick({ closest: () => null }, tableRowTarget),
  true,
  '点击普通单元格内容时应触发行点击',
)
assertEqual(
  shouldTriggerTableRowClick(null, tableRowTarget),
  true,
  '无法解析目标时应保留整行鼠标点击行为',
)

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
const itemNumberSort = mapTableChangeToQuery(
  { current: 1, pageSize: 20 },
  { field: 'itemNumber', order: 'ascend' },
)
assertEqual(itemNumberSort.sortBy, 'itemNumber', '货号列应映射为后端 itemNumber 排序字段')
assertEqual(itemNumberSort.sortDirection, 'asc', '货号列升序应映射为后端 asc')
const clearedSort = mapTableChangeToQuery(
  { current: 2, pageSize: 20 },
  { field: 'itemNumber', order: undefined },
)
assertEqual(clearedSort.sortBy, 'productCode', '取消列排序后应回退默认商品编码排序')
assertEqual(clearedSort.sortDirection, 'asc', '取消列排序后应回退默认升序')
assertEqual(getPaginatedRowNumber(1, 20, 0), 1, '第一页首行序号应为 1')
assertEqual(getPaginatedRowNumber(3, 20, 4), 45, '分页序号应按全表位置连续计算')

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

assertEqual(
  formatStatisticMessageAmounts(
    '商品统计与分店营业额统计不一致: 2026-07-08 1024, 商品金额 3812.4000000000000000000000001, 分店营业额 3922.8000, 金额差 110.3999999999999999999999999',
  ),
  '商品统计与分店营业额统计不一致: 2026-07-08 1024, 商品金额 3,812.40, 分店营业额 3,922.80, 金额差 110.40',
  '对账文案的长小数和已有小数应固定显示两位并使用千分位',
)
assertEqual(
  formatStatisticMessageAmounts(
    '商品统计与分店营业额统计不一致: 2026-06-01 S1, 商品金额 -30, 分店营业额统计缺失, 金额差 130.00, 未匹配供应商金额 15.5, 未匹配供应商数量 3, 未匹配商品数 2',
  ),
  '商品统计与分店营业额统计不一致: 2026-06-01 S1, 商品金额 -30.00, 分店营业额统计缺失, 金额差 130.00, 未匹配供应商金额 15.50, 未匹配供应商数量 3, 未匹配商品数 2',
  '金额整数、负数和诊断金额应格式化，缺失文案及诊断数量不得改写',
)
assertEqual(
  formatStatisticMessageAmounts(
    '商品金额 12345678901234567890.12, 分店营业额 9007199254740993.01, 金额差 999.999, 未匹配供应商金额 0.005',
  ),
  '商品金额 12,345,678,901,234,567,890.12, 分店营业额 9,007,199,254,740,993.01, 金额差 1,000.00, 未匹配供应商金额 0.01',
  '超出安全整数范围的金额和小数进位必须按十进制字符串精确处理',
)
assertEqual(
  formatStatisticMessageAmounts('商品金额 1,234,567.8, 金额差 -9,999.995'),
  '商品金额 1,234,567.80, 金额差 -10,000.00',
  '已有千分位和负数跨整数进位应保持正确',
)
assertEqual(
  formatStatisticMessageAmounts('商品金额 -0.004, 金额差 -0.005'),
  '商品金额 0.00, 金额差 -0.01',
  '负数舍入为零时应移除负号，非零负数仍应保留负号',
)

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
