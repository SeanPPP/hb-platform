import type { PreorderActivationItem } from '../../types/preorder'
import type { PreorderRequestContext } from './preorderContext'

const STORAGE_PREFIX = 'hb:preorder:pending-draft:'
let fallbackId = 0

export interface PendingPreorderDraft {
  contextKey: string
  savedAtUtc: string
  writeId: string
  ownerId: string
  baseDraftRevision: number
  serverFingerprint: string
  items: Array<{ activationItemGuid: string; packCount: number }>
}

export interface PendingPreorderDraftMetadata {
  ownerId: string
  baseDraftRevision: number
  serverFingerprint: string
  savedAtUtc?: string
}

export interface PendingPreorderDraftOwnerState {
  ownerId: string
  pendingOwnerId: string | null
  pendingWriteId: string | null
}

type DraftStorage = Pick<Storage, 'getItem' | 'setItem' | 'removeItem' | 'length' | 'key'>

function resolveStorage(storage?: DraftStorage) {
  if (storage) return storage
  if (typeof window === 'undefined') return null
  return window.localStorage
}

function createUniqueId(prefix: string) {
  let randomId: string
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    randomId = crypto.randomUUID()
  } else if (typeof crypto !== 'undefined' && typeof crypto.getRandomValues === 'function') {
    const randomValues = new Uint32Array(4)
    crypto.getRandomValues(randomValues)
    randomId = [...randomValues].map((value) => value.toString(36)).join('-')
  } else {
    randomId = `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}-${(++fallbackId).toString(36)}`
  }
  return `${prefix}:${randomId}`
}

export function createPendingPreorderDraftOwnerId() {
  return createUniqueId('page')
}

function getOwnerKeyPrefix(context: Pick<PreorderRequestContext, 'activationGuid' | 'storeCode'>) {
  return `${STORAGE_PREFIX}${encodeURIComponent(context.activationGuid)}:${encodeURIComponent(context.storeCode)}:owner:`
}

function getOwnerStorageKey(
  context: Pick<PreorderRequestContext, 'activationGuid' | 'storeCode'>,
  ownerId: string,
) {
  return `${getOwnerKeyPrefix(context)}${encodeURIComponent(ownerId)}`
}

function parsePendingPreorderDraft(serialized: string | null, expectedContextKey: string) {
  if (!serialized) return null
  const pending = JSON.parse(serialized) as Partial<PendingPreorderDraft>
  if (
    pending.contextKey !== expectedContextKey ||
    typeof pending.writeId !== 'string' || !pending.writeId ||
    typeof pending.ownerId !== 'string' || !pending.ownerId ||
    typeof pending.savedAtUtc !== 'string' || !Number.isFinite(Date.parse(pending.savedAtUtc)) ||
    !Number.isInteger(pending.baseDraftRevision) || pending.baseDraftRevision! < 0 ||
    typeof pending.serverFingerprint !== 'string' ||
    !Array.isArray(pending.items)
  ) return null
  const items = pending.items.filter((item): item is { activationItemGuid: string; packCount: number } => (
    Boolean(item) &&
    typeof item.activationItemGuid === 'string' &&
    Number.isInteger(item.packCount) &&
    item.packCount >= 0
  ))
  if (items.length !== pending.items.length) return null
  return { ...pending, items } as PendingPreorderDraft
}

export function writePendingPreorderDraft(
  context: Pick<PreorderRequestContext, 'activationGuid' | 'storeCode' | 'key'>,
  items: PreorderActivationItem[],
  metadata: PendingPreorderDraftMetadata,
  storage?: DraftStorage,
) {
  try {
    const target = resolveStorage(storage)
    if (!target) return null
    const writeId = createUniqueId(metadata.ownerId)
    const ownerStorageKey = getOwnerStorageKey(context, metadata.ownerId)
    target.setItem(ownerStorageKey, JSON.stringify({
      contextKey: context.key,
      savedAtUtc: metadata.savedAtUtc ?? new Date().toISOString(),
      writeId,
      ownerId: metadata.ownerId,
      baseDraftRevision: metadata.baseDraftRevision,
      serverFingerprint: metadata.serverFingerprint,
      items: items.map((item) => ({
        activationItemGuid: item.activationItemGuid,
        packCount: Math.max(0, Math.floor(item.packCount)),
      })),
    } satisfies PendingPreorderDraft))
    return writeId
  } catch {
    return null
  }
}

