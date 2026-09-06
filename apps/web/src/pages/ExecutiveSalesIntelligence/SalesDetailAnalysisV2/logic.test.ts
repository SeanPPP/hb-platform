import assert from 'node:assert/strict'
import { applyKeyword, emptySelection, initialDetailState, resizeColumns, selectDimension, sumProductPage } from './logic'
import { normalizeSalesDetailRow, sectionQuery, type SalesDetailQuery } from './reportService'

const selected = { ...emptySelection, branch: 'OR', supplier: 'HB215', product: 'P1', page: 3 }
assert.deepEqual(selectDimension(selected, 'supplier', 'HB246'), { ...selected, supplier: 'HB246', page: 1 })
assert.equal(selectDimension(selected, 'branch', 'OR').branch, undefined)
assert.equal(selectDimension(selected, 'product', 'P2').page, 3)
assert.equal(applyKeyword(selected, ' 玛索   pen ').product, undefined)
assert.equal(applyKeyword(selected, ' 玛索   pen ').keyword, '玛索 pen')
const query: SalesDetailQuery = { kind: 'china', startDate: '2026-09-01', endDate: '2026-09-06', compareMode: 'ByWeek',
  selectedBranchCode: 'OR', selectedSupplierCode: 'HB215', selectedProductCode: 'P1', search: '玛索 pen', pageIndex: 3, pageSize: 20 }
assert.equal(sectionQuery('suppliers', query).selectedSupplierCode, undefined)
assert.equal(sectionQuery('suppliers', query).selectedBranchCode, 'OR')
assert.equal(sectionQuery('suppliers', query).selectedProductCode, 'P1')
assert.equal(sectionQuery('suppliers', query).search, undefined)
assert.equal(sectionQuery('branches', query).selectedBranchCode, undefined)
assert.equal(sectionQuery('products', query).selectedProductCode, undefined)
assert.equal(sectionQuery('summary', query).selectedProductCode, 'P1')
assert.equal(sectionQuery('summary', query).pageIndex, undefined)
const row = normalizeSalesDetailRow({ Code: 'P1', Name: 'Pen', Revenue: 40, GrossProfit: 0, GrossMarginRate: 0, CompareGrossProfit: null })
assert.equal(row.grossProfit, 0)
assert.equal(row.compareGrossProfit, null)
assert.equal(row.orderCount, null, '不可用客单数不能伪装成0')
const summary = sumProductPage([row, normalizeSalesDetailRow({ revenue: 60, grossProfit: 30 })])
assert.equal(summary.grossMarginRate, 0.3, '汇总毛利率加权计算，不平均各行比率')
assert.equal(sumProductPage([row, normalizeSalesDetailRow({ revenue: 60 })]).grossProfit, null)
assert.deepEqual(resizeColumns([28,27,45], 0, 100), [37,18,45])
assert.equal(initialDetailState('?kind=china&branch=OR&startDate=2026-09-01&endDate=2026-09-06').selection.branch, 'OR')
assert.equal(initialDetailState('?startDate=2026-02-30&endDate=2026-03-01').dates.quick, 'today')
assert.equal(initialDetailState('?kind=china&compare=0').dates.compare, false)
console.log('销售明细双向筛选、全量查询参数、毛利和列宽：通过')
