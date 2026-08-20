import {
  getLocalSupplierProductSalesAnalysisOptions,
  queryLocalSupplierProductSalesAnalysisBranchDaily,
  queryLocalSupplierProductSalesAnalysisBranches,
  queryLocalSupplierProductSalesAnalysisCandidates,
  queryLocalSupplierProductSalesAnalysisInvoiceDetails,
  queryLocalSupplierProductSalesAnalysisProductDaily,
  queryLocalSupplierProductSalesAnalysisSummary,
} from './localSupplierProductSalesAnalysisService'

function equal<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) throw new Error(`${message}: expected ${String(expected)}, received ${String(actual)}`)
}

function deepEqual(actual: unknown, expected: unknown, message: string) {
  equal(JSON.stringify(actual), JSON.stringify(expected), message)
}

const originalFetch = globalThis.fetch
const captured: Array<{ url: string; init?: RequestInit }> = []

function response(payload: unknown) {
  return new Response(JSON.stringify(payload), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

try {
  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    captured.push({ url, init })
    if (url.includes('/options')) return response({ success: true, data: { WarehouseCategories: [{ Guid: 'cat-1', Name: '玩具' }], Suppliers: [{ Code: 'AU-1', Name: '澳洲供货商' }] } })
    if (url.endsWith('/candidates')) return response({ Success: true, Data: { Items: [{ ProductCode: 'LP-1', ItemNumber: '1001', ProductName: '本地玩具', ImageUrl: '/item.png' }], Total: 1, PageNumber: 2, PageSize: 20 } })
    if (url.endsWith('/summary')) return response({ success: true, data: { Totals: { PurchaseQuantity: 8, PurchaseAmount: 50, NetSalesQuantity: -2, NetSalesAmount: -12, SellThroughRate: null }, Items: [{ ProductCode: 'LP-1', Suppliers: [{ Code: 'AU-1', Name: '澳洲供货商' }], PurchaseQuantity: 8 }], Total: 1, PageNumber: 1, PageSize: 20 } })
    if (url.endsWith('/invoice-details')) return response({ success: true, data: { Items: [{ DetailGUID: 'D1', InvoiceNo: 'INV-1', PurchaseDate: '2026-08-18T00:00:00', Quantity: 3, PurchasePrice: 2.5, Amount: 7.5 }], Total: 1, PageNumber: 1, PageSize: 20 } })
    if (url.endsWith('/branches')) return response({ success: true, data: [{ BranchCode: 'S1', BranchName: '布里斯班店', NetSalesQuantity: 0, NetSalesAmount: 0, AverageUnitPrice: 0 }] })
    return response({ success: true, data: [{ Date: '2026-08-18T00:00:00', PurchaseQuantity: 3, PurchaseAmount: 7.5, NetSalesQuantity: -1, NetSalesAmount: -4, AverageUnitPrice: null }] })
  }) as typeof fetch

  const filter = { startDate: '2026-07-20', endDate: '2026-08-18', keyword: '玩具', categoryGuid: 'cat-1', supplierCode: 'AU-1', documentKeyword: 'INV' }
  const selection = { mode: 'included' as const, includedProductCodes: ['LP-1'], excludedProductCodes: [] }
  const options = await getLocalSupplierProductSalesAnalysisOptions()
  equal(options.data.warehouseCategories[0]?.guid, 'cat-1', '选项应归一化 PascalCase')
  equal(options.data.suppliers[0]?.code, 'AU-1', '供应商选项应归一化')

  const candidates = await queryLocalSupplierProductSalesAnalysisCandidates({ filter, selection, pageNumber: 2, pageSize: 20 })
  equal(candidates.data.items[0]?.productCode, 'LP-1', '候选商品应归一化')
  equal(candidates.data.pageNumber, 2, '候选分页应保留')
  deepEqual(JSON.parse(String(captured[1]?.init?.body)).filter, filter, '公共筛选字段必须按契约发送')

  const summary = await queryLocalSupplierProductSalesAnalysisSummary({ filter, selection, pageNumber: 1, pageSize: 20, forceRefresh: true })
  equal(summary.data.totals.netSalesQuantity, -2, '汇总净销量允许为负')
  equal(summary.data.totals.sellThroughRate, null, '可空售进比应保留 null')
  equal(JSON.parse(String(captured[2]?.init?.body)).forceRefresh, true, '刷新只在本轮请求携带 forceRefresh')

  const daily = await queryLocalSupplierProductSalesAnalysisProductDaily({ filter, selection, currentProductCode: 'LP-1' })
  equal(daily.data[0]?.date, '2026-08-18', '日期必须归一化为 YYYY-MM-DD')
  equal(daily.data[0]?.averageUnitPrice, null, '净销量为零或无均价时保留 null')

  const details = await queryLocalSupplierProductSalesAnalysisInvoiceDetails({ filter, selection, currentProductCode: 'LP-1', pageNumber: 1, pageSize: 20 })
  equal(details.data.items[0]?.purchaseDate, '2026-08-18', '明细日期必须归一化')

  const branches = await queryLocalSupplierProductSalesAnalysisBranches({ filter, selection, currentProductCode: 'LP-1' })
  equal(branches.data[0]?.averageUnitPrice, null, '净销量零的分店均价应归一化为空')
  const branchDaily = await queryLocalSupplierProductSalesAnalysisBranchDaily({ filter, selection, currentProductCode: 'LP-1', branchCode: 'S1' })
  equal(branchDaily.data[0]?.date, '2026-08-18', '分店日趋势日期必须归一化')
  equal(captured.map((item) => item.url).filter((url) => url.includes('/local-supplier-product-sales-analysis/')).length, 7, '必须命中全部七个契约端点')
} finally {
  globalThis.fetch = originalFetch
}

console.log('localSupplierProductSalesAnalysisService.test: ok')
