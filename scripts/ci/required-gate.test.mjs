import assert from 'node:assert/strict'
import test from 'node:test'

import {
  evaluateRequiredResults,
  evaluateRunBudget,
  fetchRunAttemptStartedAtEpoch,
} from './required-gate.mjs'

test('required gate 只接受 success', () => {
  assert.deepEqual(
    evaluateRequiredResults({ plan: 'success', linux: 'success', windows: 'success' }),
    [],
  )
  assert.deepEqual(evaluateRequiredResults({ windows: 'skipped' }), ['windows=skipped'])
})

test('required gate 报告失败、取消和未知结果', () => {
  assert.deepEqual(
    evaluateRequiredResults({
      linux: 'failure',
      windows: 'cancelled',
      macos: '',
    }),
    [
      'linux=failure',
      'windows=cancelled',
      'macos=<missing>',
    ],
  )
})

test('required gate 在端到端预算内通过，到达预算边界即失败', () => {
  assert.deepEqual(
    evaluateRunBudget({ startedAtEpoch: 1_000, budgetSeconds: 900, nowEpoch: 1_899 }),
    [],
  )
  assert.deepEqual(
    evaluateRunBudget({ startedAtEpoch: 1_000, budgetSeconds: 900, nowEpoch: 1_900 }),
    ['elapsed=900s budget=900s'],
  )
})

test('required gate 对缺失、非法或倒退的预算时间失败关闭', () => {
  assert.deepEqual(
    evaluateRunBudget({ startedAtEpoch: '', budgetSeconds: 900, nowEpoch: 1_100 }),
    ['started_at_epoch=<invalid>'],
  )
  assert.deepEqual(
    evaluateRunBudget({ startedAtEpoch: 1_000, budgetSeconds: 0, nowEpoch: 1_100 }),
    ['budget_seconds=<invalid>'],
  )
  assert.deepEqual(
    evaluateRunBudget({ startedAtEpoch: 1_000, budgetSeconds: 900, nowEpoch: 999 }),
    ['elapsed=<clock-regression>'],
  )
})

test('required gate 从当前 workflow attempt 读取端到端预算起点', async () => {
  const requests = []
  const startedAtEpoch = await fetchRunAttemptStartedAtEpoch({
    apiUrl: 'https://api.github.example',
    repository: 'owner/repository',
    runId: '12345',
    runAttempt: '2',
    token: 'test-token',
    fetchImpl: async (url, options) => {
      requests.push({ url, options })
      return {
        ok: true,
        async json() {
          return {
            run_attempt: 2,
            run_started_at: '2026-08-26T10:04:01Z',
          }
        },
      }
    },
  })

  assert.equal(startedAtEpoch, 1_787_738_641)
  assert.deepEqual(requests, [{
    url: 'https://api.github.example/repos/owner/repository/actions/runs/12345/attempts/2',
    options: {
      headers: {
        Accept: 'application/vnd.github+json',
        Authorization: 'Bearer test-token',
        'X-GitHub-Api-Version': '2022-11-28',
      },
    },
  }])
})

test('required gate 拒绝把首轮预算起点用于第二次 workflow attempt', async () => {
  await assert.rejects(
    fetchRunAttemptStartedAtEpoch({
      apiUrl: 'https://api.github.example',
      repository: 'owner/repository',
      runId: '12345',
      runAttempt: '2',
      token: 'test-token',
      fetchImpl: async () => ({
        ok: true,
        async json() {
          return {
            run_attempt: 1,
            run_started_at: '2026-08-26T09:47:17Z',
          }
        },
      }),
    }),
    /workflow attempt 不一致：expected=2, actual=1/,
  )
})
