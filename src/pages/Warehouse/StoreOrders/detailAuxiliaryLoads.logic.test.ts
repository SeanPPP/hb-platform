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
const zhFile = path.resolve(process.cwd(), 'src/i18n/locales/zh.json')
const enFile = path.resolve(process.cwd(), 'src/i18n/locales/en.json')

const detailSource = readFileSync(detailFile, 'utf8')
const zhSource = readFileSync(zhFile, 'utf8')
const enSource = readFileSync(enFile, 'utf8')

async function main() {
  const failures: string[] = []

  const auxiliaryWarningFailure = await runTest('分店下拉加载失败应降级为非阻断提示', () => {
    assert(
      detailSource.includes("message.warning(t('storeOrders.detail.loadStoreOptionsFailed'"),
      'loadStores 失败时应使用非阻断 warning 文案，避免误提示整张订货明细失败',
    )
    assert(
      !detailSource.includes("message.error(error instanceof Error ? error.message : t('storeOrders.loadStoresFailed'))"),
      'loadStores 失败时不应直接透传后端错误 message',
    )
  })
  if (auxiliaryWarningFailure) failures.push(auxiliaryWarningFailure)

  const translationFailure = await runTest('分店下拉非阻断提示应有中英文文案', () => {
    assert(
      zhSource.includes('"loadStoreOptionsFailed": "分店下拉加载失败，订单明细可继续查看"'),
      '中文文案缺少分店下拉非阻断提示',
    )
    assert(
      enSource.includes('"loadStoreOptionsFailed": "Store selector failed to load. Order details remain available."'),
      '英文文案缺少分店下拉非阻断提示',
    )
  })
  if (translationFailure) failures.push(translationFailure)

  if (failures.length > 0) {
    throw new Error(`共有 ${failures.length} 个测试失败\n- ${failures.join('\n- ')}`)
  }

  console.log('detailAuxiliaryLoads.logic.test: ok')
}

await main()
