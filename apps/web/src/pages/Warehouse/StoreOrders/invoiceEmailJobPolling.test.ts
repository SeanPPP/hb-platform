import {
  StoreOrderInvoiceEmailPollingCancelledError,
  StoreOrderInvoiceEmailPollingTimeoutError,
  createStoreOrderInvoiceEmailJobPoller,
} from './invoiceEmailJobPolling'
import type { StoreOrderInvoiceEmailJobResult } from '../../../types/storeOrder'

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

  const successFailure = await runTest('轮询器应持续查询直到邮件发送成功', async () => {
    const timer = createFakeTimer()
    const statuses: StoreOrderInvoiceEmailJobResult[] = [
      { jobId: 'job-1', status: 'Queued', message: '已提交' },
      { jobId: 'job-1', status: 'Running', message: '发送中' },
      { jobId: 'job-1', status: 'Succeeded', message: '发票邮件发送成功' },
    ]
    const requestedJobIds: string[] = []

    const poller = createStoreOrderInvoiceEmailJobPoller({
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

    const firstDelay = timer.flushNext()
    await Promise.resolve()
    const secondDelay = timer.flushNext()
    await Promise.resolve()
    const thirdDelay = timer.flushNext()
    await Promise.resolve()
    const result = await poller.promise

    assertEqual(firstDelay, 2000, '首次轮询应等待 2 秒')
    assertEqual(secondDelay, 2000, '后续轮询也应等待 2 秒')
    assertEqual(thirdDelay, 2000, '成功前的最后一次轮询也应等待 2 秒')
    assertDeepEqual(requestedJobIds, ['job-1', 'job-1', 'job-1'], '轮询应持续查询同一个 job')
    assertDeepEqual(
      result,
      { jobId: 'job-1', status: 'Succeeded', message: '发票邮件发送成功' },
      '轮询成功后应返回邮件 job 结果',
    )
  })
  if (successFailure) failures.push(successFailure)

  const failedFailure = await runTest('轮询器应把 Failed 作为后端最终状态返回', async () => {
    const timer = createFakeTimer()
    const poller = createStoreOrderInvoiceEmailJobPoller({
      jobId: 'job-2',
      pollIntervalMs: 2000,
      timeoutMs: 30000,
      getJob: async () => ({
        jobId: 'job-2',
        status: 'Failed',
        message: 'SMTP 配置错误',
      }),
      setTimeoutFn: timer.setTimeout,
      clearTimeoutFn: timer.clearTimeout,
    })

    timer.flushNext()
    const result = await poller.promise

    assertDeepEqual(
      result,
      { jobId: 'job-2', status: 'Failed', message: 'SMTP 配置错误' },
      'Failed 应作为后端最终结果透传',
    )
  })
  if (failedFailure) failures.push(failedFailure)

  const timeoutFailure = await runTest('轮询器应在超时时拒绝 promise', async () => {
    const timer = createFakeTimer()
    const poller = createStoreOrderInvoiceEmailJobPoller({
      jobId: 'job-3',
      pollIntervalMs: 2000,
      timeoutMs: 5000,
      getJob: async () => ({ jobId: 'job-3', status: 'Running' }),
      setTimeoutFn: timer.setTimeout,
      clearTimeoutFn: timer.clearTimeout,
    })

    timer.flushNext()
    await Promise.resolve()
    timer.flushNext()
    await Promise.resolve()
    timer.flushNext()

    await assertRejects(
      () => poller.promise,
      StoreOrderInvoiceEmailPollingTimeoutError,
      '超时应返回明确错误类型',
    )
  })
  if (timeoutFailure) failures.push(timeoutFailure)

  const cancelFailure = await runTest('轮询器停止时应清理定时器并拒绝 promise', async () => {
    const timer = createFakeTimer()
    const poller = createStoreOrderInvoiceEmailJobPoller({
      jobId: 'job-4',
      pollIntervalMs: 2000,
      timeoutMs: 30000,
      getJob: async () => ({ jobId: 'job-4', status: 'Running' }),
      setTimeoutFn: timer.setTimeout,
      clearTimeoutFn: timer.clearTimeout,
    })

    poller.stop()

    assertEqual(timer.pendingCount(), 0, '停止轮询应清理所有定时器')
    await assertRejects(
      () => poller.promise,
      StoreOrderInvoiceEmailPollingCancelledError,
      '停止轮询应返回取消错误类型',
    )
  })
  if (cancelFailure) failures.push(cancelFailure)

  if (failures.length > 0) {
    throw new Error(`共有 ${failures.length} 个测试失败\n- ${failures.join('\n- ')}`)
  }

  console.log('invoiceEmailJobPolling.test: ok')
}

await main()
