import assert from 'node:assert/strict'
import type { PreorderActivationDetail, PreorderActivationItem } from '../../types/preorder'
import {
  awaitCurrentSaveRequest,
  createDebouncedTask,
  exposeCurrentSaveRequest,
  exposeSaveDrain,
  freezeSaveQueueForSubmission,
  takeNextPendingSave,
  type PreorderSaveRequestResult,
} from './preorderSaveQueue'
import { createKeyedSingleFlight } from '../../services/preorderSingleFlight'
import { runPreorderSubmit } from './preorderSubmitFlow'
import { canStartPreorderSubmission, getPreorderRenderMode, summarizePreorderItems } from './preorderViewModel'
import {
  createPreorderSubmissionObservability,
  measurePreorderSubmitPayload,
  type PreorderSubmissionStage,
} from './preorderObservability'

const createDeferred = <T>() => {
  let resolve!: (value: T) => void
  let reject!: (error: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, resolve, reject }
}

const savedRequestResult = { status: 'saved' } as const
const saveDeferred = createDeferred<PreorderSaveRequestResult<PreorderActivationDetail>>()
const saveQueue = {
  currentRequestPromise: null as Promise<PreorderSaveRequestResult<PreorderActivationDetail>> | null,
  drainPromise: null as Promise<boolean> | null,
}
const exposedSave = exposeCurrentSaveRequest(saveQueue, saveDeferred.promise)
assert.equal(saveQueue.currentRequestPromise, exposedSave, '队列必须暴露当前单次 HTTP PUT Promise')
const drainDeferred = createDeferred<boolean>()
const exposedDrain = exposeSaveDrain(saveQueue, drainDeferred.promise)
assert.equal(saveQueue.drainPromise, exposedDrain)
assert.notEqual(exposedSave, exposedDrain, '单次网络 Promise 与完整 drain Promise 必须分离')
let saveSettled = false
const waitingSave = awaitCurrentSaveRequest(saveQueue).then(() => {
  saveSettled = true
})
await Promise.resolve()
assert.equal(saveSettled, false, '等待逻辑不能轮询或提前越过仍在执行的保存')
saveDeferred.resolve(savedRequestResult)
await waitingSave
assert.equal(saveSettled, true)
assert.equal(saveQueue.currentRequestPromise, null, '当前 PUT 结束后必须只清理同一个 Promise')
assert.equal(saveQueue.drainPromise, exposedDrain, 'PUT 完成不能提前清理 drain Promise')
drainDeferred.resolve(true)
await exposedDrain

const pendingRequestDeferred = createDeferred<PreorderSaveRequestResult<PreorderActivationDetail>>()
const pendingQueue = {
  currentRequestPromise: null as Promise<PreorderSaveRequestResult<PreorderActivationDetail>> | null,
  pending: 'B' as string | null,
  stopAfterCurrentRequest: false,
}
const requestSequence: string[] = ['PUT A']
pendingQueue.currentRequestPromise = pendingRequestDeferred.promise
const frozenRequest = freezeSaveQueueForSubmission(pendingQueue)
assert.equal(pendingQueue.pending, null, '提交冻结后必须立即丢弃待 drain 的 B')
assert.equal(pendingQueue.stopAfterCurrentRequest, true)
pendingRequestDeferred.resolve(savedRequestResult)
assert.equal((await frozenRequest).status, 'saved')
const nextPending = takeNextPendingSave(pendingQueue)
if (nextPending) requestSequence.push(`PUT ${nextPending}`)
requestSequence.push('POST B')
assert.deepEqual(requestSequence, ['PUT A', 'POST B'], 'pending 场景必须严格 PUT A→POST B')

