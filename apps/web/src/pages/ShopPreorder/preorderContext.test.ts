import assert from 'node:assert/strict'
import { createPreorderRequestContext, isSamePreorderRequestContext } from './preorderContext'
import {
  canEditPreorderQuantities,
  createPreorderNoDemandSnapshot,
  getPreorderActivationReadOnlyReason,
  isNoDemandConfirmationMatch,
  isEditablePreorderOrderStatus,
} from './preorderAvailability'
import {
  clearPendingPreorderDraft,
  clearPendingPreorderDraftForOwner,
  mergePendingPreorderDraft,
  readPendingPreorderDrafts,
  replacePendingPreorderDraftWriteForOwner,
  resolvePendingPreorderDraftRecovery,
  writePendingPreorderDraft,
} from './pendingDraft'
import { resolveOnlinePreorderDraftConflict, resolvePreorderSubmitReconciliation } from './onlineDraftConflict'
import {
  consumePreorderContextPersistence,
  markPreorderContextDiscarded,
  preparePreorderNavigation,
} from './navigationGuard'
import { changeStoreAfterDurableLeave, runAfterDurableLeave } from './preorderLeaveContext'
import { resolvePreorderPromptPresentation, resolveShopPreorderNavigation } from './preorderNavigation'
import { beginPostSubmitGateRefresh } from './postSubmitGateRefresh'
import type { PreorderActivationSummary, PreorderActiveResult } from '../../types/preorder'
import { useShopStore } from '../../store/shop'
import type { UserStoreDto } from '../../types/user'

const first = createPreorderRequestContext(1, 'activation-a', 'STORE-A')
const same = createPreorderRequestContext(1, 'activation-a', 'STORE-A')
const newer = createPreorderRequestContext(2, 'activation-a', 'STORE-A')
const otherStore = createPreorderRequestContext(1, 'activation-a', 'STORE-B')

assert.equal(isSamePreorderRequestContext(first, same), true)
assert.equal(isSamePreorderRequestContext(first, newer), false)
assert.equal(isSamePreorderRequestContext(first, otherStore), false)
assert.equal(isSamePreorderRequestContext(null, same), false)

assert.deepEqual(
  resolveShopPreorderNavigation({ storeCode: 'STORE-A', activationGuid: 'activation-a', loading: false, error: false }),
  { action: 'open', activationGuid: 'activation-a' },
  '有待处理批次时导航必须直接进入该批次',
)
assert.deepEqual(
  resolveShopPreorderNavigation({ storeCode: 'STORE-A', activationGuid: null, loading: true, error: false }),
  { action: 'refresh' },
  '查询中点击入口应允许重新检查，不能成为无响应的死入口',
)
assert.deepEqual(
  resolveShopPreorderNavigation({ storeCode: 'STORE-A', activationGuid: null, loading: false, error: false }),
  { action: 'empty' },
  '当前分店没有批次时应给出明确空状态',
)
assert.deepEqual(
  resolveShopPreorderNavigation({ storeCode: null, activationGuid: null, loading: false, error: false }),
  { action: 'select-store' },
)
assert.deepEqual(
  resolvePreorderPromptPresentation({
    storeCode: 'STORE-A',
    activationGuids: [],
    loading: true,
    error: false,
    bypassed: false,
    onPreorderPage: false,
  }),
  { mode: 'checking', key: 'checking:STORE-A' },
  '选定分店后应立即显示检查弹窗，不等待商品页完成加载',
)
assert.deepEqual(
  resolvePreorderPromptPresentation({
    storeCode: 'STORE-A',
    activationGuids: ['activation-a'],
    loading: false,
    error: false,
    bypassed: false,
    onPreorderPage: false,
  }),
  { mode: 'pending', key: 'pending:STORE-A:activation-a' },
)
assert.equal(resolvePreorderPromptPresentation({
  storeCode: 'STORE-A',
  activationGuids: [],
  loading: false,
  error: false,
  bypassed: false,
  onPreorderPage: false,
}).mode, 'hidden')

