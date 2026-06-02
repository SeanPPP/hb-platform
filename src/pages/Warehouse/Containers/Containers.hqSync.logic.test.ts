import { readFileSync } from 'node:fs'
import path from 'node:path'

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

const pageFile = path.resolve(process.cwd(), 'src/pages/Warehouse/Containers/index.tsx')
const pageSource = readFileSync(pageFile, 'utf8')

async function main() {
  const failures: string[] = []

  const successRefreshFailure = await runTest('同步成功后才提示成功并刷新第一页', () => {
    assert(
      pageSource.includes('if (success) {') &&
      pageSource.includes('message.success(msg)') &&
      pageSource.includes('await loadData(1, pageSize)'),
      '页面应显式区分成功分支，并在成功后提示成功且刷新第一页',
    )

    assert(
      pageSource.includes("const success = result.isSuccess ?? result.IsSuccess ?? true"),
      '页面应基于同步结果中的 success 字段判断是否成功',
    )
  })
  if (successRefreshFailure) failures.push(successRefreshFailure)

  const errorHandlingFailure = await runTest('同步失败时只展示 error.message 且不刷新', () => {
    assert(
      pageSource.includes("const errorMessage = error instanceof Error ? error.message : t('containers.messages.syncFailed')") &&
      pageSource.includes('message.error(errorMessage)'),
      '页面失败分支应优先展示 error.message，并为非 Error 异常保留兜底文案',
    )

    const loadDataCount = pageSource.split('await loadData(1, pageSize)').length - 1
    assertEqual(loadDataCount, 2, '页面源码中刷新第一页的调用次数应保持为创建成功一次、同步成功一次')
  })
  if (errorHandlingFailure) failures.push(errorHandlingFailure)

  const loadingGuardFailure = await runTest('同步按钮应保留 loading 与 disabled 行为', () => {
    assert(
      pageSource.includes('loading={syncing}') &&
      pageSource.includes('disabled={pushing}'),
      '同步按钮应继续保留 loading 和 disabled 控制',
    )
  })
  if (loadingGuardFailure) failures.push(loadingGuardFailure)

  if (failures.length > 0) {
    throw new Error(`共有 ${failures.length} 个测试失败\n- ${failures.join('\n- ')}`)
  }

  console.log('Containers.hqSync.logic.test: ok')
}

await main()
