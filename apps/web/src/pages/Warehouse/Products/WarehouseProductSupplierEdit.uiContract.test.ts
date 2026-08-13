import { readFileSync } from 'node:fs'
import path from 'node:path'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message)
}

const pageSource = readFileSync(
  path.resolve(process.cwd(), 'src/pages/Warehouse/Products/index.tsx'),
  'utf8',
)
const serviceSource = readFileSync(
  path.resolve(process.cwd(), 'src/services/warehouseProductService.ts'),
  'utf8',
)

const supplierFieldStart = pageSource.indexOf('<Form.Item name="supplierCode"')
const supplierFieldEnd = pageSource.indexOf('</Form.Item>', supplierFieldStart)

assert(supplierFieldStart >= 0 && supplierFieldEnd > supplierFieldStart, '必须保留国内供应商表单字段')

const supplierFieldSource = pageSource.slice(supplierFieldStart, supplierFieldEnd)

assert(
  !/\bdisabled\b/.test(supplierFieldSource),
  '编辑仓库商品时，国内供应商选择器必须保持可操作',
)
assert(
  pageSource.includes('supplierCode: record.domesticSupplierCode'),
  '打开编辑弹窗时必须回填当前国内供应商',
)
assert(
  pageSource.includes('supplierCode: values.supplierCode'),
  '保存仓库商品时必须提交用户选择的国内供应商',
)
assert(
  serviceSource.includes('SupplierCode: payload.supplierCode'),
  '完整更新请求必须把国内供应商映射到后端 SupplierCode 字段',
)

const batchModalStart = pageSource.indexOf("<Modal title={t('warehouse.batchEditTitle'")
const batchModalEnd = pageSource.indexOf("<Modal title={hqImageSyncFailDetail", batchModalStart)

assert(batchModalStart >= 0 && batchModalEnd > batchModalStart, '必须保留仓库商品批量修改弹窗')

const batchModalSource = pageSource.slice(batchModalStart, batchModalEnd)

assert(
  batchModalSource.includes('<Form.Item name="supplierCode"'),
  '批量修改弹窗必须提供国内供应商字段',
)
assert(
  batchModalSource.includes('options={buildSupplierOptions(suppliers)}'),
  '批量修改国内供应商必须复用活跃供应商选项',
)
assert(
  batchModalSource.includes('showSearch') && batchModalSource.includes('allowClear'),
  '批量修改国内供应商必须支持搜索，并允许清空选择以表示不修改',
)
assert(
  pageSource.includes('SupplierCode: values.supplierCode'),
  '批量修改请求必须提交用户选择的国内供应商',
)

const batchRequestTypeStart = serviceSource.indexOf('export interface WarehouseProductBatchUpdateItem')
const batchRequestTypeEnd = serviceSource.indexOf('\n}', batchRequestTypeStart)

assert(
  batchRequestTypeStart >= 0 && batchRequestTypeEnd > batchRequestTypeStart,
  '必须保留仓库商品批量修改请求类型',
)

const batchRequestTypeSource = serviceSource.slice(batchRequestTypeStart, batchRequestTypeEnd)

assert(
  batchRequestTypeSource.includes('SupplierCode?: string'),
  '仓库商品批量修改请求类型必须声明 SupplierCode',
)

console.log('WarehouseProductSupplierEdit.uiContract.test: ok')