const activePeriod = {
  status: 'Active' as const,
  startAtUtc: '2026-07-01T00:00:00Z',
  endAtUtc: '2026-08-01T00:00:00Z',
}
assert.equal(getPreorderActivationReadOnlyReason(activePeriod, Date.parse(activePeriod.startAtUtc)), null)
assert.equal(getPreorderActivationReadOnlyReason(activePeriod, Date.parse(activePeriod.endAtUtc)), 'ended')
assert.equal(getPreorderActivationReadOnlyReason({ ...activePeriod, status: 'Closed' }, Date.parse(activePeriod.startAtUtc)), 'closed')
assert.equal(isEditablePreorderOrderStatus('Draft'), true)
assert.equal(isEditablePreorderOrderStatus('ReturnedForRevision'), true)
assert.equal(isEditablePreorderOrderStatus('Submitted'), false)
assert.equal(canEditPreorderQuantities({
  hasDetail: true,
  orderResponded: false,
  hasReadOnlyReason: false,
  submitting: false,
  resolvingConflict: false,
}), true)
assert.equal(canEditPreorderQuantities({
  hasDetail: true,
  orderResponded: false,
  hasReadOnlyReason: false,
  submitting: true,
  resolvingConflict: false,
}), false, 'POST pending 时所有份数编辑必须锁定')
assert.equal(canEditPreorderQuantities({
  hasDetail: true,
  orderResponded: false,
  hasReadOnlyReason: false,
  submitting: false,
  resolvingConflict: true,
}), false, '草稿冲突协调结束前不能继续修改份数')

assert.equal(isNoDemandConfirmationMatch('放弃本次预定', '放弃本次预定'), true)
assert.equal(isNoDemandConfirmationMatch('Abandon this Preorder', 'Abandon this Preorder'), true)
assert.equal(isNoDemandConfirmationMatch('  放弃本次预定  ', '放弃本次预定'), false, '确认短语必须逐字精确匹配')
assert.equal(isNoDemandConfirmationMatch('放弃本次预定。', '放弃本次预定'), false, '多一个标点也不得通过')
assert.equal(isNoDemandConfirmationMatch('Abandon this preorder', 'Abandon this Preorder'), false, '英文大小写也必须逐字一致')

const activation = (activationGuid: string): PreorderActivationSummary => ({
  activationGuid,
  templateGuid: `template-${activationGuid}`,
  templateName: activationGuid,
  sequenceNumber: 1,
  activationNumber: activationGuid,
  startAtUtc: '2026-07-01T00:00:00Z',
  endAtUtc: '2026-08-01T00:00:00Z',
  estimatedArrivalDate: null,
  status: 'Active',
  targetStoreCount: 1,
  submittedCount: 0,
  noDemandCount: 0,
  pendingCount: 1,
  cancelledCount: 0,
})
const createDeferred = <T>() => {
  let resolve!: (value: T) => void
  let reject!: (error: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, resolve, reject }
}
const currentActivation = activation('activation-current')
const nextActivation = activation('activation-next')
const refreshedActivation = activation('activation-refreshed')
const gateWrites: PreorderActiveResult[] = []
const navigations: string[] = []
let currentStoreCode = 'STORE-A'
let refreshWarningCount = 0
let gateRequestVersion = 0
const claimGateRequest = () => {
  gateRequestVersion += 1
  return gateRequestVersion
}
const isGateRequestCurrent = (token: number) => token === gateRequestVersion
const successDeferred = createDeferred<PreorderActiveResult>()
const successRefresh = beginPostSubmitGateRefresh({
  activationGuid: currentActivation.activationGuid,
  storeCode: 'STORE-A',
  knownActivations: [currentActivation, nextActivation],
  loadGate: () => successDeferred.promise,
  getCurrentStoreCode: () => currentStoreCode,
  claimRequestToken: claimGateRequest,
  isRequestCurrent: isGateRequestCurrent,
  setGate: (gate) => gateWrites.push(gate),
  navigate: (path) => navigations.push(path),
  notifyRefreshFailed: () => { refreshWarningCount += 1 },
})
assert.deepEqual(gateWrites, [{
  activations: [nextActivation],
  normalOrderBlocked: true,
  loading: true,
  error: false,
}], '后台刷新未完成时必须立即 fail-closed 并保留其他已知批次')
assert.deepEqual(navigations, ['/shop/preorders/activation-next'], '不得等待刷新才进入下一期')
successDeferred.resolve({ normalOrderBlocked: true, activations: [refreshedActivation] })
assert.equal(await successRefresh, 'success')
assert.deepEqual(gateWrites[gateWrites.length - 1], {
  activations: [refreshedActivation],
  normalOrderBlocked: true,
  loading: false,
  error: false,
})
assert.equal(navigations.length, 1, '后台刷新完成后不得抢占式导航')

