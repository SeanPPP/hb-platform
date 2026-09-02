import type { UpdateContainerDetailRequest } from '../../../types/container'
import {
  CONTAINER_DETAIL_ENGLISH_NAME_FIELD,
  clearSavedPendingContainerDetailFields,
  mergePendingContainerDetailPatch,
  normalizeContainerDetailEnglishNameForSave,
  type ContainerDetailSaveValidationError,
  type PendingContainerDetailPatch,
  type PendingContainerDetailPatchMap,
} from './containerDetailLogic'

export const CONTAINER_DETAIL_DRAFT_TTL_MS = 7 * 24 * 60 * 60 * 1000
export const CONTAINER_DETAIL_DRAFT_MAX_FUTURE_SKEW_MS = 5 * 60 * 1000
const CONTAINER_DETAIL_DRAFT_SCHEMA_VERSION = 2
const CONTAINER_DETAIL_DRAFT_STORAGE_PREFIX = 'hb.containerDetailDraft.v2'

export interface ContainerDetailDraftStorage {
  getItem: (key: string) => string | null
  setItem: (key: string, value: string) => void
  removeItem: (key: string) => void
  readonly length?: number
  key?: (index: number) => string | null
}

export type ContainerDetailDraftFailureMap = Record<string, ContainerDetailSaveValidationError>

export interface ContainerDetailDraftState {
  pendingPatches: PendingContainerDetailPatchMap
  failures: ContainerDetailDraftFailureMap
  /** 每个待保存字段的写入版本，用于避免旧保存响应删掉新编辑。 */
  fieldVersions?: Record<string, string>
}

export interface RestoredContainerDetailDraft extends ContainerDetailDraftState {
  restored: boolean
}

interface StoredContainerDetailDraftField {
  schemaVersion: number
  updatedAt: number
  version: string
  hguid: string
  field: string
  value: string | number | boolean
  failure?: ContainerDetailSaveValidationError
}

function getFieldVersionKey(hguid: string, field: string) {
  return `${hguid}:${field}`
}

function buildContainerDetailDraftFieldStorageKey(userGuid: string, containerGuid: string, hguid: string, field: string) {
  return `${buildContainerDetailDraftStorageKey(userGuid, containerGuid)}:${encodeURIComponent(hguid)}:${encodeURIComponent(field)}`
}

function enumerateContainerDetailDraftStorageKeys(
  storage: ContainerDetailDraftStorage,
  userGuid: string,
  containerGuid: string,
) {
  if (typeof storage.length !== 'number' || typeof storage.key !== 'function') return []
  const prefix = `${buildContainerDetailDraftStorageKey(userGuid, containerGuid)}:`
  const keys: string[] = []
  for (let index = 0; index < storage.length; index += 1) {
    const key = storage.key(index)
    if (key?.startsWith(prefix)) keys.push(key)
  }
  return keys
}

function getPatchFieldValue(patch: PendingContainerDetailPatch, field: string): string | number | boolean | undefined {
  if (field === '进口价格') return patch.进口价格
  if (field === '贴牌价格') return patch.贴牌价格
  if (field === CONTAINER_DETAIL_ENGLISH_NAME_FIELD) {
    return patch.ClearEnglishName === true ? true : patch.英文名称
  }
  return undefined
}

function makeDraftFieldVersion(updatedAt: number) {
  return `${updatedAt}-${Math.random().toString(36).slice(2, 12)}`
}

export function assignContainerDetailDraftFieldVersions(
  pendingPatches: PendingContainerDetailPatchMap,
  previous: Record<string, string> = {},
  updatedAt = Date.now(),
) {
  return Object.values(pendingPatches).reduce<Record<string, string>>((versions, patch) => {
    getSubmittedFields(patch).forEach((field) => {
      const key = getFieldVersionKey(patch.hguid, field)
      versions[key] = previous[key] ?? makeDraftFieldVersion(updatedAt)
    })
    return versions
  }, {})
}

