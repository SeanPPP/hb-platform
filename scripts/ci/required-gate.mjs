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

export async function fetchRunAttemptStartedAtEpoch({
  apiUrl,
  repository,
  runId,
  runAttempt,
  token,
  fetchImpl = globalThis.fetch,
}) {
  const expectedAttempt = parsePositiveInteger(runAttempt)
  if (expectedAttempt === null) {
    throw new Error('workflow attempt 无效')
  }
  if (!apiUrl || !repository || !runId || !token || typeof fetchImpl !== 'function') {
    throw new Error('缺少读取 workflow attempt 的 GitHub 上下文')
  }

  const endpoint = `${String(apiUrl).replace(/\/$/, '')}/repos/${repository}/actions/runs/${runId}/attempts/${expectedAttempt}`
  const response = await fetchImpl(endpoint, {
    headers: {
      Accept: 'application/vnd.github+json',
      Authorization: `Bearer ${token}`,
      'X-GitHub-Api-Version': '2022-11-28',
    },
  })
  if (!response.ok) {
    throw new Error(`读取 workflow attempt 失败：HTTP ${response.status ?? '<unknown>'}`)
  }

  const attempt = await response.json()
  const actualAttempt = parsePositiveInteger(attempt.run_attempt)
  if (actualAttempt !== expectedAttempt) {
    throw new Error(`workflow attempt 不一致：expected=${expectedAttempt}, actual=${actualAttempt ?? '<invalid>'}`)
  }

  const startedAtMilliseconds = Date.parse(attempt.run_started_at)
  if (!Number.isFinite(startedAtMilliseconds) || startedAtMilliseconds <= 0) {
    throw new Error('workflow attempt 的 run_started_at 无效')
  }
  return Math.floor(startedAtMilliseconds / 1000)
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

async function main() {
  const serialized = process.argv[2] || process.env.CI_JOB_RESULTS
  if (!serialized) {
    throw new Error('缺少 CI job 结果 JSON')
  }
  const startedAtEpoch = await fetchRunAttemptStartedAtEpoch({
    apiUrl: process.env.CI_API_URL,
    repository: process.env.CI_REPOSITORY,
    runId: process.env.CI_RUN_ID,
    runAttempt: process.env.CI_RUN_ATTEMPT,
    token: process.env.GITHUB_TOKEN,
  })
  const results = JSON.parse(serialized)
  const rejected = [
    ...evaluateRequiredResults(results),
    ...evaluateRunBudget({
      startedAtEpoch,
      budgetSeconds: process.env.CI_RUN_BUDGET_SECONDS,
      nowEpoch: Math.floor(Date.now() / 1000),
    }),
  ]
  if (rejected.length > 0) {
    console.error(`PR CI 必需门禁失败: ${rejected.join(', ')}`)
    process.exitCode = 1
    return
  }
  const elapsedSeconds = Math.floor(Date.now() / 1000) - startedAtEpoch
  console.log(
    `PR CI 必需门禁通过: ${Object.keys(results).length} 个依赖 job，端到端耗时 ${elapsedSeconds}s`,
  )
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  main().catch((error) => {
    console.error(`PR CI 必需门禁失败: ${error.message}`)
    process.exitCode = 1
  })
}
