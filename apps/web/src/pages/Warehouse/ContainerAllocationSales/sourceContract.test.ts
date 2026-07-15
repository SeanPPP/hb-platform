import { readFileSync } from 'node:fs'
import { join } from 'node:path'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message)
}

const routeSource = readFileSync(join(process.cwd(), 'src/router/routes.tsx'), 'utf8')
const listSource = readFileSync(join(process.cwd(), 'src/pages/Warehouse/Containers/index.tsx'), 'utf8')
const reportSource = readFileSync(join(process.cwd(), 'src/pages/Warehouse/ContainerAllocationSales/index.tsx'), 'utf8')

assert(routeSource.includes("import ContainerAllocationSalesPage from '../pages/Warehouse/ContainerAllocationSales'"), '应导入货柜配销数据页')
assert(routeSource.includes("path: '/warehouse/container/allocation-sales/:containerGuid'"), '应注册带货柜 GUID 的隐藏路由')
assert(routeSource.includes("activeMenu: '/warehouse/containers'"), '配销数据页应保持货柜菜单激活')
assert(routeSource.includes("accessKey: 'canViewContainers'"), '配销数据页应沿用货柜查看权限')
assert(listSource.includes('配销数据'), '货柜列表操作列应提供配销数据入口')
assert(listSource.includes('`/warehouse/container/allocation-sales/${record.hguid}`'), '入口应带上当前货柜 GUID')

assert(reportSource.includes('queryContainerAllocationSalesBranches'), '点击商品后应懒加载分店数据')
assert(reportSource.includes('<Drawer'), '分店明细应使用右侧抽屉')
assert(!reportSource.includes('useMemo<ColumnsType<ContainerAllocationSalesProduct>>'), '商品列不得缓存首屏日期闭包，避免首次加载后点击商品无响应')
assert(reportSource.includes('getContainerAllocationSalesViewState'), '页面应实际使用纯 view-state helper 判断表格和空态')
assert(reportSource.includes('buildQuickRangeQuery'), '页面快捷周数应实际使用纯查询 helper')
assert(reportSource.includes('buildBranchQuery'), '分店懒加载应实际使用纯查询 helper')
assert(reportSource.includes('useStableRouteContext') && reportSource.includes('useKeepAliveContext'), 'KeepAlive 页面应使用稳定路由参数和 active 上下文')
assert(!reportSource.includes('useParams'), 'KeepAlive 页面不得直接读取会随全局 URL 变化的 useParams')
assert(reportSource.includes('shouldLoadContainerAllocationSales'), '页面应实际使用自动加载 helper 跳过隐藏和已加载实例')
assert(reportSource.includes('mainRequestGuardRef') && reportSource.includes('branchRequestGuardRef'), '主表与分店请求应分别使用 latest-wins 守卫')
const weekHandlerSource = reportSource.split('const applyWeekRange')[1]?.split('const applyCustomEndDate')[0] || ''
assert(!weekHandlerSource.includes('setSelectedWeeks') && !weekHandlerSource.includes('setStartDate'), '快捷区间请求成功前不得切换已提交控件状态')
const customDateHandlerSource = reportSource.split('const applyCustomEndDate')[1]?.split('const openBranches')[0] || ''
assert(!customDateHandlerSource.includes('setSelectedWeeks') && !customDateHandlerSource.includes('setEndDate'), '自定义日期请求成功前不得切换已提交控件状态')
const tableHandlerSource = reportSource.split('const handleTableChange')[1]?.split('const productColumns')[0] || ''
assert(!tableHandlerSource.includes('setPageNumber') && !tableHandlerSource.includes('setSortBy'), '分页排序请求成功前不得切换已提交控件状态')
assert(tableHandlerSource.split('loadReport(next)').length - 1 === 1, '每次分页或排序只能发起一次主表请求')
assert(reportSource.includes('search: appliedSearch.trim() || undefined'), '默认主查询应使用已提交搜索词，不得偷偷应用输入框 draft')
assert(reportSource.split('report?.rangeLabel').length - 1 === 1, '统计区间说明只应显示一次')
assert(reportSource.includes('render: formatAustralianCurrency') && !reportSource.includes('¥'), '页面金额列应实际使用澳洲金额格式')
assert(reportSource.includes('scroll={{ x:'), '窄屏表格应支持横向滚动')

console.log('containerAllocationSales.sourceContract.test: ok')
