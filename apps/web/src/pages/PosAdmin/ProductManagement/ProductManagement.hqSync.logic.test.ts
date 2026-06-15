import { readFileSync } from 'node:fs'
import path from 'node:path'
import type { CurrentUser } from '../../../types/auth'
import type { ProductStoreRecordDto } from '../../../types/posProduct'
import { buildAccess } from '../../../utils/access'
import {
  compareProductStoreRecordsByActive,
  compareProductStoreRecordsByAutoPricing,
  compareProductStoreRecordsByDiscountRate,
  compareProductStoreRecordsByName,
  compareProductStoreRecordsByPurchasePrice,
  compareProductStoreRecordsByRetailPrice,
  compareProductStoreRecordsBySpecialProduct,
  compareProductStoreRecordsByStoreCode,
  compareProductStoreRecordsByStoreProductCode,
  compareProductStoreRecordsByUpdatedAt,
  compareProductStoreRecordsByUpdatedBy,
} from './storeRecordSorting'
import {
  buildProductIntegrityFixSummary,
  buildProductIntegritySummary,
} from './productIntegrityReport'

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

const pageFile = path.resolve(process.cwd(), 'src/pages/PosAdmin/ProductManagement/index.tsx')
const typeFile = path.resolve(process.cwd(), 'src/types/posProduct.ts')
const productIntegrityTypeFile = path.resolve(process.cwd(), 'src/types/productIntegrity.ts')
const serviceFile = path.resolve(process.cwd(), 'src/services/posProductService.ts')
const productIntegrityHelperFile = path.resolve(process.cwd(), 'src/pages/PosAdmin/ProductManagement/productIntegrityReport.ts')
const pageSource = readFileSync(pageFile, 'utf8')
const typeSource = readFileSync(typeFile, 'utf8')
const productIntegrityTypeSource = readFileSync(productIntegrityTypeFile, 'utf8')
const serviceSource = readFileSync(serviceFile, 'utf8')
const productIntegrityHelperSource = readFileSync(productIntegrityHelperFile, 'utf8')

function assertSourceOrder(source: string, first: string, second: string, message: string) {
  const firstIndex = source.indexOf(first)
  const secondIndex = source.indexOf(second)

  assert(firstIndex >= 0, `${message}：缺少 ${first}`)
  assert(secondIndex >= 0, `${message}：缺少 ${second}`)
  assert(firstIndex < secondIndex, message)
}

