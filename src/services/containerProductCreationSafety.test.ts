import { upsertForActiveStores as upsertMultiCodeForActiveStores } from './storeMultiCodePriceService'
import { upsertForActiveStores as upsertRetailForActiveStores } from './storeRetailPriceService'
import { batchCreateProducts } from './warehouseProductService'

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

async function withFetch(responseBody: unknown, execute: () => Promise<unknown>) {
  const originalFetch = globalThis.fetch
  globalThis.fetch = (async () => new Response(JSON.stringify(responseBody), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })) as typeof fetch

  try {
    await execute()
  } finally {
    globalThis.fetch = originalFetch
  }
}

async function assertRejects(execute: () => Promise<unknown>, expectedMessage: string) {
  try {
    await execute()
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error)
    assert(message.includes(expectedMessage), `错误信息应包含 ${expectedMessage}`)
    return
  }

  throw new Error('预期业务失败抛错，但实际成功')
}

async function main() {
  const failures: string[] = []

  const warehouseFailure = await runTest('仓库批量创建 success false 应抛出业务错误', async () => {
    await assertRejects(
      () => withFetch(
        { success: false, message: '仓库批量创建失败' },
        () => batchCreateProducts([{ ProductCode: 'P001' }]),
      ),
      '仓库批量创建失败',
    )
  })
  if (warehouseFailure) failures.push(warehouseFailure)

  const warehouseFailedCountFailure = await runTest('仓库批量创建 FailedCount 大于 0 应抛出业务错误', async () => {
    await assertRejects(
      () => withFetch(
        { success: true, data: { FailedCount: 1, Errors: ['明细失败'] } },
        () => batchCreateProducts([{ ProductCode: 'P001' }]),
      ),
      '明细失败',
    )
  })
  if (warehouseFailedCountFailure) failures.push(warehouseFailedCountFailure)

  const retailFailure = await runTest('门店零售价 upsert Failed 大于 0 应抛出业务错误', async () => {
    await assertRejects(
      () => withFetch(
        { success: true, data: { Success: 1, Failed: 1 } },
        () => upsertRetailForActiveStores([{ ProductCode: 'P001', PurchasePrice: 1 }]),
      ),
      '门店零售价',
    )
  })
  if (retailFailure) failures.push(retailFailure)

  const multiCodeFailure = await runTest('门店多码价格 upsert success false 应抛出业务错误', async () => {
    await assertRejects(
      () => withFetch(
        { success: false, message: '门店多码价格失败', data: { failed: 1 } },
        () => upsertMultiCodeForActiveStores([{ ProductCode: 'P001', PurchasePrice: 1 }]),
      ),
      '门店多码价格失败',
    )
  })
  if (multiCodeFailure) failures.push(multiCodeFailure)

  if (failures.length) {
    throw new Error(failures.join('\n'))
  }
}

main().catch((error) => {
  console.error(error)
  process.exitCode = 1
})
