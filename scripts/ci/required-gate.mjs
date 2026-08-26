import { pathToFileURL } from 'node:url'

const ACCEPTED_RESULTS = new Set(['success'])

export function evaluateRequiredResults(results) {
  return Object.entries(results).flatMap(([name, rawResult]) => {
    const result = String(rawResult || '')
    return ACCEPTED_RESULTS.has(result)
      ? []
      : [`${name}=${result || '<missing>'}`]
  })
}

function parsePositiveInteger(rawValue) {
  const value = typeof rawValue === 'number'
    ? rawValue
    : typeof rawValue === 'string' && /^\d+$/.test(rawValue)
      ? Number(rawValue)
      : Number.NaN
  return Number.isSafeInteger(value) && value > 0 ? value : null
}

export function evaluateRunBudget({ startedAtEpoch, budgetSeconds, nowEpoch }) {
  const startedAt = parsePositiveInteger(startedAtEpoch)
  if (startedAt === null) {
    return ['started_at_epoch=<invalid>']
  }

  const budget = parsePositiveInteger(budgetSeconds)
  if (budget === null) {
    return ['budget_seconds=<invalid>']
  }

  const currentTime = parsePositiveInteger(nowEpoch)
  if (currentTime === null) {
    return ['now_epoch=<invalid>']
  }
  if (currentTime < startedAt) {
    return ['elapsed=<clock-regression>']
  }

  const elapsed = currentTime - startedAt
  return elapsed < budget ? [] : [`elapsed=${elapsed}s budget=${budget}s`]
}

function main() {
  const serialized = process.argv[2] || process.env.CI_JOB_RESULTS
  if (!serialized) {
    throw new Error('缺少 CI job 结果 JSON')
  }
  const results = JSON.parse(serialized)
  const rejected = [
    ...evaluateRequiredResults(results),
    ...evaluateRunBudget({
      startedAtEpoch: process.env.CI_RUN_STARTED_AT_EPOCH,
      budgetSeconds: process.env.CI_RUN_BUDGET_SECONDS,
      nowEpoch: Math.floor(Date.now() / 1000),
    }),
  ]
  if (rejected.length > 0) {
    console.error(`PR CI 必需门禁失败: ${rejected.join(', ')}`)
    process.exitCode = 1
    return
  }
  const elapsedSeconds = Math.floor(Date.now() / 1000) - Number(process.env.CI_RUN_STARTED_AT_EPOCH)
  console.log(
    `PR CI 必需门禁通过: ${Object.keys(results).length} 个依赖 job，端到端耗时 ${elapsedSeconds}s`,
  )
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  main()
}