async function main() {
  const failures: string[] = []

  const adminAccessFailure = await runTest('Admin 权限判断成立', () => {
    const access = buildAccess(createCurrentUser({ roleNames: ['Admin'] }))
    assertEqual(access.isAdmin, true, 'Admin 应被识别为管理员')
  })
  if (adminAccessFailure) failures.push(adminAccessFailure)

  const warehouseAccessFailure = await runTest('WarehouseStaff 权限判断成立', () => {
    const access = buildAccess(createCurrentUser({ roleNames: ['WarehouseStaff'] }))
    assertEqual(access.isAdmin, false, 'WarehouseStaff 不应被识别为管理员')
    assertEqual(access.isWarehouseStaff, true, 'WarehouseStaff 应被识别为仓库员工')
  })
  if (warehouseAccessFailure) failures.push(warehouseAccessFailure)

  const productIntegritySummaryFailure = await runTest('商品一致性检查应把后端分组报告转换成页面问题行', () => {
    const summary = buildProductIntegritySummary({
      checkTime: '2026-06-15T00:00:00Z',
      durationSeconds: 1.25,
      productSetCodeReport: {
        tableName: 'ProductSetCode',
        totalChecked: 10,
        orphanedCount: 2,
        missingCount: 0,
        orphanedProductCodes: ['P001', 'P002'],
        missingProductCodes: [],
      },
      storeReports: [
        {
          storeCode: 'S1',
          storeName: 'Sunnybank',
          tableReports: [
            {
              tableName: 'StoreRetailPrice',
              totalChecked: 100,
              orphanedCount: 0,
              missingCount: 3,
              orphanedProductCodes: [],
              missingProductCodes: ['P100', 'P101'],
            },
            {
              tableName: 'StoreMultiCodeProduct',
              totalChecked: 8,
              orphanedCount: 1,
              missingCount: 4,
              orphanedProductCodes: ['P200'],
              missingProductCodes: ['P201', 'P202'],
            },
          ],
        },
      ],
    })

    assertEqual(summary.storeCount, 1, '应统计检查分店数量')
    assertEqual(summary.totalChecked, 118, '应汇总总部和分店表的检查记录数')
    assertEqual(summary.issueCount, 10, '问题数应为孤立记录和缺失记录合计')
    assertEqual(summary.issueRows.length, 4, '应按范围、表名和问题类型生成问题行')
    assert(summary.issueRows.some((row) =>
      row.scope === '总部' &&
      row.tableName === 'ProductSetCode' &&
      row.issueType === '孤立记录' &&
      row.count === 2 &&
      row.sampleProductCodes.includes('P001'),
    ), 'ProductSetCode 孤立记录应生成总部问题行')
    assert(summary.issueRows.some((row) =>
      row.scope === 'Sunnybank (S1)' &&
      row.tableName === 'StoreRetailPrice' &&
      row.issueType === '缺失记录' &&
      row.count === 3 &&
      row.sampleProductCodes.includes('P100'),
    ), 'StoreRetailPrice 缺失记录应生成分店问题行')
    assert(summary.issueRows.some((row) =>
      row.tableName === 'StoreMultiCodeProduct' &&
      row.issueType === '孤立记录' &&
      row.sampleProductCodes.includes('P200'),
    ), 'StoreMultiCodeProduct 孤立记录应保留样本编码')
    assert(summary.issueRows.some((row) =>
      row.tableName === 'StoreMultiCodeProduct' &&
      row.issueType === '缺失记录' &&
      row.sampleProductCodes.includes('P202'),
    ), 'StoreMultiCodeProduct 缺失记录应保留样本编码')
  })
  if (productIntegritySummaryFailure) failures.push(productIntegritySummaryFailure)

  const productIntegrityAllPassFailure = await runTest('商品一致性检查无问题时 issueCount 应为 0', () => {
    const summary = buildProductIntegritySummary({
      checkTime: '2026-06-15T00:00:00Z',
      durationSeconds: 0.5,
      productSetCodeReport: {
        tableName: 'ProductSetCode',
        totalChecked: 1,
        orphanedCount: 0,
        missingCount: 0,
        orphanedProductCodes: [],
        missingProductCodes: [],
      },
      storeReports: [],
    })

    assertEqual(summary.issueCount, 0, '无孤立和缺失记录时应视为检查通过')
    assertEqual(summary.issueRows.length, 0, '无问题时不应生成表格行')
  })
  if (productIntegrityAllPassFailure) failures.push(productIntegrityAllPassFailure)

  const productIntegrityFixSummaryFailure = await runTest('商品一致性修复结果应从 reports 汇总', () => {
    const summary = buildProductIntegrityFixSummary({
      fixTime: '2026-06-15T00:00:00Z',
      durationSeconds: 2,
      isDryRun: false,
      reports: [
        {
          tableName: 'StoreRetailPrice',
          deletedCount: 2,
          addedCount: 3,
          errorCount: 0,
          errors: [],
        },
        {
          tableName: 'StoreMultiCodeProduct',
          deletedCount: 1,
          addedCount: 4,
          errorCount: 1,
          errors: ['S1 修复失败'],
        },
      ],
    })

    assertEqual(summary.deletedCount, 3, '修复结果应汇总删除数量')
    assertEqual(summary.addedCount, 7, '修复结果应汇总新增数量')
    assertEqual(summary.errorCount, 1, '修复结果应汇总错误数量')
    assert(summary.errors.includes('S1 修复失败'), '修复结果应保留错误明细')
  })
  if (productIntegrityFixSummaryFailure) failures.push(productIntegrityFixSummaryFailure)

  const productIntegritySourceFailure = await runTest('商品一致性检查页面和类型应使用后端分组报告结构', () => {
    assert(
      productIntegrityTypeSource.includes('storeReports: StoreIntegrityReport[]') &&
        productIntegrityTypeSource.includes('productSetCodeReport?: TableIntegrityReport | null') &&
        productIntegrityTypeSource.includes('reports: TableFixReport[]'),
      '商品一致性类型应声明后端真实返回的分组报告和修复 reports',
    )
    assert(
      productIntegrityHelperSource.includes('后端返回的是按分店、按表聚合的报告') &&
        productIntegrityHelperSource.includes('issueCount') &&
        productIntegrityHelperSource.includes('sampleProductCodes'),
      '商品一致性 helper 应把后端分组报告转换成页面 summary 和问题行',
    )
    assert(
      pageSource.includes('buildProductIntegritySummary') &&
        pageSource.includes('integritySummary.issueCount') &&
        pageSource.includes('sampleProductCodes'),
      '商品管理页应通过 summary 展示检查结果',
    )
    assert(
      pageSource.includes('fixStoreRetailPrice: true') &&
        pageSource.includes('fixStoreMultiCodeProduct: true') &&
        pageSource.includes('fixProductSetCode: true') &&
        pageSource.includes('buildProductIntegrityFixSummary'),
      '商品管理页自动修复应提交后端真实字段并汇总 reports',
    )
    assert(
      !pageSource.includes('integrityResult.issues') &&
        !pageSource.includes('integrityResult.totalProducts') &&
        !pageSource.includes('integrityResult.passedCount') &&
        !pageSource.includes('result.fixedCount'),
      '商品管理页不应再读取旧版扁平字段',
    )
  })
  if (productIntegritySourceFailure) failures.push(productIntegritySourceFailure)

  const adminButtonGuardFailure = await runTest('页面应使用 Admin 权限控制 HQ 同步按钮', () => {
    assert(
      pageSource.includes("t('posAdmin.products.fullSyncFromHQ', '全量同步')") &&
        pageSource.includes("t('posAdmin.products.incrementalSyncFromHQ', '增量同步')"),
      '页面源码中应存在“全量同步”和“增量同步”两个按钮',
    )
    assert(
      pageSource.includes('useAuthStore') && pageSource.includes('isAdmin'),
      '页面应显式读取 auth store，并基于 Admin 权限决定是否渲染 HQ 同步按钮',
    )
    assert(
      pageSource.includes('ensureCanSyncProductsFromHq') &&
        pageSource.includes('if (!ensureCanSyncProductsFromHq()) return'),
      'HQ 同步打开弹窗和确认提交时都应有 Admin 权限守卫',
    )
  })
  if (adminButtonGuardFailure) failures.push(adminButtonGuardFailure)

  const writePermissionGuardFailure = await runTest('页面写操作应使用 POS 商品管理权限控制', () => {
    assert(
      pageSource.includes('canManagePosProducts'),
      '页面应读取 canManagePosProducts 控制商品编辑、分类管理、批量和同步到分店等写操作',
    )
    assert(
      pageSource.includes('ensureCanManagePosProducts') &&
        pageSource.includes("t('posAdmin.products.noManagePermission'"),
      '写操作处理函数应有统一权限保护，避免只读用户绕过按钮直接触发',
    )
  })
  if (writePermissionGuardFailure) failures.push(writePermissionGuardFailure)

  const createProductModalFailure = await runTest('页面应提供创建商品弹窗并调用 create-with-prices 接口', () => {
    assert(
      typeSource.includes('CreateProductWithPricesDto') &&
        typeSource.includes('CreateProductWithPricesResultDto') &&
        typeSource.includes('storeProductCodes: Record<string, string>'),
      '商品类型定义应声明创建商品请求和结果 DTO',
    )
    assert(
      serviceSource.includes('createProductWithPrices') &&
        serviceSource.includes("`${API_BASE}/create-with-prices`"),
      '服务层应提供 createProductWithPrices 并调用 create-with-prices 接口',
    )
    assert(
      pageSource.includes('canCreateStoreProducts') &&
        pageSource.includes("t('posAdmin.products.createProduct', '创建商品')") &&
        pageSource.includes('openCreateModal') &&
        pageSource.includes('handleCreateSave'),
      '页面应读取 canCreateStoreProducts，并提供创建商品按钮、打开弹窗和保存处理函数',
    )
    assert(
      pageSource.includes('ensureCanCreateStoreProducts') &&
        pageSource.includes("t('posAdmin.products.noCreatePermission'"),
      '创建商品入口和提交都应有单独的创建权限守卫',
    )
    assert(
      pageSource.includes('const [createVisible, setCreateVisible] = useState(false)') &&
        pageSource.includes('const [createSubmitting, setCreateSubmitting] = useState(false)') &&
        pageSource.includes('const [createForm] = Form.useForm()'),
      '页面应维护创建商品弹窗、提交状态和独立表单实例',
    )
    assert(
      pageSource.includes('await createProductWithPrices({') &&
        pageSource.includes('productType: 0') &&
        pageSource.includes('isActive: true'),
      '创建商品提交应固定普通商品，并默认启用',
    )
    assert(
      pageSource.includes('Object.keys(result.storeProductCodes ?? {}).length') &&
        pageSource.includes('result.productCode') &&
        pageSource.includes("t('posAdmin.products.createProductSuccess'"),
      '创建成功提示应展示 productCode 和已创建分店商品数量',
    )
    assert(
      pageSource.includes('setCreateVisible(false)') &&
        pageSource.includes('createForm.resetFields()') &&
        pageSource.includes('void loadData()'),
      '创建成功后应关闭弹窗、清空表单并刷新列表',
    )
    assert(
      pageSource.includes("title={t('posAdmin.products.createProduct', '创建商品')}") &&
        pageSource.includes('open={createVisible}') &&
        pageSource.includes('confirmLoading={createSubmitting}') &&
        pageSource.includes("name=\"productName\"") &&
        pageSource.includes("name=\"productImage\"") &&
        pageSource.includes("name=\"barcode\"") &&
        pageSource.includes("name=\"localSupplierCode\"") &&
        pageSource.includes("name=\"purchasePrice\"") &&
        pageSource.includes("name=\"retailPrice\""),
      '创建商品弹窗应包含基础字段并绑定提交状态',
    )
    assert(
      !pageSource.includes("t('posAdmin.products.setProduct', '套装商品')") ||
        pageSource.includes('name="productType"'),
      '创建商品弹窗范围内不应支持套装/多码切换',
    )
  })
  if (createProductModalFailure) failures.push(createProductModalFailure)

  const editSetCodesIsolationFailure = await runTest('商品编辑弹窗应隔离套装和多码明细状态', () => {
    assert(
      pageSource.includes('resetEditSetCodeState') &&
        pageSource.includes('setEditSetCodes([])') &&
        pageSource.includes('setEditSetPriceEdits({})') &&
        pageSource.includes('setEditPendingDeletes({})'),
      '页面应提供统一重置函数，清空编辑弹窗条码明细、编辑缓存和待删缓存',
    )

    const openEditStart = pageSource.indexOf('const openEdit = (record: PosProductDto) => {')
    const openEditEnd = pageSource.indexOf('const openStoreRecords', openEditStart)
    assert(openEditStart >= 0 && openEditEnd > openEditStart, '页面应保留 openEdit 编辑入口')
    const openEditSource = pageSource.slice(openEditStart, openEditEnd)
    assertSourceOrder(
      openEditSource,
      'resetEditSetCodeState()',
      'setEditingProduct(record)',
      '打开新商品编辑弹窗前应先清空上一个商品的条码明细状态',
    )

    const effectStart = pageSource.indexOf('useEffect(() => {\n    const requestSeq = editSetCodesRequestSeqRef.current + 1')
    const effectEnd = pageSource.indexOf('const handleProductTypeChange', effectStart)
    assert(effectStart >= 0 && effectEnd > effectStart, '加载编辑弹窗条码明细的 effect 应使用请求序号保护')
    const effectSource = pageSource.slice(effectStart, effectEnd)
    assert(
      pageSource.includes('const editSetCodesRequestSeqRef = useRef(0)'),
      '页面应使用 ref 保存条码明细请求序号，避免旧请求覆盖新商品',
    )
    assert(
      pageSource.includes('const editingProductCode = editingProduct?.productCode'),
      '多码/套装明细加载依赖应绑定当前编辑商品 productCode',
    )
    assert(
      effectSource.includes('getGridData({ productCode: editingProductCode, pageIndex: 1, pageSize: 200 })'),
      '条码明细加载应使用当前编辑商品 productCode',
    )
    assert(
      effectSource.includes('if (requestSeq === editSetCodesRequestSeqRef.current)') &&
        effectSource.includes('setEditSetCodes(items)') &&
        effectSource.includes('setEditSetCodesLoading(false)'),
      '条码明细请求返回和 loading 收尾都应先校验请求序号',
    )
    assert(
      pageSource.includes('}, [editVisible, productTypeWatch, editingProductCode, resetEditSetCodeState, t])'),
      '条码明细加载 effect 依赖应包含当前商品 productCode、类型和重置函数',
    )
    assert(
      effectSource.includes('resetEditSetCodeState()') &&
        effectSource.includes('setEditSetCodesLoading(false)'),
      '非套装/非多码状态应清空条码明细并退出 loading',
    )
  })
  if (editSetCodesIsolationFailure) failures.push(editSetCodesIsolationFailure)

  const editProductImageFailure = await runTest('商品编辑弹窗应显示并保留商品图片字段', () => {
    const openEditStart = pageSource.indexOf('const openEdit = (record: PosProductDto) => {')
    const openEditEnd = pageSource.indexOf('const openStoreRecords', openEditStart)
    assert(openEditStart >= 0 && openEditEnd > openEditStart, '页面应保留 openEdit 编辑入口')
    const openEditSource = pageSource.slice(openEditStart, openEditEnd)
    assert(
      openEditSource.includes('productImage: record.productImage || buildDefaultProductImageUrl(record.itemNumber || record.productCode)'),
      '打开编辑弹窗时应优先回填商品图片 URL，无图时按货号生成默认图片 URL',
    )
    assert(
      pageSource.includes("const DEFAULT_PRODUCT_IMAGE_BASE_URL = 'https://hotbargain-yw-2023-1300114625.cos.ap-shanghai.myqcloud.com/YW200'") &&
        pageSource.includes('function buildDefaultProductImageUrl') &&
        pageSource.includes("`${DEFAULT_PRODUCT_IMAGE_BASE_URL}/${encodeURIComponent(normalizedItemNumber)}.jpg`"),
      '商品图片默认 URL 应使用 YW200 COS 地址并按货号生成 jpg',
    )

    const handleEditSaveStart = pageSource.indexOf('const handleEditSave = async () => {')
    const handleEditSaveEnd = pageSource.indexOf('const handleBatchEnable', handleEditSaveStart)
    assert(handleEditSaveStart >= 0 && handleEditSaveEnd > handleEditSaveStart, '页面应保留 handleEditSave 保存入口')
    const handleEditSaveSource = pageSource.slice(handleEditSaveStart, handleEditSaveEnd)
    assert(
      handleEditSaveSource.includes("productImage: values.productImage ?? editingProduct.productImage ?? ''"),
      '商品保存 payload 应显式带回 productImage，避免添加多码后覆盖清空图片字段',
    )

    const editModalStart = pageSource.indexOf('<Modal\n        open={editVisible}')
    const editModalEnd = pageSource.indexOf('{productTypeWatch === 1 &&', editModalStart)
    assert(editModalStart >= 0 && editModalEnd > editModalStart, '页面应保留编辑商品弹窗表单')
    const editModalSource = pageSource.slice(editModalStart, editModalEnd)
    assert(
      editModalSource.includes('name="productImage"') &&
        editModalSource.includes('pos-products-edit-image-preview') &&
        editModalSource.includes('prev.productImage !== cur.productImage'),
      '编辑弹窗应提供商品图片 URL 输入和随表单值变化的图片预览',
    )
  })
  if (editProductImageFailure) failures.push(editProductImageFailure)

  const editSupplierFailure = await runTest('商品编辑弹窗供应商应允许修改并随保存提交', () => {
    const handleEditSaveStart = pageSource.indexOf('const handleEditSave = async () => {')
    const handleEditSaveEnd = pageSource.indexOf('const handleBatchEnable', handleEditSaveStart)
    assert(handleEditSaveStart >= 0 && handleEditSaveEnd > handleEditSaveStart, '页面应保留 handleEditSave 保存入口')
    const handleEditSaveSource = pageSource.slice(handleEditSaveStart, handleEditSaveEnd)
    assert(
      handleEditSaveSource.includes('localSupplierCode: values.localSupplierCode'),
      '商品保存 payload 应提交编辑表单中的供应商编码',
    )

    const editModalStart = pageSource.indexOf('<Modal\n        open={editVisible}')
    const editModalEnd = pageSource.indexOf('{productTypeWatch === 1 &&', editModalStart)
    assert(editModalStart >= 0 && editModalEnd > editModalStart, '页面应保留编辑商品弹窗表单')
    const editModalSource = pageSource.slice(editModalStart, editModalEnd)
    const supplierFieldStart = editModalSource.indexOf('name="localSupplierCode"')
    const supplierFieldEnd = editModalSource.indexOf('name="productType"', supplierFieldStart)
    assert(supplierFieldStart >= 0 && supplierFieldEnd > supplierFieldStart, '编辑弹窗应保留供应商下拉表单项')
    const supplierFieldSource = editModalSource.slice(supplierFieldStart, supplierFieldEnd)
    assert(
      supplierFieldSource.includes('options={supplierOptions}') &&
        supplierFieldSource.includes("placeholder={t('posAdmin.products.selectSupplier', '请选择供应商')}") &&
        !supplierFieldSource.includes('disabled'),
      '编辑弹窗供应商下拉应打开编辑，不能禁用',
    )
  })
  if (editSupplierFailure) failures.push(editSupplierFailure)

  const productTypeColumnFailure = await runTest('商品列表应显示商品类型并按类型显示条码记录数量', () => {
    assert(
      pageSource.includes('function normalizeProductType(productType: unknown): 0 | 1 | 2') &&
        pageSource.includes('function getProductTypeColor(productType: unknown): string') &&
        pageSource.includes('const getProductTypeLabel = useCallback((productType: unknown) => {'),
      '页面应集中定义商品类型归一、颜色和文案函数',
    )
    assert(
      pageSource.includes("if (normalizedType === 1) return t('posAdmin.products.setProduct', '套装')") &&
        pageSource.includes("if (normalizedType === 2) return t('posAdmin.products.multiCodeProductShort', '多码')") &&
        pageSource.includes("return t('posAdmin.products.normalProduct', '普通')"),
      '商品类型文案应覆盖普通、套装和多码',
    )

    const productTypeColumnStart = pageSource.indexOf("title: t('posAdmin.products.productTypeLabel', '商品类型')")
    const barcodeRecordColumnStart = pageSource.indexOf("title: t('posAdmin.products.barcodeRecordCount', '条码记录')")
    const storeRecordColumnStart = pageSource.indexOf("title: t('posAdmin.products.storeRecords', '分店记录')")
    assert(productTypeColumnStart >= 0, '表格应存在商品类型列')
    assert(barcodeRecordColumnStart > productTypeColumnStart, '条码记录列应位于商品类型列之后')
    assert(storeRecordColumnStart > barcodeRecordColumnStart, '分店记录列应位于条码记录列之后')

    const productTypeColumnSource = pageSource.slice(productTypeColumnStart, barcodeRecordColumnStart)
    assert(
      productTypeColumnSource.includes("dataIndex: 'productType'") &&
        productTypeColumnSource.includes('getProductTypeColor(v)') &&
        productTypeColumnSource.includes('getProductTypeLabel(v)'),
      '商品类型列应读取 productType 并显示类型 Tag',
    )

    const barcodeRecordColumnSource = pageSource.slice(barcodeRecordColumnStart, storeRecordColumnStart)
    assert(
      barcodeRecordColumnSource.includes("dataIndex: 'setCount'") &&
        barcodeRecordColumnSource.includes('if (!isBarcodeManagedProduct(record.productType))') &&
        barcodeRecordColumnSource.includes('return <span>0</span>') &&
        barcodeRecordColumnSource.includes('openSetCodeManager(record)'),
      '条码记录列应读取 setCount，多码/套装按 productType 判断，普通商品显示 0',
    )
    assert(
      !barcodeRecordColumnSource.includes("dataIndex: 'isSet'") &&
        !barcodeRecordColumnSource.includes('record.isSet'),
      '条码记录列不能再依赖 isSet，否则多码商品数量会漏显示',
    )
  })
  if (productTypeColumnFailure) failures.push(productTypeColumnFailure)

  const productTypeActionFailure = await runTest('商品列表操作列应允许套装和多码进入条码管理', () => {
    const actionColumnStart = pageSource.indexOf("key: 'actions'")
    const columnsEnd = pageSource.indexOf('  ]', actionColumnStart)
    assert(actionColumnStart >= 0 && columnsEnd > actionColumnStart, '页面应保留操作列')
    const actionColumnSource = pageSource.slice(actionColumnStart, columnsEnd)
    assert(
      actionColumnSource.includes('isBarcodeManagedProduct(record.productType)') &&
        actionColumnSource.includes('normalizeProductType(record.productType) === 2') &&
        actionColumnSource.includes("t('posAdmin.products.multiBarcodeManagement', '多码管理')") &&
        actionColumnSource.includes("t('posAdmin.products.setManagement', '套装管理')"),
      '操作列应按 productType 允许套装和多码进入条码管理，并按类型显示按钮文案',
    )
    assert(
      !actionColumnSource.includes('record.isSet &&'),
      '操作列不能继续只按 isSet 展示条码管理入口',
    )
  })
  if (productTypeActionFailure) failures.push(productTypeActionFailure)

  const categoryParentValueFailure = await runTest('商品分类父级 Cascader 应只提交叶子 GUID', () => {
    assert(
      pageSource.includes('resolveCascaderLeafValue') &&
        pageSource.includes('const parentGuid = resolveCascaderLeafValue(values.parentGuid)'),
      '分类父级保存前应把 Cascader 路径转换为最后一级 GUID',
    )
    assert(
      pageSource.includes('parentGuid: getCategoryValueFromGuid(node.parentGuid, categoryTree)'),
      '编辑分类时应使用完整路径回填父分类 Cascader',
    )
    assert(
      pageSource.includes('categoryParentDisabledGuids.has(parentGuid)') &&
        pageSource.includes('invalidParentCategory'),
      '保存分类时应再次校验父级不能是自身或子分类，避免绕过 UI 禁用',
    )
  })
  if (categoryParentValueFailure) failures.push(categoryParentValueFailure)

  const syncFieldsRequestFailure = await runTest('同步到分店应改为后台 job 提交并轮询结果', () => {
    assert(
      typeSource.includes('SyncProductsToStoresField') &&
        typeSource.includes('fields: SyncProductsToStoresField[]'),
      'SyncProductsToStoresRequest 应声明同步字段列表',
    )
    assert(
      typeSource.includes('SyncProductsToStoresJobResult') &&
        typeSource.includes('jobId: string') &&
        typeSource.includes('status: SyncProductsToStoresJobStatus') &&
        typeSource.includes('operationId?: string') &&
        typeSource.includes('isDuplicateRequest?: boolean'),
      '类型层应声明同步到分店后台 job 结果、状态和重复提交标记',
    )
    assert(
      serviceSource.includes('startSyncProductsToStoresJob') &&
        serviceSource.includes("`${API_BASE}/sync-to-stores/jobs`") &&
        serviceSource.includes('getSyncProductsToStoresJob') &&
        serviceSource.includes("`${API_BASE}/sync-to-stores/jobs/${encodeURIComponent(jobId)}`"),
      '服务层应提供同步到分店 job 的创建与查询接口',
    )
    assert(
      pageSource.includes('buildSyncProductsToStoresFields(values)') &&
        pageSource.includes('fields: syncFields') &&
        pageSource.includes('selectSyncFields'),
      '同步到分店应根据复选框构造 fields，并校验至少选择一个字段',
    )
    assert(
      pageSource.includes('startSyncProductsToStoresJob(req)') &&
        pageSource.includes('createHqSyncJobPoller<SyncProductsToStoresJobResult>') &&
        pageSource.includes('getSyncProductsToStoresJob(jobId)'),
      '页面提交同步到分店后应创建后台 job，并使用共享轮询器查询任务状态',
    )
    assert(
      pageSource.includes('setSyncToStoreVisible(false)') &&
        pageSource.includes("t('posAdmin.products.syncToStoreJobSubmitted', '同步任务已提交，正在后台执行。完成后会自动提示结果。')"),
      '同步到分店 job 创建成功后应立即关闭弹窗并提示后台执行',
    )
    assert(
      pageSource.includes('createdCount') &&
        pageSource.includes('updatedCount') &&
        pageSource.includes('failedCount') &&
        pageSource.includes('result.errors') &&
        pageSource.includes('job.message'),
      '同步到分店最终提示应读取 job.result 的创建、更新、失败和错误明细，以及后端 message',
    )
    assert(
      pageSource.includes('error instanceof HqProductSyncPollingTimeoutError') &&
        pageSource.includes("t('posAdmin.products.syncToStoreJobTimeout', '后台仍在执行，请稍后刷新查看')"),
      '同步到分店轮询超时时应提示后台仍在执行，而不是误报同步完成',
    )
    assert(
      pageSource.includes('consecutivePollingFailures') &&
        pageSource.includes('error instanceof RequestError') &&
        pageSource.includes('error.status === 404') &&
        pageSource.includes('error.status === 401') &&
        pageSource.includes('error.status === 403') &&
        pageSource.includes('syncToStoreJobMissingTitle') &&
        pageSource.includes('syncToStoreJobAuthFailedTitle') &&
        pageSource.includes('syncToStoreJobPollingStoppedTitle'),
      '同步到分店 job 查询失败不应全部伪装成 Running，404/权限/连续失败都要停止轮询并提示用户',
    )
    assert(
      !pageSource.includes('await syncProductsToStores(req)') &&
        !pageSource.includes("t('posAdmin.products.syncToStoreComplete', '同步完成：成功 {{success}}，失败 {{failed}}'"),
      '页面不应继续直调同步接口或展示成功 0/失败 0 的旧提示',
    )
  })
  if (syncFieldsRequestFailure) failures.push(syncFieldsRequestFailure)

  const syncToStoreResultGuardFailure = await runTest('同步到分店 job 结果展示应区分失败、部分成功和成功', () => {
    const showResultStart = pageSource.indexOf('function showSyncToStoreJobResult(job: SyncProductsToStoresJobResult)')
    const showResultEnd = pageSource.indexOf('const ensureCanSyncProductsFromHq', showResultStart)
    assert(showResultStart >= 0 && showResultEnd > showResultStart, '页面应保留 showSyncToStoreJobResult 结果展示函数')
    const showResultSource = pageSource.slice(showResultStart, showResultEnd)

    assertSourceOrder(
      showResultSource,
      "if (job.status === 'Failed')",
      'if (errors.length || (result.failedCount ?? 0) > 0)',
      'job.status 为 Failed 时应先进入失败分支，不能被 failedCount/errors 误判为部分成功',
    )
    assert(
      showResultSource.includes("Modal.error({\n        title: t('posAdmin.products.syncToStoreFailed', '同步到分店失败')") &&
        showResultSource.includes("Modal.warning({\n        title: t('posAdmin.products.syncToStorePartialSucceeded', '同步到分店部分成功')") &&
        showResultSource.includes("Modal.success({\n      title: t('posAdmin.products.syncToStoreSucceeded', '同步到分店完成')"),
      '同步到分店 job 结果应分别使用失败、部分成功 warning 和成功弹窗',
    )
    assert(
      showResultSource.includes('const errors = result.errors ?? job.errors ?? []') &&
        showResultSource.includes('errors.length || (result.failedCount ?? 0) > 0'),
      'status Succeeded 但存在 failedCount/errors 时应显示部分成功 warning',
    )
    const failedBranchStart = showResultSource.indexOf("if (job.status === 'Failed')")
    const failedBranchReturn = showResultSource.indexOf('      return', failedBranchStart)
    const partialBranchStart = showResultSource.indexOf('if (errors.length || (result.failedCount ?? 0) > 0)', failedBranchStart)
    const failedBranch = showResultSource.slice(failedBranchStart, failedBranchReturn)
    assert(
      failedBranch.includes("t('posAdmin.products.syncToStoreFailed', '同步到分店失败')") &&
        !failedBranch.includes('setSelectedRowKeys([])') &&
        !failedBranch.includes('void loadData()') &&
        failedBranchReturn < partialBranchStart,
      'Failed 分支只展示失败结果并立即返回，不应清空选择或刷新成部分成功路径',
    )
  })
  if (syncToStoreResultGuardFailure) failures.push(syncToStoreResultGuardFailure)

  const syncToStorePollingGuardFailure = await runTest('同步到分店 job 轮询异常应停止并给出明确提示', () => {
    assertSourceOrder(
      pageSource,
      'if (error instanceof RequestError && (error.status === 404 || error.status === 401 || error.status === 403))',
      'consecutivePollingFailures += 1',
      '404/401/403 应立即抛出停止轮询，不能计入普通连续失败后继续伪装 Running',
    )
    assert(
      pageSource.includes('consecutivePollingFailures >= 3') &&
        pageSource.includes('throw error') &&
        pageSource.includes("title: t('posAdmin.products.syncToStoreJobPollingStoppedTitle', '同步到分店任务状态获取失败')"),
      '连续查询失败达到阈值后应停止轮询，并用 warning 告知用户刷新确认',
    )
    assert(
      pageSource.includes("title: t('posAdmin.products.syncToStoreJobMissingTitle', '同步到分店任务不存在')") &&
        pageSource.includes("title: t('posAdmin.products.syncToStoreJobAuthFailedTitle', '无法查询同步到分店任务')"),
      '同步到分店 job 轮询 404 和 401/403 应分别给出任务缺失与权限失败提示',
    )
  })
  if (syncToStorePollingGuardFailure) failures.push(syncToStorePollingGuardFailure)

  const storeRecordsFailure = await runTest('商品管理应显示分店记录数量并点击查看分店记录明细', () => {
    assert(
      typeSource.includes('storeRecordCount?: number') &&
        typeSource.includes('ProductStoreRecordDto'),
      '前端商品类型应包含分店记录数量和分店记录明细 DTO',
    )
    assert(
      serviceSource.includes('getProductStoreRecords') &&
        serviceSource.includes('/store-records'),
      '商品服务应提供按商品编码读取分店记录明细的接口',
    )
    assert(
      pageSource.includes('storeRecordCount') &&
        pageSource.includes('openStoreRecords') &&
        pageSource.includes('storeRecordsRequestSeqRef') &&
        pageSource.includes('requestSeq === storeRecordsRequestSeqRef.current') &&
        pageSource.includes('storeRecordsVisible') &&
        pageSource.includes('storeRecordsLoading') &&
        pageSource.includes('canManageStoreProducts') &&
        pageSource.includes('count > 0 && canManageStoreProducts') &&
        pageSource.includes('getProductStoreRecords(record.productCode)') &&
        pageSource.includes("dataIndex: 'storeName'") &&
        pageSource.includes('sorter: compareProductStoreRecordsByName'),
      '商品管理页面应新增分店记录数量列、点击处理、加载状态、请求竞态保护、分店名称排序器，并仅允许有分店商品权限时点击',
    )
    assert(
      pageSource.includes("t('posAdmin.products.storeRecords', '分店记录')") &&
        pageSource.includes("t('posAdmin.products.noStoreRecords', '暂无分店记录')") &&
        pageSource.includes("t('posAdmin.products.loadStoreRecordsFailed', '加载分店记录失败')"),
      '分店记录列和弹窗应有中文兜底文案',
    )
  })
  if (storeRecordsFailure) failures.push(storeRecordsFailure)

  const storeRecordFiltersFailure = await runTest('商品管理应支持分店记录数量筛选并把范围参数发送到商品列表接口', () => {
    assert(
      typeSource.includes('storeRecordCountMin?: number') &&
        typeSource.includes('storeRecordCountMax?: number'),
      'PosProductFilterParams 应声明分店记录数量最小值/最大值',
    )
    assert(
      serviceSource.includes('storeRecordCountMin: params.storeRecordCountMin') &&
        serviceSource.includes('storeRecordCountMax: params.storeRecordCountMax'),
      'getProducts 请求体应把分店记录数量范围原样发送给后端列表接口',
    )
    assert(
      pageSource.includes("const [storeRecordCountMode, setStoreRecordCountMode] = useState<'all' | 'hasRecords' | 'noRecords' | 'custom'>('all')") &&
        pageSource.includes("const [storeRecordCountModeInput, setStoreRecordCountModeInput] = useState<'all' | 'hasRecords' | 'noRecords' | 'custom'>('all')") &&
        pageSource.includes('const [storeRecordCountMin, setStoreRecordCountMin] = useState<number | undefined>(undefined)') &&
        pageSource.includes('const [storeRecordCountMax, setStoreRecordCountMax] = useState<number | undefined>(undefined)') &&
        pageSource.includes('const [storeRecordCountMinInput, setStoreRecordCountMinInput] = useState<number | undefined>(undefined)') &&
        pageSource.includes('const [storeRecordCountMaxInput, setStoreRecordCountMaxInput] = useState<number | undefined>(undefined)'),
      '页面应分别维护已生效和输入中的分店记录筛选模式与范围',
    )
    assert(
      pageSource.includes('storeRecordCountMin: storeRecordCountMin') &&
        pageSource.includes('storeRecordCountMax: storeRecordCountMax'),
      '查询生效后 loadData 应把当前分店记录筛选条件带入请求参数',
    )
    assert(
      pageSource.includes('let nextStoreRecordCountMin = storeRecordCountMinInput') &&
        pageSource.includes('let nextStoreRecordCountMax = storeRecordCountMaxInput') &&
        pageSource.includes("storeRecordCountModeInput === 'hasRecords'") &&
        pageSource.includes('nextStoreRecordCountMin = 1') &&
        pageSource.includes("storeRecordCountModeInput === 'noRecords'") &&
        pageSource.includes('nextStoreRecordCountMin = 0') &&
        pageSource.includes('nextStoreRecordCountMax = 0') &&
        pageSource.includes('let nextStoreRecordCountMode = storeRecordCountModeInput') &&
        pageSource.includes("nextStoreRecordCountMode = 'all'") &&
        pageSource.includes('setStoreRecordCountMode(nextStoreRecordCountMode)') &&
        pageSource.includes('setStoreRecordCountMin(nextStoreRecordCountMin)') &&
        pageSource.includes('setStoreRecordCountMax(nextStoreRecordCountMax)'),
      '点击查询后应按筛选模式把输入条件折算成真实查询范围，再应用到请求状态',
    )
    assert(
      pageSource.includes("storeRecordCountModeInput === 'custom'") &&
        pageSource.includes('storeRecordCountMinInput === undefined') &&
        pageSource.includes('storeRecordCountMaxInput === undefined') &&
        pageSource.includes("message.warning(t('posAdmin.products.storeRecordFilterInvalidRange', '最小数量不能大于最大数量'))") &&
        pageSource.includes('return'),
      '自定义范围两端为空应回到全部，最小值大于最大值时应提示并停止查询',
    )
    assert(
      !pageSource.includes('setStoreRecordCountMin(storeRecordCountMinInput)') &&
        !pageSource.includes('setStoreRecordCountMax(storeRecordCountMaxInput)'),
      '查询时不能把输入态范围直接写入生效态，应只写入按模式折算后的范围',
    )
    assert(
      pageSource.includes("setStoreRecordCountModeInput('all')") &&
        pageSource.includes("setStoreRecordCountMode('all')") &&
        pageSource.includes('setStoreRecordCountMinInput(undefined)') &&
        pageSource.includes('setStoreRecordCountMaxInput(undefined)') &&
        pageSource.includes('setStoreRecordCountMin(undefined)') &&
        pageSource.includes('setStoreRecordCountMax(undefined)'),
      '点击重置时应清空分店记录筛选模式与范围',
    )
    assert(
      pageSource.includes("t('posAdmin.products.storeRecordFilterPlaceholder', '分店记录')") &&
        pageSource.includes("t('posAdmin.products.storeRecordFilterAll', '全部')") &&
        pageSource.includes("t('posAdmin.products.storeRecordFilterHasRecords', '有记录')") &&
        pageSource.includes("t('posAdmin.products.storeRecordFilterNoRecords', '无记录')") &&
        pageSource.includes("t('posAdmin.products.storeRecordFilterCustom', '自定义范围')") &&
        pageSource.includes("t('posAdmin.products.storeRecordFilterMin', '最小数量')") &&
        pageSource.includes("t('posAdmin.products.storeRecordFilterMax', '最大数量')"),
      '页面应提供分店记录筛选模式与范围输入控件',
    )
  })
  if (storeRecordFiltersFailure) failures.push(storeRecordFiltersFailure)

  const storeRecordListSortFailure = await runTest('商品管理主列表分店记录列应启用服务端排序映射', () => {
    assert(
      pageSource.includes("storeRecordCount: 'storerecordcount'"),
      'SORT_FIELD_MAP 应把 storeRecordCount 映射到后端 storerecordcount 排序字段',
    )
    assert(
      pageSource.includes("dataIndex: 'storeRecordCount'") &&
        pageSource.includes('sorter: true') &&
        pageSource.includes("sortOrder: sortBy === 'storeRecordCount' ? sortOrder : undefined"),
      '分店记录主列表列应开启服务端排序，并把当前排序状态绑定到 storeRecordCount',
    )
    assert(
      pageSource.includes('count > 0 && canManageStoreProducts') &&
        pageSource.includes('compareProductStoreRecordsByName'),
      '分店记录主列表列仍应保持只有有权限且数量大于 0 时才可点击查看明细',
    )
  })
  if (storeRecordListSortFailure) failures.push(storeRecordListSortFailure)

  const productAutoPricingColumnFailure = await runTest('商品管理主列表应显示自动定价列', () => {
    const columnsStart = pageSource.indexOf('const columns: ColumnsType<ProductRow> = [')
    const columnsEnd = pageSource.indexOf('\n  ]\n\n  return (', columnsStart)
    const mainColumnsSource = pageSource.slice(columnsStart, columnsEnd)

    assert(
      mainColumnsSource.includes("title: t('posAdmin.products.autoPricing', '自动定价')") &&
        mainColumnsSource.includes("dataIndex: 'isAutoPricing'") &&
        mainColumnsSource.includes("<Tag color={value ? 'green' : 'default'}>") &&
        mainColumnsSource.includes("t('common.yes', '是')") &&
        mainColumnsSource.includes("t('common.no', '否')"),
      '商品管理主表应新增自动定价列，并以是/否展示 ProductRow.isAutoPricing',
    )
  })
  if (productAutoPricingColumnFailure) failures.push(productAutoPricingColumnFailure)

  const storeRecordsBatchUpdateFailure = await runTest('分店记录弹窗应支持批量修改分店业务字段', () => {
    assert(
      typeSource.includes('BatchUpdateProductStoreRecordsRequest') &&
        typeSource.includes('BatchUpdateProductStoreRecordsResult') &&
        typeSource.includes('purchasePrice?: number') &&
        typeSource.includes('storeRetailPriceValue?: number') &&
        typeSource.includes('discountRate?: number') &&
        typeSource.includes('isAutoPricing?: boolean') &&
        typeSource.includes('isSpecialProduct?: boolean') &&
        typeSource.includes('isActive?: boolean'),
      '类型层应声明分店记录批量修改请求/结果，以及六个可改字段',
    )
    assert(
      serviceSource.includes('batchUpdateProductStoreRecords') &&
        serviceSource.includes('/store-records/batch-update'),
      '服务层应提供分店记录批量修改接口',
    )
    assert(
      pageSource.includes('const canEditStoreProducts = useAuthStore((state) => state.access.canEditStoreProducts)'),
      '页面应从 auth store 读取 canEditStoreProducts',
    )
    assert(
      pageSource.includes('const [storeRecordSelectedRowKeys, setStoreRecordSelectedRowKeys] = useState<React.Key[]>([])') &&
        pageSource.includes('const [storeRecordBatchEditVisible, setStoreRecordBatchEditVisible] = useState(false)') &&
        pageSource.includes('const [storeRecordBatchUpdating, setStoreRecordBatchUpdating] = useState(false)') &&
        pageSource.includes('const [storeRecordBatchEditForm] = Form.useForm()'),
      '页面应维护分店记录选择、批量子弹窗可见性、提交态和表单状态',
    )
    assert(
      pageSource.includes('rowSelection={{') &&
        pageSource.includes('selectedRowKeys: storeRecordSelectedRowKeys') &&
        pageSource.includes('setStoreRecordSelectedRowKeys(keys)'),
      '分店记录表格应支持行选择，并单独维护选中 key',
    )
    assert(
      pageSource.includes("t('posAdmin.products.batchUpdateStoreRecords', '批量修改')") &&
        pageSource.includes('disabled={!canEditStoreProducts || !storeRecordSelectedRowKeys.length}') &&
        pageSource.includes("t('common.close', '关闭')"),
      '分店记录弹窗 footer 应提供受编辑权限和选中记录控制的“批量修改/关闭”按钮',
    )
    assert(
      pageSource.includes('batchUpdateProductStoreRecords(storeRecordsProduct.productCode, {') &&
        pageSource.includes('storeCodes: selectedStoreCodes') &&
        pageSource.includes('changes,'),
      '提交时应使用当前商品编码、选中分店代码和 changes 调用批量修改接口',
    )
    assert(
      pageSource.includes('const selectedStoreCodes = storeRecordSelectedRows') &&
        pageSource.includes('.map((record) => record.storeCode)') &&
        pageSource.includes('.filter((storeCode): storeCode is string => !!storeCode)'),
      '提交目标应从已选记录提取非空 storeCode',
    )
    assert(
      pageSource.includes("t('posAdmin.invoiceDetail.purchasePrice', '进货价')") &&
        pageSource.includes("t('posAdmin.invoiceDetail.retailPrice', '零售价')") &&
        pageSource.includes("t('posAdmin.productPrice.discountRate', '折扣率')") &&
        pageSource.includes("t('posAdmin.products.autoPricing', '自动定价')") &&
        pageSource.includes("t('posAdmin.products.specialProduct', '特殊商品')") &&
        pageSource.includes("t('posAdmin.cashierUsers.status', '状态')"),
      '批量修改子弹窗应包含六个业务字段',
    )
    assert(
      pageSource.includes("t('posAdmin.products.toggleFieldUpdate', '修改该字段')") &&
        pageSource.includes('precision={2}') &&
        pageSource.includes('precision={4}') &&
        pageSource.includes("value: true, label: t('common.yes', '是')") &&
        pageSource.includes("value: false, label: t('common.no', '否')"),
      '每个字段应由“修改该字段”控制纳入 changes，数字精度与布尔选项要明确',
    )
    assert(
      pageSource.includes('if (!storeRecordSelectedRowKeys.length) {') &&
        pageSource.includes("message.warning(t('posAdmin.products.selectStoreRecordsFirst', '请先选择分店记录'))") &&
        pageSource.includes("message.warning(t('posAdmin.products.selectAtLeastOneStoreRecordField', '请至少选择一个要修改的字段'))") &&
        pageSource.includes("message.warning(t('posAdmin.products.completeStoreRecordFields', '请填写已勾选的字段值'))"),
      '提交前应校验已选分店、至少一个字段、以及已勾选字段必须有值',
    )
    assert(
      pageSource.includes('message.success(t(\'posAdmin.products.batchUpdateStoreRecordsResult\'') &&
        pageSource.includes('success: result.successCount') &&
        pageSource.includes('failed: result.failedCount') &&
        pageSource.includes('Modal.error({') &&
        pageSource.includes('result.errors.join'),
      '提交成功后应提示成功/失败统计，有错误明细时要弹窗展示',
    )
    assert(
      pageSource.includes('await openStoreRecords(storeRecordsProduct)') &&
        pageSource.includes('await loadData()'),
      '批量修改成功后应刷新当前分店记录和主列表',
    )
    assert(
      pageSource.includes('setStoreRecordSelectedRowKeys([])') &&
        pageSource.includes('storeRecordBatchEditForm.resetFields()') &&
        pageSource.includes('setStoreRecordBatchEditVisible(false)'),
      '关闭分店记录弹窗时应清理分店记录选择和批量子弹窗状态',
    )
  })
  if (storeRecordsBatchUpdateFailure) failures.push(storeRecordsBatchUpdateFailure)

  const storeRecordSorterFailure = await runTest('分店记录名称排序应同名按分店代码兜底', () => {
    const records: ProductStoreRecordDto[] = [
      { storeCode: 'S01', storeName: 'Beta', isActive: true, isAutoPricing: false, isSpecialProduct: false },
      { storeCode: 'S99', storeName: '', isActive: true, isAutoPricing: false, isSpecialProduct: false },
      { storeCode: 'S04', storeName: 'Alpha', isActive: true, isAutoPricing: false, isSpecialProduct: false },
      { storeCode: 'S03', storeName: 'Alpha', isActive: true, isAutoPricing: false, isSpecialProduct: false },
      { storeCode: 'S02', storeName: 'Gamma', isActive: true, isAutoPricing: false, isSpecialProduct: false },
    ]

    const sortedCodes = records.slice().sort(compareProductStoreRecordsByName).map((item) => item.storeCode)

    assertEqual(sortedCodes.join(','), 'S03,S04,S01,S02,S99', '分店记录排序应先按分店名称，再按分店代码稳定排序')
  })
  if (storeRecordSorterFailure) failures.push(storeRecordSorterFailure)

  const storeRecordColumnSorterFailure = await runTest('分店记录弹窗各列应支持前端排序', () => {
    assert(
      pageSource.includes('sorter: compareProductStoreRecordsByStoreCode') &&
        pageSource.includes('sorter: compareProductStoreRecordsByName') &&
        pageSource.includes('sorter: compareProductStoreRecordsByStoreProductCode') &&
        pageSource.includes('sorter: compareProductStoreRecordsByPurchasePrice') &&
        pageSource.includes('sorter: compareProductStoreRecordsByRetailPrice') &&
        pageSource.includes('sorter: compareProductStoreRecordsByDiscountRate') &&
        pageSource.includes('sorter: compareProductStoreRecordsByAutoPricing') &&
        pageSource.includes('sorter: compareProductStoreRecordsBySpecialProduct') &&
        pageSource.includes('sorter: compareProductStoreRecordsByActive') &&
        pageSource.includes('sorter: compareProductStoreRecordsByUpdatedAt') &&
        pageSource.includes('sorter: compareProductStoreRecordsByUpdatedBy'),
      '分店记录弹窗应给分店代码、名称、编码、价格、折扣率、布尔状态、更新时间和更新人列绑定前端 sorter',
    )

    const records: ProductStoreRecordDto[] = [
      {
        storeCode: '1002',
        storeName: 'Beta',
        storeProductCode: 'B',
        purchasePrice: 4.87,
        storeRetailPriceValue: 12.5,
        discountRate: 0,
        isAutoPricing: true,
        isSpecialProduct: false,
        isActive: true,
        updatedAt: '2025-01-01T00:00:00Z',
        updatedBy: 'ReactSync',
      },
      {
        storeCode: '1001',
        storeName: 'Alpha',
        storeProductCode: 'A',
        purchasePrice: 1.25,
        storeRetailPriceValue: 9.99,
        discountRate: 0.1,
        isAutoPricing: false,
        isSpecialProduct: true,
        isActive: false,
        updatedAt: '2024-01-01T00:00:00Z',
        updatedBy: 'admin',
      },
      {
        storeCode: '1003',
        storeName: 'Gamma',
        storeProductCode: 'C',
        isAutoPricing: true,
        isSpecialProduct: false,
        isActive: true,
      },
    ]

    assertEqual(records.slice().sort(compareProductStoreRecordsByStoreCode)[0]?.storeCode, '1001', '分店代码应按文本升序排序')
    assertEqual(records.slice().sort(compareProductStoreRecordsByStoreProductCode)[0]?.storeProductCode, 'A', '分店商品编码应按文本升序排序')
    assertEqual(records.slice().sort(compareProductStoreRecordsByPurchasePrice)[0]?.purchasePrice, 1.25, '进货价应按数字升序排序')
    assertEqual(records.slice().sort(compareProductStoreRecordsByRetailPrice)[0]?.storeRetailPriceValue, 9.99, '零售价应按数字升序排序')
    assertEqual(records.slice().sort(compareProductStoreRecordsByDiscountRate)[0]?.discountRate, 0, '折扣率应按数字升序排序')
    assertEqual(records.slice().sort(compareProductStoreRecordsByAutoPricing)[0]?.isAutoPricing, false, '自动定价应按否到是排序')
    assertEqual(records.slice().sort(compareProductStoreRecordsBySpecialProduct)[0]?.isSpecialProduct, false, '特殊商品应按否到是排序')
    assertEqual(records.slice().sort(compareProductStoreRecordsByActive)[0]?.isActive, false, '状态应按禁用到启用排序')
    assertEqual(records.slice().sort(compareProductStoreRecordsByUpdatedAt)[0]?.updatedAt, '2024-01-01T00:00:00Z', '更新时间应按时间升序排序')
    assertEqual(records.slice().sort(compareProductStoreRecordsByUpdatedBy)[0]?.updatedBy, undefined, '更新人空值应按文本空值排序')
  })
  if (storeRecordColumnSorterFailure) failures.push(storeRecordColumnSorterFailure)

  const pushToHqFailure = await runTest('选中商品发送到 HQ 应复用选择、权限和防重复提交保护', () => {
    assert(
      typeSource.includes('PushProductsToHqRequest') &&
        typeSource.includes('productCodes: string[]') &&
        typeSource.includes('PushProductsToHqResult'),
      '类型层应声明发送到 HQ 的请求和结果契约',
    )
    assert(
      serviceSource.includes('pushProductsToHq') &&
        serviceSource.includes("`${API_BASE}/push-to-hq`") &&
        serviceSource.includes('normalizePushProductsToHqResult'),
      '服务层应提供固定接口调用，并归一后端统计字段',
    )
    assert(
      pageSource.includes('handlePushToHq') &&
        pageSource.includes('selectedRowKeys.map(String)') &&
        pageSource.includes('pushProductsToHq({') &&
        pageSource.includes('showPushToHqResult(result)'),
      '页面应把当前选中商品编码发送到 HQ，并展示成功和错误明细',
    )
    assert(
      pageSource.includes('extractPushToHqErrorResult(error)') &&
        pageSource.includes('payload.details') &&
        pageSource.includes('Modal.error({') &&
        pageSource.includes('errorResult.errors.join'),
      '发送到 HQ 失败时应从后端 data/details 中提取错误明细并弹窗展示',
    )
    assert(
      pageSource.includes('pushToHqAffectedRows') &&
        pageSource.includes('affectedRowCount') &&
        pageSource.includes('商品成功 {{success}}'),
      '发送到 HQ 结果应区分商品成功数和 HQ 影响记录数，避免统计语义混淆',
    )
    assert(
      pageSource.includes("t('posAdmin.products.productSetCodesCreated', '套装编码新增')") &&
        pageSource.includes("t('posAdmin.products.productSetCodesUpdated', '套装编码更新')") &&
        pageSource.includes("t('posAdmin.products.storeMultiCodesCreated', '门店多码新增')") &&
        pageSource.includes("t('posAdmin.products.storeMultiCodesUpdated', '门店多码更新')") &&
        serviceSource.includes('Number(payload.productSetCodesCreated ?? payload.productSetCodesAdded ?? 0)') &&
        serviceSource.includes('Number(payload.storeMultiCodesCreated ?? 0)') &&
        serviceSource.includes('Number(payload.storeMultiCodesUpdated ?? 0)'),
      '发送到 HQ 成功弹窗和服务归一化应覆盖套装编码与门店多码统计',
    )
    assert(
      pageSource.includes('if (!ensureCanManagePosProducts()) return') &&
        pageSource.includes('canManagePosProducts') &&
        pageSource.includes("t('posAdmin.products.pushToHq', '发送到HQ')"),
      '发送到 HQ 按钮和处理函数应复用 POS 商品管理权限',
    )
    assert(
      pageSource.includes('const pushToHqLoadingRef = useRef(false)') &&
        pageSource.includes('if (pushToHqLoadingRef.current) return') &&
        pageSource.includes('pushToHqLoadingRef.current = true') &&
        pageSource.includes('pushToHqLoadingRef.current = false') &&
        pageSource.includes('disabled={!selectedRowKeys.length || pushToHqLoading}'),
      '发送到 HQ 应使用 ref 锁和 loading 状态防止连续点击重复提交',
    )
  })
  if (pushToHqFailure) failures.push(pushToHqFailure)

  const selectedFromHqFailure = await runTest('选中商品从 HQ 同步应复用选择、Admin 权限和防重复提交保护', () => {
    assert(
      typeSource.includes('SyncSelectedProductsFromHqRequest') &&
        typeSource.includes('productCodes: string[]'),
      '类型层应声明选中商品从 HQ 同步的请求契约',
    )
    assert(
      serviceSource.includes('syncSelectedProductsFromHq') &&
        serviceSource.includes("`${API_BASE}/sync-selected-from-hq`") &&
        serviceSource.includes('normalizeHqProductSyncResult'),
      '服务层应提供选中商品从 HQ 同步接口，并复用 HQ 同步结果归一化',
    )
    assert(
      pageSource.includes('handleSyncSelectedFromHq') &&
        pageSource.includes('selectedRowKeys.map(String)') &&
        pageSource.includes('syncSelectedProductsFromHq({') &&
        pageSource.includes('showSelectedFromHqResult(result)'),
      '页面应把当前选中商品编码发送给从 HQ 选中同步接口，并展示结果明细',
    )
    assert(
      pageSource.includes('const selectedFromHqLoadingRef = useRef(false)') &&
        pageSource.includes('if (selectedFromHqLoadingRef.current) return') &&
        pageSource.includes('selectedFromHqLoadingRef.current = true') &&
        pageSource.includes('selectedFromHqLoadingRef.current = false'),
      '选中商品从 HQ 同步应使用 ref 锁防止连续点击重复提交',
    )
    assert(
      pageSource.includes('ensureCanSyncProductsFromHq') &&
        pageSource.includes('isAdmin') &&
        pageSource.includes("t('posAdmin.products.syncSelectedFromHq', '从HQ同步选中')") &&
        pageSource.includes('disabled={!selectedRowKeys.length || selectedFromHqLoading}'),
      '从 HQ 同步选中按钮应只对 Admin 显示，并在未选择或 loading 时禁用',
    )
  })
  if (selectedFromHqFailure) failures.push(selectedFromHqFailure)

  const batchTranslateFailure = await runTest('选中商品批量翻译应默认中文到英文并覆盖商品名称', () => {
    assert(
      pageSource.includes("../../../services/translationService") &&
        pageSource.includes('batchTranslate'),
      '页面应引入批量翻译服务',
    )
    assert(
      pageSource.includes('const [translating, setTranslating] = useState(false)') &&
        pageSource.includes('setTranslating(true)') &&
        pageSource.includes('setTranslating(false)'),
      '页面应维护批量翻译 loading 状态',
    )
    assert(
      pageSource.includes('containsChineseText') &&
        pageSource.includes('buildProductNameTranslationUpdates') &&
        pageSource.includes('!containsChineseText(translatedName)'),
      '批量翻译应过滤空结果、未变化结果和仍包含中文的结果',
    )
    assert(
      pageSource.includes('const selectedRows = data.filter((row) => selectedRowKeys.includes(row.key))') &&
        pageSource.includes('Array.from(new Set(selectedRows.map((row) => row.productName.trim())') &&
        pageSource.includes('const translations = await batchTranslate(names)'),
      '批量翻译应只使用当前页选中商品名称去重后调用翻译接口',
    )
    assert(
      pageSource.includes('const result = await batchUpdateProducts(updates)') &&
        pageSource.includes('productCode: row.productCode') &&
        pageSource.includes('productName: translatedName') &&
        pageSource.includes('englishName: translatedName'),
      '批量翻译应通过批量更新接口提交 productCode、翻译后的 productName 和 englishName',
    )
    assert(
      typeSource.includes('englishName?: string'),
      '批量更新商品 DTO 应声明 englishName 字段以同步写入后端 EnglishName',
    )
    assert(
      pageSource.includes("t('posAdmin.products.batchTranslate', '批量翻译')") &&
        pageSource.includes('disabled={!selectedRowKeys.length || translating}') &&
        pageSource.includes('loading={translating}'),
      '工具栏应提供受选中状态和 loading 控制的批量翻译按钮',
    )
  })
  if (batchTranslateFailure) failures.push(batchTranslateFailure)

  const jobEndpointFailure = await runTest('全量和增量应创建后台 job 而不是直接等待长同步请求', () => {
    assert(
      serviceSource.includes("`${SYNC_API_BASE}/products/jobs`") &&
        serviceSource.includes("`${SYNC_API_BASE}/products-incremental/jobs`") &&
        (serviceSource.includes("`${SYNC_API_BASE}/products/jobs/${encodeURIComponent(jobId)}`") ||
          serviceSource.includes("`${SYNC_API_BASE}/products/jobs/${jobId}`")),
      '服务层应提供商品 HQ 同步 job 创建和查询接口',
    )
    assert(
        pageSource.includes('createProductFullHqSyncJob({ operationId })') &&
        pageSource.includes('createProductIncrementalHqSyncJob({') &&
        pageSource.includes("const startDate = values.startDate ? values.startDate.format('YYYY-MM-DD')"),
      '页面应按同步模式分别创建全量/增量 job，增量需要传 YYYY-MM-DD 起始日期',
    )
    assert(
      !pageSource.includes('await syncProductsFromHqFull()') &&
        !pageSource.includes('await syncProductsFromHqIncremental({'),
      '页面不应继续直接等待长同步接口完成',
    )
  })
  if (jobEndpointFailure) failures.push(jobEndpointFailure)

  const syncResultMappingFailure = await runTest('HqProductSyncResult 与页面文案应切到新字段', () => {
    assert(
      typeSource.includes('productsAdded?: number') &&
        typeSource.includes('productsUpdated?: number') &&
        typeSource.includes('productsDeleted?: number'),
      'HqProductSyncResult 类型应声明 productsAdded/productsUpdated/productsDeleted',
    )
    assert(
      pageSource.includes('productsAdded') &&
        pageSource.includes('productsUpdated') &&
        pageSource.includes('productsDeleted'),
      '页面同步成功提示应读取 productsAdded/productsUpdated/productsDeleted',
    )
  })
  if (syncResultMappingFailure) failures.push(syncResultMappingFailure)

  const duplicateClickGuardFailure = await runTest('HQ 同步确认应防止连续点击重复提交', () => {
    assert(
      pageSource.includes('const [hqSyncSubmitting, setHqSyncSubmitting] = useState(false)') &&
        pageSource.includes('const hqSyncSubmittingRef = useRef(false)'),
      '页面应维护 hqSyncSubmitting 状态和 ref 锁',
    )
    assert(
      pageSource.includes('if (hqSyncSubmittingRef.current) return') &&
        pageSource.includes('hqSyncSubmittingRef.current = true') &&
        pageSource.includes('hqSyncSubmittingRef.current = false'),
      '同步处理函数应在连续点击时直接返回，并在结束后释放锁',
    )
    assert(
      pageSource.includes('confirmLoading={hqSyncSubmitting}') &&
        pageSource.includes('disabled={hqSyncSubmitting}'),
      '同步按钮和弹窗确认应绑定 submitting 状态',
    )
  })
  if (duplicateClickGuardFailure) failures.push(duplicateClickGuardFailure)

  const backgroundJobFailure = await runTest('HQ 同步应提交后台 job 后立即关闭弹窗并提示后台执行', () => {
    assert(
      pageSource.includes('setHqSyncVisible(false)') &&
        pageSource.includes('hqSyncJobSubmitted') &&
        pageSource.includes('startHqSyncJobPolling(activeJob)'),
      '创建 job 成功后应关闭弹窗、提示后台执行，并启动轮询',
    )
  })
  if (backgroundJobFailure) failures.push(backgroundJobFailure)

  const activeJobFailure = await runTest('HQ 同步 active job 应写入 localStorage 并在刷新后恢复轮询', () => {
    assert(
      pageSource.includes('PRODUCT_HQ_SYNC_ACTIVE_JOB_STORAGE_KEY') &&
        pageSource.includes('localStorage.setItem(PRODUCT_HQ_SYNC_ACTIVE_JOB_STORAGE_KEY') &&
        pageSource.includes('localStorage.removeItem(PRODUCT_HQ_SYNC_ACTIVE_JOB_STORAGE_KEY'),
      '页面应使用固定 key 保存和清理 active job',
    )
    assert(
      pageSource.includes('restoreActiveHqSyncJob()') &&
        pageSource.includes('readActiveProductHqSyncJob()') &&
        pageSource.includes('startHqSyncJobPolling(restoredJob)'),
      '页面刷新后应读取 active job 并恢复轮询',
    )
    assert(
      pageSource.includes('}, [stopHqSyncJobPolling])') &&
        pageSource.includes('}, [restoreActiveHqSyncJob])'),
      '卸载清理和恢复轮询应拆成独立 effect，避免分页/筛选变化误停轮询',
    )
  })
  if (activeJobFailure) failures.push(activeJobFailure)

  const hqSyncArchitectureFailure = await runTest('商品 HQ 同步页面应使用共享轮询器和统一 operationId', () => {
    assert(
      serviceSource.includes('export function buildProductHqSyncOperationId') &&
        pageSource.includes('buildProductHqSyncOperationId'),
      'operationId 应由服务层统一导出，页面不应维护另一套生成规则',
    )
    assert(
      serviceSource.includes('createProductHqSyncJobPoller') &&
        pageSource.includes('createProductHqSyncJobPoller'),
      '页面和服务兼容 wrapper 应共用商品 HQ 同步轮询器',
    )
    assert(
      !pageSource.includes("type HqSyncJobStatus = 'Queued'") &&
        !pageSource.includes('type ProductHqSyncJobResult = HqProductSyncResult &'),
      '页面不应镜像服务层已有 HQ 同步 job 类型',
    )
    assert(
      !pageSource.includes('setTimeout(poll, PRODUCT_HQ_SYNC_POLL_INTERVAL_MS)') &&
        !pageSource.includes('async function poll()'),
      '页面不应保留原始 setTimeout 轮询状态机',
    )
  })
  if (hqSyncArchitectureFailure) failures.push(hqSyncArchitectureFailure)

  const supplierImagePollingFailure = await runTest('供应商图片批量修改 job 轮询不应把 404 和权限错误伪装成 Running', () => {
    assert(
      pageSource.includes('error instanceof RequestError') &&
        pageSource.includes('error.status === 404') &&
        pageSource.includes('error.status === 401') &&
        pageSource.includes('error.status === 403'),
      '图片批量修改 job 轮询应按 RequestError.status 分类处理',
    )
    assert(
      pageSource.includes('clearActiveImageBatchJob(job.localSupplierCode)') &&
        pageSource.includes('batchImageJobMissingTitle') &&
        pageSource.includes('batchImageJobAuthFailedTitle'),
      '404/权限错误应清理 active job 并停止轮询提示用户',
    )
  })
  if (supplierImagePollingFailure) failures.push(supplierImagePollingFailure)

  const supplierImageConcurrentJobFailure = await runTest('供应商图片批量修改应按供应商跟踪 active job', () => {
    assert(
      pageSource.includes('type ActiveSupplierImageBatchJobMap') &&
        pageSource.includes('readActiveSupplierImageBatchJobs') &&
        pageSource.includes('saveActiveSupplierImageBatchJobs') &&
        pageSource.includes('activeImageBatchJobs'),
      '图片批量修改 active job 应保存为按供应商代码索引的 map',
    )
    assert(
      pageSource.includes('stopSupplierImageBatchPolling(job.localSupplierCode)') &&
        pageSource.includes('stopSupplierImageBatchPollingRef.current[jobKey] = poller.stop') &&
        pageSource.includes('clearActiveImageBatchJob(job.localSupplierCode)'),
      '轮询启动、停止和清理应只作用于对应供应商',
    )
    assert(
      pageSource.includes('getActiveImageBatchJobBySupplier(values.localSupplierCode)') &&
        pageSource.includes('showActiveSupplierImageBatchStatus(existingActiveJob)') &&
        !pageSource.includes('const storedActiveJob = activeImageBatchJob ?? readActiveSupplierImageBatchJob()'),
      '提交时只阻止同一供应商已有任务，不应因为其他供应商任务而阻止打开弹窗',
    )
    assert(
      pageSource.indexOf('imageBatchForm.resetFields()') < pageSource.indexOf('getActiveImageBatchJobBySupplier(values.localSupplierCode)'),
      '打开图片批量修改弹窗时应允许切换到其他供应商，不能先用默认供应商 active job 直接拦截',
    )
    assert(
      pageSource.includes('hbwebSkippedExistingImageCount') &&
        pageSource.includes('hqSkippedExistingImageCount'),
      '结果弹窗应展示 Hbweb/HQ 已有图片跳过数量',
    )
  })
  if (supplierImageConcurrentJobFailure) failures.push(supplierImageConcurrentJobFailure)

  const existingJobFailure = await runTest('已有 active job 时 HQ 同步按钮只展示状态不新建任务', () => {
    assert(
      pageSource.includes('const storedActiveJob = activeHqSyncJob ?? readActiveProductHqSyncJob()') &&
        pageSource.includes('showActiveHqSyncJobStatus(storedActiveJob)'),
      '打开 HQ 同步弹窗前应先判断 active job 并展示状态',
    )
    assert(
      pageSource.includes('hqSyncInProgress'),
      '已有任务时按钮应展示同步中状态',
    )
  })
  if (existingJobFailure) failures.push(existingJobFailure)

  const terminalResultFailure = await runTest('HQ 同步 job 完成、失败、Succeeded 加错误明细应展示友好结果', () => {
    assert(
      pageSource.includes("result.status === 'Failed'") &&
        pageSource.includes('hqSyncJobPartialSucceeded') &&
        pageSource.includes('hqSyncJobSucceeded') &&
        pageSource.includes('hqSyncJobTimeout'),
      '轮询终态应区分失败、完成、错误明细部分成功和超时',
    )
    assert(
      pageSource.includes('HqProductSyncPollingTimeoutError') &&
        pageSource.includes('showPollingTimeout()'),
      '轮询超时应由共享 poller 抛出专门错误，并走统一超时提示',
    )
    assert(
      pageSource.includes('incrementalStartDateRequired') &&
        pageSource.includes('allowClear={false}'),
      '增量同步起始日期应必填，避免提交无范围的增量 job',
    )
  })
  if (terminalResultFailure) failures.push(terminalResultFailure)

  if (failures.length > 0) {
    throw new Error(`共有 ${failures.length} 个测试失败\n- ${failures.join('\n- ')}`)
  }

  console.log('ProductManagement.hqSync.logic.test: ok')
}

await main()
