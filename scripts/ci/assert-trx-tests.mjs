import { readFileSync } from 'node:fs'
import { pathToFileURL } from 'node:url'

const REQUIRED_COUNTER_NAMES = ['total', 'executed', 'passed', 'failed', 'error', 'timeout', 'aborted']
const NON_SUCCESS_COUNTER_NAMES = [
  'failed', 'error', 'timeout', 'aborted', 'inconclusive', 'passedButRunAborted',
  'notRunnable', 'notExecuted', 'disconnected', 'warning', 'completed', 'inProgress', 'pending',
]

export function validateTrxSource(source, label = 'TRX', minimumExecuted = 1) {
  const countersElement = source.match(/<Counters\b([^>]*)\/?\s*>/i)
  if (!countersElement) {
    throw new Error(`${label} TRX 缺少 Counters`)
  }

  const attributes = new Map(
    [...countersElement[1].matchAll(/([A-Za-z][\w.-]*)="(\d+)"/g)]
      .map((match) => [match[1], Number(match[2])]),
  )
  const missingCounters = REQUIRED_COUNTER_NAMES.filter((name) => !attributes.has(name))
  if (missingCounters.length > 0) {
    throw new Error(`${label} TRX 缺少关键计数: ${missingCounters.join(', ')}`)
  }

  const counters = Object.fromEntries(REQUIRED_COUNTER_NAMES.map((name) => [name, attributes.get(name)]))
  if (counters.total < minimumExecuted) {
    throw new Error(`${label} 执行测试数为 0，拒绝假绿`)
  }

  if (counters.total !== counters.executed || counters.total !== counters.passed) {
    throw new Error(
      `${label} TRX 计数不一致: total=${counters.total}, executed=${counters.executed}, passed=${counters.passed}`,
    )
  }

  const failures = NON_SUCCESS_COUNTER_NAMES
    .filter((name) => attributes.has(name) && attributes.get(name) !== 0)
    .map((name) => `${name}=${attributes.get(name)}`)
  if (failures.length > 0) {
    throw new Error(`${label} 存在非成功结果: ${failures.join(', ')}`)
  }
  return counters
}

function main() {
  const [trxPath, label = 'TRX', minimum = '1'] = process.argv.slice(2)
  if (!trxPath) {
    throw new Error('用法: node assert-trx-tests.mjs <trx-path> [label] [minimum-executed]')
  }
  const minimumExecuted = Number(minimum)
  if (!Number.isInteger(minimumExecuted) || minimumExecuted < 1) {
    throw new Error(`minimum-executed 必须是正整数: ${minimum}`)
  }
  const counters = validateTrxSource(readFileSync(trxPath, 'utf8'), label, minimumExecuted)
  console.log(`${label} TRX 验证通过: executed=${counters.executed}, passed=${counters.passed}`)
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  main()
}
