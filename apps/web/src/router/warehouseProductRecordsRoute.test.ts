import { readFileSync } from 'node:fs'
import type { CurrentUser } from '../types/auth'
import { P } from '../types/permissions'
import { buildAccess } from '../utils/access'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message)
}

const routeSource = readFileSync('src/router/routes.tsx', 'utf8')
const listSource = readFileSync('src/pages/Warehouse/Products/index.tsx', 'utf8')
const pageSource = readFileSync('src/pages/Warehouse/ProductRecords/index.tsx', 'utf8')
const salesPanelSource = readFileSync('src/pages/Warehouse/ProductRecords/SalesPanel.tsx', 'utf8')
const allocationsPanelSource = readFileSync('src/pages/Warehouse/ProductRecords/AllocationsPanel.tsx', 'utf8')
const packageSource = readFileSync('package.json', 'utf8')
const zhSource = readFileSync('src/i18n/locales/zh.json', 'utf8')
const enSource = readFileSync('src/i18n/locales/en.json', 'utf8')

assert(
  routeSource.includes("const WarehouseProductRecordsPage = lazy(() => import('../pages/Warehouse/ProductRecords'))"),
  '路由应懒加载商品数据查询页',
)
assert(
  routeSource.includes("path: '/warehouse/products/:productCode/records'"),
  '商品数据查询应注册带商品编码的隐藏路由',
)
assert(
  routeSource.includes("activeMenu: '/warehouse/products'"),
  '商品数据查询应保持仓库商品菜单激活',
)
assert(
  routeSource.includes("accessKey: 'canManageWarehouseProducts'"),
  '商品数据查询应沿用仓库商品管理权限',
)
assert(
  routeSource.includes('hidden: true') && routeSource.includes('keepAlive: true'),
  '商品数据查询路由应为隐藏且保持存活',
)
assert(
  routeSource.includes('element: <WarehouseProductRecordsPage />'),
  '商品数据查询路由应渲染新页面',
)

assert(
  listSource.includes("`/warehouse/products/${encodeURIComponent(record.productCode)}/records`"),
  '商品行入口应编码商品编码并跳转数据查询页',
)
assert(
  listSource.includes('access.canManageWarehouseProducts && (access.canViewContainers || access.canViewProductSalesAnalysis)'),
  '商品行入口应按仓库商品管理且具备货柜或销售查看权限显示',
)

assert(
  pageSource.includes('useStableRouteContext') && pageSource.includes('useKeepAliveContext'),
  'KeepAlive 页面应使用稳定路由参数和 active 上下文',
)
assert(!pageSource.includes('useParams'), 'KeepAlive 页面不得直接读取会随全局 URL 变化的 useParams')
assert(pageSource.includes('canViewContainers') && pageSource.includes('canViewProductSalesAnalysis'), '页面应按权限决定可用 Tab')
assert(pageSource.includes('Result') && pageSource.includes('noPermission'), '无可用 Tab 时应显示无权限结果')
assert(
  pageSource.includes('visitedTabKeys') && pageSource.includes('destroyOnHidden={false}'),
  '页签应保留已访问内容且不得销毁隐藏面板',
)
assert(
  salesPanelSource.includes('allowNonFreshData: true'),
  '商品销售页应显式请求保留非 Fresh 数据',
)
assert(
  !salesPanelSource.includes('salesNotRealtime') && !salesPanelSource.includes('notFresh'),
  '商品销售页不得显示非实时或统计未就绪提示',
)
assert(
  salesPanelSource.includes('setBrisbaneToday') && allocationsPanelSource.includes('setBrisbaneToday'),
  'KeepAlive 页面重新启用时应刷新布里斯班日期基准',
)

assert(zhSource.includes('"warehouseProductRecords": "商品数据查询"'), '中文菜单文案应存在')
assert(enSource.includes('"warehouseProductRecords": "Product Data Query"'), '英文菜单文案应存在')
assert(packageSource.includes('"test:warehouse-product-records"'), 'package 应提供专项测试脚本')

const createUser = (
  options: { permissions?: string[]; exactPermissions?: string[]; roleNames?: string[] } = {},
): CurrentUser => ({
  userGUID: 'warehouse-product-records-test-user',
  username: 'warehouse-product-records-tester',
  email: 'warehouse-product-records@example.com',
  permissions: options.permissions ?? [],
  exactPermissions: options.exactPermissions,
  roleNames: options.roleNames ?? [],
  storeNames: [],
})

const warehouseAndContainer = buildAccess(createUser({
  permissions: [P.Warehouse.ManageProducts, P.Container.View],
}))
assert(
  warehouseAndContainer.canManageWarehouseProducts && warehouseAndContainer.canViewContainers,
  '同时具备仓库商品管理与货柜查看权限时应显示货柜/配货入口',
)

const warehouseOnly = buildAccess(createUser({ permissions: [P.Warehouse.ManageProducts] }))
assert(
  warehouseOnly.canManageWarehouseProducts && !warehouseOnly.canViewContainers && !warehouseOnly.canViewProductSalesAnalysis,
  '只有仓库商品管理权限时不得显示任何数据查询入口',
)

const salesOnly = buildAccess(createUser({
  exactPermissions: [P.Reports.ProductMovementView, P.Warehouse.ManageProducts],
  permissions: [P.Warehouse.ManageProducts],
}))
assert(
  salesOnly.canManageWarehouseProducts && salesOnly.canViewProductSalesAnalysis,
  '同时具备仓库商品管理与销售分析权限时应显示销售入口',
)

console.log('warehouseProductRecordsRoute.test: ok')
