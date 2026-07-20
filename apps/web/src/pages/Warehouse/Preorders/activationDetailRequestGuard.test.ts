import assert from 'node:assert/strict'
import {
  beginActivationDetailRequest,
  createActivationDetailRequestGuard,
  getActivationActionAvailability,
  invalidateActivationDetailRequest,
  isCurrentActivationDetailRequest,
  isPreorderReturnContextCurrent,
} from './activationDetailRequestGuard'

const guard = createActivationDetailRequestGuard()
const activationA = beginActivationDetailRequest(guard, 'activation-a')

assert.equal(isCurrentActivationDetailRequest(guard, activationA, 'activation-a'), true)
assert.equal(isCurrentActivationDetailRequest(guard, activationA, 'activation-b'), false)

// 切换路由后 A 请求必须被取消，且不得覆盖 B 的页面状态。
const activationB = beginActivationDetailRequest(guard, 'activation-b')
assert.equal(activationA.signal.aborted, true)
assert.equal(isCurrentActivationDetailRequest(guard, activationA, 'activation-b'), false)
assert.equal(isCurrentActivationDetailRequest(guard, activationB, 'activation-b'), true)

invalidateActivationDetailRequest(guard)
assert.equal(activationB.signal.aborted, true)
assert.equal(isCurrentActivationDetailRequest(guard, activationB, 'activation-b'), false)

assert.deepEqual(getActivationActionAvailability('activation-a', 'activation-b', 'Active'), {
  hasCurrentDetail: false,
  canAdjust: false,
  canClose: false,
})
assert.deepEqual(getActivationActionAvailability('activation-b', 'activation-b', 'Scheduled'), {
  hasCurrentDetail: true,
  canAdjust: true,
  canClose: false,
})

assert.equal(isPreorderReturnContextCurrent('activation-a', 'activation-a', 'activation-a'), true)
assert.equal(isPreorderReturnContextCurrent('activation-b', 'activation-a', 'activation-a'), false, '切换批次后旧退回确认不得提交')
assert.equal(isPreorderReturnContextCurrent('activation-a', 'activation-a', 'activation-b'), false, '页面详情与确认批次不一致时不得提交')

console.log('activationDetailRequestGuard tests passed')
