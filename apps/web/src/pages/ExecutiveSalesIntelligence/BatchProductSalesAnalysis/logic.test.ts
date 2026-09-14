import assert from 'node:assert/strict'
import type { BatchSalesBranch, BatchSalesDaily, BatchSalesMetrics } from '../../../types/batchProductSalesAnalysis'
import {
  escapeCsvCell,
  formatCsvRow,
  getBatchProductSalesDateRangeError,
  hasBatchProductSalesDailyActivity,
  sortBatchProductSalesBranchesByQuantity,
} from './logic'

function metrics(overrides: Partial<BatchSalesMetrics> = {}): BatchSalesMetrics {
  return {
    quantity: 0,
    regularQuantity: 0,
    discountQuantity: 0,
    unknownQuantity: 0,
    returnQuantity: 0,
    salesAmount: 0,
    discountStatus: 'complete',
    originalPriceMin: null,
    originalPriceMax: null,
    discountPriceMin: null,
    discountPriceMax: null,
    ...overrides,
  }
}

function daily(overrides: Partial<BatchSalesMetrics> = {}): BatchSalesDaily {
  return { date: '2026-09-14', metrics: metrics(overrides) }
}

function branch(code: string, quantity: number): BatchSalesBranch {
  return { branchCode: code, branchName: code, metrics: metrics({ quantity }), daily: [] }
}

assert.equal(getBatchProductSalesDateRangeError('2025-08-18', '2026-08-18', '2026-08-18'), undefined, '含首尾共 366 天的范围必须允许')
assert.equal(getBatchProductSalesDateRangeError('2025-08-17', '2026-08-18', '2026-08-18'), '日期范围不能超过 366 天', '超过 366 天必须拒绝')
assert.equal(getBatchProductSalesDateRangeError('2026-08-19', '2026-08-18', '2026-08-18'), '开始日期不能晚于结束日期', '开始日期晚于结束日期必须拒绝')
assert.equal(getBatchProductSalesDateRangeError('2026-02-31', '2026-08-18', '2026-08-18'), '日期格式无效', '不存在的日期必须拒绝')
assert.equal(getBatchProductSalesDateRangeError('2026-08-18', '2026-08-19', '2026-08-18'), '结束日期不能晚于今天', '结束日期晚于传入业务日期必须拒绝')

assert.equal(escapeCsvCell('plain'), 'plain', '普通单元格不应额外转义')
assert.equal(escapeCsvCell('a,"b"'), '"a,""b"""', 'CSV 引号和逗号必须按 RFC 4180 转义')
assert.equal(escapeCsvCell('line1\nline2'), '"line1\nline2"', 'CSV 换行必须被包裹')
assert.equal(escapeCsvCell('=SUM(A1:A2)'), "'=SUM(A1:A2)", '等号开头必须防止公式执行')
assert.equal(escapeCsvCell('  +1'), "'  +1", '带前导空白的公式前缀也必须防护')
assert.equal(escapeCsvCell('-12'), "'-12", '负号开头必须防止 CSV 被解释为公式')
assert.equal(escapeCsvCell('@cmd'), "'@cmd", '@ 开头必须防止 CSV 被解释为公式')
assert.equal(escapeCsvCell(-3), '-3', '数值型退货数量必须保持为可计算数字')
assert.equal(escapeCsvCell('-3'), "'-3", '字符串型负数仍必须防止 CSV 被解释为公式')
assert.equal(escapeCsvCell(Number.NaN), '', '非有限数值不得导出为错误数值')
assert.equal(formatCsvRow(['货号', '=A1', 'a,b']), "货号,'=A1,\"a,b\"", '整行导出必须复用安全单元格转义')

assert.equal(hasBatchProductSalesDailyActivity(daily()), false, '全零补齐日必须隐藏')
assert.equal(hasBatchProductSalesDailyActivity(daily({ regularQuantity: 3, quantity: 3 })), true, '正价销售必须保留')
assert.equal(hasBatchProductSalesDailyActivity(daily({ discountQuantity: 2, quantity: 2 })), true, '折扣销售必须保留')
assert.equal(hasBatchProductSalesDailyActivity(daily({ unknownQuantity: 1, quantity: 1 })), true, '未知分类销售必须保留')
assert.equal(hasBatchProductSalesDailyActivity(daily({ regularQuantity: -2, quantity: -2 })), true, '纯退货日必须保留')
assert.equal(hasBatchProductSalesDailyActivity(daily({ regularQuantity: 4, discountQuantity: -4, quantity: 0 })), true, '正负分类相互抵消日必须保留')
assert.equal(hasBatchProductSalesDailyActivity(daily({ returnQuantity: 4 })), true, '同类销售与退货抵消后，非零退货量仍证明当天有真实活动')
assert.equal(hasBatchProductSalesDailyActivity(daily({ salesAmount: 12.5 })), true, '非零销售额也应避免把真实业务日误认为补齐日')
assert.equal(hasBatchProductSalesDailyActivity(daily({ quantity: 6, discountStatus: 'pending' })), true, '待统计且分类不可用时，非零总销量必须保留')
assert.equal(hasBatchProductSalesDailyActivity(daily({ quantity: -1, discountStatus: 'pending' })), true, '待统计纯退货的非零总销量必须保留')
assert.equal(hasBatchProductSalesDailyActivity(daily({ quantity: 0, discountStatus: 'pending' })), false, '待统计的全零补齐日仍必须隐藏')

const branches = [branch('B', 4), branch('A', 9), branch('C', 4), branch('D', -2)]
const sortedBranches = sortBatchProductSalesBranchesByQuantity(branches)
assert.deepEqual(sortedBranches.map((item) => item.branchCode), ['A', 'B', 'C', 'D'], '分店必须按销量稳定降序排列')
assert.deepEqual(branches.map((item) => item.branchCode), ['B', 'A', 'C', 'D'], '排序不得修改服务端原始数组')
assert.notEqual(sortedBranches, branches, '排序结果必须是新的数组')

console.log('BatchProductSalesAnalysis.logic.test: ok')
