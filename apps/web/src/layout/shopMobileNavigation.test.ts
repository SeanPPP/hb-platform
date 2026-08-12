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
assert.ok(iconImport.includes('HomeOutlined'), '商城布局必须导入 HomeOutlined 作为首页入口图标')

const mobileGridStart = layout.indexOf('<div className="shop-mobile-grid">')
const mobileGridEnd = layout.indexOf('<div className="shop-nav-bar">', mobileGridStart)
assert.ok(mobileGridStart >= 0 && mobileGridEnd > mobileGridStart, '商城窄屏导航网格必须位于独立容器中')

const mobileGrid = layout.slice(mobileGridStart, mobileGridEnd)
const firstRowEnd = mobileGrid.indexOf("navigate('/shop/best-sellers')")
assert.ok(firstRowEnd > 0, '窄屏导航首行后必须保留热销商品入口')

const firstRow = mobileGrid.slice(0, firstRowEnd)
const homeIndex = firstRow.indexOf("onClick={() => navigate('/shop')}")
const categoriesIndex = firstRow.indexOf('onClick={() => setMobileCategoryVisible(true)}')
const preorderIndex = firstRow.indexOf('onClick={handleOpenPreorder}')

assert.ok(homeIndex >= 0, '窄屏导航首行必须提供首页入口并 navigate(\'/shop\')')
assert.ok(categoriesIndex > homeIndex, '窄屏导航首行第二项必须是分类入口')
assert.ok(preorderIndex > categoriesIndex, '窄屏导航首行第三项必须是预订货入口')
assert.match(
  firstRow.slice(homeIndex, categoriesIndex),
  /<HomeOutlined className="icon" \/>[\s\S]*?t\('shop\.shopHome', 'Shop Home'\)/,
  '首页入口必须使用 HomeOutlined 和 shop.shopHome',
)
assert.match(
  firstRow.slice(categoriesIndex, preorderIndex),
  /<MenuOutlined className="icon" \/>[\s\S]*?t\('shop\.categories', 'Categories'\)/,
  '分类入口必须继续使用 MenuOutlined 和 shop.categories，并打开既有移动分类抽屉',
)
assert.match(
  firstRow.slice(preorderIndex),
  /<GiftOutlined className="icon" \/>[\s\S]*?t\('shop\.preorder\.navigation', 'Preorder'\)/,
  '预订货入口必须使用 shop.preorder.navigation',
)

assert.ok(
  layout.includes("title={t('shop.categories', 'Categories')}")
    && layout.includes('open={mobileCategoryVisible}')
    && layout.includes('onClose={() => setMobileCategoryVisible(false)}'),
  '移动分类 Drawer 必须使用 shop.categories 标题并保持既有开关状态',
)
assert.ok(
  layout.includes("window.open('/dashboard', '_blank')"),
  '现有 dashboard 入口必须继续在新窗口打开 /dashboard',
)

assert.equal(zhShop?.categories, '分类', '中文 shop.categories 必须为“分类”')
assert.equal(enShop?.categories, 'Categories', '英文 shop.categories 必须为“Categories”')
assert.equal(zhShop?.products, '商品', '中文 shop.products 必须继续为“商品”')
assert.equal(enShop?.products, 'Products', '英文 shop.products 必须继续为“Products”')

console.log('shopMobileNavigation.test: ok')
