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

function readColumnBlock(source: string, marker: string) {
  const markerPosition = source.indexOf(marker)
  if (markerPosition < 0) return ''
  const blockStart = source.lastIndexOf('      {', markerPosition)
  const nextBlockStart = source.indexOf('      {', markerPosition + marker.length)
  return source.slice(blockStart, nextBlockStart > 0 ? nextBlockStart : source.length)
}

function readFunctionBlock(source: string, startMarker: string, endMarker: string) {
  const blockStart = source.indexOf(startMarker)
  if (blockStart < 0) return ''
  const blockEnd = source.indexOf(endMarker, blockStart + startMarker.length)
  return source.slice(blockStart, blockEnd > 0 ? blockEnd : source.length)
}

const pageFile = path.resolve(process.cwd(), 'src/pages/Warehouse/ProductGradeManagement/index.tsx')
const serviceFile = path.resolve(process.cwd(), 'src/services/productGradeService.ts')
const typeFile = path.resolve(process.cwd(), 'src/types/productGrade.ts')
const packageFile = path.resolve(process.cwd(), 'package.json')

function readSource(file: string) {
  // 统一换行，避免 Windows CRLF 让源码契约断言误判。
  return readFileSync(file, 'utf8').replace(/\r\n/g, '\n')
}

const pageSource = readSource(pageFile)
const serviceSource = readSource(serviceFile)
const typeSource = readSource(typeFile)
const packageSource = readSource(packageFile)

