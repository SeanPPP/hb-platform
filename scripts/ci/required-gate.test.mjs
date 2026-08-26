import assert from 'node:assert/strict'
import test from 'node:test'

import { evaluateRequiredResults, evaluateRunBudget } from './required-gate.mjs'

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