const frozenConflictDetail = {
  items: [],
  draftRevision: 11,
  orderStatus: 'Draft',
} as unknown as PreorderActivationDetail
const conflictPutDeferred = createDeferred<void>()
let frozenPutCount = 0
let frozenDetailGetCount = 0
let frozenPostCount = 0
let coordinatedRevision = 0
const frozenConflictQueue = {
  currentRequestPromise: null as Promise<PreorderSaveRequestResult<PreorderActivationDetail>> | null,
  pending: 'newest-snapshot' as string | null,
  stopAfterCurrentRequest: false,
}
const frozenConflictRequest = (async (): Promise<PreorderSaveRequestResult<PreorderActivationDetail>> => {
  frozenPutCount += 1
  await conflictPutDeferred.promise
  frozenDetailGetCount += 1
  return { status: 'conflict', detail: frozenConflictDetail }
})()
exposeCurrentSaveRequest(frozenConflictQueue, frozenConflictRequest)
const frozenConflictOutcomePromise = freezeSaveQueueForSubmission(frozenConflictQueue)
conflictPutDeferred.resolve()
const frozenConflictOutcome = await frozenConflictOutcomePromise
assert.equal(frozenConflictOutcome.status, 'conflict')
const frozenSubmitOutcome = await runPreorderSubmit({
  initialConflictDetail: frozenConflictOutcome.status === 'conflict' ? frozenConflictOutcome.detail : undefined,
  submit: async () => { frozenPostCount += 1 },
  loadDetail: async () => {
    frozenDetailGetCount += 1
    return frozenConflictDetail
  },
  isConflict: () => true,
  coordinateConflict: (detail) => {
    coordinatedRevision = detail.draftRevision
  },
})
assert.equal(frozenSubmitOutcome, 'coordinated')
assert.deepEqual(
  { put: frozenPutCount, detailGet: frozenDetailGetCount, post: frozenPostCount },
  { put: 1, detailGet: 1, post: 0 },
  'in-flight PUT 冲突必须复用其 detail 进入提交协调，不能 POST、stale PUT 或第二次 GET',
)
assert.equal(coordinatedRevision, 11, '提交协调必须复用第一次 detail GET 的 revision')

const frozenTerminalDetail = {
  ...frozenConflictDetail,
  draftRevision: 12,
  orderStatus: 'Submitted',
} as PreorderActivationDetail
let terminalPostCount = 0
let terminalDetailGetCount = 1
let terminalCoordinateCount = 0
let terminalCallbackRevision = 0
const frozenTerminalOutcome = await runPreorderSubmit({
  initialConflictDetail: frozenTerminalDetail,
  submit: async () => { terminalPostCount += 1 },
  loadDetail: async () => {
    terminalDetailGetCount += 1
    return frozenTerminalDetail
  },
  isConflict: () => true,
  onTerminal: (detail) => { terminalCallbackRevision = detail.draftRevision },
  coordinateConflict: () => { terminalCoordinateCount += 1 },
})
assert.equal(frozenTerminalOutcome, 'terminal')
assert.deepEqual(
  { post: terminalPostCount, detailGet: terminalDetailGetCount, coordinate: terminalCoordinateCount },
  { post: 0, detailGet: 1, coordinate: 0 },
  'in-flight PUT 的唯一 GET 已返回终态时必须直接完成，不得弹草稿冲突或发起额外请求',
)
assert.equal(terminalCallbackRevision, 12)

const failedPutDeferred = createDeferred<PreorderSaveRequestResult<PreorderActivationDetail>>()
const failedPutQueue = {
  currentRequestPromise: null as Promise<PreorderSaveRequestResult<PreorderActivationDetail>> | null,
  pending: 'snapshot' as string | null,
  stopAfterCurrentRequest: false,
}
exposeCurrentSaveRequest(failedPutQueue, failedPutDeferred.promise)
const frozenFailedPut = freezeSaveQueueForSubmission(failedPutQueue)
failedPutDeferred.resolve({ status: 'failed', error: new Error('network') })
assert.equal((await frozenFailedPut).status, 'failed', '非冲突 PUT 失败必须保持 fail-closed，不能继续 POST')

let putCount = 0
const debouncedSave = createDebouncedTask(() => {
  putCount += 1
}, 1)
debouncedSave.cancel()
await new Promise((resolve) => setTimeout(resolve, 5))
assert.equal(putCount, 0, '提交锁定前尚未启动的 debounce 必须取消，不能制造 PUT')

