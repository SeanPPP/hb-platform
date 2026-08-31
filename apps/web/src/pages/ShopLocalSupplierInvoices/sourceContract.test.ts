import assert from 'node:assert/strict'
import { existsSync, readFileSync } from 'node:fs'

const read = (path: string) => readFileSync(path, 'utf8')
const readJson = (path: string) => JSON.parse(read(path)) as Record<string, unknown>

const listPath = 'src/pages/ShopLocalSupplierInvoices/index.tsx'
const detailPath = 'src/pages/ShopLocalSupplierInvoiceDetail/index.tsx'

assert.ok(existsSync(listPath), '必须提供商城澳洲本地进货单列表页')
assert.ok(existsSync(detailPath), '必须提供商城澳洲本地进货单明细页')

const app = read('src/App.tsx')
const layout = read('src/layout/ShopLayout.tsx')
const list = read(listPath)
const detail = read(detailPath)
const styles = read('src/styles/global.css')
const zh = readJson('src/i18n/locales/zh.json')
const en = readJson('src/i18n/locales/en.json')

assert.ok(app.includes("path=\"local-supplier-invoices\""), 'App 必须接入澳洲本地进货单列表路由')
assert.ok(
  app.includes('path="local-supplier-invoices/:invoiceGuid"'),
  'App 必须接入澳洲本地进货单明细路由',
)

const primaryNavigationStart = layout.indexOf('<nav className="shop-primary-nav"')
const primaryNavigationEnd = layout.indexOf('</nav>', primaryNavigationStart)
assert.ok(primaryNavigationStart >= 0 && primaryNavigationEnd > primaryNavigationStart, '商城必须提供语义化桌面一级导航')
const primaryNavigation = layout.slice(primaryNavigationStart, primaryNavigationEnd)
const orderNavigationIndex = primaryNavigation.indexOf('to="/shop/orders"')
const localInvoiceNavigationIndex = primaryNavigation.indexOf('to="/shop/local-supplier-invoices"')
assert.ok(orderNavigationIndex >= 0, '商城导航必须保留历史订单')
assert.ok(
  localInvoiceNavigationIndex > orderNavigationIndex,
  '澳洲本地进货单入口必须排在历史订单后面',
)
assert.ok(
  layout.includes("location.pathname.startsWith('/shop/local-supplier-invoices')"),
  '澳洲本地进货单 active 状态必须覆盖列表与明细路由',
)
assert.ok(
  (layout.match(/to="\/shop\/local-supplier-invoices"/g) ?? []).length >= 2,
  '桌面一级导航和移动 More 抽屉都必须提供语义化澳洲本地进货单链接',
)
assert.ok(layout.includes("aria-current={isLocalSupplierInvoicesPage ? 'page' : undefined}"), '当前列表或明细路由必须向辅助技术暴露 active 状态')

for (const contract of [
  'buildShopLocalSupplierInvoiceGridRequest',
  'getShopLocalSupplierInvoiceGrid',
  'getShopLocalSupplierInvoiceFilterOptions',
  'const [pageSize, setPageSize] = useState(20)',
  "setSelectedStore(nextStore)",
  "value={storeCode ?? '__all__'}",
  'currentPageAmount',
  'loadError',
  'noMatchingInvoices',
]) {
  assert.ok(list.includes(contract), `列表页必须保留只读筛选/分页契约：${contract}`)
}
assert.doesNotMatch(list, /\bgetInvoiceGrid\b/, '商城列表页不得复用后台高风险 grid service')
assert.ok(
  list.indexOf('loadError ? (') < list.indexOf('invoices.length ? ('),
  '列表加载失败必须优先于正常空数据渲染',
)
assert.doesNotMatch(
  list,
  /createInvoice|updateInvoice|deleteInvoice|pushInvoices|syncLocalSupplierInvoicesFromHq/,
  '商城列表页不得引入进货单写操作',
)
assert.doesNotMatch(
  list,
  /invoice\.remarks|common\.remarks|shop-order-card-remarks/,
  '商城进货单列表不得显示备注字段、标签或样式块',
)

