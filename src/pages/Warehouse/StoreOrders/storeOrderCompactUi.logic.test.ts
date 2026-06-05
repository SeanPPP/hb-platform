import { readFileSync } from 'node:fs'
import path from 'node:path'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

async function runTest(name: string, execute: () => void | Promise<void>): Promise<string | null> {
  try {
    await execute()
    console.log(`ok - ${name}`)
    return null
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error)
    console.error(`not ok - ${name}`)
    console.error(reason)
    return `${name}: ${reason}`
  }
}

const storeOrdersFile = path.resolve(process.cwd(), 'src/pages/Warehouse/StoreOrders/index.tsx')
const detailFile = path.resolve(process.cwd(), 'src/pages/Warehouse/StoreOrders/Detail.tsx')
const compactCssFile = path.resolve(process.cwd(), 'src/pages/Warehouse/StoreOrders/compact.css')
const pickingListFile = path.resolve(process.cwd(), 'src/pages/Warehouse/StoreOrders/PickingList.tsx')
const invoiceFile = path.resolve(process.cwd(), 'src/pages/Warehouse/StoreOrders/Invoice.tsx')
const printCssFile = path.resolve(process.cwd(), 'src/pages/Warehouse/StoreOrders/print.css')
const packageFile = path.resolve(process.cwd(), 'package.json')

const storeOrdersSource = readFileSync(storeOrdersFile, 'utf8')
const detailSource = readFileSync(detailFile, 'utf8')
const compactCssSource = readFileSync(compactCssFile, 'utf8')
const pickingListSource = readFileSync(pickingListFile, 'utf8')
const invoiceSource = readFileSync(invoiceFile, 'utf8')
const printCssSource = readFileSync(printCssFile, 'utf8')
const packageSource = readFileSync(packageFile, 'utf8')
const detailMainTableSource = detailSource.slice(detailSource.indexOf('const columns: ColumnsType<StoreOrderDetailLine>'))

function readCssRule(source: string, selector: string) {
  const escapedSelector = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  const match = source.match(new RegExp(`${escapedSelector}\\s*\\{([\\s\\S]*?)\\}`))
  return match?.[1] ?? ''
}

function readColumnBlock(source: string, dataIndex: string) {
  const dataIndexPosition = source.indexOf(`dataIndex: '${dataIndex}'`)
  if (dataIndexPosition < 0) {
    return ''
  }
  const blockStart = source.lastIndexOf('    {', dataIndexPosition)
  const nextBlockStart = source.indexOf('    {', dataIndexPosition + dataIndex.length)
  return source.slice(blockStart, nextBlockStart > 0 ? nextBlockStart : source.length)
}

function readNumericValue(source: string, pattern: RegExp) {
  const match = source.match(pattern)
  return match ? Number(match[1]) : Number.NaN
}

