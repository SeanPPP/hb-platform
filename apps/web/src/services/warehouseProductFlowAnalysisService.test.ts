import {
  getWarehouseProductFlowOptions,
  queryWarehouseProductFlowCandidates,
  queryWarehouseProductFlowOrderShipmentDaily,
  queryWarehouseProductFlowSalesDaily,
  queryWarehouseProductFlowSummary,
} from './warehouseProductFlowAnalysisService'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) throw new Error(`${message}。Expected: ${String(expected)}, received: ${String(actual)}`)
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  assertEqual(JSON.stringify(actual), JSON.stringify(expected), message)
}

const originalFetch = globalThis.fetch
const captured: Array<{ url: string; init?: RequestInit }> = []

function jsonResponse(payload: unknown) {
  return new Response(JSON.stringify(payload), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

try {
  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    captured.push({ url, init })
    if (url.endsWith('/options') || url.endsWith('/options?forceRefresh=true')) return jsonResponse({ success: true, data: { DomesticSuppliers: [{ Code: 'CN-1', Name: '优品玩具' }] } })
    if (url.endsWith('/candidates')) return jsonResponse({ success: true, data: { Items: [{ ProductCode: 'HB-1', ItemNumber: '001', ProductName: '粉色玩具', Barcode: '123' }], Total: 1, PageNumber: 1, PageSize: 20 } })
    if (url.endsWith('/summary')) return jsonResponse({ success: true, data: { Totals: { InboundQuantity: 12 }, CurrentProduct: { ProductCode: 'HB-1', Metrics: { InboundQuantity: 12 } }, Items: [{ ProductCode: 'HB-1', Metrics: { InboundQuantity: 12 } }], Total: 1, PageNumber: 1, PageSize: 20 } })
    return jsonResponse({ success: true, data: [{ Date: '2026-08-17T00:00:00', OrderedQuantity: 4, ShippedQuantity: 3, NetSalesQuantity: -1, NetSalesAmount: -6, AverageUnitPrice: 6 }] })
  }) as typeof fetch

  const filter = { keyword: '玩具', warehouseCategoryGuids: ['cat-1'], supplierCodes: ['CN-1'], documentKeyword: 'OOLU' }
  const periods = {
    containerPeriod: { startDate: '2025-09-01', endDate: '2026-08-18' },
    orderShipmentPeriod: { startDate: '2026-03-01', endDate: '2026-08-18' },
    salesPeriod: { startDate: '2026-03-01', endDate: '2026-08-18' },
  }
  const selection = { mode: 'included' as const, includedProductCodes: ['HB-1'], excludedProductCodes: [] }

  const options = await getWarehouseProductFlowOptions(filter)
  assertEqual(captured[0].url, '/api/react/v1/dashboard/warehouse-product-flow-analysis/options', 'options 必须保持 GET 路由且不受主档筛选影响')
  assertDeepEqual(options.data.domesticSuppliers, [{ code: 'CN-1', name: '优品玩具' }], '国内供应商选项必须归一化')

  await getWarehouseProductFlowOptions(undefined, undefined, true)
  assertEqual(captured[1].url, '/api/react/v1/dashboard/warehouse-product-flow-analysis/options?forceRefresh=true', '刷新全部必须显式绕过固定 options 缓存')

  const candidates = await queryWarehouseProductFlowCandidates({ filter, pageNumber: 1, pageSize: 20, sortBy: 'itemNumber', sortDirection: 'asc', forceRefresh: true })
  assertDeepEqual(JSON.parse(String(captured[2].init?.body)), {
    filter, pageNumber: 1, pageSize: 20, sortBy: 'itemNumber', sortDirection: 'asc', forceRefresh: true,
  }, 'candidates 必须使用独立 CandidateRequest，不能包含 periods、selection 或当前商品')
  assertEqual('metrics' in candidates.data.items[0], false, '候选商品主档不得归一化区间指标')

  const summary = await queryWarehouseProductFlowSummary({ filter, periods, selection, currentProductCode: 'HB-1', pageNumber: 1, pageSize: 20, sortBy: 'itemNumber', sortDirection: 'asc' })
  assertDeepEqual(JSON.parse(String(captured[3].init?.body)), {
    filter, periods, selection, currentProductCode: 'HB-1', pageNumber: 1, pageSize: 20, sortBy: 'itemNumber', sortDirection: 'asc',
  }, 'summary 必须携带三套 periods、选择和分页')
  assertEqual(summary.data.items[0].metrics.inboundQuantity, 12, 'summary 商品必须保留指标')
  assertEqual(summary.data.currentProduct?.productCode, 'HB-1', '当前商品必须独立于 summary 分页返回')

  const productRequest = { filter, periods, currentProductCode: 'HB-1' }
  const orderShipmentDaily = await queryWarehouseProductFlowOrderShipmentDaily(productRequest)
  const salesDaily = await queryWarehouseProductFlowSalesDaily(productRequest)
  assertEqual(captured[4].url.endsWith('/order-shipment-daily'), true, '订货发货趋势必须独立请求')
  assertEqual(captured[5].url.endsWith('/sales-daily'), true, '销售趋势必须独立请求')
  assertEqual(orderShipmentDaily.data[0].orderedQuantity, 4, '订货发货趋势必须归一化 daily orderedQuantity')
  assertEqual(salesDaily.data[0].netSalesQuantity, -1, '销售趋势必须归一化 daily netSalesQuantity')

  console.log('warehouseProductFlowAnalysisService.test: ok')
} finally {
  globalThis.fetch = originalFetch
}
