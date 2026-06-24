import { readFileSync } from 'node:fs'
import path from 'node:path'
import {
  createWarehouseProductHqSyncJob,
  getWarehouseProductHqSyncJob,
  syncWarehouseProductsFromHq,
} from '../../../services/warehouseProductService'
import type { CurrentUser } from '../../../types/auth'
import { buildAccess } from '../../../utils/access'
import {
  ALL_PRODUCTS_FILTER_KEY,
  UNCATEGORIZED_PRODUCTS_FILTER_KEY,
} from '../Categories/categoryProductFilters'
import {
  buildCategoryQueryValue,
  buildComparableFilterTokens,
  buildRangeFilterTokens,
  buildTextFilterTokens,
  getSingleFilterValue,
  normalizeTableFilters,
  parseComparableFilterTokens,
  parseTextFilterTokens,
  resolveCategoryFilterValueFromTableFilters,
  setFilterValues,
} from './columnFilters'

function createCurrentUser(overrides: Partial<CurrentUser> = {}): CurrentUser {
  return {
    userGUID: 'test-user-guid',
    username: 'tester',
    email: 'tester@example.com',
    permissions: [],
    roleNames: [],
    storeNames: [],
    ...overrides,
  }
}

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  const actualText = JSON.stringify(actual)
  const expectedText = JSON.stringify(expected)
  if (actualText !== expectedText) {
    throw new Error(`${message}。Expected: ${expectedText}, received: ${actualText}`)
  }
}