const failureWrites: PreorderActiveResult[] = []
let failureWarningCount = 0
assert.equal(await beginPostSubmitGateRefresh({
  activationGuid: currentActivation.activationGuid,
  storeCode: 'STORE-A',
  knownActivations: [currentActivation],
  loadGate: async () => { throw new Error('network') },
  getCurrentStoreCode: () => 'STORE-A',
  claimRequestToken: claimGateRequest,
  isRequestCurrent: isGateRequestCurrent,
  setGate: (gate) => failureWrites.push(gate),
  navigate: () => undefined,
  notifyRefreshFailed: () => { failureWarningCount += 1 },
}), 'failed')
assert.deepEqual(failureWrites[failureWrites.length - 1], { activations: [], normalOrderBlocked: true, loading: false, error: true })
assert.equal(failureWarningCount, 1)

const timeoutDeferred = createDeferred<PreorderActiveResult>()
const timeoutWrites: PreorderActiveResult[] = []
assert.equal(await beginPostSubmitGateRefresh({
  activationGuid: currentActivation.activationGuid,
  storeCode: 'STORE-A',
  knownActivations: [currentActivation],
  loadGate: () => timeoutDeferred.promise,
  getCurrentStoreCode: () => 'STORE-A',
  claimRequestToken: claimGateRequest,
  isRequestCurrent: isGateRequestCurrent,
  setGate: (gate) => timeoutWrites.push(gate),
  navigate: () => undefined,
  notifyRefreshFailed: () => undefined,
  timeoutMs: 1,
}), 'failed')
assert.deepEqual(timeoutWrites[timeoutWrites.length - 1], { activations: [], normalOrderBlocked: true, loading: false, error: true })

const staleDeferred = createDeferred<PreorderActiveResult>()
const staleWrites: PreorderActiveResult[] = []
currentStoreCode = 'STORE-A'
const staleRefresh = beginPostSubmitGateRefresh({
  activationGuid: currentActivation.activationGuid,
  storeCode: 'STORE-A',
  knownActivations: [currentActivation],
  loadGate: () => staleDeferred.promise,
  getCurrentStoreCode: () => currentStoreCode,
  claimRequestToken: claimGateRequest,
  isRequestCurrent: isGateRequestCurrent,
  setGate: (gate) => staleWrites.push(gate),
  navigate: () => undefined,
  notifyRefreshFailed: () => { refreshWarningCount += 1 },
})
currentStoreCode = 'STORE-B'
claimGateRequest()
currentStoreCode = 'STORE-A'
claimGateRequest()
staleDeferred.reject(new Error('old STORE-A request failed late'))
assert.equal(await staleRefresh, 'stale')
assert.equal(staleWrites.length, 1, 'A→B→A 后必须丢弃旧 A 刷新回写')
assert.equal(refreshWarningCount, 0, '旧 A 请求晚到失败也不得提示')

const firstSameStoreDeferred = createDeferred<PreorderActiveResult>()
const secondSameStoreDeferred = createDeferred<PreorderActiveResult>()
const sameStoreWrites: PreorderActiveResult[] = []
const firstSameStoreRefresh = beginPostSubmitGateRefresh({
  activationGuid: currentActivation.activationGuid,
  storeCode: 'STORE-A',
  knownActivations: [currentActivation],
  loadGate: () => firstSameStoreDeferred.promise,
  getCurrentStoreCode: () => 'STORE-A',
  claimRequestToken: claimGateRequest,
  isRequestCurrent: isGateRequestCurrent,
  setGate: (gate) => sameStoreWrites.push(gate),
  navigate: () => undefined,
  notifyRefreshFailed: () => { refreshWarningCount += 1 },
})
const secondSameStoreRefresh = beginPostSubmitGateRefresh({
  activationGuid: currentActivation.activationGuid,
  storeCode: 'STORE-A',
  knownActivations: [currentActivation],
  loadGate: () => secondSameStoreDeferred.promise,
  getCurrentStoreCode: () => 'STORE-A',
  claimRequestToken: claimGateRequest,
  isRequestCurrent: isGateRequestCurrent,
  setGate: (gate) => sameStoreWrites.push(gate),
  navigate: () => undefined,
  notifyRefreshFailed: () => { refreshWarningCount += 1 },
})
secondSameStoreDeferred.resolve({ normalOrderBlocked: false, activations: [] })
assert.equal(await secondSameStoreRefresh, 'success')
firstSameStoreDeferred.resolve({ normalOrderBlocked: true, activations: [refreshedActivation] })
assert.equal(await firstSameStoreRefresh, 'stale')
assert.deepEqual(sameStoreWrites[sameStoreWrites.length - 1], {
  activations: [], normalOrderBlocked: false, loading: false, error: false,
}, '同店双请求逆序返回时只允许最新请求回写')

