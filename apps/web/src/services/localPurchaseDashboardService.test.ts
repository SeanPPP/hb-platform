import {
  __localPurchaseDashboardServiceTestOnly,
  getLocalPurchaseDashboard,
  getLocalPurchaseSupplierDetails,
} from './localPurchaseDashboardService'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  assertEqual(JSON.stringify(actual), JSON.stringify(expected), message)
}

const rawDashboard = {
  endMonth: '2026-07',
  months: ['2025-08', '2026-06', '2026-07'],
  warehouseTotal: 200,
  localSupplierTotal: 110,
  totalAmount: 310,
  stores: [
    {
      storeCode: 'S001',
      storeName: 'Bankstown',
      warehouseTotal: 200,
      localSupplierTotal: 110,
      totalAmount: 310,
      months: [
        { month: '2026-06', warehouseAmount: 0, localSupplierAmount: 0, totalAmount: 0, salesAmount: 450 },
        { month: '2026-07', warehouseAmount: 200, localSupplierAmount: 110, totalAmount: 310, SalesAmount: 900 },
      ],
    },
    { StoreName: '缺少编码' },
  ],
}
const rawDashboardSnapshot = JSON.stringify(rawDashboard)
const normalizedDashboard = __localPurchaseDashboardServiceTestOnly.normalizeDashboardResponse(
  rawDashboard,
  '2026-07',
)

assertEqual(normalizedDashboard.localSupplierAmount, 110, '本地供应商金额已经未税，normalizer 不得再除以 1.1')
assertEqual(normalizedDashboard.stores.length, 1, '主表 normalizer 应过滤缺少分店编码的行')
assertDeepEqual(
  normalizedDashboard.stores[0].monthlyAmounts,
  [
    { month: '2025-08', warehouseAmount: 0, localSupplierAmount: 0, totalAmount: 0, salesAmount: 0 },
    { month: '2026-06', warehouseAmount: 0, localSupplierAmount: 0, totalAmount: 0, salesAmount: 450 },
    { month: '2026-07', warehouseAmount: 200, localSupplierAmount: 110, totalAmount: 310, salesAmount: 900 },
  ],
  '稀疏月份应补零，且营业额应兼容 camelCase 与 PascalCase',
)
assertEqual(JSON.stringify(rawDashboard), rawDashboardSnapshot, '主表归一化不得修改接口源数组')

const normalizedDetails = __localPurchaseDashboardServiceTestOnly.normalizeSupplierDetailResponse({
  storeCode: 'S001',
  storeName: 'Bankstown',
  endMonth: '2026-07',
  months: ['2026-06', '2026-07'],
  warehouseTotal: 200,
  localSupplierTotal: 110,
  totalAmount: 310,
  suppliers: [
    {
      sourceCode: 'WAREHOUSE_ORDER',
      supplierName: 'Warehouse Orders',
      sourceType: 'WAREHOUSE_ORDER',
      totalAmount: 200,
      months: [{ month: '2026-07', amount: 200 }],
    },
    {
      sourceCode: 'SUP-1',
      supplierCode: 'SUP-1',
      supplierName: 'Supplier One',
      sourceType: 'LOCAL_SUPPLIER',
      totalAmount: 110,
      months: [{ month: '2026-07', amount: 110 }],
    },
  ],
}, 'S001', '2026-07')

const sourceCodeCollisionDetails = __localPurchaseDashboardServiceTestOnly.normalizeSupplierDetailResponse({
  storeCode: 'S001',
  months: ['2026-07'],
  suppliers: [
    {
      sourceCode: 'WAREHOUSE_ORDER',
      sourceType: 'WAREHOUSE_ORDER',
      supplierName: '虚拟仓库订单',
      months: [],
    },
    {
      sourceCode: 'WAREHOUSE_ORDER',
      supplierCode: 'WAREHOUSE_ORDER',
      sourceType: 'LOCAL_SUPPLIER',
      supplierName: '真实同名编码供应商',
      months: [],
    },
  ],
}, 'S001', '2026-07')

