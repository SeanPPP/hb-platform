import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'

const read = (path: string) => readFileSync(path, 'utf8')
const readJson = (path: string) => JSON.parse(read(path)) as Record<string, unknown>

const page = read('src/pages/ShopOrders/index.tsx')
const storeOrderTypes = read('src/types/storeOrder.ts')
const zhLocale = readJson('src/i18n/locales/zh.json')
const enLocale = readJson('src/i18n/locales/en.json')

assert.match(
  storeOrderTypes,
  /export interface StoreOrderListQuery\s*\{[\s\S]*?\bstoreCode\?: string/,
  '订货历史查询类型必须支持单值 storeCode',
)
assert.match(
  page,
  /storeCode:\s*selectedStore\?\.storeCode\s*\|\|\s*undefined/,
  '订货前台必须按当前选中门店传递单值 storeCode',
)
assert.doesNotMatch(
  page,
  /storeCodes:\s*selectedStore\?\.storeCode\s*\?\s*\[selectedStore\.storeCode\]/,
  '订货前台不能使用仅供管理端多门店筛选的 storeCodes',
)

for (const field of [
  'pageNumber: currentPage',
  'pageSize,',
  'keyword: keyword || undefined',
  "startDate: dateRange[0].format('YYYY-MM-DD')",
  "endDate: dateRange[1].format('YYYY-MM-DD')",
  'statusList: statusQueryMap[statusFilter]',
  "sortBy: 'OrderDate'",
  'sortDescending: true',
]) {
  assert.ok(page.includes(field), `修复后仍须保留历史订单查询字段：${field}`)
}

assert.ok(page.includes('const [loadError, setLoadError] = useState(false)'), '页面必须区分加载失败和正常空数据')
assert.ok(page.includes('setLoadError(false)'), '每次重新加载前必须清除旧错误状态')
assert.ok(page.includes('setLoadError(true)'), '请求失败时必须进入错误状态')
assert.ok(page.includes('const [reloadVersion, setReloadVersion] = useState(0)'), '页面必须提供显式重试触发器')
assert.ok(page.includes("t('shopOrders.loadFailed')"), '加载失败状态必须使用独立文案')
assert.ok(page.includes("t('common.retry')"), '加载失败状态必须提供重试入口')

const loadErrorBranch = page.indexOf('loadError ? (')
const emptyDataBranch = page.indexOf('orders.length ? (')
assert.ok(loadErrorBranch >= 0 && loadErrorBranch < emptyDataBranch, '加载失败必须优先于正常空数据渲染')

for (const [localeName, locale] of [
  ['zh', zhLocale],
  ['en', enLocale],
] as const) {
  const shopOrders = locale.shopOrders as Record<string, unknown> | undefined
  assert.equal(typeof shopOrders?.loadFailed, 'string', `${localeName} 必须提供历史订单加载失败文案`)
}

console.log('Shop orders contract tests passed')