/** 用户再次编辑同一字段时必须换版本，旧保存响应只能结算它开始时看到的版本。 */
export function refreshContainerDetailDraftFieldVersions(
  pendingPatches: PendingContainerDetailPatchMap,
  previous: Record<string, string> = {},
  changedPatches: PendingContainerDetailPatch[] = [],
  updatedAt = Date.now(),
) {
  const next = assignContainerDetailDraftFieldVersions(pendingPatches, previous, updatedAt)
  changedPatches.forEach((patch) => {
    getSubmittedFields(patch).forEach((field) => {
      const key = getFieldVersionKey(patch.hguid, field)
      // 同字段版本时间戳严格单调递增；同一毫秒编辑也不能与旧保存快照混淆。
      const nextTimestamp = Math.max(updatedAt, getDraftFieldVersionTimestamp(previous[key]) + 1)
      next[key] = makeDraftFieldVersion(nextTimestamp)
    })
  })
  return next
}

export function captureContainerDetailDraftFieldVersions(
  fieldVersions: Record<string, string> | undefined,
  updates: UpdateContainerDetailRequest[],
) {
  return updates.reduce<Record<string, string>>((snapshot, update) => {
    getSubmittedFields(update).forEach((field) => {
      const key = getFieldVersionKey(update.hguid, field)
      if (fieldVersions?.[key]) snapshot[key] = fieldVersions[key]
    })
    return snapshot
  }, {})
}

export function captureSuccessfullySavedContainerDetailDraftFieldVersions(
  fieldVersions: Record<string, string> | undefined,
  updates: UpdateContainerDetailRequest[],
  validationErrors: ContainerDetailSaveValidationError[],
) {
  const failedFields = new Set(validationErrors.flatMap((error) => {
    const update = updates.find((item) => item.hguid === error.hguid)
    if (!update) return []
    return error.field === '*'
      ? getSubmittedFields(update).map((field) => getFieldVersionKey(error.hguid, field))
      : [getFieldVersionKey(error.hguid, error.field)]
  }))
  const all = captureContainerDetailDraftFieldVersions(fieldVersions, updates)
  return Object.fromEntries(Object.entries(all).filter(([key]) => !failedFields.has(key)))
}

export interface ContainerDetailDraftConditionalClearResult {
  persisted: boolean
  removedFieldCount: number
  hasNewerFieldVersion: boolean
  newerFieldVersionKeys: string[]
}

/** 定位草稿只能在目标无筛选查询成功返回后消费，失败时保留标记供用户重试。 */
export function shouldConsumePendingContainerDetailLocate(input: {
  pendingQueryKey: string
  activeQueryKey: string
  loadedQueryKey?: string
  pendingGeneration: number
  loadedGeneration?: number
  isResetLoading: boolean
}) {
  return Boolean(
    input.pendingQueryKey
    && input.pendingQueryKey === input.activeQueryKey
    && input.loadedQueryKey === input.activeQueryKey
    && input.pendingGeneration === input.loadedGeneration
    && !input.isResetLoading,
  )
}

export function createContainerDetailDraftLocateResetPlan(input: {
  hasRemoteFilter: boolean
  activeQueryKey: string
  generation: number
}) {
  return input.hasRemoteFilter
    ? { awaitingUnfilteredReset: true, queryKey: '', generation: input.generation + 1 }
    : { awaitingUnfilteredReset: false, queryKey: input.activeQueryKey, generation: input.generation }
}

export function getContainerDetailDraftExternalApplyMode(
  previous: ContainerDetailDraftState,
  next: ContainerDetailDraftState,
) {
  const previousFields = new Set(Object.keys(assignContainerDetailDraftFieldVersions(
    previous.pendingPatches,
    previous.fieldVersions,
  )))
  const nextFields = new Set(Object.keys(assignContainerDetailDraftFieldVersions(
    next.pendingPatches,
    next.fieldVersions,
  )))
  for (const fieldKey of previousFields) {
    if (!nextFields.has(fieldKey)) return 'reload' as const
  }
  return 'patch' as const
}

export function shouldRetryPendingContainerDetailLocateReset(input: {
  hasLocalFilter: boolean
  awaitingUnfilteredReset: boolean
  hasMatchedReset: boolean
}) {
  return !input.hasLocalFilter && input.awaitingUnfilteredReset && !input.hasMatchedReset
}

function getDraftFieldVersionTimestamp(version?: string) {
  const normalizedVersion = version ?? ''
  const separatorIndex = normalizedVersion.indexOf('-')
  const timestamp = Number(normalizedVersion.slice(0, separatorIndex))
  return Number.isFinite(timestamp) ? timestamp : 0
}

/**
 * 仅删除仍等于本次操作快照版本的字段。另一个标签页已写入新版本时必须保留。
 */
