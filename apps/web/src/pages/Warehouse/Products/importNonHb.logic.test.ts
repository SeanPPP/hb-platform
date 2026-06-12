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

const modalFile = path.resolve(process.cwd(), 'src/pages/Warehouse/Products/ImportNonHbModal.tsx')
const pageFile = path.resolve(process.cwd(), 'src/pages/Warehouse/Products/index.tsx')
const modalSource = readFileSync(modalFile, 'utf8')
const pageSource = readFileSync(pageFile, 'utf8')

async function main() {
  const failures: string[] = []

  const supplierFilterFailure = await runTest('供应商 200 不应被前端过滤', () => {
    assert(
      !modalSource.includes(".filter((item) => item.localSupplierCode !== '200')"),
      '供应商列表不应再过滤 localSupplierCode 为 200 的数据',
    )
  })
  if (supplierFilterFailure) failures.push(supplierFilterFailure)

  const queryShapeFailure = await runTest('列表查询仍保留搜索、分页与供应商筛选参数结构', () => {
    assert(
      modalSource.includes('page: nextPage') &&
        modalSource.includes('pageSize: nextPageSize') &&
        modalSource.includes('globalSearch: nextSearchText.trim() || undefined') &&
        modalSource.includes('filters: nextSupplierCode ? { localSupplierCode: [nextSupplierCode] } : undefined'),
      '列表查询参数结构被意外改动',
    )
  })
  if (queryShapeFailure) failures.push(queryShapeFailure)

  const importPayloadFailure = await runTest('导入动作仍按 productCodes 语义传递选中编码', () => {
    assert(
      modalSource.includes('importNonHotbargainProducts(selectedRowKeys.map(String))'),
      '导入动作应继续以选中 productCodes 发起请求',
    )
  })
  if (importPayloadFailure) failures.push(importPayloadFailure)

  const accessGuardFailure = await runTest('仓库商品页仅对后端允许的导入角色显示非国内商品入口', () => {
    assert(
      pageSource.includes('const canImportNonHbProducts = access.isAdmin || access.isWarehouseManager') &&
        pageSource.includes("{canImportNonHbProducts ? (<Button icon={<UploadOutlined />} onClick={() => setImportNonHbOpen(true)}>") &&
        pageSource.includes("{t('warehouse.importNonHb.title')}") &&
        pageSource.includes('</Button>) : null}'),
      '导入非国内商品按钮应与后端 Admin/WarehouseManager 导入权限保持一致',
    )
  })
  if (accessGuardFailure) failures.push(accessGuardFailure)

  if (failures.length > 0) {
    throw new Error(`共有 ${failures.length} 个测试失败\\n- ${failures.join('\\n- ')}`)
  }

  console.log('importNonHb.logic.test: ok')
}

await main()
