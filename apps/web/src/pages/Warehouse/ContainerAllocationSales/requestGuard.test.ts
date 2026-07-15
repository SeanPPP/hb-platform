import { createLatestRequestGuard } from './requestGuard'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, resolve, reject }
}

const guard = createLatestRequestGuard()
const first = deferred<string>()
const second = deferred<string>()
const writes: string[] = []
const loadingWrites: string[] = []

async function runRequest(promise: Promise<string>) {
  const requestId = guard.begin()
  try {
    const value = await promise
    if (guard.isLatest(requestId)) writes.push(value)
  } finally {
    if (guard.isLatest(requestId)) loadingWrites.push(`done-${requestId}`)
  }
}

const firstRun = runRequest(first.promise)
const secondRun = runRequest(second.promise)
first.resolve('first')
await firstRun
assertEqual(writes.length, 0, '新请求仍在进行时，旧响应不得写入数据')
assertEqual(loadingWrites.length, 0, '新请求仍在进行时，旧 finally 不得关闭 loading')
second.resolve('second')
await secondRun

assertEqual(writes.join(','), 'second', '后发请求应胜出，旧响应不得覆盖最新数据')
assertEqual(loadingWrites.length, 1, '旧请求 finally 不得关闭新请求的 loading')

const invalidated = deferred<string>()
const invalidatedRun = runRequest(invalidated.promise)
guard.invalidate()
invalidated.resolve('hidden-page-result')
await invalidatedRun
assertEqual(writes.includes('hidden-page-result'), false, '页面隐藏或 Drawer 关闭后旧响应必须失效')

console.log('containerAllocationSales.requestGuard.test: ok')
