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
const containerProductPickerFile = path.resolve(process.cwd(), 'src/pages/Warehouse/StoreOrders/components/ContainerProductPicker.tsx')
const printCssFile = path.resolve(process.cwd(), 'src/pages/Warehouse/StoreOrders/print.css')
const packageFile = path.resolve(process.cwd(), 'package.json')

function readSource(file: string) {
  // 统一换行，避免 Windows CRLF 让源码契约断言误判。
  return readFileSync(file, 'utf8').replace(/\r\n/g, '\n')
}

const storeOrdersSource = readSource(storeOrdersFile)
const detailSource = readSource(detailFile)
const compactCssSource = readSource(compactCssFile)
const pickingListSource = readSource(pickingListFile)
const invoiceSource = readSource(invoiceFile)
const containerProductPickerSource = readSource(containerProductPickerFile)
const printCssSource = readSource(printCssFile)
const packageSource = readSource(packageFile)
const detailMainTableSource = detailSource.slice(detailSource.indexOf('const columns: ColumnsType<StoreOrderDetailLine>'))
const detailKeyboardHandlerSource = detailSource.slice(
  detailSource.indexOf('const handleDetailInputKeyDown'),
  detailSource.indexOf('const handleCompleteOrder'),
)

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

  const listColumnDragFailure = await runTest('列表页主表应支持和货柜明细一致的表头列拖拽', () => {
    assert(
      storeOrdersSource.includes('DndContext') &&
        storeOrdersSource.includes('SortableContext') &&
        storeOrdersSource.includes('useSortable') &&
        storeOrdersSource.includes('horizontalListSortingStrategy'),
      '列表页主表应复用 @dnd-kit 横向排序能力',
    )
    assert(
      storeOrdersSource.includes("const STORE_ORDER_LIST_COLUMN_ORDER_STORAGE_KEY = 'hbweb_rv.storeOrders.list.columnOrder.v1'") &&
        storeOrdersSource.includes('localStorage.setItem(STORE_ORDER_LIST_COLUMN_ORDER_STORAGE_KEY') &&
        storeOrdersSource.includes('mergeStoreOrderListColumnOrder('),
      '列表页列顺序应保存到专用 localStorage key，并兼容列增删',
    )
    assert(
      storeOrdersSource.includes('components={{ header: { cell: DraggableHeaderCell } }}') &&
        storeOrdersSource.includes('<SortableContext items={columnOrder} strategy={horizontalListSortingStrategy}>') &&
        storeOrdersSource.includes('<DndContext sensors={columnDragSensors} collisionDetection={closestCenter} onDragEnd={handleColumnDragEnd}>'),
      '列表页表格应接入可拖拽表头 cell 与横向 SortableContext',
    )
    assert(
      storeOrdersSource.includes('isStoreOrderListColumnOrderCustomized(columnOrder, draggableColumnKeys)') &&
        storeOrdersSource.includes('setColumnOrder(draggableColumnKeys)') &&
        storeOrdersSource.includes('localStorage.removeItem(STORE_ORDER_LIST_COLUMN_ORDER_STORAGE_KEY)'),
      '列表页拖拽列后应提供重置列按钮并清除本地列顺序',
    )
    assert(
      storeOrdersSource.includes('const draggableColumnKeys = baseColumns.map((column) => String(column.key) as StoreOrderListTableColumnKey)') &&
        storeOrdersSource.includes('rowSelection={') &&
        !storeOrdersSource.includes("columnOrder.includes('selection')"),
      '列表页选择列仍应由 rowSelection 管理，不能进入业务列拖拽顺序',
    )
    assert(
      compactCssSource.includes('.store-order-list-draggable-header') &&
        compactCssSource.includes('cursor: move') &&
        compactCssSource.includes('user-select: none'),
      '列表页拖拽表头应有局部样式，避免影响其他表格',
    )
  })
  if (listColumnDragFailure) failures.push(listColumnDragFailure)

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

  const listColumnFilterFailure = await runTest('列表页主表列头筛选应走服务端查询参数并支持重置', () => {
    assert(storeOrdersSource.includes('StoreOrderListColumnFilters'), '列表页应引入列头筛选类型')
    assert(storeOrdersSource.includes('const [columnFilters, setColumnFilters] = useState<StoreOrderListColumnFilters>({})'), '列表页应维护列头筛选状态')
    assert(storeOrdersSource.includes('columnFilters: cleanStoreOrderListColumnFilters('), '列表查询应携带清理后的 columnFilters')
    assert(storeOrdersSource.includes('setColumnFilters({})'), '重置按钮应清空列头筛选状态')
    assert(storeOrdersSource.includes('columnFilters: undefined'), '重置查询应显式清空服务端列筛选参数')
    assert(storeOrdersSource.includes('makeTextFilterDropdown') && storeOrdersSource.includes('makeNumberRangeFilterDropdown') && storeOrdersSource.includes('makeDateRangeFilterDropdown'), '列表页应提供文本、数值范围和日期范围筛选弹层')
    assert(storeOrdersSource.includes('makeStoreFilterDropdown') && storeOrdersSource.includes('makeStatusFilterDropdown') && storeOrdersSource.includes('makeOrderDateFilterDropdown'), '分店、状态和订单日期列头筛选应复用顶部筛选状态')
    assert(storeOrdersSource.includes("onMouseDown={(event) => event.stopPropagation()}"), '列头筛选弹层应阻止鼠标事件冒泡，避免触发表头拖拽')
    assert(compactCssSource.includes('.store-order-list-column-filter'), '列头筛选弹层应有局部紧凑样式')
  })
  if (listColumnFilterFailure) failures.push(listColumnFilterFailure)

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

  const containerPickerRetailPriceFailure = await runTest('货柜选品弹窗商品表格应展示零售价列', () => {
    const retailPriceColumn = readColumnBlock(containerProductPickerSource, '零售价格')
    const importPricePosition = containerProductPickerSource.indexOf("title: t('column.importPrice')")
    const retailPricePosition = containerProductPickerSource.indexOf("title: t('column.retailPrice')")
    const containerQtyPosition = containerProductPickerSource.indexOf("title: t('column.containerQty')")

    assert(retailPricePosition > importPricePosition, '零售价列应位于进口价列之后')
    assert(retailPricePosition < containerQtyPosition, '零售价列应位于货柜数量列之前')
    assert(retailPriceColumn.includes("title: t('column.retailPrice')"), '零售价列应使用 column.retailPrice 翻译')
    assert(retailPriceColumn.includes('record.商品信息?.零售价格'), '零售价列应读取商品信息中的零售价格')
    assert(retailPriceColumn.includes("value === undefined || value === null ? '--' : Number(value).toFixed(2)"), '零售价列缺失显示 --，有效值应保留两位')
    assert(!containerProductPickerSource.includes('retailPrice:'), '货柜选品加入订单 payload 不应写入零售价')
  })
  if (containerPickerRetailPriceFailure) failures.push(containerPickerRetailPriceFailure)

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
    assert(importPriceColumn.includes('controls={false}'), '进口价输入框应隐藏加减按钮，避免误触改价')
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

  const detailBulkSaveFailure = await runTest('详情页应提供整单保存且只提交已修改明细行', () => {
    assert(detailSource.includes('handleSaveEditedLines'), '详情页缺少整单保存处理函数')
    assert(detailSource.includes('getEditedLinePayloads()'), '整单保存应从已修改行生成 payload')
    assert(detailSource.includes('batchUpdateStoreOrderLines({'), '整单保存应复用明细批量保存接口')
    assert(detailSource.includes('detailGUID: item.detailGUID'), '整单保存 payload 应携带明细 GUID 以命中后端快路径')
    assert(detailSource.includes("t('storeOrders.detail.saveEditedLines'"), '详情页缺少整单保存按钮文案')
    assert(
      detailSource.includes('disabled={isReadonlyOrder || isPasteOptimisticPreviewActive || editedLineCount === 0}'),
      '整单保存应在只读、临时预览或无修改时禁用',
    )
    assert(detailSource.includes('setEditingRows((current) => {') && detailSource.includes('savedDetailGUIDs'), '整单保存成功后应清理已保存行编辑状态')
  })
  if (detailBulkSaveFailure) failures.push(detailBulkSaveFailure)

  const detailRefreshImportPriceFailure = await runTest('详情页应允许仓库管理员二次确认后从仓库表更新进货价', () => {
    assert(detailSource.includes('refreshStoreOrderImportPrices'), '详情页应调用更新进货价专用服务')
    assert(detailSource.includes('handleRefreshImportPricesFromWarehouse'), '详情页缺少更新进货价处理函数')
    assert(detailSource.includes("t('storeOrders.detail.refreshImportPrices'"), '详情页缺少更新进货价按钮文案')
    assert(
      detailSource.includes('detailGUIDs: isSelectedScope ? targetDetailGUIDs : undefined'),
      '有选中行时应传明细 GUID，未选中时应交给后端整单刷新',
    )
    assert(
      detailSource.includes("t('storeOrders.detail.refreshImportPricesSelectedContent'") &&
        detailSource.includes("t('storeOrders.detail.refreshImportPricesWholeOrderContent'"),
      '更新进货价二次确认应区分选中行和整单范围',
    )
    assert(
      detailSource.includes('disabled={!detail || isPasteOptimisticPreviewActive || refreshImportPriceLoading}'),
      '更新进货价按钮不应因为 isReadonlyOrder 禁用，但临时预览期间应等待后台刷新后再操作',
    )
  })
  if (detailRefreshImportPriceFailure) failures.push(detailRefreshImportPriceFailure)

  const warehouseManagerActionFailure = await runTest('仓库员工仅可看到详情页只读文档入口，不应看到订货管理功能按钮', () => {
    const orderDetailSectionSource = detailSource.slice(
      detailSource.indexOf("title={t('storeOrders.orderDetailSection')}"),
      detailSource.indexOf('className="store-order-detail-filter-bar"'),
    )
    const pickingButtonSource = orderDetailSectionSource.slice(
      orderDetailSectionSource.indexOf('icon={<PrinterOutlined />}'),
      orderDetailSectionSource.indexOf("t('storeOrders.pickingList')"),
    )
    const managerGuardText = '{canUseWarehouseManagerActions ? ('
    const detailExtraGuardText = 'canUseStoreOrderDetailExtraActions ? ('
    const isInsideGuard = (guardText: string, targetPosition: number) => {
      const guardPosition = orderDetailSectionSource.lastIndexOf(guardText, targetPosition)
      const guardClosePosition = orderDetailSectionSource.lastIndexOf(') : null}', targetPosition)
      return guardPosition >= 0 && guardPosition > guardClosePosition
    }
    const invoiceButtonPosition = orderDetailSectionSource.indexOf("t('storeOrders.invoice')")
    const pickingButtonPosition = orderDetailSectionSource.indexOf("t('storeOrders.pickingList')")
    const managerOnlyDetailActions = [
      "t('storeOrders.quickAdd')",
      "t('storeOrders.selectProduct')",
      "t('storeOrders.containerPicker')",
      "t('storeOrders.excelPaste')",
      "t('storeOrders.detail.saveEditedLines')",
      "t('storeOrders.detail.refreshImportPrices')",
      "t('storeOrders.batchModify')",
      "t('storeOrders.detail.selectedRows'",
    ]

    assert(
      storeOrdersSource.includes('const isWarehouseStaffOnly =') &&
        storeOrdersSource.includes('const canUseWarehouseManagerActions = access.canManageWarehouseOrders && !isWarehouseStaffOnly') &&
        storeOrdersSource.includes('const canCreateStoreOrder = access.canWriteOrder || canUseWarehouseManagerActions') &&
        storeOrdersSource.includes('const canDeleteStoreOrder = access.canDeleteOrder || canUseWarehouseManagerActions'),
      '列表页应使用仓库订货管理权限开关，并排除纯 WarehouseStaff 写权限',
    )
    assert(
      storeOrdersSource.includes('{canUseWarehouseManagerActions ? (') &&
        storeOrdersSource.includes("t('storeOrders.syncIncrementalOrders')") &&
        storeOrdersSource.includes("t('storeOrders.fixStoreGuid', '修复分店 GUID')") &&
        storeOrdersSource.includes("t('storeOrders.newOrder')") &&
        storeOrdersSource.includes('disabled={!canCreateStoreOrder}') &&
        storeOrdersSource.includes("t('storeOrders.copyOrder'") &&
        storeOrdersSource.includes("t('storeOrders.batchSubmitted')") &&
        storeOrdersSource.includes("t('storeOrders.batchCompleted')") &&
        storeOrdersSource.includes('{canDeleteStoreOrder ? ('),
      '列表页同步、修复、新建、复制、删除和批量状态按钮应仅仓库订货管理权限可见',
    )
    assert(
      storeOrdersSource.includes('canUseWarehouseManagerActions && (record.flowStatus === FlowStatus.Submitted || record.flowStatus === FlowStatus.Picking)'),
      '列表页配货入口应仅仓库管理员可见',
    )
    assert(
      storeOrdersSource.includes('rowSelection={\n                canUseWarehouseManagerActions'),
      '列表页勾选列应仅仓库管理员可见',
    )
    assert(
      detailSource.includes('const isWarehouseStaffOnly =') &&
        detailSource.includes('const canUseWarehouseManagerActions = access.canManageWarehouseOrders && !isWarehouseStaffOnly'),
      '详情页应使用仓库订货管理权限开关，并排除纯 WarehouseStaff 写权限',
    )
    assert(
      detailSource.includes('const canUseStoreOrderDocumentActions = access.isWarehouseStaff'),
      '详情页应为 WarehouseStaff 提供只读文档入口权限开关',
    )
    assert(
      detailSource.includes('const canUseStoreOrderDetailExtraActions = canUseWarehouseManagerActions || canUseStoreOrderDocumentActions'),
      '详情页明细卡片 extra 应同时允许仓库管理员和 WarehouseStaff 文档入口，避免中文仓库经理被误隐藏',
    )
    assert(
      detailSource.includes('if (canUseWarehouseManagerActions && canEditOrder)'),
      '详情页编辑保护应同时检查仓库管理员权限',
    )
    assert(
      detailSource.includes('extra={\n                  canUseWarehouseManagerActions ? ('),
      '详情页订单头功能按钮应仅仓库管理员可见',
    )
    assert(
      orderDetailSectionSource.includes('canUseStoreOrderDetailExtraActions ? (\n                  <Space wrap>') &&
        orderDetailSectionSource.indexOf(detailExtraGuardText) >= 0 &&
        orderDetailSectionSource.indexOf(detailExtraGuardText) < pickingButtonPosition &&
        !isInsideGuard(managerGuardText, pickingButtonPosition) &&
        pickingButtonSource.includes('navigate(`/warehouse/store-order/picking/${detail.orderGUID}`)') &&
        pickingButtonSource.includes('icon={<PrinterOutlined />}'),
      '详情页配货单按钮应受只读文档入口权限控制，不能只由仓库管理员权限包住',
    )
    assert(
      invoiceButtonPosition > 0 && isInsideGuard(managerGuardText, invoiceButtonPosition),
      '详情页发票按钮仍应仅仓库管理员可见',
    )
    assert(
      managerOnlyDetailActions.every((actionText) => {
        const actionPosition = orderDetailSectionSource.indexOf(actionText)
        return actionPosition > 0 && isInsideGuard(managerGuardText, actionPosition)
      }),
      '详情页明细管理功能按钮应继续受仓库管理员权限保护',
    )
    assert(
      detailSource.includes("column.key !== 'actions'") &&
        detailSource.includes('rowSelection={\n                  canUseWarehouseManagerActions'),
      '详情页行操作列和勾选列应仅仓库管理员可见',
    )
    assert(
      detailSource.includes('disabled={!canUseWarehouseManagerActions || isReadonlyOrder}') &&
        detailSource.includes('disabled={!canUseWarehouseManagerActions || !canEditOutboundDate}'),
      '详情页非仓库管理员应不能编辑订单头和明细输入',
    )
  })
  if (warehouseManagerActionFailure) failures.push(warehouseManagerActionFailure)

  const importPriceConfirmFailure = await runTest('详情页保存进口价变更前应提示同步仓库商品表和分店表', () => {
    assert(detailSource.includes('confirmImportPriceSync'), '详情页缺少进口价同步确认 helper')
    assert(detailSource.includes("t('storeOrders.detail.importPriceSyncConfirmTitle'"), '进口价同步确认缺少标题文案')
    assert(detailSource.includes("t('storeOrders.detail.importPriceSyncConfirmContent'"), '进口价同步确认缺少内容文案')
    assert(detailSource.includes('Checkbox') && detailSource.includes('defaultChecked'), '进口价同步确认应提供默认勾选的 Checkbox')
    assert(detailSource.includes("t('storeOrders.detail.syncImportPriceCheckbox'"), '进口价同步确认缺少勾选文案')
    assert(detailSource.includes('getEditedLinePayloads(syncImportPrice)'), '整单保存应按勾选状态决定是否提交进口价')
    assert(detailSource.includes('importPrice: importPriceChanged ? importPrice : undefined'), '单行保存应始终提交已变更的订单明细进口价')
    assert(detailSource.includes('syncImportPrice: importPriceChanged ? syncImportPrice : undefined'), '单行保存应单独提交商品/分店同步开关')
    assert(detailSource.includes('importPrice: importPriceChanged ? edited.importPrice : undefined'), '整单保存应始终提交已变更的订单明细进口价')
    assert(detailSource.includes('syncImportPrice: importPriceChanged ? syncImportPrice : undefined'), '整单保存应单独提交商品/分店同步开关')
    assert(detailSource.includes('hasImportPriceChanged(line)'), '单行保存应判断进口价是否变更')
    assert(detailSource.includes('payloads.some((item) => item.importPriceChanged)'), '整单保存应判断本次是否包含进口价变更')
  })
  if (importPriceConfirmFailure) failures.push(importPriceConfirmFailure)

  const batchCopyOrderQuantityFailure = await runTest('详情页批量修改应支持把订货数量复制给发货数量', () => {
    const copyBranchStart = detailSource.indexOf("} else if (payload.type === 'copyOrderQuantityToAllocQuantity' && copyOrderQuantityPayload)")
    const copyBranchEnd = detailSource.indexOf('} else {', copyBranchStart + 1)
    const copyBranchSource = detailSource.slice(copyBranchStart, copyBranchEnd)

    assert(
      detailSource.includes('buildBatchCopyOrderQuantityPayload') &&
        detailSource.includes('shouldSubmitBatchCopyOrderQuantity') &&
        detailSource.includes("from './batchCopyOrderQuantity'"),
      '详情页应复用批量复制订货数 helper',
    )
    assert(detailSource.includes("'copyOrderQuantityToAllocQuantity'"), '批量修改类型应包含复制订货数量到发货数量')
    assert(detailSource.includes("t('storeOrders.batchCopyOrderQuantityToAllocQuantity')"), '批量弹窗应展示复制订货数量到发货数量选项')
    assert(detailSource.includes("payload.type === 'copyOrderQuantityToAllocQuantity'"), '批量确认应处理复制订货数量分支')
    assert(copyBranchSource.includes('const changedCopyLines = selectedLines.filter'), '复制订货数量分支应计算实际变化行数')
    assert(copyBranchSource.includes('setEditingRows((current) => {'), '复制订货数量分支应只写页面草稿')
    assert(copyBranchSource.includes('changedCopyLines.forEach'), '复制订货数量分支应只把实际变化行写入发货数草稿')
    assert(copyBranchSource.includes('allocQuantity: Number(line.quantity ?? 0)'), '复制订货数量分支应把订货数量写入发货数草稿')
    assert(!copyBranchSource.includes('batchUpdateStoreOrderLines('), '复制订货数量分支不应立即提交后端')
    assert(!copyBranchSource.includes('loadDetail('), '复制订货数量分支不应立即刷新后端数据')
    assert(detailSource.includes("t('storeOrders.batchCopyOrderQuantityDraftSuccess'"), '复制草稿成功后应提示用户点击整单保存')
    assert(detailSource.includes("t('storeOrders.batchCopyOrderQuantityNoChange')"), '复制后无实际变化时应提示未产生新的发货数变更')
    assert(detailSource.includes("handleBatchConfirm({ type: 'copyOrderQuantityToAllocQuantity' })"), '页面批量复制按钮应复用同一个批量确认分支')
    assert(detailSource.includes('detailGUID: line.detailGUID'), '复制订货数量 payload 应携带明细 GUID 以命中后端快路径')
    assert(detailSource.includes("t('storeOrders.batchCopyOrderQuantityConfirmTitle')"), '风险行应弹出二次确认标题')
    assert(detailSource.includes("t('storeOrders.batchCopyOrderQuantityButton')"), '详情页应提供批量复制按钮短文案')
    assert(
      detailSource.indexOf("t('storeOrders.batchCopyOrderQuantityButton')") <
        detailSource.indexOf("t('storeOrders.pickingList')"),
      '批量复制按钮应放在配货单按钮前面',
    )
  })
  if (batchCopyOrderQuantityFailure) failures.push(batchCopyOrderQuantityFailure)

  const detailActionButtonColorFailure = await runTest('详情页整单保存和 Excel 粘贴按钮应使用不同颜色', () => {
    assert(detailSource.includes('store-order-excel-paste-button'), 'Excel 粘贴按钮应有专用颜色 class')
    assert(detailSource.includes('store-order-save-edited-lines-button'), '整单保存按钮应有专用颜色 class')
    assert(compactCssSource.includes('.store-order-excel-paste-button'), '紧凑样式缺少 Excel 粘贴按钮颜色')
    assert(compactCssSource.includes('.store-order-save-edited-lines-button'), '紧凑样式缺少整单保存按钮颜色')
  })
  if (detailActionButtonColorFailure) failures.push(detailActionButtonColorFailure)

  const keyboardNavigationFailure = await runTest('详情页明细输入框应只支持上下方向键和 Enter 移动焦点', () => {
    assert(detailSource.includes('detailInputRefs'), '详情页缺少明细输入框 ref map')
    assert(detailSource.includes('registerDetailInput'), '详情页缺少明细输入框注册函数')
    assert(detailSource.includes('focusDetailInput'), '详情页缺少明细输入框聚焦函数')
    assert(detailSource.includes('handleDetailInputKeyDown'), '详情页缺少键盘导航处理函数')
    assert(!detailKeyboardHandlerSource.includes("'ArrowRight'"), '键盘导航不应再处理 ArrowRight')
    assert(!detailKeyboardHandlerSource.includes("'ArrowLeft'"), '键盘导航不应再处理 ArrowLeft')
    assert(detailKeyboardHandlerSource.includes("'ArrowDown'") && detailKeyboardHandlerSource.includes("'Enter'"), '键盘导航应处理 ArrowDown 和 Enter')
    assert(detailKeyboardHandlerSource.includes("'ArrowUp'"), '键盘导航应处理 ArrowUp')
    assert(!detailKeyboardHandlerSource.includes("field === 'allocQuantity' ? 'importPrice' : 'allocQuantity'"), '左右键不应再在发货数和进口价之间移动')
    assert(!detailKeyboardHandlerSource.includes('nextField'), '上下方向键不应再引入横向目标字段')
    assert(detailKeyboardHandlerSource.includes('event.preventDefault()'), '上下方向键应阻止 InputNumber 默认加减')
    assert(detailKeyboardHandlerSource.includes('if (!nextRow)'), '上下方向键越过首尾行时应安全返回')
    assert(detailKeyboardHandlerSource.includes('focusDetailInput(nextRow.detailGUID, field)'), '上下方向键和 Enter 应保持当前列移动焦点')
    assert(detailSource.includes("focus?.({ cursor: 'all' })"), '方向键切入输入框后应默认全选文本，方便直接覆盖编辑')
    assert(detailMainTableSource.includes('onKeyDown={(event) => handleDetailInputKeyDown(event, record.detailGUID, \'allocQuantity\')}'), '发货数输入框应绑定键盘导航')
    assert(detailMainTableSource.includes('onKeyDown={(event) => handleDetailInputKeyDown(event, record.detailGUID, \'importPrice\')}'), '进口价输入框应绑定键盘导航')
    assert(!detailKeyboardHandlerSource.includes('updateStoreOrderLine') && !detailKeyboardHandlerSource.includes('batchUpdateStoreOrderLines'), '键盘移动不应自动调用保存接口')
  })
  if (keyboardNavigationFailure) failures.push(keyboardNavigationFailure)

  const amountLabelsFailure = await runTest('详情页顶部金额应显示预计销售额、发货金额 ex GST 和 GST 10%', () => {
    assert(detailSource.includes('estimatedSalesAmount'), '详情页缺少预计销售额计算')
    assert(detailSource.includes('gstAmount'), '详情页缺少 GST 10% 计算')
    assert(detailSource.includes('const totalAllocQuantity = useMemo') && detailSource.includes('draftDelta'), '顶部发货数量应按后端总数叠加页面草稿差值')
    assert(detailSource.includes('const totalAllocVolume = useMemo') && detailSource.includes('Number(item.volume) * (Number(editedAllocQuantity)'), '顶部发货体积应按页面草稿差值更新')
    assert(detailSource.includes('draftTotalImportAmount') && detailSource.includes('Number(allocQuantity) * Number(importPrice) - Number(savedAmount)'), '发货金额 ex GST 应按页面草稿金额差值更新')
    assert(detailSource.includes('detail?.totalAllocatedImportAmount') && detailSource.includes('line.allocatedImportAmount'), '发货金额 ex GST 应优先使用发货/发票金额字段')
    assert(detailSource.includes('line.price') && detailSource.includes('line.allocQuantity'), '预计销售额应按贴牌价和当前发货数计算')
    assert(detailSource.includes("label={t('storeOrders.orderAmountLabel')}") && detailSource.includes('formatAmount(estimatedSalesAmount)'), '订单金额位置应改为显示预计销售额')
    assert(detailSource.includes("label={t('storeOrders.importAmountLabel')}") && detailSource.includes('formatAmount(draftTotalImportAmount)'), '发货金额 ex GST 应显示草稿总金额')
    assert(detailSource.includes("label={t('storeOrders.gstAmountLabel')}") && detailSource.includes('formatAmount(gstAmount)'), '详情页应新增 GST 10% 显示')
    assert(detailMainTableSource.includes('Number(edited.allocQuantity ?? record.allocQuantity ?? 0) * Number(edited.importPrice ?? record.importPrice ?? 0)'), '明细进口金额应按当前草稿发货数和进口价显示')
    assert(detailMainTableSource.includes("sortOrder: detailColumnSortOrder('allocatedImportAmount')"), '明细发货金额列应按 allocatedImportAmount 发起服务端排序')
    assert(detailMainTableSource.includes('editedAllocQuantity !== undefined') && detailMainTableSource.includes('Number(record.volume) * Number(editedAllocQuantity)'), '明细发货体积应按当前草稿发货数显示')
  })
  if (amountLabelsFailure) failures.push(amountLabelsFailure)

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