async function main() {
  const failures: string[] = []

  const requestSignalFailure = await runTest('商品等级列表请求应支持取消且 signal 不进入 query string', () => {
    assert(typeSource.includes('signal?: AbortSignal'), 'ProductGradeListParams 缺少 signal')
    assert(serviceSource.includes('signal: params.signal'), 'getProductGradeList 未把 signal 传给 request.get')
    const paramsBlock = serviceSource.slice(
      serviceSource.indexOf('params: {'),
      serviceSource.indexOf('signal: params.signal'),
    )
    assert(!paramsBlock.includes('signal'), 'signal 不能放进 params 查询字符串')
  })
  if (requestSignalFailure) failures.push(requestSignalFailure)

  const listAbortFailure = await runTest('页面列表请求应取消旧请求并忽略旧响应', () => {
    assert(pageSource.includes('const listAbortRef = useRef<AbortController | null>(null)'), '页面缺少列表 AbortController ref')
    assert(pageSource.includes('const listRequestSeqRef = useRef(0)'), '页面缺少请求序号 ref')
    assert(pageSource.includes('listAbortRef.current?.abort()'), '新请求前应取消旧列表请求')
    assert(pageSource.includes('requestSeq !== listRequestSeqRef.current'), '旧响应不能覆盖新数据')
    assert(pageSource.includes('controller.signal.aborted'), 'Abort 请求不应弹失败提示')
  })
  if (listAbortFailure) failures.push(listAbortFailure)

  const supplierLazyFailure = await runTest('供应商选项应首次展开时异步加载并可取消', () => {
    assert(!pageSource.includes('void loadSuppliers()\n    void loadList(1, pageSize)'), '首屏不应同步加载供应商选项')
    assert(pageSource.includes('const [suppliersLoaded, setSuppliersLoaded] = useState(false)'), '缺少供应商已加载状态')
    assert(pageSource.includes('supplierAbortRef.current?.abort()'), '供应商请求缺少取消逻辑')
    assert(pageSource.includes('onDropdownVisibleChange={(open) =>'), '供应商下拉缺少展开触发加载')
    assert(pageSource.includes('if (open) void loadSuppliers()'), '供应商下拉展开时应加载选项')
  })
  if (supplierLazyFailure) failures.push(supplierLazyFailure)

  const tableChangeFailure = await runTest('表格排序过滤应统一走 Table.onChange 服务端查询', () => {
    assert(pageSource.includes('const handleTableChange = ('), '页面缺少统一表格 onChange handler')
    assert(pageSource.includes("extra: { action: 'paginate' | 'sort' | 'filter' }"), 'handler 应识别分页/排序/过滤动作')
    assert(pageSource.includes("const nextPage = extra.action === 'paginate' ? pagination.current ?? 1 : 1"), '排序/过滤变化应回到第一页')
    assert(pageSource.includes('onChange={handleTableChange}'), 'Table 未绑定统一 onChange')
    assert(!pageSource.includes('pagination={{\n            current: page') || !pageSource.includes('onChange: (nextPage, nextPageSize)'), '分页不应保留独立 onChange')
  })
  if (tableChangeFailure) failures.push(tableChangeFailure)

  const sortableColumnsFailure = await runTest('商品等级列头应启用受控服务端排序', () => {
    const markers = [
      "dataIndex: 'supplierName'",
      "dataIndex: 'supplierCode'",
      "dataIndex: 'hbProductNo'",
      "dataIndex: 'grade'",
      "dataIndex: 'domesticPrice'",
      "dataIndex: 'importPrice'",
      "dataIndex: 'oemPrice'",
    ]

    markers.forEach((marker) => {
      const block = readColumnBlock(pageSource, marker)
      assert(block.includes('sorter: true'), `${marker} 缺少 sorter: true`)
      assert(block.includes('sortOrder:'), `${marker} 缺少受控 sortOrder`)
    })
  })
  if (sortableColumnsFailure) failures.push(sortableColumnsFailure)

  const filterColumnsFailure = await runTest('商品等级列头应启用服务端过滤入口', () => {
    assert(readColumnBlock(pageSource, "dataIndex: 'supplierCode'").includes('filterDropdown: renderSupplierFilterDropdown'), '供应商代码列缺少供应商过滤')
    assert(readColumnBlock(pageSource, "dataIndex: 'hbProductNo'").includes('filterDropdown: renderTextFilterDropdown'), '货号列缺少文本过滤')
    assert(readColumnBlock(pageSource, "dataIndex: 'grade'").includes('filters: Object.keys(PRODUCT_GRADE_CONFIG)'), '等级列缺少 A/B/C/D 过滤')
    const warehouseMarker = pageSource.indexOf("dataIndex: 'warehouseIsActive'")
    const warehouseStatusColumn = pageSource.slice(warehouseMarker, pageSource.indexOf("dataIndex: 'domesticPrice'", warehouseMarker))
    assert(warehouseStatusColumn.includes('sorter: true'), '仓库上下架列缺少服务端排序')
    assert(warehouseStatusColumn.includes("sortField === 'warehouseIsActive'"), '仓库上下架列缺少受控排序状态')
    assert(warehouseStatusColumn.includes("value: 'true'") && warehouseStatusColumn.includes("value: 'false'"), '仓库上下架列缺少 true/false 过滤项')
    assert(warehouseStatusColumn.includes('filteredValue: columnFilters.warehouseIsActive === undefined'), '仓库上下架列缺少受控过滤状态')
    assert(readColumnBlock(pageSource, "dataIndex: 'domesticPrice'").includes('filterDropdown: renderPriceFilterDropdown'), '国内价列缺少区间过滤')
    assert(readColumnBlock(pageSource, "dataIndex: 'importPrice'").includes('filterDropdown: renderPriceFilterDropdown'), '进口价列缺少区间过滤')
    assert(readColumnBlock(pageSource, "dataIndex: 'oemPrice'").includes('filterDropdown: renderPriceFilterDropdown'), '零售价列缺少区间过滤')
    assert(pageSource.includes('getFiltersFromTable(filters)'), '表格过滤参数未统一转换为接口参数')
    assert(pageSource.includes('warehouseIsActive: getBooleanFilterValue(filters.warehouseIsActive)'), '表格过滤未把 warehouseIsActive 转成 boolean')
  })
  if (filterColumnsFailure) failures.push(filterColumnsFailure)

  const priceRangeClearFailure = await runTest('价格区间过滤应允许只清空单侧边界', () => {
    assert(pageSource.includes('const selectedRangeValue = selectedKeys[0]'), '价格筛选应显式读取当前 selectedKey')
    assert(pageSource.includes("const hasSelectedRange = typeof selectedRangeValue === 'string'"), '价格筛选应区分无 selectedKey 和空边界 selectedKey')
    assert(pageSource.includes('const min = hasSelectedRange ? range.min : minValue'), '清空最小值后不能回填旧最小值')
    assert(pageSource.includes('const max = hasSelectedRange ? range.max : maxValue'), '清空最大值后不能回填旧最大值')
    assert(pageSource.includes("return `${min ?? ''}|${max ?? ''}`"), '价格区间编码应保留单侧空边界')
  })
  if (priceRangeClearFailure) failures.push(priceRangeClearFailure)

  const lazyImageFailure = await runTest('商品图片应懒加载并异步解码', () => {
    const imageColumn = readColumnBlock(pageSource, "dataIndex: 'productImage'")
    assert(imageColumn.includes('loading="lazy"'), '图片列缺少 loading="lazy"')
    assert(imageColumn.includes('decoding="async"'), '图片列缺少 decoding="async"')
    assert(imageColumn.includes('width={48}') && imageColumn.includes('height={48}'), '图片列应保持稳定尺寸')
    assert(imageColumn.includes('alt={record.productName || record.hbProductNo || record.productCode}'), '图片列缺少可读 alt')
  })
  if (lazyImageFailure) failures.push(lazyImageFailure)

  const productNameColumnFailure = await runTest('商品等级表格应在货号和图片之间显示商品名称列', () => {
    const productNameColumn = readColumnBlock(pageSource, "dataIndex: 'productName'")
    const itemNumberIndex = pageSource.indexOf("dataIndex: 'hbProductNo'")
    const productNameIndex = pageSource.indexOf("dataIndex: 'productName'")
    const imageIndex = pageSource.indexOf("dataIndex: 'productImage'")
    assert(productNameColumn.includes("title: t('column.productName')"), '商品名称列标题应复用 column.productName')
    assert(productNameColumn.includes('width: 220'), '商品名称列宽度应稳定')
    assert(productNameColumn.includes('ellipsis: true'), '商品名称列应启用 ellipsis 防止撑宽表格')
    assert(productNameColumn.includes('<Tooltip title={value || undefined}>'), '商品名称列长文本应有 Tooltip')
    assert(productNameColumn.includes('{value || \'--\'}'), '商品名称列空值应显示 --')
    assert(itemNumberIndex > 0 && productNameIndex > itemNumberIndex && imageIndex > productNameIndex, '商品名称列应位于货号和图片之间')
  })
  if (productNameColumnFailure) failures.push(productNameColumnFailure)

  const serviceParamsFailure = await runTest('商品等级 service 应传递列头过滤和排序参数', () => {
    ;[
      'hbProductNo',
      'categoryGuid',
      'uncategorizedOnly',
      'domesticPriceMin',
      'domesticPriceMax',
      'importPriceMin',
      'importPriceMax',
      'oemPriceMin',
      'oemPriceMax',
      'warehouseIsActive',
      'sortField',
      'sortDirection',
    ].forEach((field) => {
      assert(serviceSource.includes(`${field}: params.${field}`), `service 缺少 ${field} 参数透传`)
    })
  })
  if (serviceParamsFailure) failures.push(serviceParamsFailure)

  const categoryDataFailure = await runTest('商品等级数据层应包含仓库分类字段和过滤参数', () => {
    ;[
      'categoryGuid?: string',
      'categoryName?: string',
      'categoryChineseName?: string',
      'uncategorizedOnly?: boolean',
    ].forEach((field) => {
      assert(typeSource.includes(field), `类型定义缺少 ${field}`)
    })
    assert(serviceSource.includes('const categoryGuid = raw.categoryGuid'), 'service 未转换 categoryGuid 响应字段')
    assert(serviceSource.includes('const categoryName = raw.categoryName'), 'service 未转换 categoryName 响应字段')
    assert(serviceSource.includes('const categoryChineseName = raw.categoryChineseName'), 'service 未转换 categoryChineseName 响应字段')
    assert(serviceSource.includes('categoryGuid: params.categoryGuid || undefined'), 'service 未透传 categoryGuid 查询参数')
    assert(serviceSource.includes('uncategorizedOnly: params.uncategorizedOnly'), 'service 未透传 uncategorizedOnly 查询参数')
  })
  if (categoryDataFailure) failures.push(categoryDataFailure)

  const categoryFilterFailure = await runTest('分类列应懒加载分类树并转换列头过滤参数', () => {
    const categoryColumn = readColumnBlock(pageSource, "dataIndex: 'categoryGuid'")
    assert(pageSource.includes('getCategoryTree'), '页面未引入分类树接口')
    assert(pageSource.includes('buildFilterCategoryOptions'), '页面未复用分类过滤选项构建函数')
    assert(pageSource.includes('UNCATEGORIZED_PRODUCTS_FILTER_KEY'), '页面缺少未分类过滤常量')
    assert(pageSource.includes('formatWarehouseCategoryNodeName'), '分类列显示未复用分类名称本地化格式化')
    assert(pageSource.includes('const [categoriesLoaded, setCategoriesLoaded] = useState(false)'), '分类树缺少懒加载状态')
    assert(pageSource.includes('const loadCategories = useCallback(async () =>'), '分类树缺少异步加载函数')
    assert(pageSource.includes('message.error(t(\'productGrade.loadCategoriesFailed\'))'), '分类树加载失败缺少提示')
    assert(pageSource.includes('categoryGuid: activeFilters.categoryGuid'), '列表请求未带 categoryGuid')
    assert(pageSource.includes('uncategorizedOnly: activeFilters.uncategorizedOnly'), '列表请求未带 uncategorizedOnly')
    assert(categoryColumn.includes("title: t('productGrade.category')"), '表格缺少分类列标题')
    assert(categoryColumn.includes('filterDropdown: renderCategoryFilterDropdown'), '分类列缺少自定义过滤下拉')
    assert(categoryColumn.includes('filteredValue: columnFilters.uncategorizedOnly'), '分类列缺少受控 filteredValue')
    assert(pageSource.includes('formatProductGradeCategory(record)'), '分类列未显示本地化分类名称')
    assert(pageSource.includes('const categoryFilterValue = getSingleFilterValue(filters.categoryGuid)'), '表格过滤未读取分类列值')
    assert(pageSource.includes('categoryGuid: categoryFilterValue'), '表格过滤未转换 categoryGuid')
    assert(pageSource.includes('uncategorizedOnly: categoryFilterValue === UNCATEGORIZED_PRODUCTS_FILTER_KEY ? true : undefined'), '表格过滤未转换未分类参数')
  })
  if (categoryFilterFailure) failures.push(categoryFilterFailure)

  const categoryEditFailure = await runTest('分类单元格应可点击打开弹窗并直接修改目标分类', () => {
    const categoryColumn = readColumnBlock(pageSource, "dataIndex: 'categoryGuid'")
    const saveCategoryBlock = readFunctionBlock(
      pageSource,
      'const handleCategoryEditSave = async () =>',
      'const getFiltersFromTable =',
    )
    assert(pageSource.includes('batchAssignProducts'), '页面未复用 batchAssignProducts 修改商品分类')
    assert(pageSource.includes("import CategoryTreePicker from '../Products/CategoryTreePicker'"), '分类编辑弹窗未复用 CategoryTreePicker')
    assert(pageSource.includes('const [categoryEditOpen, setCategoryEditOpen] = useState(false)'), '缺少分类编辑弹窗状态')
    assert(pageSource.includes('const [categoryEditRecord, setCategoryEditRecord] = useState<ProductGradeListItem | null>(null)'), '缺少当前编辑商品状态')
    assert(pageSource.includes('const [targetCategoryGuid, setTargetCategoryGuid] = useState<string | undefined>(undefined)'), '缺少目标分类状态')
    assert(pageSource.includes('const [categorySaving, setCategorySaving] = useState(false)'), '缺少分类保存 loading 状态')
    assert(pageSource.includes('const openCategoryEditModal = useCallback(async (record: ProductGradeListItem) =>'), '缺少分类单元格打开弹窗逻辑')
    assert(pageSource.includes('setTargetCategoryGuid(record.categoryGuid)'), '打开分类弹窗应预选当前分类')
    assert(pageSource.includes('setCategoryExpandedKeys(collectCategoryExpandedKeys(tree, 1))'), '打开分类弹窗应默认展开一级分类')
    assert(categoryColumn.includes('<Button') && categoryColumn.includes('void openCategoryEditModal(record)'), '分类列应渲染可点击入口')
    assert(pageSource.includes("title={t('productGrade.editCategoryTitle')}"), '分类编辑 Modal 缺少标题')
    assert(pageSource.includes('<CategoryTreePicker'), '分类编辑 Modal 缺少分类树选择器')
    assert(pageSource.includes("disabled:\n            categoryLoading\n            || !targetCategoryGuid\n            || targetCategoryGuid === categoryEditRecord?.categoryGuid"), '目标分类未变时保存按钮应禁用')
    assert(saveCategoryBlock.includes('const affected = await batchAssignProducts(targetCategoryGuid, [categoryEditRecord.productCode])'), '保存分类应读取 batchAssignProducts 实际影响行数')
    assert(saveCategoryBlock.includes('if (affected < 1)'), '保存分类应拦截没有实际更新商品的假成功')
    assert(saveCategoryBlock.includes("throw new Error(t('productGrade.categoryUpdateFailed'))"), '没有实际更新商品时不能继续本地更新当前行')
    assert(saveCategoryBlock.includes('setData((items) => items.map((item) => ('), '保存成功应局部更新当前行')
    assert(saveCategoryBlock.includes('setData((items) => items.filter((item) => item.productCode !== categoryEditRecord.productCode))'), '筛选冲突时应移除当前页行')
    assert(saveCategoryBlock.includes('setTotal((current) => Math.max(0, current - 1))'), '筛选冲突时应同步修正 total')
    assert(!saveCategoryBlock.includes('void loadList'), '分类保存成功不应重载整页列表')
  })
  if (categoryEditFailure) failures.push(categoryEditFailure)

  const batchCategoryFailure = await runTest('已选商品工具条应支持批量修改分类', () => {
    const batchCategoryBlock = readFunctionBlock(
      pageSource,
      'const handleBatchCategorySave = async () =>',
      'const handleCategoryEditSave = async () =>',
    )
    assert(pageSource.includes('const [batchCategoryOpen, setBatchCategoryOpen] = useState(false)'), '缺少批量分类弹窗状态')
    assert(pageSource.includes('const openBatchCategoryModal = useCallback(async () =>'), '缺少批量分类打开逻辑')
    assert(pageSource.includes("title={t('productGrade.batchCategoryTitle')}"), '批量分类 Modal 缺少标题')
    assert(pageSource.includes("onClick={() => void openBatchCategoryModal()}"), '已选工具条缺少批量分类按钮')
    assert(batchCategoryBlock.includes('const selectedProductCodes = selectedRowKeys.map(String)'), '批量分类应按选中的商品编码提交')
    assert(batchCategoryBlock.includes('const affected = await batchAssignProducts(targetCategoryGuid, selectedProductCodes)'), '批量分类应复用 batchAssignProducts')
    assert(batchCategoryBlock.includes('if (affected < selectedProductCodes.length)'), '批量分类应校验实际影响商品数')
    assert(batchCategoryBlock.includes('setData((items) => items.map((item) => ('), '批量分类成功后应局部更新当前页行')
    assert(batchCategoryBlock.includes('setSelectedRowKeys([])'), '批量分类成功后应清空已选商品')
  })
  if (batchCategoryFailure) failures.push(batchCategoryFailure)

  const selectionFailure = await runTest('跨页选择应保留选中商品编码', () => {
    assert(pageSource.includes('rowKey="productCode"'), '表格 rowKey 应使用 productCode 支持跨页回查')
    assert(pageSource.includes('preserveSelectedRowKeys: true'), '跨页选择缺少 preserveSelectedRowKeys')
    assert(pageSource.includes('selectedRowKeys.map(String)'), '提交/导出前应把选中 key 归一为商品编码')
  })
  if (selectionFailure) failures.push(selectionFailure)

  const exportRegressionFailure = await runTest('商品等级 Excel 导出链路应保留完整字段回查和选中顺序', () => {
    assert(pageSource.includes('getGradesByProductCodes(selectedProductCodes)'), '导出前应按商品编码回查完整字段')
    assert(pageSource.includes('const rowOrder = new Map(selectedProductCodes.map((code, index) => [code, index]))'), '导出应记录选中顺序')
    assert(pageSource.includes('.sort((a, b) => (rowOrder.get(a.productCode) ?? 0) - (rowOrder.get(b.productCode) ?? 0))'), '导出应按选中顺序排序')
    assert(pageSource.includes('includeProductImage: exportIncludeImage'), '导出应传递是否包含图片')
  })
  if (exportRegressionFailure) failures.push(exportRegressionFailure)

  const addToOrderFailure = await runTest('商品等级加入订单应按 by-codes 完整回查后提交', () => {
    assert(pageSource.includes("const [addOrderMode, setAddOrderMode] = useState<AddToOrderMode>('existing')"), '加入订单默认应使用已有订单模式')
    assert(!pageSource.includes('void loadEditableOrders(\'\')'), '打开加入订单弹窗不应预加载完整订单下拉')
    assert(pageSource.includes('const latestRows = await getGradesByProductCodes(selectedProductCodes)'), '加入订单前应按商品编码回查最新商品字段')
    assert(pageSource.includes('const missingProductCodes = selectedProductCodes.filter((productCode) => !latestByCode.has(productCode))'), 'by-codes 部分缺失时应识别缺失商品')
    const missingCheckIndex = pageSource.indexOf('if (missingProductCodes.length > 0)')
    const createOrderIndex = pageSource.indexOf('orderGUID = await createStoreOrder')
    const addLinesIndex = pageSource.indexOf('await batchAddStoreOrderLines')
    assert(missingCheckIndex > 0 && createOrderIndex > missingCheckIndex && addLinesIndex > missingCheckIndex, 'by-codes 缺失检查必须早于创建订单/提交行')
    assert(pageSource.includes('return\n      }'), 'by-codes 缺失时应提前返回，不能继续提交或创建订单')
    assert(pageSource.includes('let orderGUID = targetOrderGuid!'), '已有订单模式应直接使用目标订单 GUID')
    assert(pageSource.includes("if (addOrderMode === 'new')"), '新建订单模式缺少分支')
    assert(pageSource.includes('orderGUID = await createStoreOrder'), '新建订单后应使用新订单 GUID')
    assert(pageSource.includes('await batchAddStoreOrderLines({ orderGUID, items })'), '创建/选择订单后应批量加入订单行')
  })
  if (addToOrderFailure) failures.push(addToOrderFailure)

  const orderSearchFailure = await runTest('订单搜索应带 keyword 和可编辑状态集合', () => {
    assert(pageSource.includes('keyword: keyword.trim() || undefined'), '订单搜索缺少 keyword 参数')
    assert(pageSource.includes('statusList: EDITABLE_STORE_ORDER_STATUSES'), '订单搜索缺少可编辑状态集合')
    assert(pageSource.includes('EDITABLE_STORE_ORDER_STATUSES.includes(item.flowStatus)'), '订单结果应再次过滤可编辑状态')
    assert(pageSource.includes('onSearch={(value) =>'), '已有订单选择器缺少远程搜索入口')
    assert(pageSource.includes('void loadEditableOrders({ keyword: value, pageNumber: 1 })'), '订单远程搜索应带输入 keyword 并重置第一页')
  })
  if (orderSearchFailure) failures.push(orderSearchFailure)

  const orderDropdownLazyFailure = await runTest('已有订单下拉应按创建时间倒序懒加载第一页并滚动追加', () => {
    const loadEditableOrdersBlock = readFunctionBlock(
      pageSource,
      'const loadEditableOrders = useCallback',
      'const loadStores = useCallback',
    )
    assert(pageSource.includes('const ORDER_DROPDOWN_PAGE_SIZE = 20'), '已有订单下拉缺少 20 条分页常量')
    assert(loadEditableOrdersBlock.includes('pageSize: ORDER_DROPDOWN_PAGE_SIZE'), '已有订单下拉请求未使用 20 条分页')
    assert(loadEditableOrdersBlock.includes("sortBy: 'createdAt'"), '已有订单下拉请求缺少 createdAt 排序字段')
    assert(loadEditableOrdersBlock.includes('sortDescending: true'), '已有订单下拉请求缺少创建时间倒序')
    assert(loadEditableOrdersBlock.includes('const requestSeq = orderRequestSeqRef.current + 1'), '已有订单下拉缺少独立请求序号')
    assert(loadEditableOrdersBlock.includes('orderRequestSeqRef.current = requestSeq'), '已有订单下拉新请求未更新请求序号')
    assert(loadEditableOrdersBlock.includes('if (requestSeq !== orderRequestSeqRef.current) return'), '已有订单下拉旧响应不能写入选项')
    assert(loadEditableOrdersBlock.includes('if (requestSeq === orderRequestSeqRef.current)'), '已有订单下拉旧请求不能关闭新请求 loading')
    assert(loadEditableOrdersBlock.includes('setOrderOptionsLoaded(false)'), '搜索/重置时应清除订单下拉已加载状态')
    assert(pageSource.includes('onDropdownVisibleChange={(open) =>'), '已有订单下拉缺少首次展开加载入口')
    assert(pageSource.includes('if (open && !orderOptionsLoaded)'), '已有订单下拉不应每次展开重复加载第一页')
    assert(pageSource.includes('onPopupScroll={(event) =>'), '已有订单下拉缺少滚动加载入口')
    assert(pageSource.includes('append: true'), '已有订单下拉滚动触底应追加下一页')
    assert(pageSource.includes('const byGuid = new Map(current.map((item) => [item.orderGUID, item]))'), '追加订单选项时应按 orderGUID 去重')
  })
  if (orderDropdownLazyFailure) failures.push(orderDropdownLazyFailure)

  const scriptFailure = await runTest('package 应提供商品等级性能专项测试脚本', () => {
    assert(packageSource.includes('"test:product-grade-performance"'), 'package.json 缺少 test:product-grade-performance')
  })
  if (scriptFailure) failures.push(scriptFailure)

  if (failures.length > 0) {
    throw new Error(`商品等级性能测试失败:\n${failures.join('\n')}`)
  }

  console.log('productGradePerformance.logic.test.ts: ok')
}

main().catch((error) => {
  console.error(error)
  process.exitCode = 1
})
