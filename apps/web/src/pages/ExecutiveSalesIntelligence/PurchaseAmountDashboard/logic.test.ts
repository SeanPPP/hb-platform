import type { LocalPurchaseSupplierSummary, LocalPurchaseStoreSummary } from '../../../types/localPurchaseDashboard'

const originalNumberFormat = Intl.NumberFormat
let numberFormatConstructionCount = 0
const trackingNumberFormat = new Proxy(originalNumberFormat, {
  construct(target, args, newTarget) {
    numberFormatConstructionCount += 1
    return Reflect.construct(target, args, newTarget)
  },
})

Object.defineProperty(Intl, 'NumberFormat', {
  configurable: true,
  writable: true,
  value: trackingNumberFormat,
})

const {
  buildRollingMonths,
  buildPurchaseMonthRows,
  createLatestRequestGuard,
  filterPurchaseStores,
  formatPurchaseAmount,
  getPurchaseMatrixScroll,
  getPurchaseMonthColumnLayout,
  getPurchaseStoreMonthAmount,
  getSupplierDetailScroll,
  getSupplierDisplayName,
  resolvePurchaseReportViewState,
  sortPurchaseMonthsDescending,
  sortPurchaseSuppliers,
} = await import('./logic')

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  assertEqual(JSON.stringify(actual), JSON.stringify(expected), message)
}

assertEqual(numberFormatConstructionCount, 1, '金额格式化器应在模块加载时只创建一次')
assertEqual(formatPurchaseAmount(1234.5), '$1,234.50', '金额应按澳元格式显示')
assertEqual(formatPurchaseAmount(0), '$0.00', '零金额应按澳元格式显示')
assertEqual(numberFormatConstructionCount, 1, '重复格式化金额时不得重新创建 Intl.NumberFormat')

Object.defineProperty(Intl, 'NumberFormat', {
  configurable: true,
  writable: true,
  value: originalNumberFormat,
})

assertDeepEqual(
  buildRollingMonths('2026-07'),
  [
    '2025-08', '2025-09', '2025-10', '2025-11', '2025-12', '2026-01',
    '2026-02', '2026-03', '2026-04', '2026-05', '2026-06', '2026-07',
  ],
  '结束月份应生成从旧到新的连续 12 个月',
)

const sourceMonths = ['2025-11', '2025-12', '2026-01', '2026-02']
assertDeepEqual(
  sortPurchaseMonthsDescending(sourceMonths),
  ['2026-02', '2026-01', '2025-12', '2025-11'],
  '展示层月份应跨年按降序排列',
)
assertDeepEqual(
  buildPurchaseMonthRows(sourceMonths),
  [{ month: '2026-02' }, { month: '2026-01' }, { month: '2025-12' }, { month: '2025-11' }],
  '主表应构建月份为行的数据源',
)
assertDeepEqual(
  sourceMonths,
  ['2025-11', '2025-12', '2026-01', '2026-02'],
  '展示层月份降序不得修改接口源数组',
)

assertEqual(
  resolvePurchaseReportViewState({ loading: false, hasError: true, hasReport: false, hasRows: false }),
  'error',
  '主表或抽屉请求失败时错误态应排除 Empty',
)
assertEqual(
  resolvePurchaseReportViewState({ loading: true, hasError: false, hasReport: false, hasRows: false }),
  'loading',
  '未取得成功报表前应保持加载态而不是零值或 Empty',
)
assertEqual(
  resolvePurchaseReportViewState({ loading: false, hasError: false, hasReport: true, hasRows: false }),
  'empty',
  '只有成功报表的空行集合才应展示 Empty',
)
assertEqual(
  resolvePurchaseReportViewState({ loading: false, hasError: false, hasReport: true, hasRows: true }),
  'ready',
  '成功且有数据时应展示表格',
)
assertDeepEqual(
  getPurchaseMonthColumnLayout(),
  { key: 'month', fixed: 'left', width: 112 },
  '月份列配置应明确固定在左侧',
)
assertDeepEqual(
  getPurchaseMatrixScroll(4),
  { x: 848, y: 'max(320px, calc(100vh - 520px))' },
  '主表滚动配置应按动态分店数计算横向宽度并保留自适应纵向高度',
)
assertDeepEqual(
  getSupplierDetailScroll(12),
  { x: 1952 },
  '供应商抽屉横向滚动宽度应覆盖固定列、月份列和合计列',
)

