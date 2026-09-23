import assert from 'node:assert/strict'
import { formatSupplyExpected } from './formatSupplyExpected'

// 只关心键与插值参数：文案本身由懒注册的资源决定。
const t = ((key: string, params?: Record<string, unknown>) =>
  params ? `${key}:${JSON.stringify(params)}` : key) as unknown as Parameters<typeof formatSupplyExpected>[1]

const thisYear = new Date().getFullYear()

assert.equal(
  formatSupplyExpected({ expectedFrom: null, expectedTo: null, expectedPrecision: 'Unknown', isOverdue: false }, t),
  'supplyNotice.expectedUnknown',
  '时间待定',
)
assert.equal(
  formatSupplyExpected({ expectedFrom: `${thisYear}-10-05`, expectedTo: `${thisYear}-10-05`, expectedPrecision: 'Day', isOverdue: false }, t),
  '10月5日',
  '同年某日不带年份',
)
assert.equal(
  formatSupplyExpected({ expectedFrom: `${thisYear + 1}-10-05`, expectedTo: `${thisYear + 1}-10-05`, expectedPrecision: 'Day', isOverdue: false }, t),
  `${thisYear + 1}年10月5日`,
  '跨年某日带年份',
)
assert.equal(
  formatSupplyExpected({ expectedFrom: `${thisYear}-10-05`, expectedTo: `${thisYear}-10-10`, expectedPrecision: 'Range', isOverdue: false }, t),
  'supplyNotice.expectedRange:{"from":"10月5日","to":"10月10日"}',
  '范围',
)
assert.equal(
  formatSupplyExpected({ expectedFrom: `${thisYear}-10-01`, expectedTo: `${thisYear}-10-31`, expectedPrecision: 'Month', isOverdue: false }, t),
  'supplyNotice.expectedMonth:{"month":10}',
  '同年某月',
)
assert.equal(
  formatSupplyExpected({ expectedFrom: `${thisYear + 1}-09-01`, expectedTo: `${thisYear + 1}-09-30`, expectedPrecision: 'Month', isOverdue: false }, t),
  `supplyNotice.expectedMonthWithYear:{"year":${thisYear + 1},"month":9}`,
  '跨年某月带年份',
)
// 逾期优先于一切日期：不再把过期日期当承诺展示。
assert.equal(
  formatSupplyExpected({ expectedFrom: '2020-01-01', expectedTo: '2020-01-01', expectedPrecision: 'Day', isOverdue: true }, t),
  'supplyNotice.expectedOverdue',
  '逾期',
)

console.log('formatSupplyExpected.test: ok')