export function clearContainerDetailDraftFieldsIfVersionMatches(
  storage: ContainerDetailDraftStorage | null,
  userGuid: string,
  containerGuid: string,
  fieldVersions: Record<string, string>,
) {
  if (!storage || !userGuid || !containerGuid) {
    return { persisted: false, removedFieldCount: 0, hasNewerFieldVersion: false, newerFieldVersionKeys: [] }
  }
  try {
    let removedFieldCount = 0
    let hasNewerFieldVersion = false
    const newerFieldVersionKeys: string[] = []
    Object.entries(fieldVersions).forEach(([fieldVersionKey, expectedVersion]) => {
      const separatorIndex = fieldVersionKey.lastIndexOf(':')
      if (separatorIndex <= 0) return
      const hguid = fieldVersionKey.slice(0, separatorIndex)
      const field = fieldVersionKey.slice(separatorIndex + 1)
      const key = buildContainerDetailDraftFieldStorageKey(userGuid, containerGuid, hguid, field)
      const raw = storage.getItem(key)
      if (!raw) return
      const stored = JSON.parse(raw) as Partial<StoredContainerDetailDraftField>
      if (stored.version === expectedVersion) {
        storage.removeItem(key)
        removedFieldCount += 1
        return
      }
      // 较旧残留不会代表用户的新编辑，可安全删除；较新版本由其他标签页写入，必须重新合并。
      const expectedTimestamp = getDraftFieldVersionTimestamp(expectedVersion)
      const storedTimestamp = getDraftFieldVersionTimestamp(typeof stored.version === 'string' ? stored.version : '')
      if (storedTimestamp < expectedTimestamp) {
        storage.removeItem(key)
        removedFieldCount += 1
      } else {
        hasNewerFieldVersion = true
        newerFieldVersionKeys.push(fieldVersionKey)
      }
    })
    return { persisted: true, removedFieldCount, hasNewerFieldVersion, newerFieldVersionKeys }
  } catch {
    return { persisted: false, removedFieldCount: 0, hasNewerFieldVersion: false, newerFieldVersionKeys: [] }
  }
}

/**
 * storage 仅作为指定字段的新版本来源；本页未落盘字段必须留在内存，不能整份替换。
 */
export function mergeContainerDetailDraftNewerFields(
  current: ContainerDetailDraftState,
  restored: ContainerDetailDraftState,
  newerFieldVersionKeys: string[],
): ContainerDetailDraftState {
  const pendingPatches = { ...current.pendingPatches }
  const failures = { ...current.failures }
  const fieldVersions = { ...(current.fieldVersions ?? {}) }
  newerFieldVersionKeys.forEach((fieldVersionKey) => {
    const separatorIndex = fieldVersionKey.lastIndexOf(':')
    if (separatorIndex <= 0) return
    const hguid = fieldVersionKey.slice(0, separatorIndex)
    const field = fieldVersionKey.slice(separatorIndex + 1)
    const restoredPatch = restored.pendingPatches[hguid]
    const value = restoredPatch ? getPatchFieldValue(restoredPatch, field) : undefined
    if (value === undefined || !restored.fieldVersions?.[fieldVersionKey]) return
    const nextPatch = { ...(pendingPatches[hguid] ?? { hguid }) }
    if (field === '进口价格') nextPatch.进口价格 = value as number
    if (field === '贴牌价格') nextPatch.贴牌价格 = value as number
    if (field === CONTAINER_DETAIL_ENGLISH_NAME_FIELD) {
      delete nextPatch.英文名称
      delete nextPatch.ClearEnglishName
      if (value === true) nextPatch.ClearEnglishName = true
      else nextPatch.英文名称 = value as string
    }
    pendingPatches[hguid] = nextPatch
    fieldVersions[fieldVersionKey] = restored.fieldVersions[fieldVersionKey]
    delete failures[getFailureKey(hguid, field)]
    delete failures[getFailureKey(hguid, '*')]
    const restoredFailure = restored.failures[getFailureKey(hguid, field)]
    if (restoredFailure) failures[getFailureKey(hguid, field)] = restoredFailure
  })
  return {
    pendingPatches,
    failures: reconcileContainerDetailDraftFailures(failures, pendingPatches),
    fieldVersions,
  }
}

function emptyContainerDetailDraft(): RestoredContainerDetailDraft {
  return { pendingPatches: {}, failures: {}, restored: false }
}

