import { readFileSync } from 'node:fs'
import path from 'node:path'
import {
  createWarehouseProductHqSyncJob,
  getWarehouseProductHqSyncJob,
  syncWarehouseProductsFromHq,
} from '../../../services/warehouseProductService'
import type { CurrentUser } from '../../../types/auth'
import { buildAccess } from '../../../utils/access'

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
      "key: 'nameEn'",
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
        australianSupplierSection.includes('record.localSupplierName || record.localSupplierCode'),
      '国内/澳洲供应商列都应优先显示各自名称，名称缺失时回退各自代码',
    )
  })
  if (supplierColumnDisplayFailure) failures.push(supplierColumnDisplayFailure)

  const compactTableFailure = await runTest('仓库商品主表应使用紧凑行高、媒体尺寸和列宽', () => {
    assert(
      pageSource.includes('const WAREHOUSE_TABLE_ROW_MAX_HEIGHT = 60'),
      '商品管理主表行高应压缩到紧凑值 60px',
    )
    assert(
      pageSource.includes('.warehouse-products-table .ant-table-thead > tr > th,') &&
        pageSource.includes('padding: 4px 6px !important') &&
        pageSource.includes('.warehouse-products-table .ant-table-column-title') &&
        pageSource.includes('-webkit-line-clamp: 2'),
      '商品管理主表应使用紧凑单元格 padding，且表头标题允许两行截断',
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
        columnsSection.includes("key: 'isActive'") &&
        columnsSection.includes('width: 92') &&
        columnsSection.includes("key: 'productType'") &&
        columnsSection.includes("key: 'domesticPrice'") &&
        columnsSection.includes('width: 82') &&
        columnsSection.includes("key: 'packingQty'") &&
        columnsSection.includes('width: 96') &&
        columnsSection.includes("key: 'minOrderQuantity'") &&
        columnsSection.includes("dataIndex: 'minOrderQuantity'") &&
        columnsSection.includes('width: 84'),
      '图片、状态、商品类型、价格、装箱数和中包数等关键列应使用压缩列宽，且中包数仍绑定 minOrderQuantity',
    )
    assert(
      columnsSection.includes('BarcodePreview value={value} textMaxWidth={150} compactCopy') &&
        pageSource.includes('scroll={{ x: 2130, y: 620 }}'),
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

  if (failures.length > 0) {
    throw new Error(`共有 ${failures.length} 个测试失败\n- ${failures.join('\n- ')}`)
  }

  console.log('WarehouseProducts.hqSync.logic.test: ok')
}

await main()