for (const contract of [
  'getShopLocalSupplierInvoice(invoiceGuid',
  'getShopLocalSupplierInvoiceDetailsGrid',
  'Promise.allSettled',
  'RequestError',
  'error.status === 403',
  'error.status === 404',
  'pageSizeOptions={[50, 100, 200]}',
  'item.itemNumber',
  'item.barcode',
  'item.productName',
  'item.specification',
  'item.unit',
  'item.quantity',
  'item.purchasePrice',
  'item.lastPurchasePrice',
  'item.retailPrice',
  'item.newAutoRetailPrice',
  'item.amount',
]) {
  assert.ok(detail.includes(contract), `明细页必须保留并发只读加载与商品字段：${contract}`)
}
assert.doesNotMatch(detail, /\bgetInvoice\s*\(/, '商城明细页不得复用后台高风险 header service')
assert.doesNotMatch(
  detail,
  /updateInvoice|deleteInvoice|batchUpsertDetails|deleteDetails|pushInvoices|syncLocalSupplierInvoicesFromHq/,
  '商城明细页不得引入进货单写操作',
)
assert.doesNotMatch(detail, /via\.placeholder\.com/, '商品缺图或图片加载失败不得请求第三方占位服务')
assert.ok(detail.includes('data:image/svg+xml'), '商品图片必须提供无网络依赖的本地 fallback')
assert.doesNotMatch(
  detail,
  /invoice\.remarks|shopLocalSupplierInvoiceDetail\.(?:remarks|noRemarks)|shop-order-detail-note/,
  '商城进货单明细页不得显示备注字段、标签、空态或样式块',
)
assert.ok(
  detail.includes('shop-local-invoice-detail-info-grid'),
  '移除备注后门店和供应商信息区必须占满可用宽度',
)
assert.match(
  styles,
  /\.shop-local-invoice-detail-info-grid\s*\{[^}]*grid-template-columns:\s*1fr/,
  '单块信息区不得保留空白网格列',
)

for (const [localeName, locale] of [
  ['zh', zh],
  ['en', en],
] as const) {
  const shop = locale.shop as Record<string, unknown> | undefined
  const localInvoices = locale.shopLocalSupplierInvoices as Record<string, unknown> | undefined
  const localInvoiceDetail = locale.shopLocalSupplierInvoiceDetail as Record<string, unknown> | undefined
  assert.equal(typeof shop?.localSupplierInvoices, 'string', `${localeName} 必须提供导航标题`)
  assert.equal(typeof shop?.localSupplierInvoicesBannerSubtitle, 'string', `${localeName} 必须提供 banner 副标题`)
  assert.equal(typeof localInvoices?.loadFailed, 'string', `${localeName} 必须提供列表失败文案`)
  assert.equal(typeof localInvoices?.noMatchingInvoices, 'string', `${localeName} 必须提供列表空态文案`)
  assert.equal(typeof localInvoiceDetail?.forbidden, 'string', `${localeName} 必须提供 403 文案`)
  assert.equal(typeof localInvoiceDetail?.notFound, 'string', `${localeName} 必须提供 404 文案`)
  assert.equal(typeof localInvoiceDetail?.loadFailed, 'string', `${localeName} 必须提供一般失败文案`)
  assert.equal(typeof localInvoiceDetail?.lastPurchasePrice, 'string', `${localeName} 必须提供上次进价文案`)
  assert.equal(typeof localInvoiceDetail?.newAutoRetailPrice, 'string', `${localeName} 必须提供新自动零售价文案`)
}

assert.match(styles, /\.shop-local-invoice-card-footer:focus-visible/,
  '查看明细入口必须有清晰的键盘焦点状态')
assert.match(styles, /\.shop-primary-nav__item:focus-visible/,
  '桌面一级导航入口必须有清晰的键盘焦点状态')
assert.match(styles, /\.shop-mobile-more-menu > a:focus-visible/,
  '移动 More 导航入口必须有清晰的键盘焦点状态')
assert.match(
  styles,
  /\.shop-primary-nav\s*\{[\s\S]*?white-space:\s*nowrap/,
  '桌面一级导航必须维持单行，避免英文长标签破坏头部结构',
)
assert.match(styles, /@media \(max-width: 480px\)[\s\S]*?\.shop-local-invoice/,
  '必须为约 390px 手机宽度提供专属响应式规则')

console.log('Shop local supplier invoices source contract tests passed')