function getSubmittedFields(update: UpdateContainerDetailRequest) {
  const fields: string[] = []
  if ('进口价格' in update) fields.push('进口价格')
  if ('贴牌价格' in update) fields.push('贴牌价格')
  if ('英文名称' in update || update.ClearEnglishName === true) {
    fields.push(CONTAINER_DETAIL_ENGLISH_NAME_FIELD)
  }
  return fields
}

function getFailureKey(hguid: string, field: string) {
  return `${hguid}:${field}`
}

function hasPendingField(patch: PendingContainerDetailPatch | undefined, field: string) {
  if (!patch) return false
  if (field === '进口价格') return patch.进口价格 != null
  if (field === '贴牌价格') return patch.贴牌价格 != null
  if (field === CONTAINER_DETAIL_ENGLISH_NAME_FIELD) {
    return patch.英文名称 !== undefined || patch.ClearEnglishName === true
  }
  return false
}

function isCurrentPendingValue(
  patch: PendingContainerDetailPatch | undefined,
  update: UpdateContainerDetailRequest,
  field: string,
) {
  if (!patch || !hasPendingField(patch, field)) return false
  if (field === '进口价格') return patch.进口价格 === update.进口价格
  if (field === '贴牌价格') return patch.贴牌价格 === update.贴牌价格
  if (field === CONTAINER_DETAIL_ENGLISH_NAME_FIELD) {
    if (update.ClearEnglishName === true) return patch.ClearEnglishName === true
    return patch.英文名称 !== undefined
      && normalizeContainerDetailEnglishNameForSave(patch.英文名称) === update.英文名称
  }
  return false
}

function normalizeStoredFailure(value: unknown): ContainerDetailSaveValidationError | null {
  if (!value || typeof value !== 'object') return null
  const source = value as Record<string, unknown>
  if (
    typeof source.hguid !== 'string'
    || typeof source.field !== 'string'
    || typeof source.code !== 'string'
    || typeof source.message !== 'string'
    || !source.hguid.trim()
    || !source.field.trim()
    || !source.code.trim()
    || !source.message.trim()
  ) {
    return null
  }
  return {
    hguid: source.hguid.trim(),
    field: source.field.trim(),
    code: source.code.trim(),
    message: source.message.trim(),
  }
}

export function reconcileContainerDetailDraftFailures(
  failures: ContainerDetailDraftFailureMap,
  pendingPatches: PendingContainerDetailPatchMap,
) {
  return Object.values(failures).reduce<ContainerDetailDraftFailureMap>((next, failure) => {
    if (failure.field === '*') {
      const patch = pendingPatches[failure.hguid]
      if (patch && countPendingContainerDetailFields({ [failure.hguid]: patch }) > 0) {
        next[getFailureKey(failure.hguid, failure.field)] = failure
      }
      return next
    }
    if (hasPendingField(pendingPatches[failure.hguid], failure.field)) {
      next[getFailureKey(failure.hguid, failure.field)] = failure
    }
    return next
  }, {})
}

export function buildContainerDetailDraftStorageKey(userGuid: string, containerGuid: string) {
  return `${CONTAINER_DETAIL_DRAFT_STORAGE_PREFIX}:${encodeURIComponent(userGuid)}:${encodeURIComponent(containerGuid)}`
}