const layoutToken = claimGateRequest()
const postSubmitDeferred = createDeferred<PreorderActiveResult>()
const postSubmitRefresh = beginPostSubmitGateRefresh({
  activationGuid: currentActivation.activationGuid,
  storeCode: 'STORE-A',
  knownActivations: [currentActivation],
  loadGate: () => postSubmitDeferred.promise,
  getCurrentStoreCode: () => 'STORE-A',
  claimRequestToken: claimGateRequest,
  isRequestCurrent: isGateRequestCurrent,
  setGate: () => undefined,
  navigate: () => undefined,
  notifyRefreshFailed: () => undefined,
})
assert.equal(isGateRequestCurrent(layoutToken), false, 'post-submit 必须使共享 token 中的 ShopLayout 旧请求失效')
postSubmitDeferred.resolve({ normalOrderBlocked: false, activations: [] })
assert.equal(await postSubmitRefresh, 'success')

const storeA = { storeCode: 'STORE-A' } as UserStoreDto
const storeB = { storeCode: 'STORE-B' } as UserStoreDto
const shopStore = useShopStore.getState()
const initialRequestVersion = shopStore.preorderGateRequestVersion
shopStore.setSelectedStore(storeA)
const afterSelectA = useShopStore.getState().preorderGateRequestVersion
shopStore.setSelectedStore(storeB)
shopStore.setSelectedStore(storeA)
const afterRoundTrip = useShopStore.getState().preorderGateRequestVersion
assert(afterSelectA > initialRequestVersion)
assert(afterRoundTrip > afterSelectA, '切店 A→B→A 必须立即使旧 token 失效')
useShopStore.getState().setUserStores([storeB])
const afterUserStoresChange = useShopStore.getState().preorderGateRequestVersion
assert(afterUserStoresChange > afterRoundTrip, 'setUserStores 改变当前分店时必须使旧 token 失效')
useShopStore.getState().reset()
assert(useShopStore.getState().preorderGateRequestVersion > afterUserStoresChange, 'reset 必须单调递增，不得归零')

const values = new Map<string, string>()
const storage = {
  get length() { return values.size },
  key: (index: number) => [...values.keys()][index] ?? null,
  getItem: (key: string) => values.get(key) ?? null,
  setItem: (key: string, value: string) => { values.set(key, value) },
  removeItem: (key: string) => { values.delete(key) },
}
const draftItems = [{
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
}]
const noDemandSnapshot = createPreorderNoDemandSnapshot(draftItems)
assert.equal(noDemandSnapshot[0].packCount, 0)
assert.equal(noDemandSnapshot[0].orderedQuantity, 0)
assert.equal(draftItems[0].packCount, 2, '放弃时不得直接篡改页面中的原始数量')
const refreshedServerDraft = {
  draftRevision: 7,
  items: [{ ...draftItems[0], packCount: 5, orderedQuantity: 30 }],
}
const keepLocalResolution = resolveOnlinePreorderDraftConflict(
  refreshedServerDraft,
  [{ ...draftItems[0], packCount: 2, orderedQuantity: 12 }],
  'local',
)
assert.equal(keepLocalResolution.draftRevision, 7, '继续使用本地草稿时必须改用服务器最新 revision')
assert.equal(keepLocalResolution.items[0].packCount, 2)
assert.equal(keepLocalResolution.shouldSave, true)
const useServerResolution = resolveOnlinePreorderDraftConflict(
  refreshedServerDraft,
  [{ ...draftItems[0], packCount: 2, orderedQuantity: 12 }],
  'server',
)
assert.equal(useServerResolution.draftRevision, 7)
assert.equal(useServerResolution.items[0].packCount, 5)
assert.equal(useServerResolution.shouldSave, false)
const terminalServerDraft = {
  ...refreshedServerDraft,
  orderStatus: 'Submitted' as const,
}
const terminalResolution = resolveOnlinePreorderDraftConflict(
  terminalServerDraft,
  [{ ...draftItems[0], packCount: 2, orderedQuantity: 12 }],
  'local',
)
assert.equal(terminalResolution.items[0].packCount, 5, '其他设备已响应时必须强制采用服务器 items')
assert.equal(terminalResolution.shouldSave, false, '终态订单不能再提供本地覆盖保存')
assert.equal(terminalResolution.forcedServer, true)
const returnedResolution = resolveOnlinePreorderDraftConflict(
  { ...refreshedServerDraft, orderStatus: 'ReturnedForRevision' },
  [{ ...draftItems[0], packCount: 2, orderedQuantity: 12 }],
  'local',
)
assert.equal(returnedResolution.items[0].packCount, 2, '退回修改后允许保留当前填写并重新保存')
assert.equal(returnedResolution.shouldSave, true)
assert.equal(returnedResolution.forcedServer, false)
assert.equal(resolvePreorderSubmitReconciliation('Submitted', false), 'terminal')
assert.equal(resolvePreorderSubmitReconciliation('NoDemand', true), 'terminal')
assert.equal(resolvePreorderSubmitReconciliation('ReturnedForRevision', true), 'coordinate')
assert.equal(resolvePreorderSubmitReconciliation('Draft', true), 'coordinate')
assert.equal(resolvePreorderSubmitReconciliation('Draft', false), 'failed')

