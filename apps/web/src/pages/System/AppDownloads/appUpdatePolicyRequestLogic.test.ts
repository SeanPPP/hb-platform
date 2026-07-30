import {
  executeLatestRequestLane,
  LatestRequestLane,
  savePolicyWithConflictReload,
} from './appUpdatePolicyRequestLogic'

interface Deferred<T> {
  promise: Promise<T>
  resolve: (value: T) => void
  reject: (error: unknown) => void
}

function createDeferred<T>(): Deferred<T> {
  let resolve!: (value: T) => void
  let reject!: (error: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, resolve, reject }
}

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}: expected ${String(expected)}, got ${String(actual)}`)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)
  if (actualJson !== expectedJson) {
    throw new Error(`${message}: expected ${expectedJson}, got ${actualJson}`)
  }
}

const latestLane = new LatestRequestLane()
const first = createDeferred<string>()
const second = createDeferred<string>()
const committedValues: string[] = []
const firstSignals: AbortSignal[] = []

const firstRun = executeLatestRequestLane(
  latestLane,
  (signal) => {
    firstSignals.push(signal)
    return first.promise
  },
  (value) => committedValues.push(value),
)
const secondRun = executeLatestRequestLane(
  latestLane,
  () => second.promise,
  (value) => committedValues.push(value),
)

assertEqual(firstSignals[0]?.aborted, true, '同一通道的新请求必须取消旧请求')
second.resolve('new')
assertEqual((await secondRun).status, 'applied', '最新请求应提交结果')
first.resolve('old')
assertEqual((await firstRun).status, 'stale', '晚到的旧请求必须被识别为过期')
assertDeepEqual(committedValues, ['new'], '乱序完成不得让旧请求覆盖最新状态')

const mobileLane = new LatestRequestLane()
const storeLane = new LatestRequestLane()
const mobile = createDeferred<string>()
const mobileSignals: AbortSignal[] = []
const localFailure = new Error('store options unavailable')

const mobileRun = executeLatestRequestLane(
  mobileLane,
  (signal) => {
    mobileSignals.push(signal)
    return mobile.promise
  },
  () => undefined,
)
const storeResult = await executeLatestRequestLane(
  storeLane,
  async () => {
    throw localFailure
  },
  () => undefined,
)

assertEqual(storeResult.status, 'failed', '单通道错误必须保留为局部失败')
assertEqual(
  storeResult.status === 'failed' ? storeResult.error : null,
  localFailure,
  '局部失败必须保留原始错误',
)
assertEqual(mobileSignals[0]?.aborted, false, 'Store options 失败不得取消 Mobile 通道')
mobile.resolve('mobile-policy')
assertEqual((await mobileRun).status, 'applied', '其他通道应继续独立完成')

const conflict = {
  status: 409,
  payload: { errorCode: 'APP_UPDATE_POLICY_VERSION_CONFLICT' },
}
const mutation = createDeferred<void>()
const authoritativeReload = createDeferred<'applied' | 'stale' | 'failed'>()
let mutationCalls = 0
let reloadCalls = 0
const conflictRun = savePolicyWithConflictReload(
  () => {
    mutationCalls += 1
    return mutation.promise
  },
  () => {
    reloadCalls += 1
    return authoritativeReload.promise
  },
  (error) => error === conflict,
)

mutation.reject(conflict)
await Promise.resolve()
assertEqual(mutationCalls, 1, '409 后不得自动重放策略写入')
assertEqual(reloadCalls, 1, '409 后必须且仅需发起一次权威状态重载')
authoritativeReload.resolve('applied')
assertEqual(
  await conflictRun,
  'conflict-reloaded',
  '权威状态成功重载后才能提示已重新加载',
)
assertEqual(mutationCalls, 1, '权威状态重载完成后仍不得自动重放写入')

const failedReloadResult = await savePolicyWithConflictReload(
  async () => {
    throw conflict
  },
  async () => 'failed',
  (error) => error === conflict,
)
assertEqual(
  failedReloadResult,
  'conflict-reload-failed',
  '权威状态加载失败时必须返回准确的失败结果',
)

const supersededReloadResult = await savePolicyWithConflictReload(
  async () => {
    throw conflict
  },
  async () => 'stale',
  (error) => error === conflict,
)
assertEqual(
  supersededReloadResult,
  'conflict-reload-superseded',
  '权威重载被更新请求取代时不得误报为加载失败',
)

console.log('appUpdatePolicyRequestLogic.test.ts: ok')