export function readContainerDetailDraft(
  storage: ContainerDetailDraftStorage | null,
  userGuid: string,
  containerGuid: string,
  now = Date.now(),
): RestoredContainerDetailDraft {
  if (!storage || !userGuid || !containerGuid) return emptyContainerDetailDraft()
  try {
    const pendingPatches: PendingContainerDetailPatchMap = {}
    const failures: ContainerDetailDraftFailureMap = {}
    const fieldVersions: Record<string, string> = {}
    enumerateContainerDetailDraftStorageKeys(storage, userGuid, containerGuid).forEach((key) => {
      const raw = storage.getItem(key)
      if (!raw) return
      try {
        const stored = JSON.parse(raw) as Partial<StoredContainerDetailDraftField>
        const expired = typeof stored.updatedAt !== 'number'
          || !Number.isFinite(stored.updatedAt)
          || now - stored.updatedAt > CONTAINER_DETAIL_DRAFT_TTL_MS
          || stored.updatedAt > now + CONTAINER_DETAIL_DRAFT_MAX_FUTURE_SKEW_MS
        if (
          stored.schemaVersion !== CONTAINER_DETAIL_DRAFT_SCHEMA_VERSION
          || expired
          || typeof stored.version !== 'string'
          || !stored.version
          || typeof stored.hguid !== 'string'
          || !stored.hguid
          || typeof stored.field !== 'string'
          || !stored.field
        ) {
          storage.removeItem(key)
          return
        }
        const patch: PendingContainerDetailPatch = { hguid: stored.hguid }
        if (stored.field === '进口价格' && typeof stored.value === 'number' && Number.isFinite(stored.value)) {
          patch.进口价格 = stored.value
        } else if (stored.field === '贴牌价格' && typeof stored.value === 'number' && Number.isFinite(stored.value)) {
          patch.贴牌价格 = stored.value
        } else if (stored.field === CONTAINER_DETAIL_ENGLISH_NAME_FIELD) {
          if (stored.value === true) patch.ClearEnglishName = true
          else if (typeof stored.value === 'string') patch.英文名称 = stored.value
        }
        if (!hasPendingField(patch, stored.field)) {
          storage.removeItem(key)
          return
        }
        pendingPatches[stored.hguid] = mergePendingContainerDetailPatch(pendingPatches, patch)[stored.hguid]
        fieldVersions[getFieldVersionKey(stored.hguid, stored.field)] = stored.version
        const failure = normalizeStoredFailure(stored.failure)
        if (failure && failure.hguid === stored.hguid && failure.field === stored.field) {
          failures[getFailureKey(failure.hguid, failure.field)] = failure
        }
      } catch {
        storage.removeItem(key)
      }
    })
    const reconciledFailures = reconcileContainerDetailDraftFailures(failures, pendingPatches)
    return countPendingContainerDetailFields(pendingPatches) > 0
      ? { pendingPatches, failures: reconciledFailures, fieldVersions, restored: true }
      : emptyContainerDetailDraft()
  } catch {
    // localStorage 被浏览器策略禁用时仅降级为内存草稿。
    return emptyContainerDetailDraft()
  }
}

export function writeContainerDetailDraft(
  storage: ContainerDetailDraftStorage | null,
  userGuid: string,
  containerGuid: string,
  state: ContainerDetailDraftState,
  updatedAt = Date.now(),
  didRetryAfterExpiryCleanup = false,
  changedPatches?: PendingContainerDetailPatch[],
  removedFieldVersions: Record<string, string> = {},
) {
  if (!storage || !userGuid || !containerGuid) return false
  try {
    const failures = reconcileContainerDetailDraftFailures(state.failures, state.pendingPatches)
    const removedResult = clearContainerDetailDraftFieldsIfVersionMatches(
      storage,
      userGuid,
      containerGuid,
      removedFieldVersions,
    )
    if (!removedResult.persisted) return false
    const versions = assignContainerDetailDraftFieldVersions(state.pendingPatches, state.fieldVersions, updatedAt)
    // 只写本次编辑的字段。否则另一标签页携带的旧内存快照会覆盖较新的字段版本。
    const patchesToWrite = changedPatches ?? Object.values(state.pendingPatches)
    patchesToWrite.forEach((changedPatch) => {
      const patch = state.pendingPatches[changedPatch.hguid]
      if (!patch) return
      getSubmittedFields(changedPatch).forEach((field) => {
        const value = getPatchFieldValue(patch, field)
        if (value === undefined) return
        const fieldKey = getFieldVersionKey(patch.hguid, field)
        const payload: StoredContainerDetailDraftField = {
          schemaVersion: CONTAINER_DETAIL_DRAFT_SCHEMA_VERSION,
          updatedAt,
          version: versions[fieldKey],
          hguid: patch.hguid,
          field,
          value,
          failure: failures[getFailureKey(patch.hguid, field)],
        }
        storage.setItem(
          buildContainerDetailDraftFieldStorageKey(userGuid, containerGuid, patch.hguid, field),
          JSON.stringify(payload),
        )
      })
    })
    return true
  } catch {
    // 配额不足时仅删除本功能中过期记录后重试一次，绝不清理其他业务的 localStorage。
    if (didRetryAfterExpiryCleanup) return false
    try {
      enumerateContainerDetailDraftStorageKeys(storage, userGuid, containerGuid).forEach((key) => {
        const raw = storage.getItem(key)
        if (!raw) return
        const stored = JSON.parse(raw) as Partial<StoredContainerDetailDraftField>
        if (typeof stored.updatedAt !== 'number' || updatedAt - stored.updatedAt > CONTAINER_DETAIL_DRAFT_TTL_MS) {
          storage.removeItem(key)
        }
      })
      return writeContainerDetailDraft(storage, userGuid, containerGuid, state, updatedAt, true, changedPatches, removedFieldVersions)
    } catch {
      return false
    }
  }
}

