import type { UpdateContainerDetailRequest } from '../../../types/container'
import {
  CONTAINER_DETAIL_DRAFT_TTL_MS,
  CONTAINER_DETAIL_DRAFT_MAX_FUTURE_SKEW_MS,
  buildContainerDetailDraftStorageKey,
  captureContainerDetailDraftFieldVersions,
  captureSuccessfullySavedContainerDetailDraftFieldVersions,
  clearContainerDetailDraftFieldsIfVersionMatches,
  clearContainerDetailDraft,
  createContainerDetailDraftLocateResetPlan,
  clearContainerDetailDraftFailuresForPatches,
  countPendingContainerDetailFields,
  getContainerDetailDraftFieldFailure,
  getContainerDetailDraftExternalApplyMode,
  markContainerDetailDraftSaveFailure,
  mergeContainerDetailDraftNewerFields,
  readContainerDetailDraft,
  reconcileContainerDetailDraftFailures,
  refreshContainerDetailDraftFieldVersions,
  settleContainerDetailDraftSaveSuccess,
  scopeContainerDetailRowsToContainer,
  shouldConsumePendingContainerDetailLocate,
  shouldRetryPendingContainerDetailLocateReset,
  writeContainerDetailDraft,
  type ContainerDetailDraftState,
  type ContainerDetailDraftStorage,
} from './containerDetailDraft'

