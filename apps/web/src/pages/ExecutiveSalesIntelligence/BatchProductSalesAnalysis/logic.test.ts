import assert from 'node:assert/strict'
import { escapeCsvCell, formatCsvRow, getBatchProductSalesDateRangeError } from './logic'

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

console.log('BatchProductSalesAnalysis.logic.test: ok')
