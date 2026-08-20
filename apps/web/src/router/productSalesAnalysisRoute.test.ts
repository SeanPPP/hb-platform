import { readFileSync } from 'node:fs'
import type { CurrentUser } from '../types/auth'
import { P } from '../types/permissions'
import { buildAccess } from '../utils/access'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

const routeSource = readFileSync('src/router/routes.tsx', 'utf8')
const menuPreviewSource = readFileSync('src/utils/webMenuPreview.ts', 'utf8')
const packageSource = readFileSync('package.json', 'utf8')
const zhSource = readFileSync('src/i18n/locales/zh.json', 'utf8')
const enSource = readFileSync('src/i18n/locales/en.json', 'utf8')

assert(
  routeSource.includes(
    "import WarehouseProductFlowAnalysisPage from '../pages/ExecutiveSalesIntelligence/WarehouseProductFlowAnalysis'",
  ),
  '路由应导入仓库商品流转分析页面',
)
assert(
  routeSource.includes(
    "import LocalProductSalesAnalysisPage from '../pages/ExecutiveSalesIntelligence/LocalProductSalesAnalysis'",
  ),
  '路由应导入澳洲本地商品分析页面',
)
assert(
  routeSource.includes("path: '/executive-sales-intelligence/warehouse-product-flow-analysis'"),
  '仓库商品流转分析应使用固定路由',
)
assert(
  routeSource.includes("title: 'menu.warehouseProductFlowAnalysis'"),
  '仓库商品流转分析应使用本地化菜单键',
)
assert(
  routeSource.includes('element: <WarehouseProductFlowAnalysisPage />'),
  '仓库商品流转分析路由应渲染新页面',
)
assert(
  routeSource.includes("path: '/executive-sales-intelligence/local-product-sales-analysis'"),
  '澳洲本地商品分析应使用固定路由',
)
assert(
  routeSource.includes("title: 'menu.localProductSalesAnalysis'"),
  '澳洲本地商品分析应使用本地化菜单键',
)
assert(
  routeSource.includes('element: <LocalProductSalesAnalysisPage />'),
  '澳洲本地商品分析路由应渲染新页面',
)
assert(
  routeSource.includes("path: '/executive-sales-intelligence/product-sales-analysis'")
    && routeSource.includes("to=\"/executive-sales-intelligence/warehouse-product-flow-analysis\""),
  '旧商品销量分析地址应兼容跳转到仓库商品流转分析',
)
assert(
  menuPreviewSource.includes(
    "{ path: '/executive-sales-intelligence/warehouse-product-flow-analysis', title: 'menu.warehouseProductFlowAnalysis', accessKey: 'canViewProductSalesAnalysis' }",
  ),
  '角色菜单预览应展示仓库商品流转入口',
)
assert(
  menuPreviewSource.includes(
    "{ path: '/executive-sales-intelligence/local-product-sales-analysis', title: 'menu.localProductSalesAnalysis', accessKey: 'canManageLocalPurchase' }",
  ),
  '角色菜单预览应展示澳洲本地商品入口',
)
assert(zhSource.includes('"warehouseProductFlowAnalysis": "仓库商品流转分析"'), '仓库中文菜单文案应存在')
assert(zhSource.includes('"localProductSalesAnalysis": "澳洲本地商品分析"'), '本地中文菜单文案应存在')
assert(enSource.includes('"warehouseProductFlowAnalysis": "Warehouse Product Flow Analysis"'), '仓库英文菜单文案应存在')
assert(enSource.includes('"localProductSalesAnalysis": "Australian Local Product Analysis"'), '本地英文菜单文案应存在')
assert(packageSource.includes('"test:product-flow-analysis"'), 'package 应提供双页面专项测试脚本')

const createUser = (
  options: { permissions?: string[]; exactPermissions?: string[]; roleNames?: string[] } = {},
): CurrentUser => ({
  userGUID: 'product-sales-analysis-test-user',
  username: 'product-sales-analysis-tester',
  email: 'product-sales-analysis@example.com',
  permissions: options.permissions ?? [],
  exactPermissions: options.exactPermissions,
  roleNames: options.roleNames ?? [],
  storeNames: [],
})

assert(
  buildAccess(createUser({ exactPermissions: [P.Reports.ProductMovementView] })).canViewProductSalesAnalysis,
  'exactPermissions 含 ProductMovementView 应允许进入商品销量分析',
)
assert(
  !buildAccess(
    createUser({
      permissions: [P.Reports.ProductMovementView, P.Reports.View],
      exactPermissions: [P.Reports.View],
    }),
  ).canViewProductSalesAnalysis,
  'exact 只有 Reports.View 时应拒绝商品销量分析，即使 permissions 已展开 ProductMovementView',
)
assert(
  !buildAccess(createUser({ permissions: [P.Reports.ProductMovementView] })).canViewProductSalesAnalysis,
  'exactPermissions 字段缺失时非管理员应拒绝商品销量分析',
)
assert(
  buildAccess(createUser({ roleNames: ['超级管理员'], exactPermissions: [] })).canViewProductSalesAnalysis,
  '超级管理员别名应允许进入商品销量分析',
)

const localPurchaseAccess = buildAccess(createUser({ permissions: [P.LocalPurchase.View] }))
assert(localPurchaseAccess.canManageLocalPurchase, 'LocalPurchase.View 应允许进入澳洲本地商品分析')
assert(localPurchaseAccess.canViewSalesIntelligence, '仅有本地进货权限时也应显示销售看板父级')

console.log('productSalesAnalysisRoute.test: ok')
