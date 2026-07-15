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
assert(reportSource.includes("title: '序号 / 图片'") && !reportSource.includes("title: '商品编码'"), '首列应显示序号和图片，不再显示商品编码')
assert(reportSource.includes('<Image') && reportSource.includes('PRODUCT_IMAGE_FALLBACK'), '商品图片应使用 Ant Design Image 并提供无图占位')
assert(reportSource.includes('preview={false}') && reportSource.includes('alt=""'), '商品图片应作为非交互装饰，避免重复名称和不可键盘操作的预览')
assert(reportSource.includes('getPaginatedRowNumber(pageNumber, pageSize, index)'), '商品序号应按服务端分页位置连续计算')
const firstColumnSource = reportSource.split("key: 'imageAndIndex'")[1]?.split("title: '货号 / 商品名称'")[0] || ''
assert(firstColumnSource.indexOf('<Typography.Text') < firstColumnSource.indexOf('<Image'), '首列应先显示非按钮序号，再显示商品图片')
assert(
  firstColumnSource.includes('<Button')
    && firstColumnSource.includes('type="text"')
    && firstColumnSource.includes('aria-label={`查看第 ${getPaginatedRowNumber(pageNumber, pageSize, index)} 个商品 ${record.itemNumber || record.productName || record.productCode} 的分店明细`}')
    && firstColumnSource.includes('onClick={() => void openBranches(record)}'),
  '首列应提供真实可聚焦的极简明细按钮作为键盘和读屏入口',
)
const productTableSource = reportSource.split('rowKey="productCode"')[1]?.split('summary={() =>')[0] || ''
assert(productTableSource.includes('onRow={(record) => ({'), '主表应通过 onRow 让整行打开分店明细')
assert(!productTableSource.includes("role: 'button'"), '可点击行不得覆盖原生 row 语义，以保留表头与单元格关联')
assert(!productTableSource.includes('tabIndex: 0') && !productTableSource.includes('onKeyDown:'), '原生表格行不得冒充键盘控件，键盘操作应由首列真实按钮承载')
assert(productTableSource.includes("style: { cursor: 'pointer' }"), '可点击行应显示指针并沿用 Ant Design 行悬停反馈')
assert(productTableSource.includes('onClick: (event) =>') && productTableSource.includes('openBranches(record)'), '点击商品行应打开分店明细')
assert(
  productTableSource.includes('shouldTriggerTableRowClick(event.target, event.currentTarget)'),
  '整行点击应通过纯 helper 隔离行内按钮、链接和输入控件',
)
const productColumnSource = reportSource.split("title: '货号 / 商品名称'")[1]?.split("title: '装柜数量'")[0] || ''
assert(
  productColumnSource.includes("dataIndex: 'itemNumber'") && productColumnSource.includes('sorter: true'),
  '货号 / 商品名称列应按货号接入服务端升降序排序',
)
assert(
  productColumnSource.includes("sortOrder: sortBy === 'itemNumber'")
    && productColumnSource.includes("sortDirection === 'asc' ? 'ascend' : 'descend'"),
  '货号列排序箭头应由已提交排序状态控制，请求失败时保持原状态',
)
assert(
  reportSource.includes('formatStatisticMessageAmounts(report.statisticMessage)')
    && reportSource.includes('formatStatisticMessageAmounts(branchReport.statisticMessage)'),
  '主表和分店销售统计警告中的金额应固定显示两位小数',
)
const branchColumnsSource = reportSource.split('const branchColumns')[1]?.split('const isNotFound')[0] || ''
assert(branchColumnsSource.split('sorter:').length - 1 === 9, '分店明细九个业务列都应提供纯前端排序')
for (const field of [
  'branchCode',
  'branchName',
  'isActive',
  'allocationQuantity',
  'allocationImportAmount',
  'salesQuantity',
  'salesAmount',
  'averageSalesPrice',
  'grossMarginRate',
]) {
  assert(
    branchColumnsSource.includes(`compareContainerAllocationSalesBranches(left, right, '${field}', sortOrder)`),
    `分店明细 ${field} 列应使用带排序方向的本地比较器`,
  )
}
const branchTableSource = reportSource.split('rowKey="branchCode"')[1]?.split('/>')[0] || ''
assert(!branchTableSource.includes('onChange='), '分店排序不得新增后端请求或服务端排序回调')

console.log('containerAllocationSales.sourceContract.test: ok')
