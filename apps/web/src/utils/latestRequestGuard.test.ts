import {
  createLatestRequestGuard,
  runLatestGuardedRequest,
} from './latestRequestGuard'

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`)
  }
}

function createDeferred<T>() {
  let resolvePromise!: (value: T) => void
  let rejectPromise!: (error: unknown) => void
  const promise = new Promise<T>((resolve, reject) => {
    resolvePromise = resolve
    rejectPromise = reject
  })
  return { promise, resolve: resolvePromise, reject: rejectPromise }
}

const guard = createLatestRequestGuard()
const state = { data: '', page: 0, error: '', loading: false }

function handlers(page: number) {
  return {
    onStart: () => { state.loading = true },
    onSuccess: (data: string) => {
      state.data = data
      state.page = page
    },
    onError: (error: unknown) => { state.error = String(error) },
    onSettled: () => { state.loading = false },
  }
}

const first = createDeferred<string>()
const firstRun = runLatestGuardedRequest(guard, () => first.promise, handlers(1))
const second = createDeferred<string>()
const secondRun = runLatestGuardedRequest(guard, () => second.promise, handlers(2))

first.resolve('stale page 1')
await firstRun
assertEqual(state.data, '', '旧成功响应不能写入数据')
assertEqual(state.page, 0, '旧成功响应不能写入页码')
assertEqual(state.loading, true, '旧 finally 不能关闭最新请求 loading')

second.resolve('latest page 2')
await secondRun
assertEqual(state.data, 'latest page 2', '最新成功响应应写入数据')
assertEqual(state.page, 2, '最新成功响应应写入页码')
assertEqual(state.loading, false, '最新 finally 应关闭 loading')

const staleFailure = createDeferred<string>()
const staleFailureRun = runLatestGuardedRequest(guard, () => staleFailure.promise, handlers(3))
const retry = createDeferred<string>()
const retryRun = runLatestGuardedRequest(guard, () => retry.promise, handlers(4))

staleFailure.reject(new Error('stale failure'))
await staleFailureRun
assertEqual(state.error, '', '旧失败不能写入错误状态')
assertEqual(state.loading, true, '旧失败 finally 不能关闭最新请求 loading')

retry.resolve('latest page 4')
await retryRun
assertEqual(state.data, 'latest page 4', '重试成功应保留最新数据')

const lateOld = createDeferred<string>()
const lateOldRun = runLatestGuardedRequest(guard, () => lateOld.promise, handlers(5))
const earlyLatest = createDeferred<string>()
const earlyLatestRun = runLatestGuardedRequest(guard, () => earlyLatest.promise, handlers(6))

earlyLatest.resolve('early latest page 6')
await earlyLatestRun
lateOld.resolve('late stale page 5')
await lateOldRun
assertEqual(state.data, 'early latest page 6', '晚到旧响应不能覆盖已完成的新响应')
assertEqual(state.page, 6, '晚到旧响应不能覆盖最新页码')

const invalidated = createDeferred<string>()
const invalidatedRun = runLatestGuardedRequest(guard, () => invalidated.promise, handlers(7))
guard.invalidate()
invalidated.resolve('ignored after invalidate')
await invalidatedRun
assertEqual(state.data, 'early latest page 6', 'invalidate 后请求不能写入数据')
assertEqual(state.page, 6, 'invalidate 后请求不能写入页码')
assertEqual(state.loading, true, 'invalidate 后旧 finally 不应写入页面状态')

console.log('latestRequestGuard.test.ts: ok')
