import {
  buildComparableFilterTokens,
  buildLocationFilterQuery,
  buildTextFilterTokens,
  normalizeLocationTableFilters,
} from './columnFilters'

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

async function main() {
  const failures: string[] = []

  const mappingFailure = await runTest('列头筛选应按接口字段分流', () => {
    const query = buildLocationFilterQuery({
      locationCode: buildTextFilterTokens('contains', 'A-01'),
      locationType: ['1'],
      locationBarcode: buildTextFilterTokens('eq', 'B-02'),
      status: ['0'],
      usage: ['true'],
      productItemNumber: buildTextFilterTokens('starts', 'HB'),
      productBarcode: buildTextFilterTokens('contains', '9300'),
      productName: buildTextFilterTokens('ends', 'Cream'),
      updatedAt: buildComparableFilterTokens('range', { min: '2026-07-01', max: '2026-07-02' }),
      updatedBy: buildTextFilterTokens('contains', 'Sean'),
    })

    assert(query.filters?.locationCode?.[0]?.includes('A-01'), 'locationCode 应保留 token 进入 filters')
    assert(query.filters?.locationType?.[0] === '1', 'locationType 应进入 filters')
    assert(query.filters?.locationBarcode?.[0]?.includes('B-02'), 'locationBarcode 应保留 token 进入 filters')
    assert(query.filters?.status?.[0] === '0', 'status 应进入 filters')
    assert(query.isUsed === true, 'usage 应映射为顶层 isUsed')
    assert(query.filters?.updatedBy?.[0]?.includes('Sean'), 'updatedBy 应保留 token 进入 filters')
    assert(query.filters?.productItemNumber?.[0]?.includes('starts'), '商品货号应使用 filters.productItemNumber')
    assert(query.filters?.productBarcode?.[0]?.includes('9300'), '商品条码应使用 filters.productBarcode')
    assert(query.filters?.productName?.[0]?.includes('Cream'), '商品名称应使用 filters.productName')
    assert(query.filters?.updatedAt?.[0] === 'gte:2026-07-01', '更新时间起始应保留范围 token')
    assert(!query.filters?.usage, 'usage 不能重复进入 filters')
  })
  if (mappingFailure) failures.push(mappingFailure)

  const storageLocationFailure = await runTest('存货位列筛选应按后端约定发送 2', () => {
    const query = buildLocationFilterQuery({
      locationType: ['2'],
    })

    assert(query.filters?.locationType?.[0] === '2', '存货位筛选必须发送 LocationType=2')
  })
  if (storageLocationFailure) failures.push(storageLocationFailure)

  const normalizeFailure = await runTest('AntD 表格 filters 应规范成列过滤状态', () => {
    const filters = normalizeLocationTableFilters({
      locationCode: ['  A-01  '],
      itemNumbers: ['HB-1'],
      productName: null,
    })

    assert(filters.locationCode?.[0] === 'A-01', '文本值应 trim 后保留')
    assert(filters.productItemNumber?.[0] === 'HB-1', '商品货号列 key 应映射为接口 filters.productItemNumber')
    assert(!filters.productName, '空 filters 应被忽略，便于重置清空')
  })
  if (normalizeFailure) failures.push(normalizeFailure)

  const resetFailure = await runTest('空列过滤不应生成 filters 对象', () => {
    const query = buildLocationFilterQuery({})
    assert(query.filters === undefined, '重置后请求不应继续携带旧 filters')
    assert(query.isUsed === undefined && query.status === undefined, '重置后顶层筛选应为空')
  })
  if (resetFailure) failures.push(resetFailure)

  if (failures.length > 0) {
    throw new Error(`共有 ${failures.length} 个测试失败\n- ${failures.join('\n- ')}`)
  }

  console.log('warehouseLocationsColumnFilters.logic.test: ok')
}

await main()
