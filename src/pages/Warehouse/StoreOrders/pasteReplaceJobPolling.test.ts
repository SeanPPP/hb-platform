import {
  StoreOrderPasteReplacePollingCancelledError,
  StoreOrderPasteReplacePollingTimeoutError,
  createStoreOrderPasteReplaceJobPoller,
} from './pasteReplaceJobPolling'
import type { StoreOrderPasteReplaceJobResult } from '../../../types/storeOrder'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)

  if (actualJson !== expectedJson) {
    throw new Error(`${message}。Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

async function assertRejects(
  execute: () => Promise<unknown>,
  expectedError: new (...args: any[]) => Error,
  message: string,
) {
  try {
    await execute()
  } catch (error) {
    assert(error instanceof expectedError, message)
    return
  }

  throw new Error(`${message}。Expected promise to reject`)
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

type TimeoutTask = {
  id: number
  execute: () => void
  delay: number
  dueAt: number
}

function createFakeTimer() {
  let sequence = 0
  let now = 0
  const tasks = new Map<number, TimeoutTask>()

  return {
    setTimeout: (callback: () => void, delay: number) => {
      const id = sequence + 1
      sequence = id
      tasks.set(id, { id, execute: callback, delay, dueAt: now + delay })
      return id
    },
    clearTimeout: (id: unknown) => {
      if (typeof id === 'number') {
        tasks.delete(id)
      }
    },
    flushNext: () => {
      const next = Array.from(tasks.values()).sort((left, right) => {
        if (left.dueAt !== right.dueAt) {
          return left.dueAt - right.dueAt
        }
        return left.id - right.id
      })[0]
      if (!next) {
        throw new Error('没有可执行的定时任务')
      }
      tasks.delete(next.id)
      now = next.dueAt
      next.execute()
      return next.delay
    },
    pendingCount: () => tasks.size,
  }
}

async function main() {
  const failures: string[] = []

  const successFailure = await runTest('轮询器应持续查询直到 Excel 粘贴导入成功', async () => {
    const timer = createFakeTimer()
    const statuses: StoreOrderPasteReplaceJobResult[] = [
      { jobId: 'job-1', status: 'Queued', message: '已提交' },
      { jobId: 'job-1', status: 'Running', message: '导入中' },
      { jobId: 'job-1', status: 'Succeeded', message: '导入完成', importedCount: 2, skippedCount: 1 },
    ]
    const requestedJobIds: string[] = []

    const poller = createStoreOrderPasteReplaceJobPoller({
      jobId: 'job-1',
      pollIntervalMs: 2000,
      timeoutMs: 30000,
      getJob: async (jobId) => {
        requestedJobIds.push(jobId)
        return statuses.shift()!
      },
      setTimeoutFn: timer.setTimeout,
      clearTimeoutFn: timer.clearTimeout,
    })

    timer.flushNext()
    await Promise.resolve()
    timer.flushNext()
    await Promise.resolve()
    timer.flushNext()

    const result = await poller.promise
    assertDeepEqual(requestedJobIds, ['job-1', 'job-1', 'job-1'], '轮询应持续查询同一个 job')
    assertDeepEqual(result, { jobId: 'job-1', status: 'Succeeded', message: '导入完成', importedCount: 2, skippedCount: 1 }, '轮询应返回成功终态')
    assert(timer.pendingCount() === 0, '成功后应清理所有定时器')
  })
  if (successFailure) failures.push(successFailure)

  const failedFailure = await runTest('轮询器遇到失败终态也应返回结果交给页面展示', async () => {
    const timer = createFakeTimer()
    const poller = createStoreOrderPasteReplaceJobPoller({
      jobId: 'job-failed',
      pollIntervalMs: 2000,
      timeoutMs: 30000,
      getJob: async () => ({ jobId: 'job-failed', status: 'Failed', message: '导入失败' }),
      setTimeoutFn: timer.setTimeout,
      clearTimeoutFn: timer.clearTimeout,
    })

    timer.flushNext()
    const result = await poller.promise

    assertDeepEqual(result, { jobId: 'job-failed', status: 'Failed', message: '导入失败' }, '失败终态应返回给调用方')
    assert(timer.pendingCount() === 0, '失败终态后应清理定时器')
  })
  if (failedFailure) failures.push(failedFailure)

  const timeoutFailure = await runTest('轮询器超时应抛出专用错误', async () => {
    const timer = createFakeTimer()
    const poller = createStoreOrderPasteReplaceJobPoller({
      jobId: 'job-timeout',
      pollIntervalMs: 2000,
      timeoutMs: 3000,
      getJob: async () => ({ jobId: 'job-timeout', status: 'Running' }),
      setTimeoutFn: timer.setTimeout,
      clearTimeoutFn: timer.clearTimeout,
    })

    timer.flushNext()
    await Promise.resolve()
    timer.flushNext()

    await assertRejects(() => poller.promise, StoreOrderPasteReplacePollingTimeoutError, '超时应抛出 timeout 错误')
    assert(timer.pendingCount() === 0, '超时后应清理定时器')
  })
  if (timeoutFailure) failures.push(timeoutFailure)

  const hangingTimeoutFailure = await runTest('轮询器应在查询挂起时仍按独立时钟超时', async () => {
    const timer = createFakeTimer()
    const poller = createStoreOrderPasteReplaceJobPoller({
      jobId: 'job-hanging',
      pollIntervalMs: 1000,
      timeoutMs: 3000,
      getJob: async () => new Promise<StoreOrderPasteReplaceJobResult>(() => {}),
      setTimeoutFn: timer.setTimeout,
      clearTimeoutFn: timer.clearTimeout,
    })

    timer.flushNext()
    await Promise.resolve()
    timer.flushNext()

    await assertRejects(() => poller.promise, StoreOrderPasteReplacePollingTimeoutError, '查询挂起时也应按 wall-clock 超时')
    assert(timer.pendingCount() === 0, '挂起超时后应清理所有定时器')
  })
  if (hangingTimeoutFailure) failures.push(hangingTimeoutFailure)

  const cancelFailure = await runTest('轮询器停止后应抛出取消错误并清理定时器', async () => {
    const timer = createFakeTimer()
    const poller = createStoreOrderPasteReplaceJobPoller({
      jobId: 'job-cancel',
      pollIntervalMs: 2000,
      timeoutMs: 30000,
      getJob: async () => ({ jobId: 'job-cancel', status: 'Running' }),
      setTimeoutFn: timer.setTimeout,
      clearTimeoutFn: timer.clearTimeout,
    })

    poller.stop()

    await assertRejects(() => poller.promise, StoreOrderPasteReplacePollingCancelledError, '停止轮询应抛出 cancel 错误')
    assert(timer.pendingCount() === 0, '取消后应清理定时器')
  })
  if (cancelFailure) failures.push(cancelFailure)

  const inflightCancelFailure = await runTest('轮询器停止后进行中的查询返回也不应重新排队', async () => {
    const timer = createFakeTimer()
    let resolveJob: (result: StoreOrderPasteReplaceJobResult) => void = () => {}
    const poller = createStoreOrderPasteReplaceJobPoller({
      jobId: 'job-inflight-cancel',
      pollIntervalMs: 2000,
      timeoutMs: 30000,
      getJob: async () =>
        new Promise<StoreOrderPasteReplaceJobResult>((resolve) => {
          resolveJob = resolve
        }),
      setTimeoutFn: timer.setTimeout,
      clearTimeoutFn: timer.clearTimeout,
    })

    timer.flushNext()
    poller.stop()

    await assertRejects(() => poller.promise, StoreOrderPasteReplacePollingCancelledError, '进行中查询停止后应抛出 cancel 错误')
    resolveJob({ jobId: 'job-inflight-cancel', status: 'Running' })
    await Promise.resolve()

    assert(timer.pendingCount() === 0, '停止后的查询返回不应重新排队')
  })
  if (inflightCancelFailure) failures.push(inflightCancelFailure)

  if (failures.length) {
    throw new Error(failures.join('\n'))
  }

  console.log('pasteReplaceJobPolling.test: ok')
}

main().catch((error) => {
  console.error(error)
  process.exitCode = 1
})