async function assertRejects(execute: () => Promise<unknown>, expectedMessage: string, label: string) {
  try {
    await execute()
  } catch (error) {
    const actualMessage = error instanceof Error ? error.message : String(error)
    assertEqual(actualMessage, expectedMessage, label)
    return
  }

  throw new Error(`${label}。Expected promise to reject`)
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

function extractSection(source: string, startText: string, endText: string) {
  const startIndex = source.indexOf(startText)
  assert(startIndex >= 0, `未找到代码片段：${startText}`)

  const endIndex = source.indexOf(endText, startIndex)
  assert(endIndex >= 0, `未找到结束片段：${endText}`)

  return source.slice(startIndex, endIndex)
}

const pageFile = path.resolve(process.cwd(), 'src/pages/Warehouse/Products/index.tsx')
const pageSource = readFileSync(pageFile, 'utf8')
const columnFiltersFile = path.resolve(process.cwd(), 'src/pages/Warehouse/Products/columnFilters.ts')
const columnFiltersSource = readFileSync(columnFiltersFile, 'utf8')
const categoryTreePickerFile = path.resolve(process.cwd(), 'src/pages/Warehouse/Products/CategoryTreePicker.tsx')
const categoryTreePickerSource = readFileSync(categoryTreePickerFile, 'utf8')

async function main() {
  const failures: string[] = []

  const adminAccessFailure = await runTest('Admin 权限判断成立', () => {
    const access = buildAccess(
      createCurrentUser({
        roleNames: ['Admin'],
      }),
    )

    assertEqual(access.isAdmin, true, 'Admin 应被识别为管理员')
  })
  if (adminAccessFailure) failures.push(adminAccessFailure)

  const nonAdminAccessFailure = await runTest('非 Admin 权限不会显示同步按钮', () => {
    const access = buildAccess(
      createCurrentUser({
        roleNames: ['WarehouseStaff'],
      }),
    )

    assertEqual(access.isAdmin, false, 'WarehouseStaff 不应被识别为管理员')
  })
  if (nonAdminAccessFailure) failures.push(nonAdminAccessFailure)

  const shelfStatusTextFailure = await runTest('仓库商品状态文案应使用上架和下架', () => {
    assert(
      pageSource.includes("function getShelfStatusLabel(isActive: boolean") &&
        pageSource.includes("t('warehouse.onShelf', '上架')") &&
        pageSource.includes("t('warehouse.offShelf', '下架')"),
      '页面应通过 getShelfStatusLabel 统一仓库商品上下架文案',
    )

    const formModalSection = extractSection(
      pageSource,
      'function ProductFormModal',
      'function SetItemsModal',
    )
    assert(
      formModalSection.includes("label={t('warehouse.isListed')}") &&
        formModalSection.includes('checkedChildren={getShelfStatusLabel(true, t)}') &&
        formModalSection.includes('unCheckedChildren={getShelfStatusLabel(false, t)}'),
      '编辑弹窗状态字段应显示是否上架和上架/下架 Switch 文案',
    )

    const columnsSection = extractSection(
      pageSource,
      'const baseColumns = useMemo',
      'return (<>',
    )
    assert(
      columnsSection.includes('checkedChildren={getShelfStatusLabel(true, t)}') &&
        columnsSection.includes('unCheckedChildren={getShelfStatusLabel(false, t)}') &&
        !columnsSection.includes("t('warehouse.active')") &&
        !columnsSection.includes("t('warehouse.inactive')"),
      '主表状态列应显示上架/下架，不能继续使用启用/停用文案',
    )

    const batchSection = extractSection(
      pageSource,
      'const handleBatchToggleActive = async',
      'const handleToggleSingleActive',
    )
    const singleSection = extractSection(
      pageSource,
      'const handleToggleSingleActive = async',
      'const handleOpenSetItems',
    )
    assert(
      batchSection.includes('status: getShelfStatusLabel(nextIsActive, t)') &&
        singleSection.includes('status: getShelfStatusLabel(nextIsActive, t)'),
      '批量和单条状态成功提示应统一使用上架/下架文案',
    )
  })
  if (shelfStatusTextFailure) failures.push(shelfStatusTextFailure)

  const productTypeAndActionFailure = await runTest('仓库商品类型列和操作入口应区分普通套装多码', () => {
    assert(
      pageSource.includes('function getProductTypeTagColor(value: ProductType)') &&
        pageSource.includes('if (value === ProductType.SET) return') &&
        pageSource.includes('if (value === ProductType.MULTICODE) return') &&
        pageSource.includes('function canManageProductDetails(productType: ProductType)'),
      '页面应声明商品类型颜色和可管理类型判断',
    )

    const columnsSection = extractSection(
      pageSource,
      'const baseColumns = useMemo',
      'return (<>',
    )
    assert(
      columnsSection.includes("title: t('column.productType')") &&
        columnsSection.includes('dataIndex: \'productType\'') &&
        columnsSection.includes('<Tag color={getProductTypeTagColor(value)}>{getProductTypeLabel(value, t)}</Tag>'),
      '商品类型列应以 Tag 显示普通、套装和多码',
    )
    assert(
      columnsSection.includes('canManageProductDetails(record.productType)') &&
        columnsSection.includes('getProductDetailsActionLabel(record.productType, t)') &&
        columnsSection.includes('getProductDetailsDisabledHint(t)') &&
        !columnsSection.includes('record.productType === 1 ?'),
      '操作列应允许套装和多码进入管理入口，不能再只判断 productType === 1',
    )
    assert(
      pageSource.includes("t('warehouse.multiCodeManagement', '多码管理')") &&
        pageSource.includes("t('warehouse.normalProductNoDetails', '普通商品没有套装或多码明细')"),
      '多码商品和普通商品应有明确操作文案',
    )
  })
  if (productTypeAndActionFailure) failures.push(productTypeAndActionFailure)

  const productDetailsModalFailure = await runTest('套装和多码应复用明细弹窗但按类型显示标题和提示', () => {
    const modalSection = extractSection(
      pageSource,
      'function SetItemsModal',
      'export default function WarehouseProductsPage',
    )
    assert(
      modalSection.includes('title={getProductDetailsModalTitle(product, t)}') &&
        modalSection.includes('getProductDetailsHint(product?.productType, t)') &&
        modalSection.includes("t('warehouse.addMultiCodeDetail', '新增多码')"),
      '明细弹窗应按商品类型展示套装或多码标题、提示和新增按钮',
    )
    assert(
      pageSource.includes("t('warehouse.multiCodeDetailsTitle', '多码管理 - {{name}}'") &&
        pageSource.includes("t('warehouse.multiCodeEditHint', '多码商品可维护多码条码、价格和分店同步使用的明细。')"),
      '多码明细弹窗应有独立标题和说明文案',
    )
  })
  if (productDetailsModalFailure) failures.push(productDetailsModalFailure)

  const warehouseProductSetCodesFailure = await runTest('仓库套装明细弹窗应读取并保存 product-set-codes 明细', () => {
    assert(
      pageSource.includes("from '../../../services/multiCodeSetService'") &&
        pageSource.includes('getGridData as getSetCodeGridData') &&
        pageSource.includes('batchCreateSetCodes') &&
        pageSource.includes('batchUpdateBarcodes as batchUpdateSetBarcodes') &&
        pageSource.includes('batchUpdatePrices as batchUpdateSetPrices') &&
        pageSource.includes('batchDelete as batchDeleteSetCodes'),
      '仓库商品页应使用 product-set-codes 服务维护套装/多码明细',
    )
    assert(
      !pageSource.includes('getDomesticProductSetItems') &&
        !pageSource.includes('updateDomesticProductSetItems') &&
        !pageSource.includes('DomesticProductSetItem'),
      '仓库商品页不能继续引用国内采购 set-items 服务和类型',
    )

    const modalSection = extractSection(
      pageSource,
      'function SetItemsModal',
      'export default function WarehouseProductsPage',
    )
    assert(
      modalSection.includes('items: MulticodeSetItem[]') &&
        modalSection.includes("dataIndex: 'setItemNumber'") &&
        modalSection.includes("dataIndex: 'setBarcode'") &&
        modalSection.includes("dataIndex: 'setPurchasePrice'") &&
        modalSection.includes("dataIndex: 'setRetailPrice'") &&
        modalSection.includes("dataIndex: 'isActive'"),
      '弹窗列应使用 product-set-codes 的货号、条码、进货价、零售价和状态字段',
    )

    const openSection = extractSection(
      pageSource,
      'const handleOpenSetItems = async (record: WarehouseProductListItem) => {',
      'const handleSaveSetItems = async () => {',
    )
    assert(
      openSection.includes('getSetCodeGridData({ productCode: record.productCode') &&
        openSection.includes('setSetItemsDraft(result.items ?? [])'),
      '打开仓库套装弹窗时应按当前仓库商品 productCode 读取 product-set-codes grid',
    )

    const saveSection = extractSection(
      pageSource,
      'const handleSaveSetItems = async () => {',
      'const handleExport = async () => {',
    )
    assert(
      saveSection.includes('batchCreateSetCodes({') &&
        saveSection.includes('batchUpdateSetBarcodes({') &&
        saveSection.includes('batchUpdateSetPrices({') &&
        saveSection.includes('batchDeleteSetCodes({ ids: deletedSetCodeIds })'),
      '保存仓库套装弹窗时应分别处理新增、已有更新和删除的 product-set-codes 明细',
    )
    assert(
      saveSection.includes('invalidSetCodeItem') &&
        saveSection.includes("message.error(t('warehouse.invalidSetCodeDetail'") &&
        saveSection.includes('item.setBarcode?.trim()') &&
        saveSection.includes('item.setPurchasePrice === undefined') &&
        saveSection.includes('item.setRetailPrice === undefined'),
      '保存前应校验套装明细条码、进货价和零售价，避免空新增或清空字段静默失败',
    )
    assert(
      saveSection.indexOf('batchDeleteSetCodes({ ids: deletedSetCodeIds })') >
        saveSection.indexOf('batchUpdateSetStatus({'),
      '删除已有明细必须放在新增和更新之后，避免后续接口失败时先删数据',
    )
  })
  if (warehouseProductSetCodesFailure) failures.push(warehouseProductSetCodesFailure)

  const minOrderQuantityColumnFailure = await runTest('仓库商品列表应以 MinOrderQuantity 作为中包数列来源', () => {
    const columnsSection = extractSection(
      pageSource,
      'const baseColumns = useMemo',
      'return (<>',
    )

    assert(
      columnsSection.includes("title: t('warehouse.middlePackQuantity', '中包数')") &&
        columnsSection.includes("dataIndex: 'minOrderQuantity'"),
      '主表应新增中包数列，并绑定 WarehouseProduct.MinOrderQuantity 归一后的 minOrderQuantity',
    )
    assert(
      !columnsSection.includes("dataIndex: 'middlePackQty'"),
      '主表中包数列不能绑定 middlePackQty，避免与 MiddlePackQuantity 来源混淆',
    )
  })
  if (minOrderQuantityColumnFailure) failures.push(minOrderQuantityColumnFailure)

  const batchEditFailure = await runTest('仓库商品页应支持按选中商品批量修改常用字段', () => {
    assert(
      pageSource.includes('batchUpdateWarehouseProducts'),
      '页面应引入仓库商品批量更新服务',
    )
    assert(
      pageSource.includes('interface BatchEditFormValues') &&
        pageSource.includes('minOrderQuantity?: number'),
      '页面应声明批量编辑表单，并使用 minOrderQuantity 表示中包数',
    )

    const saveSection = extractSection(
      pageSource,
      'const handleBatchEditSave = async () => {',
      'const handleToggleSingleActive',
    )
    assert(
      saveSection.includes('MinOrderQuantity: values.minOrderQuantity') &&
        saveSection.includes('PackingQuantity: values.packingQuantity') &&
        saveSection.includes('batchUpdateWarehouseProducts(items)'),
      '批量保存应把中包数提交为 MinOrderQuantity，并复用仓库商品批量更新服务',
    )
    assert(
      saveSection.includes('只传用户填写的字段') &&
        saveSection.includes('WarehouseProduct.MinOrderQuantity'),
      '批量 payload 构造处应有中文注释说明中包数字段来源和避免误覆盖',
    )

    const toolbarSection = extractSection(
      pageSource,
      '<PageContainer title={t(\'warehouse.productManagement\')}',
      '<Card>',
    )
    assert(
      toolbarSection.includes("t('warehouse.batchEdit', '批量修改')") &&
        toolbarSection.includes('onClick={openBatchEdit}'),
      '工具栏应提供批量修改按钮',
    )

    const modalSection = extractSection(
      pageSource,
      '<Modal title={t(\'warehouse.batchEditTitle\'',
      '<ImportFromDomesticModal',
    )
    assert(
      modalSection.includes('name="domesticPrice"') &&
        modalSection.includes('name="oemPrice"') &&
        modalSection.includes('name="importPrice"') &&
        modalSection.includes('name="packingQuantity"') &&
        modalSection.includes('name="minOrderQuantity"') &&
        modalSection.includes('name="unitVolume"') &&
        modalSection.includes('name="isActive"'),
      '批量修改弹窗应包含价格、装箱数、中包数、体积和上下架字段',
    )
  })
  if (batchEditFailure) failures.push(batchEditFailure)

  const draggableColumnsFailure = await runTest('仓库商品表格应支持拖拽列头并持久化列顺序', () => {
    assert(
      pageSource.includes('DndContext') &&
        pageSource.includes('SortableContext') &&
        pageSource.includes('useSortable') &&
        pageSource.includes('horizontalListSortingStrategy'),
      '商品管理表头列拖拽应复用 @dnd-kit 横向排序能力',
    )
    assert(
      pageSource.includes("const WAREHOUSE_PRODUCT_COLUMN_ORDER_STORAGE_KEY = 'hbweb_rv.warehouseProducts.columnOrder.v1'") &&
        pageSource.includes('localStorage.setItem(WAREHOUSE_PRODUCT_COLUMN_ORDER_STORAGE_KEY') &&
        pageSource.includes('mergeWarehouseProductColumnOrder('),
      '商品管理列顺序应保存到独立 localStorage key，并兼容列增删',
    )
    assert(
      pageSource.includes('components={{ header: { cell: DraggableHeaderCell } }}') &&
        pageSource.includes('<SortableContext items={columnOrder} strategy={horizontalListSortingStrategy}>') &&
        pageSource.includes('<DndContext sensors={columnDragSensors} collisionDetection={closestCenter} onDragEnd={handleColumnDragEnd}>'),
      '商品管理表格应接入可拖拽表头 cell 与横向 SortableContext',
    )
    assert(
      pageSource.includes('const draggableColumnKeys = [...WAREHOUSE_PRODUCT_DEFAULT_COLUMN_ORDER]') &&
        pageSource.includes('rowSelection={{') &&
        !pageSource.includes("columnOrder.includes('selection')"),
      '商品管理选择列仍应由 rowSelection 管理，不能进入业务列拖拽顺序',
    )
  })
  if (draggableColumnsFailure) failures.push(draggableColumnsFailure)

  const defaultColumnOrderFailure = await runTest('仓库商品表格默认列顺序应按截图并支持重置列', () => {
    const defaultOrderSection = extractSection(
      pageSource,
      'const WAREHOUSE_PRODUCT_DEFAULT_COLUMN_ORDER',
      '] as const',
    )
    const expectedOrder = [
      "'rowNumber'",
      "'itemNumber'",
      "'productImage'",
      "'domesticSupplierCode'",
      "'categoryName'",
      "'nameEn'",
      "'minOrderQuantity'",
      "'domesticPrice'",
      "'importPrice'",
      "'labelPrice'",
      "'isActive'",
      "'productType'",
      "'barcode'",
      "'name'",
      "'packingQty'",
      "'volume'",
      "'localSupplierCode'",
      "'updatedAt'",
      "'updatedBy'",
      "'action'",
    ]
    let lastIndex = -1
    for (const key of expectedOrder) {
      const nextIndex = defaultOrderSection.indexOf(key)
      assert(nextIndex > lastIndex, `默认列顺序应包含并按截图排列 ${key}`)
      lastIndex = nextIndex
    }
    assert(
      !defaultOrderSection.includes("'selection'"),
      '默认列顺序不能包含 selection，选择列仍由 rowSelection 管理',
    )

    const columnsSection = extractSection(
      pageSource,
      'const baseColumns = useMemo',
      'const draggableColumnKeys',
    )
    assert(
      columnsSection.indexOf("key: 'domesticSupplierCode'") < columnsSection.indexOf("key: 'categoryName'") &&
        columnsSection.indexOf("key: 'categoryName'") < columnsSection.indexOf("key: 'nameEn'") &&
        columnsSection.indexOf("key: 'nameEn'") < columnsSection.indexOf("key: 'minOrderQuantity'") &&
        columnsSection.indexOf("key: 'minOrderQuantity'") < columnsSection.indexOf("key: 'domesticPrice'") &&
        columnsSection.indexOf("key: 'barcode'") < columnsSection.indexOf("key: 'name'"),
      'baseColumns 应按截图默认顺序排列，避免默认顺序依赖历史代码顺序',
    )
    assert(
      pageSource.includes('const draggableColumnKeys = [...WAREHOUSE_PRODUCT_DEFAULT_COLUMN_ORDER]') &&
        pageSource.includes('mergeWarehouseProductColumnOrder(current.length ? current : savedOrder, WAREHOUSE_PRODUCT_DEFAULT_COLUMN_ORDER)'),
      '列顺序初始化应以显式默认顺序为准，并兼容 localStorage 旧缓存',
    )

    const resetSection = extractSection(
      pageSource,
      'const handleResetColumnOrder = () => {',
      'const orderedColumns = useMemo',
    )
    assert(
      resetSection.includes('localStorage.removeItem(WAREHOUSE_PRODUCT_COLUMN_ORDER_STORAGE_KEY)') &&
        resetSection.includes('setColumnOrder([...WAREHOUSE_PRODUCT_DEFAULT_COLUMN_ORDER])') &&
        resetSection.includes('选择列仍由 Ant Design rowSelection 管理'),
      '重置列逻辑应清除列顺序缓存，恢复默认业务列顺序，并保留中文注释说明选择列边界',
    )
    assert(
      pageSource.includes("t('warehouse.resetColumns', '重置列')") &&
        pageSource.includes('onClick={handleResetColumnOrder}') &&
        pageSource.includes('disabled={!isColumnOrderCustomized}'),
      '筛选工具栏应提供重置列按钮，并仅在列顺序自定义后启用',
    )
    assert(
      pageSource.includes('rowSelection={{') &&
        !pageSource.includes("WAREHOUSE_PRODUCT_DEFAULT_COLUMN_ORDER = ['selection'"),
      '重置列功能不能改变 rowSelection 管理选择列的方式',
    )
  })
  if (defaultColumnOrderFailure) failures.push(defaultColumnOrderFailure)

  const supplierColumnDisplayFailure = await runTest('仓库商品供应商列应区分国内供应商和澳洲供应商名称显示', () => {
    const columnsSection = extractSection(
      pageSource,
      'const baseColumns = useMemo',
      'const draggableColumnKeys',
    )
    const domesticSupplierSection = extractSection(
      columnsSection,
      "key: 'domesticSupplierCode'",
      "key: 'categoryName'",
    )
    const australianSupplierSection = extractSection(
      columnsSection,
      "key: 'localSupplierCode'",
      "key: 'updatedAt'",
    )

    assert(
      domesticSupplierSection.includes("title: t('warehouse.domesticSupplier', '国内供应商')") &&
        domesticSupplierSection.includes("dataIndex: 'domesticSupplierCode'") &&
        domesticSupplierSection.includes('sorter: true'),
      '国内供应商列应绑定 domesticSupplierCode，不能显示澳洲供应商字段',
    )
    assert(
      australianSupplierSection.includes("title: t('column.australianSupplier', '澳洲供应商')") &&
        australianSupplierSection.includes("dataIndex: 'localSupplierCode'") &&
        australianSupplierSection.includes('sorter: true'),
      '澳洲供应商列应绑定 localSupplierCode，不能显示国内供应商字段',
    )
    assert(
      domesticSupplierSection.includes('record.domesticSupplierName || record.domesticSupplierCode') &&
        australianSupplierSection.includes('record.localSupplierName || localSupplierNameMap[record.localSupplierCode || \'\'] || record.localSupplierCode'),
      '国内供应商列应优先显示名称；澳洲供应商列应优先显示名称，并在行数据缺名称时用活跃供应商映射兜底',
    )
  })
  if (supplierColumnDisplayFailure) failures.push(supplierColumnDisplayFailure)

  const localSupplierFallbackFailure = await runTest('仓库商品澳洲供应商列应使用活跃供应商名称兜底', () => {
    assert(
      pageSource.includes("import { getActiveLocalSuppliers as getActiveAustralianSuppliers } from '../../../services/localSupplierService'"),
      '页面应从澳洲供应商服务导入活跃供应商列表，并使用别名避免和国内供应商混淆',
    )
    assert(
      pageSource.includes('const [localSupplierNameMap, setLocalSupplierNameMap] = useState<Record<string, string>>({})'),
      '页面应维护澳洲供应商代码到名称的兜底映射',
    )
    assert(
      pageSource.includes('getActiveAustralianSuppliers()') &&
        pageSource.includes('setLocalSupplierNameMap(') &&
        pageSource.includes('map[item.localSupplierCode] = item.name'),
      '页面加载时应读取活跃澳洲供应商并建立代码到名称映射',
    )

    const columnsSection = extractSection(
      pageSource,
      'const baseColumns = useMemo',
      'const draggableColumnKeys',
    )
    const australianSupplierSection = extractSection(
      columnsSection,
      "key: 'localSupplierCode'",
      "key: 'updatedAt'",
    )

    assert(
      australianSupplierSection.includes('record.localSupplierName || localSupplierNameMap[record.localSupplierCode || \'\'] || record.localSupplierCode'),
      '澳洲供应商列应按行内名称、活跃供应商名称映射、供应商代码的顺序显示',
    )
    assert(
      pageSource.includes('表格接口只返回澳洲供应商代码时，用活跃供应商列表补齐名称'),
      '兜底逻辑应有中文注释说明原因',
    )
  })
  if (localSupplierFallbackFailure) failures.push(localSupplierFallbackFailure)

  const categoryColumnFailure = await runTest('仓库商品表格应显示分类名称并悬浮展示完整路径', () => {
    const columnsSection = extractSection(
      pageSource,
      'const baseColumns = useMemo',
      'const draggableColumnKeys',
    )
    const categoryColumn = extractSection(
      columnsSection,
      "key: 'categoryName'",
      "key: 'minOrderQuantity'",
    )

    assert(categoryColumn.includes("title: t('column.category')"), '分类列应使用 column.category 文案')
    assert(categoryColumn.includes("dataIndex: 'categoryName'"), '分类列应绑定 categoryName')
    assert(categoryColumn.includes('renderWarehouseProductCategoryCell(record, categoryLookup, i18n.language)'), '分类列应使用分类展示 helper')
    assert(pageSource.includes('function renderWarehouseProductCategoryCell'), '页面应提供分类单元格 helper')
    assert(pageSource.includes('getWarehouseProductCategoryTooltip(record, categoryLookup, language)'), '分类 Tooltip 应优先读取完整路径 helper')
    assert(pageSource.includes('const categoryLookup = useMemo(() => buildWarehouseCategoryLookup(categories), [categories])'), '页面应基于分类树建立 GUID 和分类名到完整路径的映射')
    assert(pageSource.includes('buildWarehouseCategoryLookup') && pageSource.includes('WarehouseCategoryLookup'), '页面应复用可测试的分类路径 lookup helper')
    assert(categoryTreePickerSource.includes('formatWarehouseCategoryNodeName(node, language)'), '分类树节点应按当前语言显示名称')
    assert(pageSource.includes('buildFilterCategoryOptions(categories, t, i18n.language)'), '分类筛选下拉应使用当前语言显示分类名')
    assert(pageSource.includes("import CategoryTreePicker from './CategoryTreePicker'"), '批量分类弹窗应复用带查询的分类树组件')
    assert(pageSource.includes('setCategoryExpandedKeys(collectCategoryExpandedKeys(categories, 1));'), '批量分类弹窗每次打开应默认展开到一级分类')
    assert(pageSource.includes('<CategoryTreePicker categories={categories}') && pageSource.includes('maxHeight={420}'), '批量分类树应使用当前语言查询组件构建')
    assert(pageSource.includes('selectedTargetCategoryPath || formatWarehouseCategoryNodeName(selectedTargetCategory, i18n.language)'), '批量分类目标提示应显示当前语言完整路径')
    assert(pageSource.includes('<Tooltip title={tooltipTitle}'), '分类名称应通过 Tooltip 展示完整路径')
    assert(pageSource.includes('className="warehouse-products-category-cell"'), '分类名称应挂载紧凑样式 class')
    assert(pageSource.includes('record.categoryName ||') && pageSource.includes("'--'"), '分类列缺失名称时应显示 --')
    const batchCategorySaveSection = extractSection(
      pageSource,
      'const handleBatchCategorySave = async () => {',
      'const handleBatchEditSave = async () => {',
    )
    assert(
      batchCategorySaveSection.includes('await batchAssignProducts(targetCategoryGuid, selectedProductCodes)') &&
        batchCategorySaveSection.includes('setData((items) =>') &&
        batchCategorySaveSection.includes('selectedProductCodeSet.has(item.productCode)') &&
        batchCategorySaveSection.includes('warehouseCategoryGUID: targetCategoryGuid') &&
        batchCategorySaveSection.includes('categoryName: selectedTargetCategory') &&
        batchCategorySaveSection.includes('formatWarehouseCategoryNodeName(selectedTargetCategory, i18n.language)') &&
        !batchCategorySaveSection.includes('void loadData({ page })'),
      '仓库商品批量分类保存成功后应本地更新当前页分类，不应重新查询商品表格',
    )
    assert(
      categoryTreePickerSource.includes('function filterCategoryTree') &&
        categoryTreePickerSource.includes('buildSearchText(node, language, parentPath).includes(keyword)') &&
        categoryTreePickerSource.includes('children: childResult.nodes') &&
        categoryTreePickerSource.includes('const visibleExpandedKeys = keyword ? searchResult.expandedKeys : expandedKeys') &&
        categoryTreePickerSource.includes("placeholder={t('warehouse.categories.searchPlaceholder'"),
      '共享分类树组件应支持查询分类名和父级路径，并在搜索时自动展开命中路径',
    )
  })
  if (categoryColumnFailure) failures.push(categoryColumnFailure)

  const compactTableFailure = await runTest('仓库商品主表应使用紧凑行高、媒体尺寸和列宽', () => {
    assert(
      pageSource.includes('const WAREHOUSE_TABLE_ROW_MAX_HEIGHT = 60'),
      '商品管理主表行高应压缩到紧凑值 60px',
    )
    assert(
      pageSource.includes('.warehouse-products-table .ant-table-thead > tr > th,') &&
        pageSource.includes('padding: 4px 6px !important') &&
        pageSource.includes('.warehouse-products-table .ant-table-column-title') &&
        pageSource.includes('-webkit-line-clamp: 2') &&
        pageSource.includes('.warehouse-products-table .ant-table-filter-column') &&
        pageSource.includes('.warehouse-products-table .ant-table-filter-trigger'),
      '商品管理主表应使用紧凑单元格 padding，且表头标题、排序和筛选图标应稳定排列',
    )
    assert(
      pageSource.includes('min-height: 48px') &&
        pageSource.includes('max-height: 48px') &&
        pageSource.includes('width: 36px') &&
        pageSource.includes('height: 36px') &&
        pageSource.includes('max-height: 42px !important'),
      '商品图片和条码预览应使用紧凑尺寸，减少行内占用空间',
    )

    const columnsSection = extractSection(
      pageSource,
      'const baseColumns = useMemo',
      'return (<>',
    )
    assert(
      columnsSection.includes("key: 'productImage'") &&
        columnsSection.includes('width: 64') &&
        columnsSection.includes('<Image src={value} alt="" width={36} height={36}') &&
        columnsSection.includes("key: 'itemNumber'") &&
        columnsSection.includes('width: 122') &&
        columnsSection.includes("key: 'isActive'") &&
        columnsSection.includes('width: 104') &&
        columnsSection.includes("key: 'productType'") &&
        columnsSection.includes("key: 'domesticPrice'") &&
        columnsSection.includes('width: 96') &&
        columnsSection.includes("key: 'packingQty'") &&
        columnsSection.includes('width: 108') &&
        columnsSection.includes("key: 'minOrderQuantity'") &&
        columnsSection.includes("dataIndex: 'minOrderQuantity'") &&
        columnsSection.includes('width: 96') &&
        columnsSection.includes("key: 'updatedAt'") &&
        columnsSection.includes('width: 164'),
      '图片、状态、商品类型、价格、装箱数、中包数和更新时间等关键列应使用筛选友好列宽，且中包数仍绑定 minOrderQuantity',
    )
    assert(
      columnsSection.includes('BarcodePreview value={value} textMaxWidth={150} compactCopy') &&
        pageSource.includes('scroll={{ x: 2260, y: 620 }}'),
      '条码列和表格横向滚动宽度应按紧凑布局更新',
    )
    assert(
      pageSource.includes('components={{ header: { cell: DraggableHeaderCell } }}') &&
        pageSource.includes('rowSelection={{') &&
        pageSource.includes('const orderedColumns = useMemo'),
      '紧凑显示不能移除拖拽列头、rowSelection 或 orderedColumns',
    )
  })
  if (compactTableFailure) failures.push(compactTableFailure)

  const mainTablePaginationFailure = await runTest('仓库商品主表默认每页 100 且仅提供大分页选项', () => {
    const tableSection = extractSection(
      pageSource,
      'pagination={{',
      '}} onChange={(pagination: TablePaginationConfig',
    )

    assert(
      pageSource.includes('const WAREHOUSE_PRODUCTS_DEFAULT_PAGE_SIZE = 100') &&
        pageSource.includes("const WAREHOUSE_PRODUCTS_PAGE_SIZE_OPTIONS = ['50', '100', '200', '500', '1000']"),
      '仓库商品主表应集中声明默认分页 100 和 50/100/200/500/1000 分页选项',
    )
    assert(
      pageSource.includes('const [pageSize, setPageSize] = useState(WAREHOUSE_PRODUCTS_DEFAULT_PAGE_SIZE);'),
      '仓库商品主表 pageSize 初始值应使用默认分页常量 100',
    )
    assert(
      tableSection.includes('pageSizeOptions: WAREHOUSE_PRODUCTS_PAGE_SIZE_OPTIONS') &&
        tableSection.includes('showSizeChanger: true,'),
      '仓库商品主表分页下拉应只使用 50/100/200/500/1000 这些选项，并保留切换入口',
    )
    assert(
      pageSource.includes('virtual') &&
        pageSource.includes('scroll={{ x: 2260, y: 620 }}') &&
        pageSource.includes('const result = await getWarehouseProductsTable(query);'),
      '分页调整应保留现有虚拟表格、固定滚动高度和异步服务端分页请求',
    )
  })
  if (mainTablePaginationFailure) failures.push(mainTablePaginationFailure)

  const adminOnlyButtonFailure = await runTest('页面应仅对 Admin 渲染从 HQ 同步按钮', () => {
    assert(
      pageSource.includes('CloudSyncOutlined'),
      '页面应引入 CloudSyncOutlined 图标',
    )

    assert(
      pageSource.includes('access.isAdmin') &&
      pageSource.includes("t('warehouse.hqSync', '从HQ同步库存')"),
      '页面应基于 access.isAdmin 控制“从HQ同步库存”按钮可见性',
    )
  })
  if (adminOnlyButtonFailure) failures.push(adminOnlyButtonFailure)

  const modalConfirmFailure = await runTest('点击同步按钮前应弹出明确提示按商品编码新增更新的确认框', () => {
    const syncSection = extractSection(
      pageSource,
      'const handleSyncWarehouseProductsFromHq = () => {',
      'const baseColumns = useMemo',
    )

    assert(
      syncSection.includes('Modal.confirm({') &&
      syncSection.includes("t('warehouse.hqSyncTitle', '从HQ同步库存')") &&
      syncSection.includes('按商品编码匹配') &&
      syncSection.includes('不会删除本地缺失商品'),
      '同步前应弹出明确提示“按商品编码匹配新增/更新且不删除本地缺失商品”的确认框',
    )
  })
  if (modalConfirmFailure) failures.push(modalConfirmFailure)

  const loadingFailure = await runTest('同步按钮应在后台任务提交中或运行中展示 loading，提交请求中 disabled', () => {
    assert(
      pageSource.includes('loading={syncingFromHq || Boolean(activeHqSyncJob)}') &&
      pageSource.includes('disabled={syncingFromHq}'),
      '同步按钮应绑定提交中和后台运行中状态，并允许运行中点击查看状态',
    )
  })
  if (loadingFailure) failures.push(loadingFailure)

  const jobApiFailure = await runTest('页面应提交后台 job 并轮询查询 job 状态', () => {
    const syncSection = extractSection(
      pageSource,
      'const handleSyncWarehouseProductsFromHq = () => {',
      'const baseColumns = useMemo',
    )

    assert(
      pageSource.includes('createWarehouseProductHqSyncJob') &&
      pageSource.includes('getWarehouseProductHqSyncJob') &&
      pageSource.includes('createWarehouseProductHqSyncJobPoller'),
      '页面应使用后台 job 创建接口、查询接口和轮询器',
    )

    assert(
      syncSection.includes('createWarehouseProductHqSyncJob') &&
      !syncSection.includes('syncWarehouseProductsFromHq()'),
      '按钮确认后不应再直接等待旧同步接口完成',
    )
  })
  if (jobApiFailure) failures.push(jobApiFailure)

  const notificationFailure = await runTest('同步提交和完成结果应通过右上角 notification 返回', () => {
    const syncSection = extractSection(
      pageSource,
      'const handleSyncWarehouseProductsFromHq = () => {',
      'const baseColumns = useMemo',
    )

    assert(
      pageSource.includes('notification') &&
      pageSource.includes('notification.info') &&
      pageSource.includes('notification.success') &&
      pageSource.includes('notification.error') &&
      pageSource.includes('notification.warning'),
      '页面应使用 notification 展示提交、成功、失败和超时信息',
    )

    assert(
      syncSection.includes("t('warehouse.hqSyncJobSubmitted") &&
      syncSection.includes('startHqSyncJobPolling'),
      '提交成功后应提示后台执行并启动轮询',
    )
  })
  if (notificationFailure) failures.push(notificationFailure)

  const successRefreshFailure = await runTest('后台同步成功后右上角提示结果并刷新第一页', () => {
    const descriptionSection = extractSection(
      pageSource,
      'const buildHqSyncResultDescription',
      'const showHqSyncJobResult',
    )
    const resultSection = extractSection(
      pageSource,
      'const showHqSyncJobResult',
      'const startHqSyncJobPolling',
    )

    assert(
      resultSection.includes('notification.success') &&
      descriptionSection.includes('addedCount') &&
      descriptionSection.includes('updatedCount') &&
      descriptionSection.includes('errorCount') &&
      resultSection.includes('void loadDataRef.current?.({ page: 1 })'),
      '后台同步成功应通过 notification 展示新增/更新/错误统计并刷新第一页',
    )
  })
  if (successRefreshFailure) failures.push(successRefreshFailure)

  const failureNoRefreshFailure = await runTest('后台同步失败时只提示失败且不刷新第一页', () => {
    const resultSection = extractSection(
      pageSource,
      'const showHqSyncJobResult',
      'const startHqSyncJobPolling',
    )

    assert(
      resultSection.includes('notification.error'),
      '后台同步失败时应使用 notification.error',
    )

    assert(
      !extractSection(resultSection, 'if (!success) {', 'const errorCount').includes("loadDataRef.current?.({ page: 1 })"),
      '后台同步失败分支不应刷新第一页',
    )
  })
  if (failureNoRefreshFailure) failures.push(failureNoRefreshFailure)

  const serviceUrlFailure = await runTest('同步服务应使用正确的 URL、POST 方法，并在后端返回失败时抛出 message', async () => {
    const originalFetch = globalThis.fetch
    let capturedUrl = ''
    let capturedInit: RequestInit | undefined

    try {
      globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
        capturedUrl = String(input)
        capturedInit = init

        return new Response(JSON.stringify({
          success: true,
          data: {
            isSuccess: true,
            message: '同步完成',
          },
        }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        })
      }) as typeof fetch

      await syncWarehouseProductsFromHq()

      assertEqual(capturedUrl, '/api/react/v1/product-warehouse/sync-from-hq', '同步服务应命中既定接口地址')
      assertEqual(capturedInit?.method, 'POST', '同步服务应使用 POST 方法')

      globalThis.fetch = (async () => new Response(JSON.stringify({
        success: false,
        message: '后端返回同步失败',
      }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })) as typeof fetch

      await assertRejects(
        () => syncWarehouseProductsFromHq(),
        '后端返回同步失败',
        '后端 success=false 时应抛出后端 message',
      )

      globalThis.fetch = (async () => new Response(JSON.stringify({
        success: true,
        message: '外层成功但同步失败',
        data: {
          isSuccess: false,
          message: '内层同步事务失败',
        },
      }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })) as typeof fetch

      await assertRejects(
        () => syncWarehouseProductsFromHq(),
        '内层同步事务失败',
        '外层 success=true 但 data.isSuccess=false 时应抛出内层 message',
      )
    } finally {
      globalThis.fetch = originalFetch
    }
  })
  if (serviceUrlFailure) failures.push(serviceUrlFailure)

  const jobServiceFailure = await runTest('后台 job 服务应使用创建和查询 URL', async () => {
    const originalFetch = globalThis.fetch
    const capturedUrls: string[] = []
    const capturedMethods: Array<string | undefined> = []

    try {
      globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
        capturedUrls.push(String(input))
        capturedMethods.push(init?.method)

        return new Response(JSON.stringify({
          success: true,
          data: {
            jobId: 'warehouse-job-1',
            status: 'Running',
            createdAt: '2026-06-04T00:00:00Z',
          },
        }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        })
      }) as typeof fetch

      await createWarehouseProductHqSyncJob({ operationId: 'warehouse-products-hq-sync' })
      await getWarehouseProductHqSyncJob('warehouse-job-1')

      assertEqual(
        capturedUrls[0],
        '/api/react/v1/product-warehouse/sync-from-hq/jobs',
        '创建后台 job 应命中新接口地址',
      )
      assertEqual(capturedMethods[0], 'POST', '创建后台 job 应使用 POST 方法')
      assertEqual(
        capturedUrls[1],
        '/api/react/v1/product-warehouse/sync-from-hq/jobs/warehouse-job-1',
        '查询后台 job 应命中 job 查询接口地址',
      )
      assertEqual(capturedMethods[1], 'GET', '查询后台 job 应使用 GET 方法')
    } finally {
      globalThis.fetch = originalFetch
    }
  })
  if (jobServiceFailure) failures.push(jobServiceFailure)

  const columnFilterHelperFailure = await runTest('仓库商品列头筛选 helper 应保持运行时语义', () => {
    assertDeepEqual(
      setFilterValues({ domesticSupplierCode: ['CN-001'] }, 'domesticSupplierCode', ['  ', undefined]),
      {},
      '空值应移除对应列头筛选',
    )
    assertDeepEqual(
      buildRangeFilterTokens(' 5 ', 10),
      ['gte:5', 'lte:10'],
      '数字范围应生成后端识别的 gte/lte token',
    )
    assertDeepEqual(buildTextFilterTokens('contains', 'Clock'), ['__filter:contains:Clock'], '文本包含应生成命名空间 contains token')
    assertDeepEqual(buildTextFilterTokens('eq', 'HB001'), ['__filter:eq:HB001'], '文本等于应生成命名空间 eq token')
    assertDeepEqual(buildTextFilterTokens('starts', 'HB'), ['__filter:starts:HB'], '文本开头是应生成命名空间 starts token')
    assertDeepEqual(buildTextFilterTokens('ends', '001'), ['__filter:ends:001'], '文本结尾是应生成命名空间 ends token')
    assertDeepEqual(parseTextFilterTokens(['Clock']), { mode: 'contains', value: 'Clock' }, '旧文本裸值应兼容为 contains')
    assertDeepEqual(parseTextFilterTokens(['starts:HB']), { mode: 'contains', value: 'starts:HB' }, '旧文本保留前缀字面值')
    assertDeepEqual(parseTextFilterTokens(['__filter:starts:HB']), { mode: 'starts', value: 'HB' }, '文本 token 应能还原模式和值')
    assertDeepEqual(buildComparableFilterTokens('eq', { value: 12 }), ['__filter:eq:12'], '数字等于应生成命名空间 eq token')
    assertDeepEqual(buildComparableFilterTokens('range', { min: 5, max: 10 }), ['gte:5', 'lte:10'], '数字范围应生成 gte/lte token')
    assertDeepEqual(buildComparableFilterTokens('gte', { value: 8 }), ['gte:8'], '数字大于等于应生成 gte token')
    assertDeepEqual(buildComparableFilterTokens('lte', { value: 9 }), ['lte:9'], '数字小于等于应生成 lte token')
    assertDeepEqual(parseComparableFilterTokens(['18']), { mode: 'eq', value: '18', min: '', max: '' }, '旧数字裸值应兼容为 eq')
    assertDeepEqual(parseComparableFilterTokens(['__filter:eq:18']), { mode: 'eq', value: '18', min: '', max: '' }, '数字 eq token 应能还原模式和值')
    assertDeepEqual(parseComparableFilterTokens(['gte:2026-06-01', 'lte:2026-06-16']), {
      mode: 'range',
      min: '2026-06-01',
      max: '2026-06-16',
      value: '',
    }, '日期范围 token 应能还原为 range 模式')
    assertDeepEqual(
      normalizeTableFilters({
        name: [' Clock '],
        labelPrice: ['gte:2', 'lte:9'],
        categoryName: [UNCATEGORIZED_PRODUCTS_FILTER_KEY],
        domesticSupplierCode: ['CN-001'],
      }),
      {
        productName: ['Clock'],
        oemPrice: ['gte:2', 'lte:9'],
        domesticSupplierCode: ['CN-001'],
      },
      '普通列头筛选应映射后端 key，分类不应混入普通 Filters',
    )
    assertEqual(
      resolveCategoryFilterValueFromTableFilters({ categoryName: [UNCATEGORIZED_PRODUCTS_FILTER_KEY] }),
      UNCATEGORIZED_PRODUCTS_FILTER_KEY,
      '分类列头值应单独解析',
    )
    assertDeepEqual(
      buildCategoryQueryValue(UNCATEGORIZED_PRODUCTS_FILTER_KEY),
      { categoryGuid: undefined, uncategorizedOnly: true },
      '未分类列头应转成顶层 UncategorizedOnly',
    )
    assertDeepEqual(
      buildCategoryQueryValue('cat-runtime-001'),
      { categoryGuid: 'cat-runtime-001', uncategorizedOnly: false },
      '具体分类列头应转成顶层 CategoryGuids 查询值',
    )
    assertDeepEqual(
      buildCategoryQueryValue(ALL_PRODUCTS_FILTER_KEY),
      { categoryGuid: undefined, uncategorizedOnly: false },
      '全部商品列头应清空分类顶层字段',
    )
    assertEqual(getSingleFilterValue(['true']), 'true', '单选筛选应能同步回顶部筛选')
    assertEqual(getSingleFilterValue(['true', 'false']), undefined, '多选筛选不应强行同步为顶部单值')
  })
  if (columnFilterHelperFailure) failures.push(columnFilterHelperFailure)

  const columnFilterStateFailure = await runTest('仓库商品页应维护列头后端筛选状态并区分分类顶层字段', () => {
    assert(
      pageSource.includes('const [columnFilters, setColumnFilters] = useState<WarehouseProductColumnFilters>({})') &&
        pageSource.includes('const mergedFilters = overrides.filters ?? columnFilters;') &&
        pageSource.includes("filters: Object.keys(mergedFilters).length ? mergedFilters : undefined") &&
        pageSource.includes('列头筛选走后端 Filters，分类仍走顶层字段'),
      '页面应维护 columnFilters 状态，并在 buildGridQuery 中把普通列头筛选发到后端 Filters',
    )
    assert(
      pageSource.includes("setColumnFilters((current) => setFilterValues(current, 'domesticSupplierCode'") &&
        pageSource.includes("setColumnFilters((current) => setFilterValues(current, 'productType'") &&
        pageSource.includes("setColumnFilters((current) => setFilterValues(current, 'isActive'"),
      '顶部供应商、商品类型和状态筛选变化时应同步 columnFilters，避免旧列头值残留',
    )
  })
  if (columnFilterStateFailure) failures.push(columnFilterStateFailure)

  const topCategoryTreeFilterFailure = await runTest('仓库商品页顶部分类筛选应使用可折叠分类树', () => {
    const topFilterSection = extractSection(
      pageSource,
      '<Input value={searchText}',
      '<Select value={productType}',
    )

    assert(
      pageSource.includes('TreeSelect') &&
        pageSource.includes('buildFilterCategoryTreeOptions') &&
        pageSource.includes('const [categoryFilterExpandedKeys, setCategoryFilterExpandedKeys] = useState<string[]>([])') &&
        pageSource.includes("const [categoryFilterSearchText, setCategoryFilterSearchText] = useState('')") &&
        pageSource.includes('const hasCategoryFilterSearchText = categoryFilterSearchText.trim().length > 0;') &&
        pageSource.includes('const categoryFilterTreeOptions = useMemo(() => buildFilterCategoryTreeOptions(categories, t, i18n.language)'),
      '顶部分类筛选应引入 TreeSelect，并使用树形分类 options、搜索状态与独立展开状态',
    )
    assert(
      pageSource.includes('const firstLevelExpandedKeys = collectCategoryExpandedKeys(tree, 1);') &&
        pageSource.includes('setCategoryExpandedKeys(firstLevelExpandedKeys);') &&
        pageSource.includes('setCategoryFilterExpandedKeys(firstLevelExpandedKeys);'),
      '加载分类树后应同时初始化批量分类树和顶部筛选树的一级展开状态',
    )
    assert(
      topFilterSection.includes('<TreeSelect') &&
        topFilterSection.includes('treeData={categoryFilterTreeOptions}') &&
        topFilterSection.includes('searchValue={categoryFilterSearchText}') &&
        topFilterSection.includes('onSearch={setCategoryFilterSearchText}') &&
        topFilterSection.includes('treeExpandedKeys={hasCategoryFilterSearchText ? undefined : categoryFilterExpandedKeys}') &&
        topFilterSection.includes('if (!hasCategoryFilterSearchText)') &&
        topFilterSection.includes('treeNodeFilterProp="searchText"'),
      '顶部分类控件应绑定 treeData、搜索字段，并在搜索时让 TreeSelect 自动展开命中路径',
    )
    assert(
      topFilterSection.includes('allowClear') &&
        topFilterSection.includes('setCategoryFilterValue(value || ALL_PRODUCTS_FILTER_KEY);') &&
        topFilterSection.includes("setCategoryFilterSearchText('');") &&
        pageSource.includes('setCategoryFilterValue(UNCATEGORIZED_PRODUCTS_FILTER_KEY);') &&
        pageSource.includes('uncategorizedOnly: true,'),
      '顶部分类树清空后应回到全部商品并清空搜索词，未分类快捷按钮仍应查询 UncategorizedOnly',
    )
  })
  if (topCategoryTreeFilterFailure) failures.push(topCategoryTreeFilterFailure)

  const tableChangeColumnFilterFailure = await runTest('表格 onChange 应读取列头 filters 并重查第一页', () => {
    const tableSection = extractSection(
      pageSource,
      'onChange={(pagination: TablePaginationConfig, filters: Record<string, FilterValue | null>, sorter:',
      '}/>',
    )

    assert(
      tableSection.includes('const nextColumnFilters = normalizeTableFilters(filters);') &&
        tableSection.includes('const nextCategoryFilterValue = resolveCategoryFilterValueFromTableFilters(filters);') &&
        tableSection.includes('setColumnFilters(nextColumnFilters);'),
      '表格 onChange 应接收 AntD filters，并转换后回写 columnFilters',
    )
    assert(
      tableSection.includes("page: extra.action === 'paginate' ? pagination.current || 1 : 1,") &&
        tableSection.includes('filters: nextColumnFilters,') &&
        tableSection.includes('...categoryQuery,'),
      '列头筛选或排序变化后应带 filters 重查数据，并在非分页场景回到第一页',
    )
    assert(
      tableSection.includes('const categoryQuery = buildCategoryQueryValue(nextCategoryFilterValue);') &&
        tableSection.includes('setCategoryFilterValue(nextCategoryFilterValue);'),
      '分类列头变化时应转成顶层分类查询字段，而不是混入普通 Filters',
    )
  })
  if (tableChangeColumnFilterFailure) failures.push(tableChangeColumnFilterFailure)

  const columnFilterUiFailure = await runTest('仓库商品表格应为文本数字日期枚举列接入列头过滤 UI', () => {
    const columnsSection = extractSection(
      pageSource,
      'const baseColumns = useMemo',
      'const draggableColumnKeys',
    )

    assert(
      pageSource.includes('const renderColumnFilterPanel = (content: ReactNode, onApply: () => void, onReset: () => void) =>') &&
        pageSource.includes('统一列头筛选面板骨架') &&
        pageSource.includes('warehouse-products-column-filter-panel') &&
        pageSource.includes('warehouse-products-column-filter-body') &&
        pageSource.includes('warehouse-products-column-filter-actions') &&
        !pageSource.includes('style={{ width: 112 }}'),
      '文本、数字和日期列头筛选应复用统一面板，不能回退到窄 Select 下拉',
    )
    assert(
      pageSource.includes('const buildTextFilterDropdown = (filterKey: string, placeholder: string) =>') &&
        pageSource.includes('const buildNumberRangeFilterDropdown = (filterKey: string) =>') &&
        pageSource.includes('const buildDateRangeFilterDropdown = (filterKey: string) =>') &&
        pageSource.includes('textFilterModeOptions') &&
        pageSource.includes('comparableFilterModeOptions'),
      '页面应提供文本、数字和日期列头筛选 helper，并显示匹配方式选择',
    )
    assert(
      columnFiltersSource.includes("const filterKeyMap: Record<string, string> = {") &&
        columnFiltersSource.includes("name: 'productName'") &&
        columnFiltersSource.includes("labelPrice: 'oemPrice'"),
      'normalizeTableFilters 应显式维护列 key 到后端 filter key 的映射',
    )
    assert(
      columnsSection.includes("...textFilterProps('itemNumber'") &&
        columnsSection.includes("...textFilterProps('productName'") &&
        columnsSection.includes("...textFilterProps('nameEn'") &&
        columnsSection.includes("...textFilterProps('barcode'"),
      '货号、商品名、英文名和条码列应接入文本列头筛选',
    )
    assert(
      columnsSection.includes("...numberRangeFilterProps('minOrderQuantity')") &&
        columnsSection.includes("...numberRangeFilterProps('domesticPrice')") &&
        columnsSection.includes("...numberRangeFilterProps('importPrice')") &&
        columnsSection.includes("...numberRangeFilterProps('oemPrice')") &&
        columnsSection.includes("...numberRangeFilterProps('packingQty')") &&
        columnsSection.includes("...numberRangeFilterProps('volume')") &&
        columnsSection.includes("...dateRangeFilterProps('updatedAt')"),
      '中包数、价格、装箱数、体积和更新时间列应接入数字/日期列头筛选',
    )
    assert(
      columnsSection.includes("...enumFilterProps('domesticSupplierCode'") &&
        columnsSection.includes("...enumFilterProps('localSupplierCode'") &&
        columnsSection.includes("...enumFilterProps('isActive'") &&
        columnsSection.includes("...enumFilterProps('productType'") &&
        columnsSection.includes('filters: categoryColumnFilterOptions') &&
        columnsSection.includes('filteredValue: categoryFilterValue === ALL_PRODUCTS_FILTER_KEY ? null : [categoryFilterValue]'),
      '供应商、状态、商品类型和分类列应暴露 filters / filteredValue 形式的列头过滤 UI',
    )
    assert(
      columnsSection.includes("key: 'name'") &&
        columnsSection.includes("dataIndex: 'name'") &&
        columnsSection.includes("...textFilterProps('productName'") &&
        columnsSection.includes("key: 'labelPrice'") &&
        columnsSection.includes("dataIndex: 'labelPrice'") &&
        columnsSection.includes("...numberRangeFilterProps('oemPrice')"),
      '商品名和 OEM 列应保留原列 key，同时继续使用后端 productName / oemPrice filter key',
    )
  })
  if (columnFilterUiFailure) failures.push(columnFilterUiFailure)

  const resetColumnFilterFailure = await runTest('重置查询应清空列头筛选状态', () => {
    const resetSection = extractSection(
      pageSource,
      "<Button icon={<ReloadOutlined />} onClick={() => {",
      "{t('common.reset')}",
    )

    assert(
      resetSection.includes('setColumnFilters({});') &&
        resetSection.includes('filters: {},'),
      '点击重置时应清空 columnFilters，并按空 Filters 重查列表',
    )
  })
  if (resetColumnFilterFailure) failures.push(resetColumnFilterFailure)

  if (failures.length > 0) {
    throw new Error(`共有 ${failures.length} 个测试失败\n- ${failures.join('\n- ')}`)
  }

  console.log('WarehouseProducts.hqSync.logic.test: ok')
}

await main()