const unprotectedNavigation = await preparePreorderNavigation({
  persistCurrentOwnerJournal: () => false,
  saveAndDrainRemote: async () => false,
})
assert.deepEqual(unprotectedNavigation, { canLeave: false, protectedBy: null })
const journalProtectedNavigation = await preparePreorderNavigation({
  persistCurrentOwnerJournal: () => true,
  saveAndDrainRemote: async () => false,
})
assert.deepEqual(journalProtectedNavigation, { canLeave: true, protectedBy: 'journal' })
const serverProtectedNavigation = await preparePreorderNavigation({
  persistCurrentOwnerJournal: () => false,
  saveAndDrainRemote: async () => true,
})
assert.deepEqual(serverProtectedNavigation, { canLeave: true, protectedBy: 'server' })
let switchedStoreCode: string | null = null
assert.equal(await changeStoreAfterDurableLeave(
  'STORE-B',
  async () => false,
  (storeCode) => { switchedStoreCode = storeCode },
), false)
assert.equal(switchedStoreCode, null, 'durable leave 失败并选择停留时不能切店')
assert.equal(await changeStoreAfterDurableLeave(
  'STORE-B',
  async () => true,
  (storeCode) => { switchedStoreCode = storeCode },
), true)
assert.equal(switchedStoreCode, 'STORE-B', '服务器/journal 成功或明确放弃后才允许切店')
let logoutExecuted = false
assert.equal(await runAfterDurableLeave(async () => false, async () => { logoutExecuted = true }), false)
assert.equal(logoutExecuted, false, 'durable leave 失败并停留时不得执行 logout/reset')
assert.equal(await runAfterDurableLeave(async () => true, async () => { logoutExecuted = true }), true)
assert.equal(logoutExecuted, true)

