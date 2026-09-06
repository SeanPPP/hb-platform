import assert from 'node:assert/strict'
import { fetchSalesDetailSection, type SalesDetailQuery } from './reportService'
const original = globalThis.fetch
const controller = new AbortController()
const query: SalesDetailQuery = { kind: 'china', startDate: '2026-09-01', endDate: '2026-09-06', compareMode: 'ByWeek',
  branchCodes: ['S1', 'S2'], selectedBranchCode: 'S1', selectedSupplierCode: 'HB215', selectedProductCode: 'P1', search: '玛索 pen', pageIndex: 3, pageSize: 20 }
globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  const url = new URL(String(input), 'http://localhost')
  assert.equal(url.pathname, '/api/react/v1/dashboard/sales-detail-columns')
  assert.equal(url.searchParams.get('section'), 'products')
  assert.equal(url.searchParams.get('selectedProductCode'), null)
  assert.equal(url.searchParams.get('selectedBranchCode'), 'S1')
  assert.deepEqual(url.searchParams.getAll('branchCodes'), ['S1', 'S2'])
  assert.equal(url.searchParams.get('search'), '玛索 pen')
  assert.equal(url.searchParams.get('pageIndex'), '3')
  assert.equal(init?.signal, controller.signal)
  assert.equal(init?.credentials, 'include')
  return new Response(JSON.stringify({ success: true, statisticStatus: 'Fresh', cacheVersion: 'version-a',
    data: { total: 201, rows: [{ Code: 'P1', Revenue: 10, GrossProfit: null, GrossMarginRate: null, CompareGrossProfit: 0, CompareGrossMarginRate: 0 }] } }), { headers: { 'content-type': 'application/json' } })
}) as typeof fetch
try {
  const result = await fetchSalesDetailSection('products', query, controller.signal)
  assert.equal(result.data.total, 201)
  assert.equal(result.data.rows[0].grossProfit, null)
  assert.equal(result.data.rows[0].compareGrossProfit, 0)
  assert.equal(result.data.rows[0].compareGrossMarginRate, 0)
  assert.equal(result.cacheVersion, 'version-a')
  console.log('销售明细真实请求封装与取消信号：通过')
} finally { globalThis.fetch = original }