function assertEqual<T>(actual: T, expected: T, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)
  if (actualJson !== expectedJson) {
    throw new Error(`${label}。Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

class MemoryStorage implements ContainerDetailDraftStorage {
  private values = new Map<string, string>()

  get length() {
    return this.values.size
  }

  key(index: number) {
    return Array.from(this.values.keys())[index] ?? null
  }

  getItem(key: string) {
    return this.values.get(key) ?? null
  }

  setItem(key: string, value: string) {
    this.values.set(key, value)
  }

  removeItem(key: string) {
    this.values.delete(key)
  }
}

const now = Date.UTC(2026, 8, 2, 2, 0, 0)
const storage = new MemoryStorage()
const draftState: ContainerDetailDraftState = {
  pendingPatches: {
    'detail-1': { hguid: 'detail-1', 进口价格: 3.2, 英文名称: 'Canvas Frame' },
    'detail-2': { hguid: 'detail-2', 贴牌价格: 8.99 },
  },
  failures: {
    'detail-1:进口价格': {
      hguid: 'detail-1',
      field: '进口价格',
      code: 'SET_CHILD_COST_RECALCULATION_INCOMPLETE',
      message: '套装子项成本无法完整重算',
    },
  },
}

assertEqual(
  buildContainerDetailDraftStorageKey('user/a', 'container:b'),
  'hb.containerDetailDraft.v2:user%2Fa:container%3Ab',
  '草稿 key 应按用户和货柜隔离并编码特殊字符',
)
assertEqual(
  buildContainerDetailDraftStorageKey('user/a', 'container:b') === buildContainerDetailDraftStorageKey('user/b', 'container:b'),
  false,
  '不同用户的同一货柜不得共用草稿',
)
assertEqual(
  buildContainerDetailDraftStorageKey('user/a', 'container:b') === buildContainerDetailDraftStorageKey('user/a', 'container:c'),
  false,
  '同一用户的不同货柜不得共用草稿',
)

assertEqual(writeContainerDetailDraft(storage, 'user/a', 'container:b', draftState, now), true, '有待保存字段时应写入本地草稿')
const restoredDraft = readContainerDetailDraft(storage, 'user/a', 'container:b', now + 1)
assertDeepEqual(restoredDraft.pendingPatches, draftState.pendingPatches, '刷新或重新登录后应恢复同一用户同一货柜的草稿')
assertDeepEqual(restoredDraft.failures, draftState.failures, '刷新或重新登录后应恢复同一用户同一货柜的失败详情')
assertEqual(Object.keys(restoredDraft.fieldVersions ?? {}).length, 3, '每个待保存字段应有独立版本')

const originalVersions = restoredDraft.fieldVersions ?? {}
const submittedVersionSnapshot = captureContainerDetailDraftFieldVersions(originalVersions, [{ hguid: 'detail-1', 进口价格: 3.2 }])
const newerVersions = refreshContainerDetailDraftFieldVersions(
  draftState.pendingPatches,
  originalVersions,
  [{ hguid: 'detail-1', 进口价格: 3.3 }],
  now + 2,
)
assertEqual(
  newerVersions['detail-1:进口价格'] === submittedVersionSnapshot['detail-1:进口价格'],
  false,
  '同一字段重新编辑必须生成新版本，旧保存响应不能删除它',
)
const sameTimeRefreshVersions = refreshContainerDetailDraftFieldVersions(
  draftState.pendingPatches,
  { 'detail-1:进口价格': `${now}-previous` },
  [{ hguid: 'detail-1', 进口价格: 3.4 }],
  now,
)
assertEqual(
  sameTimeRefreshVersions['detail-1:进口价格'].startsWith(`${now + 1}-`),
  true,
  '同一毫秒重新编辑同字段时版本时间戳也必须单调递增',
)
writeContainerDetailDraft(storage, 'user/a', 'container:b', {
  ...draftState,
  pendingPatches: {
    ...draftState.pendingPatches,
    'detail-1': { hguid: 'detail-1', 进口价格: 3.3, 英文名称: 'Canvas Frame' },
  },
  fieldVersions: newerVersions,
}, now + 2)
const newerVersionClearResult = clearContainerDetailDraftFieldsIfVersionMatches(
  storage,
  'user/a',
  'container:b',
  submittedVersionSnapshot,
)
assertEqual(
  newerVersionClearResult.persisted,
  true,
  '条件清理应完成而不影响其他字段',
)
assertEqual(newerVersionClearResult.hasNewerFieldVersion, true, '其他标签页的新版本必须通知页面重新合并而非静默当作已删除')
assertEqual(
  readContainerDetailDraft(storage, 'user/a', 'container:b', now + 3).pendingPatches['detail-1']?.进口价格,
  3.3,
  '旧快照清理只能在存储版本仍匹配时删除字段',
)
assertDeepEqual(
  readContainerDetailDraft(storage, 'user/b', 'container:b', now + 1),
  { pendingPatches: {}, failures: {}, restored: false },
  '其他用户不应读到当前用户草稿',
)

assertDeepEqual(
  readContainerDetailDraft(storage, 'user/a', 'container:b', now + CONTAINER_DETAIL_DRAFT_TTL_MS + 3),
  { pendingPatches: {}, failures: {}, restored: false },
  '草稿超过七天应静默丢弃',
)
assertEqual(storage.getItem(buildContainerDetailDraftStorageKey('user/a', 'container:b')), null, '过期草稿应从存储中移除')

writeContainerDetailDraft(
  storage,
  'user/a',
  'container:b',
  draftState,
  now + CONTAINER_DETAIL_DRAFT_MAX_FUTURE_SKEW_MS,
)
assertEqual(
  readContainerDetailDraft(storage, 'user/a', 'container:b', now).restored,
  true,
  '五分钟内的轻微系统时钟偏差应允许恢复草稿',
)
writeContainerDetailDraft(
  storage,
  'user/a',
  'container:b',
  draftState,
  now + CONTAINER_DETAIL_DRAFT_MAX_FUTURE_SKEW_MS + 1,
)
assertDeepEqual(
  readContainerDetailDraft(storage, 'user/a', 'container:b', now),
  { pendingPatches: {}, failures: {}, restored: false },
  '超过容忍范围的未来时间戳应视为损坏草稿并丢弃',
)
assertEqual(
  storage.getItem(buildContainerDetailDraftStorageKey('user/a', 'container:b')),
  null,
  '远未来时间戳草稿应从存储中移除',
)

storage.setItem(buildContainerDetailDraftStorageKey('user/a', 'container:b'), '{broken-json')
assertDeepEqual(
  readContainerDetailDraft(storage, 'user/a', 'container:b', now),
  { pendingPatches: {}, failures: {}, restored: false },
  '损坏草稿应静默降级为空状态',
)

const unavailableStorage: ContainerDetailDraftStorage = {
  getItem: () => { throw new Error('storage blocked') },
  setItem: () => { throw new Error('storage blocked') },
  removeItem: () => { throw new Error('storage blocked') },
}
assertEqual(writeContainerDetailDraft(unavailableStorage, 'user/a', 'container:b', draftState, now), false, '本地存储不可用时应静默降级')
assertDeepEqual(
  readContainerDetailDraft(unavailableStorage, 'user/a', 'container:b', now),
  { pendingPatches: {}, failures: {}, restored: false },
  '本地存储读取失败时应返回空草稿',
)

const draftBaseKey = buildContainerDetailDraftStorageKey('user/a', 'container:b')
storage.setItem(`${draftBaseKey}:valid-detail:${encodeURIComponent('进口价格')}`, JSON.stringify({
  schemaVersion: 2,
  updatedAt: now,
  version: 'valid-version',
  hguid: 'valid-detail',
  field: '进口价格',
  value: 4.2,
}))
storage.setItem(`${draftBaseKey}:hguid-only-detail:${encodeURIComponent('进口价格')}`, JSON.stringify({
  schemaVersion: 2,
  updatedAt: now,
  version: 'invalid-version',
  hguid: 'hguid-only-detail',
  field: '进口价格',
  value: 'not-a-number',
}))
assertDeepEqual(
  readContainerDetailDraft(storage, 'user/a', 'container:b', now).pendingPatches,
  { 'valid-detail': { hguid: 'valid-detail', 进口价格: 4.2 } },
  '混合存储数据中只有 hguid 的幽灵补丁应静默丢弃，不得让保存按钮虚假启用',
)

const submittedUpdates: UpdateContainerDetailRequest[] = [
  { hguid: 'detail-1', 进口价格: 3.2, 英文名称: 'Canvas Frame' },
  { hguid: 'detail-2', 贴牌价格: 8.99 },
]
const partialSuccessState = settleContainerDetailDraftSaveSuccess(
  draftState,
  submittedUpdates,
  [{
    hguid: 'detail-1',
    field: '进口价格',
    code: 'SET_CHILD_COST_RECALCULATION_INCOMPLETE',
    message: '套装子项成本无法完整重算',
  }],
)
assertDeepEqual(
  partialSuccessState,
  {
    pendingPatches: {
      'detail-1': { hguid: 'detail-1', 进口价格: 3.2 },
    },
    failures: {
      'detail-1:进口价格': {
        hguid: 'detail-1',
        field: '进口价格',
        code: 'SET_CHILD_COST_RECALCULATION_INCOMPLETE',
        message: '套装子项成本无法完整重算',
      },
    },
  },
  '200 部分成功只应保留失败字段，同行英文名和其他行应清除',
)

const failedState = markContainerDetailDraftSaveFailure(
  draftState,
  submittedUpdates,
  '服务器内部错误，草稿已保留',
)
assertDeepEqual(failedState.pendingPatches, draftState.pendingPatches, '500 失败后应保留本次全部待保存字段')
assertEqual(Object.keys(failedState.failures).length, 3, '500 失败后应标记本次提交的所有字段')
assertEqual(
  getContainerDetailDraftFieldFailure(failedState.failures, 'detail-2', '贴牌价格')?.message,
  '服务器内部错误，草稿已保留',
  '单元格应可读取 500 的具体失败提示',
)

assertDeepEqual(
  clearContainerDetailDraftFailuresForPatches(
    failedState.failures,
    [{ hguid: 'detail-1', 进口价格: 3.5 }],
  ),
  {
    'detail-1:英文名称': failedState.failures['detail-1:英文名称'],
    'detail-2:贴牌价格': failedState.failures['detail-2:贴牌价格'],
  },
  '用户重新编辑字段时只应清除该字段的旧失败提示',
)
assertEqual(countPendingContainerDetailFields(draftState.pendingPatches), 3, '待保存数量应按字段而非按行统计')
const containerARows = [{ hguid: 'container-a-detail' }]
assertDeepEqual(
  scopeContainerDetailRowsToContainer(containerARows, 'container-a', 'container-b'),
  [],
  'A 切换到 B 时必须立即隐藏 A 的旧行，即使 B 已恢复本地草稿且加载失败',
)
assertDeepEqual(
  scopeContainerDetailRowsToContainer(containerARows, 'container-a', 'container-a'),
  containerARows,
  '同一货柜的行应继续显示',
)
assertDeepEqual(
  reconcileContainerDetailDraftFailures(
    {
      'detail-empty:*': {
        hguid: 'detail-empty',
        field: '*',
        code: 'DETAIL_NOT_FOUND',
        message: '明细不存在',
      },
    },
    { 'detail-empty': { hguid: 'detail-empty' } },
  ),
  {},
  '只剩 hguid 的空补丁不得让整行失败提示长期残留',
)

writeContainerDetailDraft(storage, 'user/a', 'container:b', draftState, now)
assertEqual(clearContainerDetailDraft(storage, 'user/a', 'container:b'), true, '显式清空草稿应删除对应本地存储')
assertEqual(storage.getItem(buildContainerDetailDraftStorageKey('user/a', 'container:b')), null, '清空后本地不应留存草稿')

const matchingClearStorage = new MemoryStorage()
writeContainerDetailDraft(matchingClearStorage, 'user/a', 'container:b', draftState, now)
const matchingClearDraft = readContainerDetailDraft(matchingClearStorage, 'user/a', 'container:b', now + 1)
assertEqual(
  clearContainerDetailDraftFieldsIfVersionMatches(
    matchingClearStorage,
    'user/a',
    'container:b',
    captureContainerDetailDraftFieldVersions(matchingClearDraft.fieldVersions, Object.values(matchingClearDraft.pendingPatches)),
  ).persisted,
  true,
  '保存成功的同版本字段应从本地草稿清除',
)
assertDeepEqual(
  readContainerDetailDraft(matchingClearStorage, 'user/a', 'container:b', now + 2).pendingPatches,
  {},
  '保存成功后刷新或重开不应复活已结算字段',
)

const staleStorage = new MemoryStorage()
const staleBaseKey = buildContainerDetailDraftStorageKey('user/a', 'container:b')
const staleVersion = `${now - 1}-stale`
staleStorage.setItem(`${staleBaseKey}:detail-stale:${encodeURIComponent('进口价格')}`, JSON.stringify({
  schemaVersion: 2,
  updatedAt: now - 1,
  version: staleVersion,
  hguid: 'detail-stale',
  field: '进口价格',
  value: 1.2,
}))
const staleClearResult = clearContainerDetailDraftFieldsIfVersionMatches(
  staleStorage,
  'user/a',
  'container:b',
  { 'detail-stale:进口价格': `${now}-saved` },
)
assertEqual(staleClearResult.persisted, true, '旧版本残留应可安全删除')
assertEqual(staleClearResult.removedFieldCount, 1, '保存成功应删除比本次快照更旧的残留，避免刷新复活旧值')
assertEqual(staleClearResult.hasNewerFieldVersion, false, '旧版本残留不得被误判为其他标签页的新编辑')

const sameMillisecondStorage = new MemoryStorage()
sameMillisecondStorage.setItem(`${staleBaseKey}:detail-same-ms:${encodeURIComponent('进口价格')}`, JSON.stringify({
  schemaVersion: 2,
  updatedAt: now,
  version: `${now}-another-tab`,
  hguid: 'detail-same-ms',
  field: '进口价格',
  value: 8.8,
}))
const sameMillisecondResult = clearContainerDetailDraftFieldsIfVersionMatches(
  sameMillisecondStorage,
  'user/a',
  'container:b',
  { 'detail-same-ms:进口价格': `${now}-this-tab` },
)
assertEqual(sameMillisecondResult.removedFieldCount, 0, '同毫秒但随机尾不同的版本不得被误删')
assertEqual(sameMillisecondResult.hasNewerFieldVersion, true, '同毫秒冲突版本必须保留并由页面重新合并')
assertEqual(
  readContainerDetailDraft(sameMillisecondStorage, 'user/a', 'container:b', now + 1).pendingPatches['detail-same-ms']?.进口价格,
  8.8,
  '同毫秒另一标签页的版本刷新后仍可恢复',
)
const mergedNewerDraft = mergeContainerDetailDraftNewerFields(
  {
    pendingPatches: {
      'memory-only-a': { hguid: 'memory-only-a', 进口价格: 2.2 },
      'shared-b': { hguid: 'shared-b', 贴牌价格: 3.3 },
    },
    failures: {
      'memory-only-a:进口价格': { hguid: 'memory-only-a', field: '进口价格', code: 'MEMORY_ONLY', message: '本页未落盘' },
    },
    fieldVersions: {
      'memory-only-a:进口价格': `${now + 5}-memory`,
      'shared-b:贴牌价格': `${now}-old`,
    },
  },
  {
    pendingPatches: { 'shared-b': { hguid: 'shared-b', 贴牌价格: 9.9 } },
    failures: {},
    fieldVersions: { 'shared-b:贴牌价格': `${now + 6}-other-tab` },
  },
  ['shared-b:贴牌价格'],
)
assertEqual(mergedNewerDraft.pendingPatches['memory-only-a']?.进口价格, 2.2, '他页新版本合并时不得丢失本页未落盘字段')
assertEqual(mergedNewerDraft.failures['memory-only-a:进口价格']?.code, 'MEMORY_ONLY', '本页未落盘字段的失败提示必须保留')
assertEqual(mergedNewerDraft.pendingPatches['shared-b']?.贴牌价格, 9.9, '指定他页较新字段应覆盖对应内存旧值')

const removeFailureStorage: ContainerDetailDraftStorage = {
  getItem: () => JSON.stringify({
    schemaVersion: 2,
    updatedAt: now,
    version: `${now}-current`,
    hguid: 'detail-remove-failure',
    field: '进口价格',
    value: 1.2,
  }),
  setItem: () => undefined,
  removeItem: () => { throw new Error('remove blocked') },
  length: 1,
  key: () => `${staleBaseKey}:detail-remove-failure:${encodeURIComponent('进口价格')}`,
}
assertEqual(
  clearContainerDetailDraftFieldsIfVersionMatches(
    removeFailureStorage,
    'user/a',
    'container:b',
    { 'detail-remove-failure:进口价格': `${now}-current` },
  ).persisted,
  false,
  'removeItem 异常必须明确失败，页面据此保留内存草稿和风险提示',
)
assertEqual(
  shouldConsumePendingContainerDetailLocate({
    pendingQueryKey: 'container:unfiltered',
    activeQueryKey: 'container:unfiltered',
    loadedQueryKey: 'container:filtered',
    pendingGeneration: 3,
    loadedGeneration: 2,
    isResetLoading: false,
  }),
  false,
  '旧筛选查询尚未被无筛选 reset 覆盖时不得误消费定位标记',
)
assertEqual(
  shouldConsumePendingContainerDetailLocate({
    pendingQueryKey: 'container:unfiltered',
    activeQueryKey: 'container:unfiltered',
    loadedQueryKey: 'container:unfiltered',
    pendingGeneration: 3,
    loadedGeneration: 3,
    isResetLoading: true,
  }),
  false,
  '无筛选 reset 尚在加载时不得定位旧 render',
)
assertEqual(
  shouldConsumePendingContainerDetailLocate({
    pendingQueryKey: 'container:unfiltered',
    activeQueryKey: 'container:unfiltered',
    loadedQueryKey: 'container:unfiltered',
    pendingGeneration: 3,
    loadedGeneration: 3,
    isResetLoading: false,
  }),
  true,
  '目标无筛选查询成功完成后才可消费定位标记',
)
assertEqual(
  shouldConsumePendingContainerDetailLocate({
    pendingQueryKey: 'container:unfiltered',
    activeQueryKey: 'container:unfiltered',
    loadedQueryKey: 'container:unfiltered',
    pendingGeneration: 4,
    loadedGeneration: 3,
    isResetLoading: false,
  }),
  false,
  '相同 queryKey 的历史成功结果不能替代本次 reset generation',
)
assertDeepEqual(
  createContainerDetailDraftLocateResetPlan({ hasRemoteFilter: true, activeQueryKey: 'remote-filtered', generation: 7 }),
  { awaitingUnfilteredReset: true, queryKey: '', generation: 8 },
  '清除远程筛选必须等待下一代无筛选 reset 完成',
)
assertDeepEqual(
  createContainerDetailDraftLocateResetPlan({ hasRemoteFilter: false, activeQueryKey: 'same-base-query', generation: 7 }),
  { awaitingUnfilteredReset: false, queryKey: 'same-base-query', generation: 7 },
  '仅标签或本地文本筛选不改变远程 query key，不得等待不存在的新 generation',
)
const retryResetPlan = createContainerDetailDraftLocateResetPlan({
  hasRemoteFilter: true,
  activeQueryKey: 'unfiltered',
  generation: 8,
})
assertEqual(retryResetPlan.generation, 9, '首次 reset 失败后重试必须基于当前 generation 重新递增')
assertEqual(
  shouldConsumePendingContainerDetailLocate({
    pendingQueryKey: 'unfiltered',
    activeQueryKey: 'unfiltered',
    loadedQueryKey: 'unfiltered',
    pendingGeneration: retryResetPlan.generation,
    loadedGeneration: 9,
    isResetLoading: false,
  }),
  true,
  '第二次 reset 成功并返回新 generation 后应消费定位标记',
)
assertEqual(
  getContainerDetailDraftExternalApplyMode(
    { pendingPatches: { detail: { hguid: 'detail', 进口价格: 2.2 } }, failures: {} },
    { pendingPatches: {}, failures: {} },
  ),
  'reload',
  '外部清空草稿字段时必须重载服务端行，不能留下旧的受控输入显示',
)
assertEqual(
  getContainerDetailDraftExternalApplyMode(
    { pendingPatches: { detail: { hguid: 'detail', 进口价格: 2.2 } }, failures: {} },
    { pendingPatches: { detail: { hguid: 'detail', 进口价格: 3.3 } }, failures: {} },
  ),
  'patch',
  '外部字段值更新可直接叠加到当前行而无需整表重载',
)
assertEqual(
  shouldRetryPendingContainerDetailLocateReset({ hasLocalFilter: false, awaitingUnfilteredReset: true, hasMatchedReset: false }),
  true,
  '无筛选 reset 失败后用户再次点击定位应主动重试',
)
assertEqual(
  shouldRetryPendingContainerDetailLocateReset({ hasLocalFilter: false, awaitingUnfilteredReset: true, hasMatchedReset: true }),
  false,
  '已成功匹配的 reset 不得重复发起定位重试',
)
const clearedPriceStorage = new MemoryStorage()
const clearedPriceState: ContainerDetailDraftState = {
  pendingPatches: { 'detail-price': { hguid: 'detail-price', 进口价格: 6.6, 贴牌价格: 7.7 } },
  failures: {},
}
writeContainerDetailDraft(clearedPriceStorage, 'user/a', 'container:b', clearedPriceState, now)
const beforeClearPrice = readContainerDetailDraft(clearedPriceStorage, 'user/a', 'container:b', now + 1)
writeContainerDetailDraft(clearedPriceStorage, 'user/a', 'container:b', {
  pendingPatches: { 'detail-price': { hguid: 'detail-price', 贴牌价格: 7.7 } },
  failures: {},
  fieldVersions: beforeClearPrice.fieldVersions,
}, now + 2, false, [{ hguid: 'detail-price', 进口价格: undefined }], {
  'detail-price:进口价格': beforeClearPrice.fieldVersions?.['detail-price:进口价格'] ?? '',
})
assertEqual(
  readContainerDetailDraft(clearedPriceStorage, 'user/a', 'container:b', now + 3).pendingPatches['detail-price']?.进口价格,
  undefined,
  '清空进口价后刷新不得复活旧 localStorage 值',
)
assertEqual(
  readContainerDetailDraft(clearedPriceStorage, 'user/a', 'container:b', now + 3).pendingPatches['detail-price']?.贴牌价格,
  7.7,
  '清空一个字段不得影响同一明细的其他草稿字段',
)
const partialFailureStorage = new MemoryStorage()
writeContainerDetailDraft(partialFailureStorage, 'user/a', 'container:b', clearedPriceState, now)
const partialFailureDraft = readContainerDetailDraft(partialFailureStorage, 'user/a', 'container:b', now + 1)
const partialSuccessVersions = captureSuccessfullySavedContainerDetailDraftFieldVersions(
  partialFailureDraft.fieldVersions,
  [{ hguid: 'detail-price', 进口价格: 6.6, 贴牌价格: 7.7 }],
  [{ hguid: 'detail-price', field: '进口价格', code: 'FAIL', message: '进口价失败' }],
)
clearContainerDetailDraftFieldsIfVersionMatches(partialFailureStorage, 'user/a', 'container:b', partialSuccessVersions)
assertEqual(
  readContainerDetailDraft(partialFailureStorage, 'user/a', 'container:b', now + 2).pendingPatches['detail-price']?.进口价格,
  6.6,
  '部分校验失败字段不得先从持久草稿删除，即使后续重写遇到配额失败也可刷新恢复',
)

const multiTabStorage = new MemoryStorage()
writeContainerDetailDraft(multiTabStorage, 'user/a', 'container:b', draftState, now)
const tabA = readContainerDetailDraft(multiTabStorage, 'user/a', 'container:b', now + 1)
const tabB = readContainerDetailDraft(multiTabStorage, 'user/a', 'container:b', now + 1)
const tabANextPatches = {
  ...tabA.pendingPatches,
  'detail-1': { hguid: 'detail-1', 进口价格: 4.1, 英文名称: 'Canvas Frame' },
}
const tabANextVersions = refreshContainerDetailDraftFieldVersions(
  tabANextPatches,
  tabA.fieldVersions,
  [{ hguid: 'detail-1', 进口价格: 4.1 }],
  now + 2,
)
writeContainerDetailDraft(multiTabStorage, 'user/a', 'container:b', {
  pendingPatches: tabANextPatches,
  failures: tabA.failures,
  fieldVersions: tabANextVersions,
}, now + 2, false, [{ hguid: 'detail-1', 进口价格: 4.1 }])
const tabBNextPatches = {
  ...tabB.pendingPatches,
  'detail-2': { hguid: 'detail-2', 贴牌价格: 9.1 },
}
const tabBNextVersions = refreshContainerDetailDraftFieldVersions(
  tabBNextPatches,
  tabB.fieldVersions,
  [{ hguid: 'detail-2', 贴牌价格: 9.1 }],
  now + 3,
)
writeContainerDetailDraft(multiTabStorage, 'user/a', 'container:b', {
  pendingPatches: tabBNextPatches,
  failures: tabB.failures,
  fieldVersions: tabBNextVersions,
}, now + 3, false, [{ hguid: 'detail-2', 贴牌价格: 9.1 }])
const multiTabRestored = readContainerDetailDraft(multiTabStorage, 'user/a', 'container:b', now + 4)
assertEqual(multiTabRestored.pendingPatches['detail-1']?.进口价格, 4.1, '标签页 B 保存 D2 时不得覆盖标签页 A 已更新的 D1')
assertEqual(multiTabRestored.pendingPatches['detail-2']?.贴牌价格, 9.1, '标签页 B 的 D2 新编辑应与 A 的 D1 一并恢复')
