import { readFileSync } from 'node:fs'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message)
}

const source = readFileSync('src/pages/Warehouse/ProductRecords/SalesPanel.tsx', 'utf8')

assert(source.includes('CalendarOutlined'), '分店日记录入口应使用日历图标')
assert(source.includes('Modal') && source.includes('Tooltip'), '分店日记录应使用 Modal 和 Tooltip')
assert(source.includes('<Tooltip title={viewBranchDailyLabel}>'), '日历图标应显示查看分店日记录提示')
assert(
  source.includes('type="text"') && source.includes('size="small"') && source.includes('icon={<CalendarOutlined />}'),
  '分店日记录入口应为紧凑文本图标按钮',
)
assert(
  source.includes('aria-label={`${viewBranchDailyLabel}：${branch.branchName || branch.branchCode}（${branch.branchCode}）`}'),
  '分店日记录入口的无障碍名称应包含分店名称和编码',
)

assert(source.includes('<Modal'), '分店日记录应在弹窗中展示')
assert(source.includes('width={760}') && source.includes('footer={null}'), '弹窗应为 760px 且无确认按钮')
assert(
  source.includes('keyboard')
    && source.includes('maskClosable')
    && source.includes('focusTriggerAfterClose')
    && source.includes('destroyOnHidden'),
  '弹窗应支持 Esc、遮罩关闭、焦点恢复并在关闭后销毁内容',
)
assert(source.includes('onCancel={closeBranchDailyModal}'), '所有弹窗关闭路径应复用统一清理函数')
assert(
  source.includes("scroll={{ x: 480, y: '32vh' }}"),
  '分店日记录表应限制纵向高度并保留横向滚动',
)

assert(source.includes("import DailySalesChart from '../../ExecutiveSalesIntelligence/ProductSalesAnalysis/DailySalesChart'"), '分店日记录应复用现有每日销量图表')
assert(source.includes('const branchDailyChartData = branchDaily.map'), '图表数据应直接复用分店日记录响应')
assert(
  source.includes('date: item.date')
    && source.includes('quantity: item.quantity')
    && source.includes('averageUnitPrice: item.averageUnitPrice'),
  '图表应映射日期、净销量和均价',
)
assert(source.includes('<DailySalesChart') && source.includes('height={200}'), '弹窗应渲染 200px 高的紧凑图表')
assert(
  source.includes("t('warehouseProductRecords.branchDaily')")
    && source.includes("t('warehouseProductRecords.netQuantity')")
    && source.includes("t('warehouseProductRecords.averagePrice')"),
  '图表名称和图例应复用已有分店日记录、净销量及均价文案',
)
assert(
  source.indexOf('<DailySalesChart') < source.indexOf('dataSource={branchDaily}'),
  '图表应显示在分店日记录表格上方',
)
assert(
  source.includes('!branchLoading && branchDaily.length > 0'),
  '图表只应在分店日数据加载成功且非空时显示',
)

assert(source.includes('const [branchError, setBranchError]'), '分店请求应使用独立错误状态')
assert(source.includes('setBranchError(getErrorMessage('), '分店请求错误应写入弹窗错误状态')
assert(source.includes('branchAbortRef.current?.abort()'), '分店状态清理应取消未完成请求')
assert(source.includes('branchGuardRef.current.invalidate()'), '分店状态清理应使旧请求失效')
assert(source.includes('setSelectedBranchCode(null)') && source.includes('setBranchDaily([])'), '关闭弹窗应清空分店和日记录')

assert(!source.includes('{selectedBranchCode ? ('), '分店日记录不得替换页面主表')
assert(source.includes("title={t('warehouseProductRecords.branches')}"), '分店统计应始终保留在页面')
assert(source.includes("title={t('warehouseProductRecords.dailySummary')}"), '全部日汇总应始终保留在页面')

console.log('SalesPanel.sourceContract.test: ok')
