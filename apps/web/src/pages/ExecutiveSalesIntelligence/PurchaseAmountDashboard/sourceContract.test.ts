import { readFileSync } from 'node:fs'

function assert(condition: boolean, message: string) {
  if (!condition) throw new Error(message)
}

const routeSource = readFileSync('src/router/routes.tsx', 'utf8')
const pageSource = readFileSync('src/pages/ExecutiveSalesIntelligence/PurchaseAmountDashboard/index.tsx', 'utf8')
const serviceSource = readFileSync('src/services/localPurchaseDashboardService.ts', 'utf8')
const zh = JSON.parse(readFileSync('src/i18n/locales/zh.json', 'utf8'))
const en = JSON.parse(readFileSync('src/i18n/locales/en.json', 'utf8'))

assert(
  routeSource.includes("path: '/executive-sales-intelligence/purchase-amount-dashboard'"),
  '路由应注册进货金额看板地址',
)
assert(
  routeSource.includes("title: 'menu.purchaseAmountDashboard'") &&
    routeSource.includes("accessKey: 'canManageLocalPurchase'"),
  '进货金额看板路由应绑定中英文菜单键和本地进货权限',
)
assert(zh.menu.purchaseAmountDashboard === '进货金额看板', '中文菜单名称应为“进货金额看板”')
assert(en.menu.purchaseAmountDashboard === 'Purchase Amount Dashboard', '英文菜单名称应完整')
assert(
  pageSource.includes('width="min(1100px, 92vw)"') &&
    pageSource.includes('scroll={getPurchaseMatrixScroll(visibleStores.length)}') &&
    pageSource.includes('scroll={getSupplierDetailScroll(drawerMonths.length)}'),
  '页面应使用已通过纯函数测试的主表和抽屉滚动配置',
)
assert(
  pageSource.includes('...getPurchaseMonthColumnLayout()') &&
    pageSource.includes("dataIndex: 'month'") &&
    pageSource.includes('dataSource={monthRows}'),
  '主表应接入已通过纯函数测试的固定月份列配置，并以月份为行',
)
assert(
  pageSource.includes('mode="multiple"') &&
    pageSource.includes('optionFilterProp="label"') &&
    pageSource.includes('label: `${store.storeName} (${store.storeCode})`') &&
    pageSource.includes('onChange={setSelectedStoreCodes}'),
  '分店筛选应支持按名称或编码搜索的多选模式，清空值由空数组表示全部',
)
assert(
  pageSource.includes('AbortController') && pageSource.includes('createLatestRequestGuard'),
  '主表与抽屉请求应同时支持取消和请求序号保护',
)
assert(
  pageSource.includes('const openStore = useCallback(async') &&
    pageSource.includes('}, [endMonthKey, t])') &&
    pageSource.includes('], [openStore, t, visibleStores])'),
  '分店明细回调应稳定引用当前月份和文案，并纳入动态列依赖',
)
assert(
  pageSource.includes("aria-label={t('purchaseAmountDashboard.endMonth')}") &&
    pageSource.includes('picker="month"'),
  '结束月份控件应提供本地化可访问名称',
)
assert(
  pageSource.includes("dashboardViewState === 'error' ? null") &&
    pageSource.includes("dashboardViewState === 'loading'") &&
    pageSource.includes("dashboardViewState === 'empty'") &&
    pageSource.includes("dashboardViewState === 'ready'") &&
    pageSource.includes("supplierViewState === 'error'") &&
    pageSource.includes("supplierViewState === 'loading'") &&
    pageSource.includes("supplierViewState === 'empty'") &&
    pageSource.includes("supplierViewState === 'ready'"),
  '主表和抽屉应按互斥视图状态渲染，主表错误由独立 Alert 处理',
)
assert(
  pageSource.includes('setReport(null)') && pageSource.indexOf('setReport(null)') < pageSource.indexOf('getLocalPurchaseDashboard('),
  '切换月份或刷新时应在发起主请求前清空旧报表',
)
assert(
  pageSource.includes('aria-label={t(\'purchaseAmountDashboard.openStoreDetail\'') &&
    pageSource.includes('icon={<EyeOutlined />}') &&
    pageSource.includes('onClick={() => void openStore(store)}'),
  '每个分店列头应提供有可访问名称的明细图标按钮并复用现有抽屉',
)
assert(
  pageSource.includes("t('purchaseAmountDashboard.totalShort')") &&
    pageSource.includes("t('purchaseAmountDashboard.warehouseShort')") &&
    pageSource.includes("t('purchaseAmountDashboard.localSupplierShort')") &&
    pageSource.includes("t('purchaseAmountDashboard.salesShort')") &&
    pageSource.includes('<Typography.Text strong>{formatPurchaseAmount(amount.totalAmount)}</Typography.Text>'),
  '每个金额单元格应按合计、仓库、本地、营业额展示四行，并强调合计',
)
assert(
  pageSource.indexOf("t('purchaseAmountDashboard.totalShort')") <
      pageSource.indexOf("t('purchaseAmountDashboard.warehouseShort')") &&
    pageSource.indexOf("t('purchaseAmountDashboard.warehouseShort')") <
      pageSource.indexOf("t('purchaseAmountDashboard.localSupplierShort')") &&
    pageSource.indexOf("t('purchaseAmountDashboard.localSupplierShort')") <
      pageSource.indexOf("t('purchaseAmountDashboard.salesShort')") &&
    pageSource.includes('styles.salesLine'),
  '主矩阵第四行应为带轻量分隔的营业额',
)
assert(
  pageSource.includes('buildPurchaseMonthRows(months)') &&
    pageSource.includes('sortPurchaseMonthsDescending(') &&
    pageSource.includes('...drawerMonths.map((month)'),
  '主表和供应商抽屉应复用展示层月份降序逻辑',
)
assert(
  pageSource.includes('getSupplierDisplayName(') &&
    zh.purchaseAmountDashboard.unassignedSupplier === '未匹配供应商' &&
    en.purchaseAmountDashboard.unassignedSupplier === 'Unassigned Supplier',
  '仓库和未匹配供应商名称应由 UI 使用当前语言显示',
)
assert(
  zh.purchaseAmountDashboard.allStores === '全部分店' &&
    en.purchaseAmountDashboard.allStores === 'All stores' &&
    zh.purchaseAmountDashboard.totalShort === '合计' &&
    en.purchaseAmountDashboard.totalShort === 'Total' &&
    zh.purchaseAmountDashboard.salesShort === '营业额' &&
    en.purchaseAmountDashboard.salesShort === 'Revenue',
  '转置矩阵和分店筛选的中英文文案应完整',
)
assert(
  zh.purchaseAmountDashboard.basisDescription.includes('StoreSalesStatistic.TotalAmount') &&
    zh.purchaseAmountDashboard.basisDescription.includes('按统计日期') &&
    zh.purchaseAmountDashboard.basisDescription.includes('不做 GST 换算') &&
    en.purchaseAmountDashboard.basisDescription.includes('StoreSalesStatistic.TotalAmount') &&
    en.purchaseAmountDashboard.basisDescription.includes('statistic date') &&
    en.purchaseAmountDashboard.basisDescription.includes('no GST conversion'),
  '统计口径应明确营业额来源、日期口径和 GST 处理',
)
assert(
  !serviceSource.includes('/ 1.1') && !serviceSource.includes('/1.1'),
  '本地供应商 TotalAmount 已经未税，前端不得重复扣除 GST',
)

console.log('purchaseAmountDashboard.sourceContract.test: ok')
