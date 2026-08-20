import type { WarehouseProductAllocationBranch } from '../../../types/warehouseProductRecords'
import {
  buildAllocationQuery,
  buildBrisbaneDateRange,
  buildSalesScope,
  buildSalesSelection,
  filterAllocationBranches,
  formatAveragePrice,
  formatAustralianCurrency,
  formatChineseCurrency,
  formatQuantity,
  getContainerDetailPath,
  getDateRangeError,
  getDefaultContainerStatuses,
  mapContainerTableChangeToQuery,
  sumAllocationBranchAmounts,
} from './logic'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  if (JSON.stringify(actual) !== JSON.stringify(expected)) {
    throw new Error(`${message}。Expected: ${JSON.stringify(expected)}, received: ${JSON.stringify(actual)}`)
  }
}

const now = new Date('2026-08-18T04:00:00.000Z')

const range = buildBrisbaneDateRange(30, now)
assertEqual(range.endDate, '2026-08-18', '布里斯班日期范围应以今天结束')
assertEqual(range.startDate, '2026-07-20', '30 天范围应从 30 天前开始')

assertEqual(getDateRangeError('2026-08-01', '2026-08-18', now), null, '有效范围不应报错')
assertEqual(getDateRangeError('2026-08-18', '2026-08-01', now), '开始日期不能晚于结束日期', '开始晚于结束应报错')
assertEqual(getDateRangeError('2026-08-01', '2026-08-19', now), '结束日期不能晚于今天', '结束晚于今天应报错')
assertEqual(
  getDateRangeError('2025-08-01', '2026-08-18', now),
  '日期范围不能超过 366 天',
  '超过 366 天应报错',
)

assertDeepEqual(getDefaultContainerStatuses(), [], '默认不显式传状态，由后端应用全部非取消规则')

assertDeepEqual(
  buildSalesSelection('P-001'),
  { mode: 'included', includedProductCodes: ['P-001'], excludedProductCodes: [] },
  '销售选择应固定为仅包含当前商品',
)
assertDeepEqual(
  buildSalesScope('P-001'),
  { mode: 'currentProduct', productCode: 'P-001' },
  '销售范围应固定为当前商品',
)
assertDeepEqual(
  buildAllocationQuery('2026-06-01', '2026-06-30'),
  { startDate: '2026-06-01', endDate: '2026-06-30' },
  '配货查询应携带日期范围',
)

assertDeepEqual(
  mapContainerTableChangeToQuery({ current: 2, pageSize: 50 }, { field: 'loadingQuantity', order: 'descend' }),
  { pageNumber: 2, pageSize: 50, sortBy: 'loadingQuantity', sortDirection: 'desc' },
  '货柜分页排序应映射为服务端查询',
)
assertDeepEqual(
  mapContainerTableChangeToQuery({ current: 1, pageSize: 20 }, { order: null }),
  { pageNumber: 1, pageSize: 20, sortBy: 'effectiveArrivalDate', sortDirection: 'desc' },
  '取消排序时应回退到默认排序字段',
)
assertDeepEqual(
  mapContainerTableChangeToQuery({ current: 1, pageSize: 20 }, { field: 'totalAmount', order: 'ascend' }),
  { pageNumber: 1, pageSize: 20, sortBy: 'effectiveArrivalDate', sortDirection: 'desc' },
  '不在后端白名单内的排序字段应回退到默认排序',
)

const branches: WarehouseProductAllocationBranch[] = [
  {
    storeCode: 'S1',
    storeName: '布里斯班店',
    isActive: true,
    allocationQuantity: 3,
    allocationAmount: 30,
    orderCount: 1,
    firstAllocationDate: '2026-06-01',
    lastAllocationDate: '2026-06-02',
  },
  {
    storeCode: 'S2',
    storeName: 'Sunnybank',
    isActive: false,
    allocationQuantity: 5,
    allocationAmount: 50,
    orderCount: 2,
    firstAllocationDate: '2026-06-03',
    lastAllocationDate: '2026-06-04',
  },
]

assertDeepEqual(
  sumAllocationBranchAmounts(branches),
  { allocationQuantity: 8, allocationAmount: 80 },
  '分店筛选合计应只累加可加总的数量和金额',
)
assertEqual(filterAllocationBranches(branches, 'S1').length, 1, '分店关键字应本地过滤编码')
assertEqual(filterAllocationBranches(branches, 'sunny').length, 1, '分店关键字应本地过滤名称且忽略大小写')
assertDeepEqual(
  sumAllocationBranchAmounts(filterAllocationBranches(branches, 'S1')),
  { allocationQuantity: 3, allocationAmount: 30 },
  '当前筛选合计应只统计命中的分店',
)

assertEqual(formatQuantity(1234.5), '1,234.5', '数量应按澳洲格式显示')
assertEqual(formatQuantity(-2), '-2', '负销量应保留负号')
assertEqual(formatAustralianCurrency(1234.5), '$1,234.50', '金额应按澳洲格式显示')
assertEqual(formatAustralianCurrency(-5.98), '-$5.98', '负销售额应按澳洲格式显示')
assertEqual(formatChineseCurrency(1234.5), '¥1,234.50', '国内金额应按人民币格式显示')
assertEqual(formatAveragePrice(0, null), '--', '净数量为零时应显示 --')
assertEqual(formatAveragePrice(0, 12.5), '--', '净数量为零时即使有均价也应显示 --')
assertEqual(formatAveragePrice(5, null), '--', '均价缺失时应显示 --')
assertEqual(formatAveragePrice(5, 12.5), '$12.50', '有均价时应正常显示')

assertEqual(getContainerDetailPath('CONTAINER/A'), '/warehouse/container/detail/CONTAINER%2FA', '货柜详情路径应编码货柜编码')

console.log('warehouseProductRecords.logic.test: ok')