assertEqual(sourceCodeCollisionDetails.suppliers[0].isWarehouse, true, '仓库来源应由 sourceType 识别')
assertEqual(
  sourceCodeCollisionDetails.suppliers[1].isWarehouse,
  false,
  '真实本地供应商即使编码为 WAREHOUSE_ORDER 也不得被误判为仓库',
)
assertEqual(
  new Set(sourceCodeCollisionDetails.suppliers.map((item) => (item as unknown as Record<string, unknown>).rowKey)).size,
  2,
  '仓库虚拟行与同编码真实供应商必须生成不同稳定 row key',
)

const unassignedCodeCollisionDetails = __localPurchaseDashboardServiceTestOnly.normalizeSupplierDetailResponse({
  storeCode: 'S001',
  months: ['2026-07'],
  suppliers: [
    {
      sourceCode: 'UNASSIGNED',
      sourceType: 'LOCAL_SUPPLIER',
      supplierName: '未匹配供应商',
      IsUnassigned: true,
      months: [],
    },
    {
      sourceCode: 'UNASSIGNED',
      supplierCode: 'UNASSIGNED',
      sourceType: 'LOCAL_SUPPLIER',
      supplierName: 'Real Unassigned Pty Ltd',
      isUnassigned: false,
      months: [],
    },
  ],
}, 'S001', '2026-07')

assertEqual(
  (unassignedCodeCollisionDetails.suppliers[0] as unknown as Record<string, unknown>).isUnassigned,
  true,
  'PascalCase IsUnassigned 应归一化为未匹配供应商标记',
)
assertEqual(
  (unassignedCodeCollisionDetails.suppliers[1] as unknown as Record<string, unknown>).isUnassigned,
  false,
  '真实编码为 UNASSIGNED 的供应商必须保留 false 标记',
)
assertEqual(
  new Set(unassignedCodeCollisionDetails.suppliers.map((item) => item.rowKey)).size,
  2,
  '真实与虚拟 UNASSIGNED 来源必须生成不同稳定 row key',
)

assertEqual(normalizedDetails.suppliers[0].sourceCode, 'WAREHOUSE_ORDER', '仓库虚拟来源编码应保留')
assertEqual(
  (normalizedDetails.suppliers[1] as unknown as Record<string, unknown>).localSupplierAmount,
  undefined,
  '供应商行不应伪造 GST 或金额拆分字段',
)
assertDeepEqual(
  normalizedDetails.suppliers[1].monthlyAmounts,
  [{ month: '2026-06', amount: 0 }, { month: '2026-07', amount: 110 }],
  '供应商抽屉稀疏月份应补零',
)

const originalFetch = globalThis.fetch
let dashboardUrl = ''
let supplierUrl = ''
let dashboardSignal: AbortSignal | null | undefined

try {
  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    if (url.includes('/stores/')) {
      supplierUrl = url
      return new Response(JSON.stringify({ success: true, data: { Stores: [], Suppliers: [], Months: [] } }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })
    }
    dashboardUrl = url
    dashboardSignal = init?.signal
    return new Response(JSON.stringify({ success: true, data: { Stores: [], Months: [] } }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })
  }) as typeof fetch

  const abortController = new AbortController()
  await getLocalPurchaseDashboard('2026-07', abortController.signal)
  await getLocalPurchaseSupplierDetails('STORE/A', '2026-07')

  assertEqual(
    new URL(dashboardUrl, 'https://example.test').searchParams.get('endMonth'),
    '2026-07',
    '主看板接口应传递结束月份',
  )
  assertEqual(dashboardSignal, abortController.signal, '主看板接口应把 AbortSignal 传给请求层')
  assertEqual(
    supplierUrl.includes('/stores/STORE%2FA/suppliers?endMonth=2026-07'),
    true,
    '供应商接口应编码分店并传递结束月份',
  )
} finally {
  globalThis.fetch = originalFetch
}

console.log('localPurchaseDashboardService.test: ok')