async function main() {
  const failures: string[] = []

  const detailClassFailure = await runTest('详情页主明细表应挂载紧凑样式 class', () => {
    assert(detailSource.includes("import './compact.css'"), '详情页应引入 StoreOrders 局部紧凑样式')
    assert(detailSource.includes('className="store-order-detail-table"'), '详情页主明细表缺少 store-order-detail-table class')
    assert(detailSource.includes('className="store-order-detail-filter-bar"'), '详情页筛选统计条缺少紧凑样式 class')
    assert(detailSource.includes('renderStoreOrderDetailNumericCell('), '详情页数字列应走单行等宽数字 helper')
  })
  if (detailClassFailure) failures.push(detailClassFailure)

  const listOrderNoFailure = await runTest('列表页订单号复制按钮应限制在订单号列内', () => {
    const orderCellRule = readCssRule(compactCssSource, '.store-order-list-table .store-order-list-order-cell')
    const orderButtonRule = readCssRule(compactCssSource, '.store-order-list-table .store-order-list-order-no')
    const copyButtonRule = readCssRule(compactCssSource, '.store-order-list-table .store-order-copy-button')

    assert(storeOrdersSource.includes('className="store-order-list-order-cell"'), '订单号列应挂载专属布局 class')
    assert(/width:\s*100%/.test(orderCellRule), '订单号布局容器应占满单元格宽度')
    assert(/min-width:\s*0/.test(orderCellRule), '订单号布局容器应允许内容收缩')
    assert(/flex:\s*0\s+0\s+auto/.test(orderButtonRule), '订单号文本应完整显示，不应被压缩省略')
    assert(!/text-overflow:\s*ellipsis/.test(orderButtonRule), '订单号文本不应省略显示')
    assert(!/overflow:\s*hidden/.test(orderButtonRule), '订单号文本不应被隐藏截断')
    assert(/flex:\s*0\s+0\s+20px/.test(copyButtonRule), '复制按钮应固定宽度，避免被挤出列')
  })
  if (listOrderNoFailure) failures.push(listOrderNoFailure)

  const listTwoLineFailure = await runTest('列表页分店和备注应最多显示两行', () => {
    const storeTagRule = readCssRule(compactCssSource, '.store-order-list-table .store-order-store-tag')
    const twoLineRule = readCssRule(compactCssSource, '.store-order-list-table .store-order-two-line-text')

    assert(storeOrdersSource.includes('className="store-order-store-tag"'), '分店列应挂载专属两行样式 class')
    assert(storeOrdersSource.includes('renderStoreOrderTwoLineText(value)'), '备注列应使用两行文本 helper')
    assert(/-webkit-line-clamp:\s*2/.test(storeTagRule), '分店名称应最多显示两行')
    assert(/overflow:\s*hidden/.test(storeTagRule), '分店名称超过两行应隐藏')
    assert(/white-space:\s*normal/.test(storeTagRule), '分店名称应允许换行')
    assert(/-webkit-line-clamp:\s*2/.test(twoLineRule), '备注应最多显示两行')
    assert(/overflow:\s*hidden/.test(twoLineRule), '备注超过两行应隐藏')
    assert(/white-space:\s*normal/.test(twoLineRule), '备注应允许换行')
  })
  if (listTwoLineFailure) failures.push(listTwoLineFailure)

  const listStatusFilterFailure = await runTest('列表页状态筛选应使用多选框并默认勾选已提交和配货中', () => {
    assert(storeOrdersSource.includes('Checkbox.Group'), '状态筛选应使用 Checkbox.Group')
    assert(storeOrdersSource.includes('const DEFAULT_STATUS_LIST = [FlowStatus.Submitted, FlowStatus.Picking]'), '默认状态筛选应为已提交和配货中')
    assert(storeOrdersSource.includes('useState<StoreOrderFlowStatus[]>(DEFAULT_STATUS_LIST)'), '状态筛选初始值应复用默认状态列表')
    assert(storeOrdersSource.includes('setStatusList(DEFAULT_STATUS_LIST)'), '重置时应恢复默认状态筛选')
    assert(storeOrdersSource.includes('statusList: DEFAULT_STATUS_LIST'), '重置后查询应按默认状态筛选发起')
    assert(storeOrdersSource.includes('const STATUS_FILTER_ORDER = [FlowStatus.Submitted, FlowStatus.Picking, FlowStatus.Completed]'), '状态筛选展示顺序应把已完成放在最后')
    assert(!storeOrdersSource.includes('<Select\n            mode="multiple"\n            value={statusList}'), '状态筛选不应继续使用多选 Select')
  })
  if (listStatusFilterFailure) failures.push(listStatusFilterFailure)

  const detailContentFailure = await runTest('详情页货号条码名称应保留业务可读性', () => {
    assert(detailMainTableSource.includes('width={30}') && detailMainTableSource.includes('height={30}'), '详情页主明细图片应缩到 30x30')
    assert(detailMainTableSource.includes('className="store-order-detail-copy-button"'), '详情页货号复制按钮应为无文字图标按钮')
    assert(!detailMainTableSource.includes('<Button size="small" type="link" onClick={() => void copyTextToClipboard(value)}>'), '详情页主明细货号复制按钮不应显示复制文字')
    assert(detailMainTableSource.includes('className="store-order-barcode-cell"'), '详情页条码文本应挂载不隐藏不折叠样式')
    assert(detailMainTableSource.includes('textNoWrap'), '详情页条码文本应保持单行显示')
    assert(detailMainTableSource.includes('showCopy={false}'), '详情页主明细条码列应关闭复制按钮以保留条码可读宽度')
    assert(!detailMainTableSource.includes('textMaxWidth'), '详情页条码文本不应设置 textMaxWidth 省略折叠')
    assert(detailMainTableSource.includes('renderStoreOrderTwoLineText(value)'), '详情页商品名称应最多显示两行')
    assert(detailMainTableSource.includes("Tooltip title={t('common.save')}"), '详情页操作列保存按钮应使用 Tooltip 图标按钮')
    assert(detailMainTableSource.includes('className="store-order-detail-action-button"'), '详情页操作列应使用紧凑图标按钮样式')
  })
  if (detailContentFailure) failures.push(detailContentFailure)

  const detailProductStatusCopyFailure = await runTest('详情页商品状态应使用上下架文案', () => {
    const statusColumn = readColumnBlock(detailMainTableSource, 'isActive')

    assert(statusColumn.includes("t('common.activeUpper')") && statusColumn.includes("t('common.inactiveUpper')"), '详情页商品状态列应显示上架/下架')
    assert(detailMainTableSource.includes("record.isActive ? t('common.inactiveUpper') : t('common.activeUpper')"), '详情页商品状态切换按钮应提示上架/下架')
    assert(detailSource.includes("status: line.isActive ? t('common.inactiveUpper') : t('common.activeUpper')"), '详情页商品状态切换成功提示应使用上架/下架')
    assert(detailSource.includes("{ value: 'active', label: t('common.activeUpper') }") && detailSource.includes("{ value: 'inactive', label: t('common.inactiveUpper') }"), '批量修改状态下拉应使用上架/下架')
  })
  if (detailProductStatusCopyFailure) failures.push(detailProductStatusCopyFailure)

  const densityFailure = await runTest('详情页主明细表关键字段应压缩到首屏优先显示', () => {
    const imageColumn = readColumnBlock(detailMainTableSource, 'productImage')
    const itemNumberColumn = readColumnBlock(detailMainTableSource, 'itemNumber')
    const productNameColumn = readColumnBlock(detailMainTableSource, 'productName')
    const barcodeColumn = readColumnBlock(detailMainTableSource, 'barcode')
    const locationColumn = readColumnBlock(detailMainTableSource, 'locationCode')
    const allocQuantityColumn = readColumnBlock(detailMainTableSource, 'allocQuantity')
    const importPriceColumn = readColumnBlock(detailMainTableSource, 'importPrice')
    const scrollX = readNumericValue(detailMainTableSource, /scroll=\{\{\s*x:\s*(\d+)/)

    assert(readNumericValue(imageColumn, /width:\s*(\d+)/) <= 44, '图片列宽应压到 44 以内')
    assert(readNumericValue(imageColumn, /width=\{(\d+)\}/) <= 32, '图片宽度应压到 32 以内')
    assert(readNumericValue(imageColumn, /height=\{(\d+)\}/) <= 32, '图片高度应压到 32 以内')
    assert(readNumericValue(itemNumberColumn, /width:\s*(\d+)/) <= 80, '货号列宽应压到 80 以内')
    assert(readNumericValue(productNameColumn, /width:\s*(\d+)/) >= 128, '商品名称列应保留至少 128 宽度')
    assert(readNumericValue(barcodeColumn, /width:\s*(\d+)/) <= 106, '条码列宽应控制在 106 以内')
    assert(readNumericValue(locationColumn, /width:\s*(\d+)/) <= 85, '货位列宽应压到 85 以内')
    assert(readNumericValue(allocQuantityColumn, /style=\{\{\s*width:\s*(\d+)/) <= 62, '发货数输入框宽度应压到 62 以内')
    assert(readNumericValue(importPriceColumn, /style=\{\{\s*width:\s*(\d+)/) <= 62, '进口价输入框宽度应压到 62 以内')
    assert(scrollX >= 1280 && scrollX <= 1320, '主表 scroll.x 应收敛到 1280-1320')
  })
  if (densityFailure) failures.push(densityFailure)

  const cssFailure = await runTest('局部 CSS 应提供紧凑表格、两行文本、nowrap 和等宽数字规则', () => {
    const barcodeCellRule = readCssRule(compactCssSource, '.store-order-detail-table .store-order-barcode-cell')
    const barcodeTextRule = readCssRule(compactCssSource, '.store-order-detail-table .store-order-barcode-cell .ant-typography')
    const inputNumberRule = readCssRule(compactCssSource, '.store-order-detail-table .ant-input-number')
    const detailCellRule = readCssRule(compactCssSource, '.store-order-detail-table .ant-table-cell')

    assert(compactCssSource.includes('.store-order-detail-table .ant-table-cell'), '详情表格缺少局部 cell padding 规则')
    assert(compactCssSource.includes('.store-order-list-table .store-order-list-order-cell'), '列表订单号列缺少局部防溢出样式')
    assert(compactCssSource.includes('.store-order-list-table .store-order-store-tag'), '列表分店列缺少两行截断样式')
    assert(compactCssSource.includes('.store-order-list-table .store-order-two-line-text'), '列表备注列缺少两行截断样式')
    assert(!/^\\.store-order-nowrap/m.test(compactCssSource), 'nowrap 工具类必须限定到详情主表下')
    assert(!/^\\.store-order-numeric-cell/m.test(compactCssSource), '数字工具类必须限定到详情主表下')
    assert(!/^\\.store-order-two-line-text/m.test(compactCssSource), '两行文本工具类必须限定到详情主表下')
    assert(compactCssSource.includes('-webkit-line-clamp: 2'), '紧凑样式缺少最多两行规则')
    assert(compactCssSource.includes('white-space: nowrap'), '紧凑样式缺少 nowrap 规则')
    assert(compactCssSource.includes('font-variant-numeric: tabular-nums'), '紧凑样式缺少等宽数字规则')
    assert(compactCssSource.includes('.store-order-detail-filter-bar'), '详情筛选统计条缺少紧凑样式')
    assert(compactCssSource.includes('.store-order-detail-table .store-order-barcode-cell .ant-typography'), '条码文本缺少不隐藏不折叠样式')
    assert(/vertical-align:\s*middle/.test(detailCellRule), '详情主表单元格应垂直居中')
    assert(/white-space:\s*nowrap/.test(barcodeCellRule), '条码容器应强制单行，避免条码图片和文本换行')
    assert(/overflow:\s*visible/.test(barcodeCellRule), '条码容器不应隐藏超出内容')
    assert(/text-overflow:\s*clip/.test(barcodeTextRule), '条码文本不应省略隐藏')
    assert(/white-space:\s*nowrap/.test(inputNumberRule), '详情主表输入型数字列应保持单行')
  })
  if (cssFailure) failures.push(cssFailure)

  const detailTableStripeFailure = await runTest('详情页主明细表应有隔行色并保持固定列和 hover 一致', () => {
    assert(
      compactCssSource.includes('.store-order-detail-table .ant-table-tbody > tr:nth-child(even) > td'),
      '详情主表缺少偶数行隔行色规则',
    )
    assert(
      compactCssSource.includes('.store-order-detail-table .ant-table-tbody > tr:nth-child(odd) > td'),
      '详情主表缺少奇数行隔行色规则',
    )
    assert(
      compactCssSource.includes('.store-order-detail-table .ant-table-tbody > tr:hover > td'),
      '详情主表缺少 hover 行背景规则',
    )
    assert(compactCssSource.includes('.ant-table-cell-fix-left'), '详情主表固定左列背景应跟随行背景')
    assert(compactCssSource.includes('.ant-table-cell-fix-right'), '详情主表固定右列背景应跟随行背景')
    assert(!/^\\.ant-table-tbody\s*>/m.test(compactCssSource), '隔行色规则必须限定在详情主表下')
  })
  if (detailTableStripeFailure) failures.push(detailTableStripeFailure)

  const detailTableVerticalAlignFailure = await runTest('详情页主明细表内部元素应垂直居中', () => {
    assert(
      compactCssSource.includes('.store-order-detail-table .ant-table-tbody > tr > td .ant-space'),
      '详情主表 Space 内容应垂直居中',
    )
    assert(
      compactCssSource.includes('.store-order-detail-table .ant-table-tbody > tr > td .ant-image'),
      '详情主表图片内容应垂直居中',
    )
    assert(
      compactCssSource.includes('.store-order-detail-table .ant-table-tbody > tr > td .ant-tag'),
      '详情主表状态标签应垂直居中',
    )
    assert(
      compactCssSource.includes('.store-order-detail-table .ant-table-tbody > tr > td .ant-input-number'),
      '详情主表数字输入框应垂直居中',
    )
    assert(compactCssSource.includes('.store-order-detail-table .store-order-two-line-text'), '详情主表两行文本应保留局部样式')
    assert(compactCssSource.includes('align-items: center'), '详情主表内部 flex 元素缺少居中对齐')
  })
  if (detailTableVerticalAlignFailure) failures.push(detailTableVerticalAlignFailure)

  const packageScriptFailure = await runTest('订货明细标准测试脚本应包含紧凑 UI 约束', () => {
    assert(packageSource.includes('storeOrderCompactUi.logic.test.ts'), 'test:store-order-detail 应接入 storeOrderCompactUi.logic.test.ts')
  })
  if (packageScriptFailure) failures.push(packageScriptFailure)

  const printIsolationFailure = await runTest('本次紧凑样式不应接入打印页面', () => {
    assert(!pickingListSource.includes('./compact.css'), '配货单打印页不应引入页面紧凑样式')
    assert(!invoiceSource.includes('./compact.css'), '发票页不应引入页面紧凑样式')
    assert(!printCssSource.includes('store-order-list-table'), '打印 CSS 不应包含列表页紧凑样式')
    assert(!printCssSource.includes('store-order-detail-table'), '打印 CSS 不应包含详情页紧凑样式')
  })
  if (printIsolationFailure) failures.push(printIsolationFailure)

  if (failures.length > 0) {
    throw new Error(`共有 ${failures.length} 个测试失败\n- ${failures.join('\n- ')}`)
  }

  console.log('storeOrderCompactUi.logic.test: ok')
}

await main()
