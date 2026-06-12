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

const detailFile = path.resolve(process.cwd(), 'src/pages/Warehouse/StoreOrders/Detail.tsx')
const detailSource = readFileSync(detailFile, 'utf8')

async function main() {
  const failures: string[] = []

  const supplierFilterFailure = await runTest('商品弹窗应提供澳洲供应商筛选下拉', () => {
    assert(
      detailSource.includes("placeholder={t('storeOrders.detail.filterLocalSupplier', '筛选澳洲供应商')}") &&
        detailSource.includes('showSearch') &&
        detailSource.includes('optionFilterProp="label"') &&
        detailSource.includes('allowClear'),
      '商品弹窗缺少带中文兜底的澳洲供应商筛选下拉',
    )
  })
  if (supplierFilterFailure) failures.push(supplierFilterFailure)

  const queryFailure = await runTest('商品弹窗查询必须附带供应商与排除条件', () => {
    assert(
      detailSource.includes('localSupplierCode: nextSupplierCode || undefined') &&
        detailSource.includes('excludeExistingWarehouseProducts: true') &&
        detailSource.includes('excludeOrderGUID: orderGUID'),
      '商品弹窗查询未附带供应商过滤或排除条件',
    )
  })
  if (queryFailure) failures.push(queryFailure)

  const supplierStateFailure = await runTest('商品弹窗关闭时应重置供应商筛选状态', () => {
    assert(
      detailSource.includes("const [supplierCode, setSupplierCode] = useState<string>()") &&
        detailSource.includes('setSupplierCode(undefined)') &&
        detailSource.includes('setSupplierOptions([])'),
      '商品弹窗关闭后未重置供应商筛选状态',
    )
  })
  if (supplierStateFailure) failures.push(supplierStateFailure)

  const supplierColumnFailure = await runTest('商品弹窗应显示供应商名称列', () => {
    assert(
      detailSource.includes("title: t('column.supplierName', '供应商名称')") &&
        detailSource.includes("dataIndex: 'localSupplierName'") &&
        detailSource.includes("record.localSupplierCode || '--'"),
      '商品弹窗缺少供应商名称列或编码兜底',
    )
  })
  if (supplierColumnFailure) failures.push(supplierColumnFailure)

  const quickAddFailure = await runTest('快速添加请求仍保持原始查询结构', () => {
    assert(
      detailSource.includes('const result = await getStoreOrderProducts({') &&
        detailSource.includes('itemNumber: normalizedItemNumber') &&
        detailSource.includes('pageNumber: 1') &&
        detailSource.includes('pageSize: 50') &&
        detailSource.includes("sortBy: 'Default'"),
      '快速添加商品查询结构被意外改动',
    )
  })
  if (quickAddFailure) failures.push(quickAddFailure)

  if (failures.length > 0) {
    throw new Error(`共有 ${failures.length} 个测试失败\\n- ${failures.join('\\n- ')}`)
  }

  console.log('productPickerModal.logic.test: ok')
}

await main()