let activeRequestCount = 0
const activeRequest = createDeferred<string>()
const freshActiveRequest = createDeferred<string>()
const activeSingleFlight = createKeyedSingleFlight<string, string>()
const focusRefresh = activeSingleFlight.run('STORE-A', () => {
  activeRequestCount += 1
  return activeRequest.promise
})
const visibilityRefresh = activeSingleFlight.run('STORE-A', () => {
  activeRequestCount += 1
  return Promise.resolve('unexpected')
})
// POST 完成会推进 mutation generation；提交后的刷新不能复用 POST 前已启动的旧 GET。
const submitRefresh = activeSingleFlight.run('STORE-A', () => {
  activeRequestCount += 1
  return freshActiveRequest.promise
}, 1)
const postSubmitFocusRefresh = activeSingleFlight.run('STORE-A', () => {
  activeRequestCount += 1
  return Promise.resolve('unexpected')
}, 1)
assert.equal(focusRefresh, visibilityRefresh)
assert.notEqual(visibilityRefresh, submitRefresh, 'post-commit 刷新不得复用 pre-POST active GET')
assert.equal(submitRefresh, postSubmitFocusRefresh, '同一 fresh epoch 内 focus/visibility 必须继续合并')
assert.equal(activeRequestCount, 2, 'POST 完成后必须启动第二次 active GET')
activeRequest.resolve('stale')
assert.equal(await focusRefresh, 'stale')
freshActiveRequest.resolve('fresh')
assert.equal(await submitRefresh, 'fresh', 'post-commit 成功事实只能来自 fresh epoch')
await activeSingleFlight.run('STORE-B', async () => {
  activeRequestCount += 1
  return 'store-b'
})
assert.equal(activeRequestCount, 3, '不同门店不能错误复用请求')

const item = {
  activationItemGuid: 'item-a',
  productCode: 'product-a',
  itemNumber: '10001',
  productName: 'Tea',
  importPrice: 2,
  retailPrice: 3,
  minimumOrderQuantity: 6,
  sortOrder: 0,
  packCount: 2,
  orderedQuantity: 12,
} satisfies PreorderActivationItem
const conflictDetail = {
  items: [item],
  draftRevision: 9,
  orderStatus: 'Draft',
} as PreorderActivationDetail
let submitCount = 0
let detailGetCount = 0
let coordinateCount = 0
let coordinatedDetail: PreorderActivationDetail | null = null
const conflictResult = await runPreorderSubmit({
  submit: async () => {
    submitCount += 1
    throw new Error('PREORDER_DRAFT_CONFLICT')
  },
  loadDetail: async () => {
    detailGetCount += 1
    return conflictDetail
  },
  isConflict: () => true,
  coordinateConflict: async (detail) => {
    coordinateCount += 1
    coordinatedDetail = detail
  },
})
assert.equal(conflictResult, 'coordinated')
assert.equal(submitCount, 1)
assert.equal(detailGetCount, 1, 'submit 冲突只能读取一次 detail')
assert.equal(coordinateCount, 1)
assert.equal(coordinatedDetail, conflictDetail, '冲突协调必须复用第一次 GET 的 detail 对象')

assert.equal(getPreorderRenderMode(false), 'desktop')
assert.equal(getPreorderRenderMode(true), 'mobile')
assert.equal(canStartPreorderSubmission(true, 1), true, 'autosave in-flight 时提交按钮仍应可进入 Promise 等待路径')
assert.equal(canStartPreorderSubmission(true, 0), false)
assert.equal(canStartPreorderSubmission(false, 1), false)
let visited = 0
const summary = summarizePreorderItems([
  item,
  { ...item, activationItemGuid: 'item-b', packCount: 0, orderedQuantity: 0, importPrice: 4 },
], () => { visited += 1 })
assert.deepEqual(summary, { selectedCount: 1, totalQuantity: 12, totalImportAmount: 24 })
assert.equal(visited, 2, '汇总必须在一次 reduce 中完成，每项只访问一次')

