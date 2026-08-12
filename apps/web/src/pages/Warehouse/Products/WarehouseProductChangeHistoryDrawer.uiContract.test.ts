import { existsSync, readFileSync } from 'node:fs'
import path from 'node:path'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message)
}

function readIfExists(relativePath: string) {
  const absolutePath = path.resolve(process.cwd(), relativePath)
  return existsSync(absolutePath) ? readFileSync(absolutePath, 'utf8') : ''
}

const drawerSource = readIfExists('src/pages/Warehouse/Products/WarehouseProductChangeHistoryDrawer.tsx')
const pageSource = readFileSync(path.resolve(process.cwd(), 'src/pages/Warehouse/Products/index.tsx'), 'utf8')
const containerSource = readFileSync(path.resolve(process.cwd(), 'src/pages/Warehouse/ContainerDetail/index.tsx'), 'utf8')
const zh = JSON.parse(readFileSync(path.resolve(process.cwd(), 'src/i18n/locales/zh.json'), 'utf8'))
const en = JSON.parse(readFileSync(path.resolve(process.cwd(), 'src/i18n/locales/en.json'), 'utf8'))

assert(drawerSource.includes('<Drawer'), '共享修改记录组件必须使用 AntD Drawer')
assert(drawerSource.includes('width={920}'), '修改记录抽屉宽度必须保持 920px')
assert(drawerSource.includes('AbortController'), '修改记录抽屉必须取消旧请求')
assert(drawerSource.includes('onRetry') || drawerSource.includes('handleRetry'), '修改记录错误状态必须提供重试入口')
assert(drawerSource.includes('Pagination'), '修改记录抽屉必须支持事件分页')
assert(drawerSource.includes('fieldKey') && drawerSource.includes('beforeValue') && drawerSource.includes('afterValue'), '事件明细必须展示字段、修改前和修改后')
assert(drawerSource.includes('getWarehouseProductChangeHistoryActionKey'), '动作文案必须通过统一归一化逻辑识别 PascalCase')
assert(pageSource.includes('HistoryOutlined'), '仓库商品页必须引入 HistoryOutlined')
assert(pageSource.includes('WarehouseProductChangeHistoryDrawer'), '仓库商品页必须复用共享修改记录抽屉')
assert(pageSource.includes('access.canManageWarehouseProducts'), '仓库商品页修改记录入口必须复用 canManageWarehouseProducts')
assert(containerSource.includes('HistoryOutlined'), '货柜明细必须引入 HistoryOutlined')
assert(containerSource.includes('WarehouseProductChangeHistoryDrawer'), '货柜明细必须复用共享修改记录抽屉')
assert(containerSource.includes('access.canManageWarehouseProducts'), '货柜明细修改记录入口必须复用 canManageWarehouseProducts')
assert(containerSource.includes('getContainerDetailProductCode(row)'), '货柜明细必须基于已匹配 ProductCode 显示历史入口')

for (const locale of [zh, en]) {
  assert(locale.warehouse?.changeHistory?.title, 'warehouse.changeHistory.title 必须提供中英文')
  assert(locale.warehouse?.changeHistory?.action, 'warehouse.changeHistory.action 必须提供中英文')
  assert(locale.warehouse?.changeHistory?.retry, 'warehouse.changeHistory.retry 必须提供中英文')
  assert(locale.warehouse?.changeHistory?.empty, 'warehouse.changeHistory.empty 必须提供中英文')
  assert(locale.warehouse?.changeHistory?.loadFailed, 'warehouse.changeHistory.loadFailed 必须提供中英文')
  assert(locale.warehouse?.changeHistory?.fields, 'warehouse.changeHistory.fields 必须提供中英文')
  assert(locale.containers?.actions?.viewProductHistory, '货柜明细历史入口文案必须提供中英文')
}

console.log('WarehouseProductChangeHistoryDrawer.uiContract.test: ok')