const discardedContexts = new Set<string>()
markPreorderContextDiscarded(discardedContexts, 'activation-a:STORE-A')
assert.equal(consumePreorderContextPersistence(discardedContexts, 'activation-a:STORE-B'), true)
assert.equal(consumePreorderContextPersistence(discardedContexts, 'activation-a:STORE-A'), false, '明确放弃后 cleanup 必须跳过持久化')
assert.equal(consumePreorderContextPersistence(discardedContexts, 'activation-a:STORE-A'), true, 'discard 标记只消费一次且必须按上下文隔离')
const ownerAWriteId = writePendingPreorderDraft(first, draftItems, {
  ownerId: 'page-a',
  baseDraftRevision: 2,
  serverFingerprint: 'item-a:0',
  savedAtUtc: '2026-07-18T00:00:00.000Z',
}, storage)
assert.equal(typeof ownerAWriteId, 'string')
const ownerBWriteId = writePendingPreorderDraft(first, [{ ...draftItems[0], packCount: 3 }], {
  ownerId: 'page-b',
  baseDraftRevision: 2,
  serverFingerprint: 'item-a:0',
  savedAtUtc: '2026-07-18T00:01:00.000Z',
}, storage)
assert.equal(typeof ownerBWriteId, 'string')
assert.equal(values.size, 2, 'each owner keeps exactly one independent journal key')
const bothOwners = readPendingPreorderDrafts(first, storage)
assert.deepEqual(bothOwners.map((item) => item.ownerId), ['page-b', 'page-a'])
const multipleRecovery = resolvePendingPreorderDraftRecovery(bothOwners, 2, 'item-a:0')
assert.equal(multipleRecovery.status, 'conflict')
assert.equal(multipleRecovery.items, null)
assert.equal(clearPendingPreorderDraft(first, 'page-b', ownerBWriteId!, storage), true)
assert.equal(values.size, 1, 'clearing owner B removes its journal key without leaving markers')
assert.equal(clearPendingPreorderDraft(first, 'page-b', ownerAWriteId!, storage), false)
const ownerAOnly = readPendingPreorderDrafts(first, storage)
assert.deepEqual(ownerAOnly.map((item) => item.ownerId), ['page-a'])
const compatibleRecovery = resolvePendingPreorderDraftRecovery(ownerAOnly, 2, 'item-a:0')
assert.equal(compatibleRecovery.status, 'compatible')
assert.equal(
  mergePendingPreorderDraft(
    [{ ...draftItems[0], packCount: 0, orderedQuantity: 0 }],
    compatibleRecovery.items ?? [],
  )[0].orderedQuantity,
  12,
)
const conflictingRecovery = resolvePendingPreorderDraftRecovery(ownerAOnly, 3, 'item-a:4')
assert.equal(conflictingRecovery.status, 'conflict')
assert.equal(conflictingRecovery.items, null)
const ownerANewerWriteId = writePendingPreorderDraft(first, [{ ...draftItems[0], packCount: 4 }], {
  ownerId: 'page-a',
  baseDraftRevision: 2,
  serverFingerprint: 'item-a:0',
}, storage)
assert.equal(clearPendingPreorderDraft(first, 'page-a', ownerAWriteId!, storage), false)
assert.equal(readPendingPreorderDrafts(first, storage)[0]?.writeId, ownerANewerWriteId)
assert.equal(clearPendingPreorderDraft(first, 'page-a', ownerANewerWriteId!, storage), true)
assert.equal(readPendingPreorderDrafts(first, storage).length, 0)

const foreignWriteId = writePendingPreorderDraft(first, draftItems, {
  ownerId: 'foreign-page',
  baseDraftRevision: 4,
  serverFingerprint: 'item-a:2',
}, storage)
const detachedOwnerState = {
  ownerId: 'current-page',
  pendingOwnerId: 'foreign-page' as string | null,
  pendingWriteId: foreignWriteId,
}
const failingStorage = {
  ...storage,
  setItem: () => { throw new Error('quota exceeded') },
}
const detachedNavigationResult = await preparePreorderNavigation({
  persistCurrentOwnerJournal: () => {
    const failedCurrentWriteId = writePendingPreorderDraft(first, draftItems, {
      ownerId: detachedOwnerState.ownerId,
      baseDraftRevision: 4,
      serverFingerprint: 'item-a:2',
    }, failingStorage)
    replacePendingPreorderDraftWriteForOwner(detachedOwnerState, failedCurrentWriteId)
    return Boolean(failedCurrentWriteId)
  },
  saveAndDrainRemote: async () => {
    // 模拟网络保存成功后的 cleanup；当前 owner 写失败时绝不能回退删除旧 foreign owner。
    clearPendingPreorderDraftForOwner(first, detachedOwnerState, storage)
    return true
  },
})
assert.deepEqual(detachedNavigationResult, { canLeave: true, protectedBy: 'server' })
assert.equal(readPendingPreorderDrafts(first, storage).some((candidate) => candidate.writeId === foreignWriteId), true)
clearPendingPreorderDraft(first, 'foreign-page', foreignWriteId!, storage)

const latestWriteId = writePendingPreorderDraft(first, draftItems, {
  ownerId: 'page-latest',
  baseDraftRevision: 3,
  serverFingerprint: 'item-a:2',
}, storage)
assert.equal(readPendingPreorderDrafts(first, storage)[0]?.writeId, latestWriteId)
writePendingPreorderDraft(first, draftItems, {
  ownerId: 'page-submitted-second',
  baseDraftRevision: 3,
  serverFingerprint: 'item-a:2',
}, storage)
for (const candidate of readPendingPreorderDrafts(first, storage)) {
  clearPendingPreorderDraft(first, candidate.ownerId, candidate.writeId, storage)
}
assert.equal(values.size, 0, 'submitted batch cleanup leaves no journal or marker keys')
console.log('preorderContext tests passed')