const observedStages: PreorderSubmissionStage[] = []
const observedLogs: unknown[] = []
const requestPayload = {
  storeCode: 'STORE-A',
  expectedDraftRevision: 3,
  confirmNoDemand: false,
  items: [{ activationItemGuid: 'sensitive-item-guid', packCount: 2 }],
}
const requestMetrics = measurePreorderSubmitPayload(requestPayload)
const observability = createPreorderSubmissionObservability({
  submissionId: 'submission-observed',
  activationGuid: 'activation-a',
  storeCode: 'STORE-A',
  action: 'submit',
  initialRequestCounts: { draftPut: 1, submitPost: 0, detailGet: 0, activeGet: 0 },
  ...requestMetrics,
}, (payload) => {
  observedLogs.push(payload)
  observedStages.push(payload.stage)
})
observability.record('confirm')
observability.record('wait-save-start', { hadInFlightSave: true })
observability.record('wait-save-end')
observability.incrementRequest('submitPost')
observability.record('post-start')
observability.record('post-end', { outcome: 'success' })
observability.record('success-feedback')
observability.incrementRequest('activeGet')
observability.record('background-active-refresh-finish', { outcome: 'success' })
assert.deepEqual(observedStages, [
  'confirm',
  'wait-save-start',
  'wait-save-end',
  'post-start',
  'post-end',
  'success-feedback',
  'background-active-refresh-finish',
])
assert.equal(requestMetrics.itemCount, 1)
assert(requestMetrics.requestBodyBytes > 0)
const serializedLogs = JSON.stringify(observedLogs)
assert(!serializedLogs.includes('sensitive-item-guid'), '日志不得包含商品明细')
assert(!serializedLogs.toLowerCase().includes('token'), '日志不得包含 token 字段')
assert.equal((observedLogs[0] as { submissionId?: string }).submissionId, 'submission-observed')
assert.deepEqual(
  (observedLogs[observedLogs.length - 1] as { requestCounts?: unknown }).requestCounts,
  { draftPut: 1, submitPost: 1, detailGet: 0, activeGet: 1 },
)
assert.equal((observedLogs[observedLogs.length - 1] as { requestCount?: number }).requestCount, 3)

const conflictCountLogs: Array<{ requestCounts: Record<string, number> }> = []
const conflictObservability = createPreorderSubmissionObservability({
  submissionId: 'submission-conflict-count',
  activationGuid: 'activation-a',
  storeCode: 'STORE-A',
  action: 'submit',
  initialRequestCounts: { draftPut: 0, submitPost: 0, detailGet: 0, activeGet: 0 },
  ...requestMetrics,
}, (payload) => conflictCountLogs.push(payload))
conflictObservability.incrementRequest('submitPost')
conflictObservability.incrementRequest('detailGet')
conflictObservability.record('post-end', { outcome: 'coordinated' })
assert.deepEqual(conflictCountLogs[0].requestCounts, {
  draftPut: 0,
  submitPost: 1,
  detailGet: 1,
  activeGet: 0,
}, 'submit detail 冲突必须独立记录一次 GET，且正常无 PUT 时 draftPut 为 0')

const originalFetchForTelemetry = globalThis.fetch
const originalConsoleInfo = console.info
let telemetryFetchCount = 0
globalThis.fetch = async () => {
  telemetryFetchCount += 1
  return new Response('{}', { status: 200 })
}
console.info = () => undefined
try {
  const localOnlyObservability = createPreorderSubmissionObservability({
    submissionId: 'submission-local-only',
    activationGuid: 'activation-a',
    storeCode: 'STORE-A',
    action: 'submit',
    initialRequestCounts: { draftPut: 0, submitPost: 0, detailGet: 0, activeGet: 0 },
    ...requestMetrics,
  })
  localOnlyObservability.record('confirm')
  localOnlyObservability.incrementRequest('submitPost')
  localOnlyObservability.record('post-start')
  localOnlyObservability.record('post-end', { outcome: 'success' })
  await Promise.resolve()
  assert.equal(telemetryFetchCount, 0, '本地 telemetry 不得调用 fetch 或污染业务请求数')
} finally {
  globalThis.fetch = originalFetchForTelemetry
  console.info = originalConsoleInfo
}

console.log('preorderPerformance tests passed')
