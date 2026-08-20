import { readFileSync } from 'node:fs'
import path from 'node:path'

function assertIncludes(source: string, expected: string, label: string) {
  if (!source.includes(expected)) {
    throw new Error(`${label}。Missing: ${expected}`)
  }
}

function assertMissing(source: string, forbidden: string, label: string) {
  if (source.includes(forbidden)) {
    throw new Error(`${label}。Unexpected: ${forbidden}`)
  }
}

const root = process.cwd()
const productCardSource = readFileSync(
  path.join(root, 'src/pages/ShopHome/components/ProductCard.tsx'),
  'utf8',
)
const shopHomeSource = readFileSync(
  path.join(root, 'src/pages/ShopHome/index.tsx'),
  'utf8',
)
const modalSource = readFileSync(
  path.join(root, 'src/pages/ShopHome/components/ProductActivityHistoryModal.tsx'),
  'utf8',
)

assertIncludes(productCardSource, 'onActivityClick', '商品卡必须暴露统一活动历史点击入口')
assertIncludes(productCardSource, 'role="button"', '统一入口必须可聚焦为按钮')
assertIncludes(productCardSource, 'tabIndex={0}', '统一入口必须可键盘聚焦')
assertIncludes(
  productCardSource,
  "t('shop.productActivityHistory.entryAria'",
  '统一入口必须有无障碍文案',
)
assertMissing(productCardSource, 'onOrderHistoryClick', '商品卡不得残留旧历史点击入口')
assertMissing(productCardSource, 'onSalesClick', '商品卡不得残留旧销售点击入口')

assertIncludes(shopHomeSource, 'onActivityClick={handleOpenActivity}', 'ShopHome 必须接线统一活动历史点击')
assertIncludes(shopHomeSource, 'ProductActivityHistoryModal', 'ShopHome 必须使用统一活动历史弹窗')
assertIncludes(shopHomeSource, 'setActivityModalOpen(false)', '切店或关闭时必须关闭统一弹窗')
assertMissing(shopHomeSource, 'ProductOrderHistoryModal', 'ShopHome 不得残留旧历史弹窗')
assertMissing(shopHomeSource, 'ProductSalesDetailsModal', 'ShopHome 不得残留旧销售弹窗')

assertIncludes(modalSource, 'const ACTIVITY_PAGE_SIZE = 30', '统一弹窗必须固定每页 30 条')
assertIncludes(modalSource, 'width={1120}', '统一弹窗宽度必须约 1120')
assertIncludes(modalSource, 'recordType', '统一弹窗必须按 recordType 请求/渲染')
assertIncludes(modalSource, 'onChange: (nextPage) => {', '分页必须处理远端页码切换')
assertIncludes(modalSource, 'setPage(nextPage)', '分页必须更新远端页码')
assertIncludes(modalSource, "status === 'error'", '统一弹窗必须显示错误状态')
assertIncludes(modalSource, 'setReloadToken((value) => value + 1)', '错误状态必须支持重试')
assertIncludes(modalSource, "description={t('shop.productActivityHistory.empty')}", '空数据必须显示本地化空态')
assertIncludes(modalSource, 'scroll={{ x: 1040 }}', '窄屏表格必须支持横向滚动')

const activityColumnCount = (modalSource.match(/dataIndex:/g) ?? []).length
if (activityColumnCount !== 9) {
  throw new Error(`统一弹窗必须保持 9 列。Expected: 9, received: ${activityColumnCount}`)
}

assertIncludes(modalSource, 'new AbortController()', '弹窗必须创建 AbortController 取消旧请求')
assertIncludes(modalSource, 'controller.abort()', '新请求/关闭/切实体/卸载必须取消旧 HTTP')
assertIncludes(modalSource, 'activityEntityKey', '摘要必须绑定门店与商品实体')
assertIncludes(modalSource, 'lastActivityEntityKey !== activityEntityKey', '实体变化必须同步重置分页/筛选/状态')
assertIncludes(modalSource, 'summary.storeCode === currentStoreCode', '摘要只渲染当前门店')
assertIncludes(modalSource, 'summary.productCode === productCode', '摘要只渲染当前商品')

const activityPageSizeOccurrences = (modalSource.match(/pageSize: ACTIVITY_PAGE_SIZE/g) ?? []).length
if (activityPageSizeOccurrences !== 2) {
  throw new Error(`请求与 Table 分页都必须固定每页 30 条。Expected: 2, received: ${activityPageSizeOccurrences}`)
}

assertIncludes(modalSource, "'salesSubtotal'", '统一弹窗必须渲染 salesSubtotal 小计行')
assertIncludes(modalSource, 'periodStartDate', '小计行必须读取 periodStartDate')
assertIncludes(modalSource, 'periodEndDate', '小计行必须读取 periodEndDate')
assertIncludes(modalSource, 'typeSubtotal', '小计类型必须使用本地化文案')
assertIncludes(modalSource, ' ~ ', '小计日期列必须显示 YYYY-MM-DD ~ YYYY-MM-DD')
assertIncludes(modalSource, 'value ?? 0', '小计销量缺失时必须显示 0')
assertIncludes(modalSource, 'record.salesQuantity', '小计均价必须依据销量与均价决定空值')
assertIncludes(modalSource, 'shop-product-activity-subtotal-row', '小计行必须有稳定行样式')
assertIncludes(modalSource, 'rowClassName', '小计行必须通过 rowClassName 强调')
assertMissing(modalSource, 'detailsExpanded', '不得折叠全部明细区域')
assertMissing(modalSource, '<Collapse', '订货发货和 Interval total 必须默认显示')
assertIncludes(modalSource, 'buildProductActivityTableRows(items)', '每日 Sales 必须按区间归入折叠子行')
assertIncludes(modalSource, 'const [expandedSalesPeriodKeys, setExpandedSalesPeriodKeys] = useState<string[]>([])', '销售区间必须默认收起')
assertIncludes(modalSource, 'expandedRowKeys: expandedSalesPeriodKeys', '销售区间展开状态必须由 Table 显式控制')
assertIncludes(modalSource, 'rowExpandable:', '只有包含每日 Sales 的区间行允许展开')
assertIncludes(modalSource, "aria-label={t('shop.productActivityHistory.filter')}", '筛选按钮组必须有无障碍名称')

console.log('productActivityHistoryUiContract.test: ok')
