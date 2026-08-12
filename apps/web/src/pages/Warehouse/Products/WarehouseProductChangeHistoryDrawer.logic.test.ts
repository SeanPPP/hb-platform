import {
  createWarehouseProductChangeHistoryRequestGuard,
  formatWarehouseProductChangeHistoryValue,
  getWarehouseProductChangeHistoryActionKey,
  isWarehouseProductChangeHistoryAbortError,
} from './WarehouseProductChangeHistoryDrawer.logic'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message)
}

const guard = createWarehouseProductChangeHistoryRequestGuard()
const first = guard.start('P001', 1, 20)
const second = guard.start('P002', 1, 20)
assert(!guard.isCurrent(first), '切换商品后旧请求不得再更新 Drawer')
assert(guard.isCurrent(second), '最新商品请求必须保持有效')
guard.cancel()
assert(!guard.isCurrent(second), '关闭 Drawer 后当前请求必须失效')

assert(formatWarehouseProductChangeHistoryValue(null) === '--', '空历史值应显示占位符')
assert(formatWarehouseProductChangeHistoryValue('') === '""', '空字符串必须与 null 明确区分')
assert(formatWarehouseProductChangeHistoryValue(false) === 'false', 'false 历史值不能被当作空值')
assert(formatWarehouseProductChangeHistoryValue({ code: 'P001' }) === '{"code":"P001"}', '对象历史值应稳定序列化')
assert(isWarehouseProductChangeHistoryAbortError(Object.assign(new Error('aborted'), { name: 'AbortError' })), '必须识别 AbortError')
for (const [action, expected] of [
  ['Create', 'create'],
  ['Update', 'update'],
  ['BatchUpdate', 'batchUpdate'],
  ['Patch', 'patch'],
  ['ToggleActive', 'toggleActive'],
  ['Import', 'import'],
  ['Sync', 'sync'],
] as const) {
  assert(getWarehouseProductChangeHistoryActionKey(action) === expected, `${action} 必须归一化为可翻译动作键`)
}

console.log('WarehouseProductChangeHistoryDrawer.logic.test: ok')
