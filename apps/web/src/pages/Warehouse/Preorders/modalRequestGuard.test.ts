import assert from 'node:assert/strict'
import {
  beginModalRequest,
  createModalRequestGuard,
  invalidateModalRequest,
  isCurrentModalRequest,
} from './modalRequestGuard'

const guard = createModalRequestGuard()

// A 模板慢请求在关闭弹窗后必须立即失效。
const templateA = beginModalRequest(guard)
assert.equal(isCurrentModalRequest(guard, templateA), true)
invalidateModalRequest(guard)
assert.equal(templateA.signal.aborted, true)
assert.equal(isCurrentModalRequest(guard, templateA), false)

// 随后创建模板或打开 B 时，只允许新请求写回弹窗状态。
const templateB = beginModalRequest(guard)
assert.equal(isCurrentModalRequest(guard, templateA), false)
assert.equal(isCurrentModalRequest(guard, templateB), true)

console.log('modalRequestGuard tests passed')
