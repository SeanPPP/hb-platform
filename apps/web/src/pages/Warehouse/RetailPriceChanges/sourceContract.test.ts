import { existsSync, readFileSync } from 'node:fs'
import { resolve } from 'node:path'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message)
}

const sourceRoot = resolve(process.cwd(), 'src')
const pagePath = resolve(sourceRoot, 'pages/Warehouse/RetailPriceChanges/index.tsx')
const routesPath = resolve(sourceRoot, 'router/routes.tsx')
const productsPath = resolve(sourceRoot, 'pages/Warehouse/Products/index.tsx')
const exportServicePath = resolve(sourceRoot, 'services/exportService.ts')
const pageSource = existsSync(pagePath) ? readFileSync(pagePath, 'utf8') : ''
const routesSource = readFileSync(routesPath, 'utf8')
const productsSource = readFileSync(productsPath, 'utf8')
const exportServiceSource = readFileSync(exportServicePath, 'utf8')

assert(pageSource.includes('rowKey="productCode"'), '零售价变化页必须以 productCode 作为表格 rowKey')
assert(pageSource.includes('RETAIL_PRICE_CHANGES_COLUMN_KEYS'), '零售价变化页必须声明固定五列契约')
assert(pageSource.includes('emptyText'), '零售价变化页必须具备空态或错误重试入口')
assert(pageSource.includes("import BarcodePreview from '../../../components/BarcodePreview'"), '条码列必须复用统一条码图组件')
assert(pageSource.includes('<BarcodePreview'), '条码列必须显示可扫描的条码图形，而不是只显示文本')
assert(pageSource.includes('FilePdfOutlined'), '页面必须提供清晰的 PDF 导出图标')
assert(pageSource.includes('collectRetailPriceChangesForPdf'), 'PDF 必须读取当前筛选下的全部后端分页')
assert(pageSource.includes("import('../../../services/exportService')"), 'PDF 导出器必须按需加载，避免扩大页面首屏包')
assert(pageSource.includes('loading={exporting}'), 'PDF 导出期间必须阻止重复触发')
assert(pageSource.includes("pdfImageFormat: 'JPEG'"), '月度报表必须使用高质量 JPEG 控制 PDF 文件体积')
assert(pageSource.includes('pdfRenderScale: 2'), '月度报表必须使用足够清晰且体积可控的 2 倍渲染')
assert(exportServiceSource.includes("options.pdfImageFormat === 'JPEG'"), '共享导出器必须保留默认 PNG 并支持调用方选择 JPEG')

const staticRouteIndex = routesSource.indexOf("path: '/warehouse/products/retail-price-changes'")
const dynamicRouteIndex = routesSource.indexOf("path: '/warehouse/products/:productCode/records'")
assert(staticRouteIndex >= 0, '必须注册隐藏的零售价月度变化静态路由')
assert(staticRouteIndex < dynamicRouteIndex, '零售价月度变化静态路由必须位于动态商品路由之前')
assert(
  /path: '\/warehouse\/products\/retail-price-changes',[\s\S]{0,420}accessKey: 'canManageWarehouseProducts',[\s\S]{0,220}activeMenu: '\/warehouse\/products'/.test(routesSource),
  '零售价月度变化路由必须继承仓库商品权限并保持父级菜单激活',
)
assert(productsSource.includes("'/warehouse/products/retail-price-changes'"), '仓库商品页必须提供零售价变化入口')
assert(
  /access\.canManageWarehouseProducts\s*\?\s*\(<Button[\s\S]{0,320}\/warehouse\/products\/retail-price-changes/.test(productsSource),
  '仓库商品页零售价变化入口必须显式复用 canManageWarehouseProducts 权限',
)

console.log('retailPriceChanges.sourceContract.test: ok')