export function readPendingPreorderDrafts(
  context: Pick<PreorderRequestContext, 'activationGuid' | 'storeCode' | 'key'>,
  storage?: DraftStorage,
) {
  try {
    const target = resolveStorage(storage)
    if (!target) return []
    const ownerPrefix = getOwnerKeyPrefix(context)
    const candidates: PendingPreorderDraft[] = []
    // 每个页面实例使用独立 key；枚举前缀即可发现全部候选，不维护共享索引。
    for (let index = 0; index < target.length; index += 1) {
      const key = target.key(index)
      if (!key?.startsWith(ownerPrefix)) continue
      const pending = parsePendingPreorderDraft(target.getItem(key), context.key)
      if (
        !pending ||
        key !== getOwnerStorageKey(context, pending.ownerId)
      ) continue
      candidates.push(pending)
    }
    return candidates.sort((left, right) => {
      const timeDifference = Date.parse(right.savedAtUtc) - Date.parse(left.savedAtUtc)
      return timeDifference || right.writeId.localeCompare(left.writeId)
    })
  } catch {
    return []
  }
}

export function clearPendingPreorderDraft(
  context: Pick<PreorderRequestContext, 'activationGuid' | 'storeCode' | 'key'>,
  ownerId: string,
  expectedWriteId: string,
  storage?: DraftStorage,
) {
  try {
    const target = resolveStorage(storage)
    if (!target) return false
    const current = parsePendingPreorderDraft(target.getItem(getOwnerStorageKey(context, ownerId)), context.key)
    if (!current || current.ownerId !== ownerId || current.writeId !== expectedWriteId) return false
    // owner key 已按页面实例隔离；同步校验后删除不会影响其他页面，旧 writeId 也删不掉同 owner 的新记录。
    target.removeItem(getOwnerStorageKey(context, ownerId))
    return true
  } catch {
    return false
  }
}

export function replacePendingPreorderDraftWriteForOwner(
  state: PendingPreorderDraftOwnerState,
  writeId: string | null,
) {
  // 每次写入尝试先隔离旧标识；当前 owner 写失败时绝不能回退使用之前恢复的 foreign owner。
  state.pendingOwnerId = writeId ? state.ownerId : null
  state.pendingWriteId = writeId
}

export function clearPendingPreorderDraftForOwner(
  context: Pick<PreorderRequestContext, 'activationGuid' | 'storeCode' | 'key'>,
  state: PendingPreorderDraftOwnerState,
  storage?: DraftStorage,
) {
  if (state.pendingOwnerId !== state.ownerId || !state.pendingWriteId) return false
  const cleared = clearPendingPreorderDraft(context, state.ownerId, state.pendingWriteId, storage)
  if (cleared) {
    state.pendingOwnerId = null
    state.pendingWriteId = null
  }
  return cleared
}

export function resolvePendingPreorderDraftRecovery(
  candidates: PendingPreorderDraft[],
  serverDraftRevision: number,
  serverFingerprint: string,
) {
  if (!candidates.length) {
    return { status: 'none' as const, candidate: null, candidateCount: 0, items: null }
  }
  const pending = candidates[0]
  if (
    candidates.length === 1 &&
    pending.baseDraftRevision === serverDraftRevision &&
    pending.serverFingerprint === serverFingerprint
  ) {
    return { status: 'compatible' as const, candidate: pending, candidateCount: 1, items: pending.items }
  }
  // 多候选、版本或服务器基线变化时禁止自动合并，必须由用户明确选择数据来源。
  return {
    status: 'conflict' as const,
    candidate: pending,
    candidateCount: candidates.length,
    items: null,
  }
}

export function mergePendingPreorderDraft(
  serverItems: PreorderActivationItem[],
  pendingItems: Array<{ activationItemGuid: string; packCount: number }>,
) {
  const pendingByGuid = new Map(pendingItems.map((item) => [item.activationItemGuid, item.packCount]))
  return serverItems.map((item) => {
    const packCount = pendingByGuid.get(item.activationItemGuid)
    return packCount === undefined ? item : {
      ...item,
      packCount,
      orderedQuantity: packCount * item.minimumOrderQuantity,
    }
  })
}
