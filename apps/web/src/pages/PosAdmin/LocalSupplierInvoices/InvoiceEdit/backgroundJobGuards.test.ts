import {
  canApplyCheckProductsJobResult,
  canApplyInvoiceJobResult,
} from './backgroundJobGuards'

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

async function main() {
  const failures: string[] = []

  const invoiceGuardFailure = await runTest('后台 job 只应写回提交时的同一张进货单', () => {
    assertEqual(canApplyInvoiceJobResult('invoice-1', 'invoice-1'), true, '同一张进货单应允许写回')
    assertEqual(canApplyInvoiceJobResult('invoice-2', 'invoice-1'), false, '切换到其他进货单后不应写回')
    assertEqual(canApplyInvoiceJobResult(undefined, 'invoice-1'), false, '当前没有进货单时不应写回')
  })
  if (invoiceGuardFailure) failures.push(invoiceGuardFailure)

  const checkGuardFailure = await runTest('商品检测只有同一张进货单且成功时才合并结果', () => {
    assertEqual(
      canApplyCheckProductsJobResult({
        currentInvoiceGuid: 'invoice-1',
        submittedInvoiceGuid: 'invoice-1',
        status: 'Succeeded',
        hasResult: true,
      }),
      true,
      '成功检测结果应写回同一张进货单',
    )
    assertEqual(
      canApplyCheckProductsJobResult({
        currentInvoiceGuid: 'invoice-1',
        submittedInvoiceGuid: 'invoice-1',
        status: 'Failed',
        hasResult: true,
      }),
      false,
      '失败检测即使带 result 也不应污染表格状态',
    )
    assertEqual(
      canApplyCheckProductsJobResult({
        currentInvoiceGuid: 'invoice-2',
        submittedInvoiceGuid: 'invoice-1',
        status: 'Succeeded',
        hasResult: true,
      }),
      false,
      '旧进货单检测完成时不应写回当前进货单',
    )
    assertEqual(
      canApplyCheckProductsJobResult({
        currentInvoiceGuid: 'invoice-1',
        submittedInvoiceGuid: 'invoice-1',
        status: 'Succeeded',
        hasResult: false,
      }),
      false,
      '成功但没有 result 时不应写回',
    )
  })
  if (checkGuardFailure) failures.push(checkGuardFailure)

  if (failures.length) {
    throw new Error(failures.join('\n'))
  }

  console.log('backgroundJobGuards.test: ok')
}

main().catch((error) => {
  console.error(error instanceof Error ? error.message : String(error))
  process.exit(1)
})

