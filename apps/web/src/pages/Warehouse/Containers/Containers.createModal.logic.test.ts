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

const pageFile = path.resolve(process.cwd(), 'src/pages/Warehouse/Containers/index.tsx')
const pageSource = readFileSync(pageFile, 'utf8')

async function main() {
  const failures: string[] = []

  const defaultExchangeRateFailure = await runTest('新建货柜默认汇率应为 4.7', () => {
    assert(
      pageSource.includes('initialValues={{ 汇率: 4.7 }}'),
      '新建货柜表单默认汇率应设置为 4.7',
    )
  })
  if (defaultExchangeRateFailure) failures.push(defaultExchangeRateFailure)

  const estimatedArrivalFailure = await runTest('装柜日期应自动带出四周后的工作日预计到岸日期', () => {
    assert(
      pageSource.includes('getEstimatedArrivalDate') &&
      pageSource.includes("loadingDate.add(4, 'week')"),
      '页面应有按装柜日期加四周计算预计到岸日期的 helper',
    )

    assert(
      pageSource.includes('if (estimatedArrival.day() === 6)') &&
      pageSource.includes("estimatedArrival = estimatedArrival.add(2, 'day')") &&
      pageSource.includes('if (estimatedArrival.day() === 0)') &&
      pageSource.includes("estimatedArrival = estimatedArrival.add(1, 'day')"),
      '预计到岸日期落在周六应顺延到周一，落在周日应顺延到周一',
    )

    assert(
      pageSource.includes('handleLoadingDateChange') &&
      pageSource.includes("form.setFieldsValue({ 预计到岸日期: getEstimatedArrivalDate(value) })") &&
      pageSource.includes('onChange={handleLoadingDateChange}'),
      '装柜日期 DatePicker 改动时应自动写入预计到岸日期',
    )
  })
  if (estimatedArrivalFailure) failures.push(estimatedArrivalFailure)

  if (failures.length > 0) {
    throw new Error(`共有 ${failures.length} 个测试失败\n- ${failures.join('\n- ')}`)
  }

  console.log('Containers.createModal.logic.test: ok')
}

await main()