export function clearContainerDetailDraft(
  storage: ContainerDetailDraftStorage | null,
  userGuid: string,
  containerGuid: string,
) {
  if (!storage || !userGuid || !containerGuid) return false
  try {
    // 兼容移除旧 v1 整体快照，避免升级后残留陈旧草稿。
    storage.removeItem(buildContainerDetailDraftStorageKey(userGuid, containerGuid))
    enumerateContainerDetailDraftStorageKeys(storage, userGuid, containerGuid).forEach((key) => storage.removeItem(key))
    return true
  } catch {
    return false
  }
}

export function countPendingContainerDetailFields(pendingPatches: PendingContainerDetailPatchMap) {
  return Object.values(pendingPatches).reduce((count, patch) => (
    count
    + (patch.进口价格 != null ? 1 : 0)
    + (patch.贴牌价格 != null ? 1 : 0)
    + (patch.英文名称 !== undefined || patch.ClearEnglishName === true ? 1 : 0)
  ), 0)
}

export function scopeContainerDetailRowsToContainer<TRow>(
  rows: TRow[],
  rowsContainerGuid: string,
  currentContainerGuid: string,
) {
  return rowsContainerGuid === currentContainerGuid ? rows : []
}

export function getContainerDetailDraftFieldFailure(
  failures: ContainerDetailDraftFailureMap,
  hguid: string,
  field: string,
) {
  return failures[getFailureKey(hguid, field)] ?? failures[getFailureKey(hguid, '*')]
}

export function clearContainerDetailDraftFailuresForPatches(
  failures: ContainerDetailDraftFailureMap,
  patches: Array<PendingContainerDetailPatch | UpdateContainerDetailRequest>,
) {
  const next = { ...failures }
  patches.forEach((patch) => {
    getSubmittedFields(patch).forEach((field) => {
      delete next[getFailureKey(patch.hguid, field)]
    })
    delete next[getFailureKey(patch.hguid, '*')]
  })
  return next
}

export function settleContainerDetailDraftSaveSuccess(
  current: ContainerDetailDraftState,
  submittedUpdates: UpdateContainerDetailRequest[],
  validationErrors: ContainerDetailSaveValidationError[],
): ContainerDetailDraftState {
  const pendingPatches = clearSavedPendingContainerDetailFields(
    current.pendingPatches,
    submittedUpdates,
    validationErrors,
  )
  let failures = clearContainerDetailDraftFailuresForPatches(current.failures, submittedUpdates)

  validationErrors.forEach((error) => {
    const submittedUpdate = submittedUpdates.find((update) => update.hguid === error.hguid)
    const fields = error.field === '*'
      ? (submittedUpdate ? getSubmittedFields(submittedUpdate) : [])
      : [error.field]
    fields.forEach((field) => {
      const currentPatch = pendingPatches[error.hguid]
      const shouldKeep = submittedUpdate
        ? isCurrentPendingValue(currentPatch, submittedUpdate, field)
        : hasPendingField(currentPatch, field)
      if (!shouldKeep) return
      failures[getFailureKey(error.hguid, field)] = { ...error, field }
    })
  })

  failures = reconcileContainerDetailDraftFailures(failures, pendingPatches)
  return { pendingPatches, failures }
}

export function markContainerDetailDraftSaveFailure(
  current: ContainerDetailDraftState,
  submittedUpdates: UpdateContainerDetailRequest[],
  message: string,
): ContainerDetailDraftState {
  const pendingPatches = { ...current.pendingPatches }
  const failures = clearContainerDetailDraftFailuresForPatches(current.failures, submittedUpdates)
  submittedUpdates.forEach((update) => {
    getSubmittedFields(update).forEach((field) => {
      if (!isCurrentPendingValue(pendingPatches[update.hguid], update, field)) return
      failures[getFailureKey(update.hguid, field)] = {
        hguid: update.hguid,
        field,
        code: 'SAVE_FAILED',
        message,
      }
    })
  })
  return { pendingPatches, failures: reconcileContainerDetailDraftFailures(failures, pendingPatches) }
}
