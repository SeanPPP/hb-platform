import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'

import { validateTrxSource } from './assert-trx-tests.mjs'

function trx(counters) {
  return `<?xml version="1.0"?><TestRun><ResultSummary><Counters ${counters} /></ResultSummary></TestRun>`
}

test('TRX 至少执行一个测试且没有失败时通过', () => {
  assert.deepEqual(
    validateTrxSource(trx('total="3" executed="3" passed="3" failed="0" error="0" timeout="0" aborted="0"'), 'contract'),
    { total: 3, executed: 3, passed: 3, failed: 0, error: 0, timeout: 0, aborted: 0 },
  )
})

test('TRX 零测试时失败关闭', () => {
  assert.throws(
    () => validateTrxSource(trx('total="0" executed="0" passed="0" failed="0" error="0" timeout="0" aborted="0"'), 'contract'),
    /contract.*执行测试数为 0/,
  )
})

test('TRX 缺少 Counters 或存在失败时拒绝', () => {
  assert.throws(() => validateTrxSource('<TestRun />', 'SQL'), /缺少 Counters/)
  assert.throws(
    () => validateTrxSource(trx('total="2" executed="2" passed="2" failed="1" error="0" timeout="0" aborted="0"'), 'SQL'),
    /SQL.*非成功结果/,
  )
})

test('TRX 缺少关键计数或包含未执行的测试时失败关闭', () => {
  assert.throws(
    () => validateTrxSource(trx('total="3" executed="3" failed="0" error="0" timeout="0" aborted="0"'), 'contract'),
    /contract.*缺少关键计数/,
  )
  assert.throws(
    () => validateTrxSource(trx('total="3" executed="2" passed="2" failed="0" error="0" timeout="0" aborted="0"'), 'contract'),
    /contract.*计数不一致/,
  )
})

test('TRX 的扩展非成功计数非零时失败关闭', () => {
  assert.throws(
    () => validateTrxSource(trx('total="3" executed="3" passed="3" failed="0" error="0" timeout="0" aborted="0" inconclusive="1"'), 'contract'),
    /contract.*非成功结果.*inconclusive=1/,
  )
  assert.throws(
    () => validateTrxSource(trx('total="3" executed="3" passed="3" failed="0" error="0" timeout="0" aborted="0" completed="1"'), 'contract'),
    /contract.*非成功结果.*completed=1/,
  )
})

test('Windows 组件测试按 profile 筛选性能测试并在 weekly 启用性能门禁', () => {
  const source = readFileSync(new URL('./run-windows-component.ps1', import.meta.url), 'utf8')

  assert.match(source, /\$requiredCounterNames = @\('total', 'executed', 'passed', 'failed', 'error', 'timeout', 'aborted'\)/)
  assert.match(source, /\$counters\.HasAttribute\(\$counterName\)/)
  assert.match(source, /\$counterValues\.total -ne \$counterValues\.executed -or \$counterValues\.total -ne \$counterValues\.passed/)
  assert.match(source, /'notExecuted'/)
  assert.match(source, /'completed'/)
  assert.match(source, /\[ValidateSet\('pr', 'weekly'\)\]/)
  assert.match(source, /\[string\]\$Profile = 'pr'/)
  assert.match(source, /\$env:HBPOS_RUN_PERF_TESTS = '1'/)
  assert.match(source, /'Category!=Performance&Category!=LiveE2e'/)
  assert.match(source, /'Category!=LiveE2e'/)
})

test('POS API 常规组件排除由 weekly lane 执行的 SQL 集成测试', () => {
  const source = readFileSync(new URL('./run-dotnet-component.sh', import.meta.url), 'utf8')

  assert.match(
    source,
    /pos-api\)[\s\S]*--filter 'Category!=SQL&Category!=Performance&Category!=LiveE2e'/,
  )
})