assertEqual(
  getSupplierDisplayName(
    { rowKey: 'WAREHOUSE_ORDER:WAREHOUSE_ORDER', sourceType: 'WAREHOUSE_ORDER', sourceCode: 'WAREHOUSE_ORDER', supplierName: 'Warehouse Orders', totalAmount: 0, isWarehouse: true } as LocalPurchaseSupplierSummary,
    { warehouse: '仓库订单', unassigned: '未匹配供应商' },
  ),
  '仓库订单',
  '仓库虚拟来源名称应使用当前语言文案',
)
assertEqual(
  getSupplierDisplayName(
    { rowKey: 'LOCAL_SUPPLIER:UNASSIGNED', sourceType: 'LOCAL_SUPPLIER', sourceCode: 'UNASSIGNED', supplierName: '未匹配供应商', totalAmount: 0, isWarehouse: false, isUnassigned: true } as LocalPurchaseSupplierSummary,
    { warehouse: 'Warehouse Orders', unassigned: 'Unassigned Supplier' },
  ),
  'Unassigned Supplier',
  '未匹配供应商名称应使用当前语言文案',
)
assertEqual(
  getSupplierDisplayName(
    { rowKey: 'LOCAL_SUPPLIER:UNASSIGNED', sourceType: 'LOCAL_SUPPLIER', sourceCode: 'UNASSIGNED', supplierCode: 'UNASSIGNED', supplierName: 'Real Unassigned Pty Ltd', totalAmount: 0, isWarehouse: false, isUnassigned: false } as LocalPurchaseSupplierSummary,
    { warehouse: 'Warehouse Orders', unassigned: 'Unassigned Supplier' },
  ),
  'Real Unassigned Pty Ltd',
  '真实编码为 UNASSIGNED 的供应商应显示真实名称',
)
assertEqual(
  getSupplierDisplayName(
    { rowKey: 'LOCAL_SUPPLIER:SUP-1', sourceType: 'LOCAL_SUPPLIER', sourceCode: 'SUP-1', supplierName: 'Supplier One', totalAmount: 0, isWarehouse: false } as LocalPurchaseSupplierSummary,
    { warehouse: 'Warehouse Orders', unassigned: 'Unassigned Supplier' },
  ),
  'Supplier One',
  '普通供应商应保留后端名称',
)

const stores = [
  {
    storeCode: 'S001',
    storeName: 'Bankstown',
    monthlyAmounts: [
      { month: '2026-01', warehouseAmount: 120, localSupplierAmount: 30, totalAmount: 150 },
    ],
  },
  { storeCode: 'S002', storeName: 'Charlestown', monthlyAmounts: [] },
] as LocalPurchaseStoreSummary[]
assertDeepEqual(
  filterPurchaseStores(stores, ['S002', 'UNKNOWN']).map((item) => item.storeCode),
  ['S002'],
  '分店多选应保留已知编码并忽略未知编码',
)
assertDeepEqual(
  filterPurchaseStores(stores, []).map((item) => item.storeCode),
  ['S001', 'S002'],
  '清空分店多选应恢复全部分店',
)
assertDeepEqual(
  getPurchaseStoreMonthAmount(stores[0], '2026-01'),
  { month: '2026-01', warehouseAmount: 120, localSupplierAmount: 30, totalAmount: 150, salesAmount: 0 },
  '金额矩阵应按分店与月份映射仓库、本地、合计和营业额',
)
assertDeepEqual(
  getPurchaseStoreMonthAmount(stores[0], '2025-12'),
  { month: '2025-12', warehouseAmount: 0, localSupplierAmount: 0, totalAmount: 0, salesAmount: 0 },
  '缺失月份金额应映射为包含营业额的零值四项',
)
assertDeepEqual(
  stores[0].monthlyAmounts,
  [{ month: '2026-01', warehouseAmount: 120, localSupplierAmount: 30, totalAmount: 150 }],
  '金额读取不得修改分店月度源数组',
)

const suppliers = [
  { sourceCode: 'SUP-B', supplierCode: 'SUP-B', supplierName: 'B', totalAmount: 500, isWarehouse: false },
  { sourceCode: 'WAREHOUSE_ORDER', supplierName: '仓库订单', totalAmount: 100, isWarehouse: true },
  { sourceCode: 'SUP-A', supplierCode: 'SUP-A', supplierName: 'A', totalAmount: 900, isWarehouse: false },
] as LocalPurchaseSupplierSummary[]
assertDeepEqual(
  sortPurchaseSuppliers(suppliers).map((item) => item.sourceCode),
  ['WAREHOUSE_ORDER', 'SUP-A', 'SUP-B'],
  '仓库订单应置顶，本地供应商应按期间合计降序',
)
assertDeepEqual(
  suppliers.map((item) => item.sourceCode),
  ['SUP-B', 'WAREHOUSE_ORDER', 'SUP-A'],
  '供应商排序不得原地修改接口响应',
)

const guard = createLatestRequestGuard()
const firstRequest = guard.begin()
const secondRequest = guard.begin()
assertEqual(guard.isLatest(firstRequest), false, '新请求开始后旧请求必须失效')
assertEqual(guard.isLatest(secondRequest), true, '最新请求应允许更新状态')
guard.invalidate()
assertEqual(guard.isLatest(secondRequest), false, '关闭 Drawer 或卸载页面后在途请求必须失效')

console.log('purchaseAmountDashboard.logic.test: ok')
