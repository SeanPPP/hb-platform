import assert from 'node:assert/strict'
import { existsSync, readFileSync } from 'node:fs'

const read = (path: string) => readFileSync(path, 'utf8')
const readJson = (path: string) => JSON.parse(read(path)) as Record<string, unknown>

const layoutPath = 'src/layout/ShopLayout.tsx'
const zhLocalePath = 'src/i18n/locales/zh.json'
const enLocalePath = 'src/i18n/locales/en.json'

assert.ok(existsSync(layoutPath), '商城布局源文件必须存在')
assert.ok(existsSync(zhLocalePath), '中文商城语言资源必须存在')
assert.ok(existsSync(enLocalePath), '英文商城语言资源必须存在')

const layout = read(layoutPath)
const zh = readJson(zhLocalePath)
const en = readJson(enLocalePath)
const zhShop = zh.shop as Record<string, unknown> | undefined
const enShop = en.shop as Record<string, unknown> | undefined

const iconImport = layout.slice(0, layout.indexOf("} from '@ant-design/icons'"))
for (const icon of [
  'HomeOutlined',
  'AppstoreOutlined',
  'ScanOutlined',
  'OrderedListOutlined',
  'MoreOutlined',
]) {
  assert.ok(iconImport.includes(icon), `商城移动底栏必须导入 ${icon}`)
}

const mobileNavStart = layout.indexOf('<nav className="shop-mobile-bottom-nav"')
const mobileNavEnd = layout.indexOf('</nav>', mobileNavStart)
assert.ok(mobileNavStart >= 0 && mobileNavEnd > mobileNavStart, '商城窄屏必须使用语义化固定底栏')

const mobileNav = layout.slice(mobileNavStart, mobileNavEnd)
const expectedItems = [
  'shop-mobile-bottom-nav__shop',
  'shop-mobile-bottom-nav__categories',
  'shop-mobile-bottom-nav__scan',
  'shop-mobile-bottom-nav__orders',
  'shop-mobile-bottom-nav__more',
]
let previousIndex = -1
for (const className of expectedItems) {
  const itemIndex = mobileNav.indexOf(className)
  assert.ok(itemIndex > previousIndex, `移动底栏缺少或顺序错误: ${className}`)
  previousIndex = itemIndex
}

assert.ok(mobileNav.includes("onClick={() => navigate('/shop')}"), 'Shop 入口必须继续进入 /shop')
assert.ok(mobileNav.includes('onClick={() => setMobileCategoryVisible(true)}'), '分类入口必须继续打开分类抽屉')
assert.ok(mobileNav.includes('onClick={openShopScanner}'), '扫码入口必须打开既有扫码面板')
assert.ok(mobileNav.includes("onClick={() => navigate('/shop/orders')}"), '订单入口必须继续进入 /shop/orders')
assert.ok(mobileNav.includes('onClick={() => setMobileMoreVisible(true)}'), 'More 入口必须打开扩展导航抽屉')
assert.match(mobileNav, /aria-current=\{isShopHomePage \? 'page' : undefined\}/, 'Shop 入口必须暴露当前页语义')
assert.match(mobileNav, /aria-current=\{isOrdersPage \? 'page' : undefined\}/, 'Orders 入口必须暴露当前页语义')

assert.ok(
  layout.includes("title={t('shop.categories', 'Categories')}")
    && layout.includes('open={mobileCategoryVisible}')
    && layout.includes('onClose={() => setMobileCategoryVisible(false)}'),
  '移动分类 Drawer 必须使用 shop.categories 标题并保持既有开关状态',
)

const moreDrawerStart = layout.indexOf("title={t('shop.more', 'More')}")
const moreDrawerEnd = layout.indexOf('</Drawer>', moreDrawerStart)
assert.ok(moreDrawerStart >= 0 && moreDrawerEnd > moreDrawerStart, '移动端必须保留受控 More 抽屉')
const moreDrawer = layout.slice(moreDrawerStart, moreDrawerEnd)

for (const destination of [
  'onClick={() => {\n              setMobileMoreVisible(false)\n              handleOpenPreorder()',
  'to="/shop/best-sellers"',
  'to="/shop/coming-soon"',
  'to="/shop/local-supplier-invoices"',
]) {
  assert.ok(moreDrawer.includes(destination), `More 抽屉必须保留入口: ${destination}`)
}

assert.ok(
  moreDrawer.includes('<SupplierOrderingExtensionEntry presentation="mobile-nav" />'),
  '商城首页 More 抽屉必须保留订货助手入口',
)
assert.ok(moreDrawer.includes('<LanguageSwitch'), 'More 抽屉必须保留语言切换')
assert.ok(moreDrawer.includes("window.open('/dashboard', '_blank')"), 'dashboard 入口必须继续在新窗口打开 /dashboard')
assert.ok(moreDrawer.includes('void handleLogout()'), 'More 抽屉必须保留安全退出入口')

assert.equal(zhShop?.categories, '分类', '中文 shop.categories 必须为“分类”')
assert.equal(enShop?.categories, 'Categories', '英文 shop.categories 必须为“Categories”')
assert.equal(zhShop?.products, '商品', '中文 shop.products 必须继续为“商品”')
assert.equal(enShop?.products, 'Products', '英文 shop.products 必须继续为“Products”')
assert.equal(zhShop?.allCategories, '全部分类', '中文移动底栏必须提供“全部分类”')
assert.equal(enShop?.allCategories, 'All Categories', '英文移动底栏必须提供 All Categories')
assert.equal(zhShop?.more, '更多', '中文移动底栏必须提供“更多”')
assert.equal(enShop?.more, 'More', '英文移动底栏必须提供 More')

console.log('shopMobileNavigation.test: ok')
