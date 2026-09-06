import assert from 'node:assert/strict'
import { completeSum, growth, margin, normalizeKeyword, quickDateSelection, reportPeriod, validPeriod } from './logic'

assert.deepEqual(quickDateSelection('thisWeek', '2026-09-02'), {
  startDate: '2026-08-31', endDate: '2026-09-02', quick: 'thisWeek', compare: true, compareMode: 'ByWeek',
})
assert.equal(quickDateSelection('thisMonth', '2026-09-02').endDate, '2026-09-02')
assert.equal(quickDateSelection('lastMonth', '2024-03-01').endDate, '2024-02-29')
assert.equal(reportPeriod(quickDateSelection('today', '2021-01-01')).compareStartDate, '2019-12-27')
assert.equal(reportPeriod({ ...quickDateSelection('today', '2026-09-01'), compare: false }).compareStartDate, undefined)
assert.equal(validPeriod('2026-02-30', '2026-03-01'), false)
assert.equal(validPeriod('2026-09-06', '2026-09-01'), false)
assert.equal(growth(0, 0), 0)
assert.equal(growth(10, 0), 'new')
assert.equal(growth(10, null), null)
assert.equal(completeSum([10, null]), null)
assert.equal(completeSum([10, 0]), 10)
assert.equal(margin(10, 40), 0.25)
assert.equal(margin(0, 0), null)
assert.equal(normalizeKeyword('  玛索   Notebook  '), '玛索 Notebook')
console.log('报表日期、零基数、毛利完整性：通过')
