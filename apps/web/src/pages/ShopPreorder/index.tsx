import { CheckCircleOutlined, MinusOutlined, PlusOutlined, SaveOutlined, StopOutlined } from '@ant-design/icons'
import { Alert, App, Button, Card, Empty, Image, Input, InputNumber, Space, Spin, Table, Tag, Typography } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useBlocker, useNavigate, useParams } from 'react-router-dom'
import {
  advanceActivePreorderFreshEpoch,
  getActivePreorders,
  getShopPreorderActivation,
  isPreorderDraftConflictError,
  createPreorderSubmissionId,
  saveShopPreorderDraft,
  submitShopPreorder,
} from '../../services/preorderService'
import { useShopStore } from '../../store/shop'
import type { PreorderActivationDetail, PreorderActivationItem } from '../../types/preorder'
import {
  createPreorderRequestContext,
  isSamePreorderRequestContext,
  type PreorderRequestContext,
} from './preorderContext'
import {
  canEditPreorderQuantities,
  createPreorderNoDemandSnapshot,
  getPreorderActivationReadOnlyReason,
  isNoDemandConfirmationMatch,
  isEditablePreorderOrderStatus,
} from './preorderAvailability'
import {
  consumePreorderContextPersistence,
  markPreorderContextDiscarded,
  preparePreorderNavigation,
} from './navigationGuard'
import { usePreorderLeave } from './preorderLeaveContext'
import { getPreorderDateDisplay } from './preorderDate'
import {
  resolveOnlinePreorderDraftConflict,
  type OnlinePreorderDraftChoice,
} from './onlineDraftConflict'
import {
  clearPendingPreorderDraft,
  clearPendingPreorderDraftForOwner,
  createPendingPreorderDraftOwnerId,
  mergePendingPreorderDraft,
  readPendingPreorderDrafts,
  replacePendingPreorderDraftWriteForOwner,
  resolvePendingPreorderDraftRecovery,
  writePendingPreorderDraft,
} from './pendingDraft'
import { beginPostSubmitGateRefresh } from './postSubmitGateRefresh'
import {
  awaitSaveDrain,
  createDebouncedTask,
  exposeCurrentSaveRequest,
  exposeSaveDrain,
  freezeSaveQueueForSubmission,
  takeNextPendingSave,
  type PreorderSaveRequestResult,
} from './preorderSaveQueue'
import { runPreorderSubmit } from './preorderSubmitFlow'
import { canStartPreorderSubmission, summarizePreorderItems, usePreorderRenderMode } from './preorderViewModel'
import {
  createPreorderSubmissionObservability,
  measurePreorderSubmitPayload,
  type PreorderSubmissionObservability,
} from './preorderObservability'
import './styles.css'

const { Text, Title } = Typography

type SaveState = 'idle' | 'saving' | 'saved' | 'error'
type PreorderSubmittingAction = 'submit' | 'abandon' | null

interface PreorderSaveQueue {
  context: PreorderRequestContext
  running: boolean
  currentRequestPromise: Promise<PreorderSaveRequestResult<PreorderActivationDetail>> | null
  drainPromise: Promise<boolean> | null
  pending: PreorderActivationItem[] | null
  stopAfterCurrentRequest: boolean
  submissionObservability: PreorderSubmissionObservability | null
  draftRevision: number
  lastSavedSignature: string
  ownerId: string
  pendingOwnerId: string | null
  pendingWriteId: string | null
  cancelConflictResolution: (() => void) | null
  onDetachedSaveError: () => void
}

function getItemsSignature(items: PreorderActivationItem[]) {
  return items.map((item) => `${item.activationItemGuid}:${item.packCount}`).join('|')
}

async function flushDetachedSaveQueue(queue: PreorderSaveQueue, snapshot: PreorderActivationItem[]) {
  // 页面离开前先把最新输入写入本机；即使网络失败，下次进入同一批次仍可恢复。
  const writeId = writePendingPreorderDraft(queue.context, snapshot, {
    ownerId: queue.ownerId,
    baseDraftRevision: queue.draftRevision,
    serverFingerprint: queue.lastSavedSignature,
  })
  replacePendingPreorderDraftWriteForOwner(queue, writeId)
  if (!writeId) {
    queue.onDetachedSaveError()
  }
  if (queue.running) {
    queue.pending = snapshot
    await awaitSaveDrain(queue)
    return
  }
  queue.running = true
  const savePromise = (async () => {
    let nextSnapshot: PreorderActivationItem[] | null = snapshot
    try {
      while (nextSnapshot) {
        const savingSnapshot: PreorderActivationItem[] = nextSnapshot
        queue.pending = null
        const requestPromise = (async (): Promise<PreorderSaveRequestResult<PreorderActivationDetail>> => {
          try {
            queue.submissionObservability?.incrementRequest('draftPut')
            const next = await saveShopPreorderDraft(queue.context.activationGuid, {
              storeCode: queue.context.storeCode,
              expectedDraftRevision: queue.draftRevision,
              items: savingSnapshot.map((item) => ({ activationItemGuid: item.activationItemGuid, packCount: item.packCount })),
            })
            queue.draftRevision = next.draftRevision
            queue.lastSavedSignature = getItemsSignature(next.items)
            return { status: 'saved' }
          } catch (error) {
            return { status: 'failed', error }
          }
        })()
        const requestResult = await exposeCurrentSaveRequest(queue, requestPromise)
        if (requestResult.status !== 'saved') throw new Error('Detached preorder draft save failed')
        nextSnapshot = takeNextPendingSave(queue)
      }
      clearPendingPreorderDraftForOwner(queue.context, queue)
      return true
    } catch {
      // 保留本地待同步草稿，不能因为离页请求失败静默丢弃用户输入。
      queue.onDetachedSaveError()
      return false
    } finally {
      queue.running = false
    }
  })()
  await exposeSaveDrain(queue, savePromise)
}

function clearCurrentOwnerPendingDrafts(context: PreorderRequestContext, queue: PreorderSaveQueue) {
  // 页面 owner 是 journal 的删除边界；其他 tab 即使同分店同批次，也不得被当前页面清理。
  readPendingPreorderDrafts(context)
    .filter((candidate) => candidate.ownerId === queue.ownerId)
    .forEach((candidate) => {
      clearPendingPreorderDraft(context, candidate.ownerId, candidate.writeId)
    })
  if (queue.pendingOwnerId === queue.ownerId) {
    queue.pendingOwnerId = null
    queue.pendingWriteId = null
  }
}

export default function ShopPreorderPage() {
  const { activationGuid = '' } = useParams()
  const navigate = useNavigate()
  const { message, modal } = App.useApp()
  const { registerPreorderDurableLeave } = usePreorderLeave()
  const { t, i18n } = useTranslation()
  const selectedStore = useShopStore((state) => state.selectedStore)
  const setPreorderGate = useShopStore((state) => state.setPreorderGate)
  const [detail, setDetail] = useState<PreorderActivationDetail | null>(null)
  const [items, setItems] = useState<PreorderActivationItem[]>([])
  const [loading, setLoading] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [submittingAction, setSubmittingAction] = useState<PreorderSubmittingAction>(null)
  const [saveState, setSaveState] = useState<SaveState>('idle')
  const [draftConflictResolving, setDraftConflictResolving] = useState(false)
  const [navigationResolving, setNavigationResolving] = useState(false)
  const [activationClock, setActivationClock] = useState(() => Date.now())
  const [keyword, setKeyword] = useState('')
  const [selectedOnly, setSelectedOnly] = useState(false)
  const renderMode = usePreorderRenderMode()
  const itemsRef = useRef(items)
  const contextGenerationRef = useRef(0)
  const activeContextRef = useRef<PreorderRequestContext | null>(null)
  const hydratedContextGenerationRef = useRef(0)
  const saveQueueRef = useRef<PreorderSaveQueue | null>(null)
  const submittingContextRef = useRef<PreorderRequestContext | null>(null)
  const confirmDestroyRef = useRef<(() => void) | null>(null)
  const blockerHandlingRef = useRef(false)
  const discardedContextKeysRef = useRef(new Set<string>())
  const authorizedLeaveContextKeysRef = useRef(new Set<string>())
  const durableLeavePromiseRef = useRef<Promise<boolean> | null>(null)
  const debouncedSaveRef = useRef<{ cancel: () => void } | null>(null)
  const draftOwnerIdRef = useRef('')
  if (!draftOwnerIdRef.current) {
    draftOwnerIdRef.current = createPendingPreorderDraftOwnerId()
  }
  itemsRef.current = items
  const activationReadOnlyReason = getPreorderActivationReadOnlyReason(detail, activationClock)
  const orderResponded = !isEditablePreorderOrderStatus(detail?.orderStatus)
  const returnedForRevision = detail?.orderStatus === 'ReturnedForRevision'
  const isEditable = canEditPreorderQuantities({
    hasDetail: Boolean(detail),
    orderResponded,
    hasReadOnlyReason: Boolean(activationReadOnlyReason),
    submitting,
    resolvingConflict: draftConflictResolving || navigationResolving,
  })
  const routeContextKey = selectedStore?.storeCode ? `${activationGuid}:${selectedStore.storeCode}` : ''
  const contextMatchesRoute = activeContextRef.current?.key === routeContextKey
  const blocker = useBlocker(useCallback(() => {
    const context = activeContextRef.current
    const queue = saveQueueRef.current
    if (
      !context ||
      !queue ||
      hydratedContextGenerationRef.current !== context.generation ||
      !isSamePreorderRequestContext(queue.context, context)
    ) return false
    // 父级切店/退出已完成 durable leave 时，随后触发的 Router 导航只消费一次授权，避免重复协调。
    if (authorizedLeaveContextKeysRef.current.delete(context.key)) return false
    return getItemsSignature(itemsRef.current) !== queue.lastSavedSignature
  }, []))
  const preorderDateTimeFormatter = useMemo(
    () => new Intl.DateTimeFormat(i18n.resolvedLanguage || i18n.language, { dateStyle: 'medium', timeStyle: 'short' }),
    [i18n.language, i18n.resolvedLanguage],
  )

  useEffect(() => {
    if (!detail) return
    const now = Date.now()
    const nextBoundary = [Date.parse(detail.startAtUtc), Date.parse(detail.endAtUtc)]
      .filter((value) => Number.isFinite(value) && value > now)
      .sort((left, right) => left - right)[0]
    if (nextBoundary === undefined) return
    const timer = window.setTimeout(
      () => setActivationClock(Date.now()),
      Math.min(2_147_483_647, Math.max(0, nextBoundary - now + 25)),
    )
    return () => window.clearTimeout(timer)
  }, [activationClock, detail])

  useLayoutEffect(() => {
    contextGenerationRef.current += 1
    const generation = contextGenerationRef.current
    const storeCode = selectedStore?.storeCode
    saveQueueRef.current?.cancelConflictResolution?.()
    debouncedSaveRef.current?.cancel()
    debouncedSaveRef.current = null
    confirmDestroyRef.current?.()
    confirmDestroyRef.current = null
    activeContextRef.current = null
    saveQueueRef.current = null
    hydratedContextGenerationRef.current = 0
    submittingContextRef.current = null
    // 切店或切批次必须先清空旧页面，不能在新请求期间展示或编辑旧上下文。
    setDetail(null)
    setItems([])
    setLoading(Boolean(storeCode))
    setSubmitting(false)
    setSubmittingAction(null)
    setDraftConflictResolving(false)
    setNavigationResolving(false)
    setSaveState('idle')
    if (!storeCode) return

    const context = createPreorderRequestContext(generation, activationGuid, storeCode)
    const queue: PreorderSaveQueue = {
      context,
      running: false,
      currentRequestPromise: null,
      drainPromise: null,
      pending: null,
      stopAfterCurrentRequest: false,
      submissionObservability: null,
      draftRevision: 0,
      lastSavedSignature: '',
      ownerId: draftOwnerIdRef.current,
      pendingOwnerId: null,
      pendingWriteId: null,
      cancelConflictResolution: null,
      onDetachedSaveError: () => message.error(t('shop.preorder.detachedDraftSaveFailed')),
    }
    activeContextRef.current = context
    saveQueueRef.current = queue
    let cancelled = false
    const controller = new AbortController()
    void getShopPreorderActivation(context.activationGuid, context.storeCode, controller.signal)
      .then((next) => {
        if (cancelled || !isSamePreorderRequestContext(activeContextRef.current, context)) return
        const serverSignature = getItemsSignature(next.items)
        const candidates = readPendingPreorderDrafts(context)
        const recovery = isEditablePreorderOrderStatus(next.orderStatus)
          ? resolvePendingPreorderDraftRecovery(candidates, next.draftRevision, serverSignature)
          : null
        const recoveredItems = recovery?.status === 'compatible'
          ? mergePendingPreorderDraft(next.items, recovery.items)
          : next.items
        const recoveredSignature = getItemsSignature(recoveredItems)
        if (!isEditablePreorderOrderStatus(next.orderStatus)) {
          clearCurrentOwnerPendingDrafts(context, queue)
        }
        setDetail(next)
        setItems(recoveredItems)
        queue.lastSavedSignature = serverSignature
        queue.draftRevision = next.draftRevision
        queue.pendingOwnerId = recovery?.status === 'compatible' && recoveredSignature !== serverSignature
          ? recovery.candidate.ownerId
          : null
        queue.pendingWriteId = recovery?.status === 'compatible' && recoveredSignature !== serverSignature
          ? recovery.candidate.writeId
          : null
        if (recovery?.status === 'compatible' && recoveredSignature === serverSignature) {
          if (recovery.candidate.ownerId === queue.ownerId) {
            clearPendingPreorderDraft(context, recovery.candidate.ownerId, recovery.candidate.writeId)
          }
        }
        hydratedContextGenerationRef.current = context.generation
        setSaveState(recovery?.status === 'compatible' && recoveredSignature !== serverSignature ? 'idle' : 'saved')
        if (recovery?.status === 'compatible' && recoveredSignature !== serverSignature) {
          message.info(t('shop.preorder.localDraftRecovered'))
        }
        if (recovery?.status === 'conflict' && recovery.candidate) {
          const selectedCandidate = recovery.candidate
          const confirmation = modal.confirm({
            title: t('shop.preorder.draftConflictTitle'),
            content: t('shop.preorder.draftConflictDescription', { count: recovery.candidateCount }),
            okText: t('shop.preorder.recoverLocalDraft'),
            cancelText: t('shop.preorder.useServerDraft'),
            onOk: () => {
              if (!isSamePreorderRequestContext(activeContextRef.current, context)) return
              const currentCandidates = readPendingPreorderDrafts(context)
              const currentPending = currentCandidates.find((candidate) => (
                candidate.ownerId === selectedCandidate.ownerId &&
                candidate.writeId === selectedCandidate.writeId
              ))
              if (!currentPending) {
                message.warning(t('shop.preorder.localDraftChanged'))
                return
              }
              // 用户明确选择恢复后，才以服务器最新 revision 为基线自动保存本机内容。
              const selectedItems = mergePendingPreorderDraft(next.items, currentPending.items)
              const selectedSignature = getItemsSignature(selectedItems)
              setItems(selectedItems)
              if (selectedSignature === serverSignature) {
                if (currentPending.ownerId === queue.ownerId) {
                  clearPendingPreorderDraft(context, currentPending.ownerId, currentPending.writeId)
                }
                queue.pendingOwnerId = null
                queue.pendingWriteId = null
                setSaveState('saved')
              } else {
                queue.pendingOwnerId = currentPending.ownerId
                queue.pendingWriteId = currentPending.writeId
                setSaveState('idle')
              }
              message.info(currentCandidates.length > 1
                ? t('shop.preorder.localDraftRecoveredWithOthers', { count: currentCandidates.length - 1 })
                : t('shop.preorder.localDraftRecovered'))
            },
            onCancel: () => {
              if (selectedCandidate.ownerId === queue.ownerId) {
                clearPendingPreorderDraft(context, selectedCandidate.ownerId, selectedCandidate.writeId)
              }
            },
          })
          confirmDestroyRef.current = confirmation.destroy
        }
      })
      .catch(() => {
        if (!cancelled && isSamePreorderRequestContext(activeContextRef.current, context)) {
          message.error(t('shop.preorder.detailLoadFailed'))
        }
      })
      .finally(() => {
        if (!cancelled && isSamePreorderRequestContext(activeContextRef.current, context)) {
          setLoading(false)
        }
      })
    return () => {
      cancelled = true
      controller.abort()
      queue.cancelConflictResolution?.()
      confirmDestroyRef.current?.()
      confirmDestroyRef.current = null
      const snapshot = itemsRef.current.map((item) => ({ ...item }))
      const shouldPersist = consumePreorderContextPersistence(discardedContextKeysRef.current, context.key)
      authorizedLeaveContextKeysRef.current.delete(context.key)
      if (
        shouldPersist &&
        hydratedContextGenerationRef.current === context.generation &&
        getItemsSignature(snapshot) !== queue.lastSavedSignature
      ) {
        // 离开页面时旧上下文只写回自己的批次和分店；新页面会使用全新 generation 和队列。
        void flushDetachedSaveQueue(queue, snapshot)
      }
    }
  }, [activationGuid, message, modal, selectedStore?.storeCode, t])

  const saveDraft = useCallback((
    snapshot: PreorderActivationItem[],
    requestedContext: PreorderRequestContext | null = activeContextRef.current,
  ) => {
    if (!requestedContext || !isSamePreorderRequestContext(activeContextRef.current, requestedContext)) return Promise.resolve(false)
    if (isSamePreorderRequestContext(submittingContextRef.current, requestedContext)) return Promise.resolve(false)
    const queue = saveQueueRef.current
    if (!queue || !isSamePreorderRequestContext(queue.context, requestedContext)) return Promise.resolve(false)
    if (queue.running) {
      queue.pending = snapshot
      return awaitSaveDrain(queue)
    }
    queue.running = true
    setSaveState('saving')
    const savePromise = (async () => {
      let nextSnapshot: PreorderActivationItem[] | null = snapshot
      let handlingDraftConflict = false
      try {
        // 自动保存严格串行；编辑发生在请求途中时排队下一版，防止旧 revision 造成冲突。
        while (nextSnapshot) {
          const savingSnapshot: PreorderActivationItem[] = nextSnapshot
          queue.pending = null
          const requestPromise = (async (): Promise<PreorderSaveRequestResult<PreorderActivationDetail>> => {
            try {
              queue.submissionObservability?.incrementRequest('draftPut')
              const next = await saveShopPreorderDraft(requestedContext.activationGuid, {
                storeCode: requestedContext.storeCode,
                expectedDraftRevision: queue.draftRevision,
                items: savingSnapshot.map((item) => ({ activationItemGuid: item.activationItemGuid, packCount: item.packCount })),
              })
              queue.draftRevision = next.draftRevision
              const responseSignature = getItemsSignature(next.items)
              queue.lastSavedSignature = responseSignature
              if (isSamePreorderRequestContext(activeContextRef.current, requestedContext)) {
                const currentSignature = getItemsSignature(itemsRef.current)
                if (currentSignature === responseSignature) {
                  setDetail(next)
                  setItems(next.items)
                }
              }
              return { status: 'saved' }
            } catch (error) {
              if (!isPreorderDraftConflictError(error)) return { status: 'failed', error }

              handlingDraftConflict = true
              setDraftConflictResolving(true)
              try {
                queue.submissionObservability?.incrementRequest('detailGet')
                const refreshed = await getShopPreorderActivation(
                  requestedContext.activationGuid,
                  requestedContext.storeCode,
                )
                return { status: 'conflict', detail: refreshed }
              } catch (refreshError) {
                return { status: 'failed', error: refreshError }
              }
            }
          })()
          const requestResult = await exposeCurrentSaveRequest(queue, requestPromise)
          if (requestResult.status === 'failed') {
            if (queue.stopAfterCurrentRequest) {
              takeNextPendingSave(queue)
            }
            throw requestResult.error
          }
          if (requestResult.status === 'conflict') {
            if (queue.stopAfterCurrentRequest) {
              // freeze 的提交方已拿到同一个 detail；当前 drain 只停止，不能再弹窗或重复 GET。
              takeNextPendingSave(queue)
              return false
            }

            const localSnapshot = (queue.pending ?? savingSnapshot).map((item) => ({ ...item }))
            queue.pending = null
            const conflictWriteId = writePendingPreorderDraft(requestedContext, localSnapshot, {
              ownerId: queue.ownerId,
              baseDraftRevision: queue.draftRevision,
              serverFingerprint: queue.lastSavedSignature,
            })
            replacePendingPreorderDraftWriteForOwner(queue, conflictWriteId)
            if (!conflictWriteId) {
              queue.onDetachedSaveError()
            }

            const refreshed = requestResult.detail
            if (!isSamePreorderRequestContext(activeContextRef.current, requestedContext)) return false

            queue.draftRevision = refreshed.draftRevision
            queue.lastSavedSignature = getItemsSignature(refreshed.items)
            const terminalResolution = resolveOnlinePreorderDraftConflict(refreshed, localSnapshot, 'local')
            if (terminalResolution.forcedServer) {
              // 另一设备已提交/响应时，本地草稿不再具备覆盖资格，且只能清理当前页面刚写入的 owner journal。
              itemsRef.current = terminalResolution.items
              setDetail(refreshed)
              setItems(terminalResolution.items)
              setDraftConflictResolving(false)
              setSaveState('saved')
              if (conflictWriteId) {
                clearPendingPreorderDraft(requestedContext, queue.ownerId, conflictWriteId)
                if (queue.pendingOwnerId === queue.ownerId && queue.pendingWriteId === conflictWriteId) {
                  queue.pendingOwnerId = null
                  queue.pendingWriteId = null
                }
              }
              message.info(t('shop.preorder.draftAlreadyResponded'))
              return true
            }
            const choice = await new Promise<OnlinePreorderDraftChoice | 'cancelled'>((resolve) => {
              let settled = false
              const finish = (value: OnlinePreorderDraftChoice | 'cancelled') => {
                if (settled) return
                settled = true
                queue.cancelConflictResolution = null
                resolve(value)
              }
              queue.cancelConflictResolution = () => finish('cancelled')
              const confirmation = modal.confirm({
                title: t('shop.preorder.onlineDraftConflictTitle'),
                content: t('shop.preorder.onlineDraftConflictDescription'),
                okText: t('shop.preorder.keepCurrentDraft'),
                cancelText: t('shop.preorder.useServerDraft'),
                keyboard: false,
                onOk: () => finish('local'),
                onCancel: () => finish('server'),
              })
              confirmDestroyRef.current = confirmation.destroy
            })
            confirmDestroyRef.current = null
            if (choice === 'cancelled' || !isSamePreorderRequestContext(activeContextRef.current, requestedContext)) {
              return false
            }

            const newestLocalSnapshot = (queue.pending ?? localSnapshot).map((item) => ({ ...item }))
            queue.pending = null
            const resolution = resolveOnlinePreorderDraftConflict(refreshed, newestLocalSnapshot, choice)
            queue.draftRevision = resolution.draftRevision
            itemsRef.current = resolution.items
            setDetail(refreshed)
            setItems(resolution.items)
            setDraftConflictResolving(false)
            if (!resolution.shouldSave) {
              clearCurrentOwnerPendingDrafts(requestedContext, queue)
              setSaveState('saved')
              return true
            }

            // 用户选择保留当前输入后，立即以最新 revision 重试，不等待旧 state 触发下一轮 effect。
            handlingDraftConflict = false
            setSaveState('saving')
            nextSnapshot = resolution.items
            continue
          }
          nextSnapshot = takeNextPendingSave(queue)
        }
        if (isSamePreorderRequestContext(activeContextRef.current, requestedContext)) {
          setSaveState(getItemsSignature(itemsRef.current) === queue.lastSavedSignature ? 'saved' : 'idle')
        }
        clearCurrentOwnerPendingDrafts(requestedContext, queue)
        return true
      } catch (error) {
        queue.pending = null
        if (isSamePreorderRequestContext(activeContextRef.current, requestedContext)) {
          setSaveState('error')
          if (handlingDraftConflict) {
            message.error(t('shop.preorder.draftConflictRefreshFailed'))
          }
        }
        return false
      } finally {
        queue.running = false
        queue.cancelConflictResolution = null
        if (isSamePreorderRequestContext(activeContextRef.current, requestedContext)) {
          setDraftConflictResolving(false)
        }
      }
    })()
    return exposeSaveDrain(queue, savePromise)
  }, [message, modal, t])

  const requestDurableLeave = useCallback(() => {
    if (durableLeavePromiseRef.current) return durableLeavePromiseRef.current
    const promise = (async () => {
      const context = activeContextRef.current
      const queue = saveQueueRef.current
      if (
        !context ||
        !queue ||
        !isSamePreorderRequestContext(queue.context, context) ||
        getItemsSignature(itemsRef.current) === queue.lastSavedSignature
      ) return true

      setNavigationResolving(true)
      const snapshot = itemsRef.current.map((item) => ({ ...item }))
      const result = await preparePreorderNavigation({
        persistCurrentOwnerJournal: () => {
          const writeId = writePendingPreorderDraft(context, snapshot, {
            ownerId: queue.ownerId,
            baseDraftRevision: queue.draftRevision,
            serverFingerprint: queue.lastSavedSignature,
          })
          replacePendingPreorderDraftWriteForOwner(queue, writeId)
          return Boolean(writeId)
        },
        saveAndDrainRemote: async () => {
          const accepted = await saveDraft(snapshot, context)
          if (!accepted) return false
          await awaitSaveDrain(queue)
          return getItemsSignature(snapshot) === queue.lastSavedSignature
        },
      })
      if (result.canLeave) {
        authorizedLeaveContextKeysRef.current.add(context.key)
        return true
      }

      return new Promise<boolean>((resolve) => {
        const confirmation = modal.confirm({
          title: t('shop.preorder.navigationSaveFailedTitle'),
          content: t('shop.preorder.navigationSaveFailedDescription'),
          okText: t('shop.preorder.stayOnPage'),
          cancelText: t('shop.preorder.discardAndLeave'),
          cancelButtonProps: { danger: true },
          keyboard: false,
          maskClosable: false,
          onOk: () => {
            setNavigationResolving(false)
            resolve(false)
          },
          onCancel: () => {
            // 明确放弃只清当前 owner，并标记该上下文 cleanup 不得再次持久化。
            clearCurrentOwnerPendingDrafts(context, queue)
            markPreorderContextDiscarded(discardedContextKeysRef.current, context.key)
            authorizedLeaveContextKeysRef.current.add(context.key)
            resolve(true)
          },
        })
        confirmDestroyRef.current = confirmation.destroy
      })
    })().catch(() => {
      setNavigationResolving(false)
      message.error(t('shop.preorder.navigationSaveFailedDescription'))
      return false
    }).finally(() => {
      durableLeavePromiseRef.current = null
    })
    durableLeavePromiseRef.current = promise
    return promise
  }, [message, modal, saveDraft, t])

  useEffect(() => registerPreorderDurableLeave(requestDurableLeave), [
    registerPreorderDurableLeave,
    requestDurableLeave,
  ])

  useEffect(() => {
    if (blocker.state !== 'blocked' || blockerHandlingRef.current) return
    blockerHandlingRef.current = true
    void requestDurableLeave().then((canLeave) => {
      blockerHandlingRef.current = false
      if (blocker.state !== 'blocked') return
      if (canLeave) blocker.proceed()
      else blocker.reset()
    })
  }, [blocker, requestDurableLeave])

  useEffect(() => {
    const context = activeContextRef.current
    const queue = saveQueueRef.current
    if (
      !context ||
      !queue ||
      !isSamePreorderRequestContext(queue.context, context) ||
      hydratedContextGenerationRef.current !== context.generation ||
      !detail ||
      !isEditable ||
      isSamePreorderRequestContext(submittingContextRef.current, context)
    ) return
    const signature = getItemsSignature(items)
    if (signature === queue.lastSavedSignature) return
    const snapshot = items.map((item) => ({ ...item }))
    const debouncedSave = createDebouncedTask(() => void saveDraft(snapshot, context), 500)
    debouncedSaveRef.current = debouncedSave
    return () => {
      debouncedSave.cancel()
      if (debouncedSaveRef.current === debouncedSave) {
        debouncedSaveRef.current = null
      }
    }
  }, [detail, isEditable, items, saveDraft, submitting])

  useEffect(() => {
    const handleBeforeUnload = (event: BeforeUnloadEvent) => {
      const context = activeContextRef.current
      const queue = saveQueueRef.current
      if (
        !context ||
        !queue ||
        !isSamePreorderRequestContext(queue.context, context) ||
        getItemsSignature(itemsRef.current) === queue.lastSavedSignature
      ) return
      event.preventDefault()
      event.returnValue = ''
    }
    window.addEventListener('beforeunload', handleBeforeUnload)
    return () => window.removeEventListener('beforeunload', handleBeforeUnload)
  }, [])

  const updatePackCount = (activationItemGuid: string, value: number) => {
    const context = activeContextRef.current
    // React 状态提交前 ref 已同步激活；handler 也必须检查，避免同一帧内写入 POST 快照之外的新数量。
    if (!isEditable || (context && isSamePreorderRequestContext(submittingContextRef.current, context))) return
    const packCount = Math.max(0, Math.floor(Number.isFinite(value) ? value : 0))
    setSaveState('idle')
    setItems((current) => current.map((item) => item.activationItemGuid === activationItemGuid
      ? { ...item, packCount, orderedQuantity: packCount * item.minimumOrderQuantity }
      : item))
  }

  const continueAfterTerminalSubmit = (
    context: PreorderRequestContext,
    observability: PreorderSubmissionObservability,
  ) => {
    // 已知终态后先结束用户等待；门禁刷新转入后台，失败时仍保持 fail-closed。
    advanceActivePreorderFreshEpoch(context.storeCode)
    void beginPostSubmitGateRefresh({
      activationGuid: context.activationGuid,
      storeCode: context.storeCode,
      knownActivations: useShopStore.getState().preorderActivations,
      loadGate: (signal) => getActivePreorders(context.storeCode, signal, () => {
        observability.incrementRequest('activeGet')
      }),
      getCurrentStoreCode: () => useShopStore.getState().selectedStore?.storeCode ?? null,
      claimRequestToken: useShopStore.getState().beginPreorderGateRequest,
      isRequestCurrent: useShopStore.getState().isPreorderGateRequestCurrent,
      setGate: (gate) => setPreorderGate({
        preorderActivations: gate.activations,
        preorderBlocked: gate.normalOrderBlocked,
        preorderGateLoading: gate.loading,
        preorderGateError: gate.error,
      }),
      navigate: (path) => navigate(path, { replace: true }),
      notifyRefreshFailed: () => message.warning(t('shop.preorder.submitRefreshFailed')),
    }).then((outcome) => {
      observability.record('background-active-refresh-finish', {
        outcome: outcome === 'failed' ? 'failed' : outcome,
      })
    })
  }

  const submit = async (
    confirmNoDemand: boolean,
    context: PreorderRequestContext,
    snapshot: PreorderActivationItem[],
    submissionId: string,
  ) => {
    if (
      !isSamePreorderRequestContext(activeContextRef.current, context) ||
      !detail ||
      orderResponded ||
      activationReadOnlyReason
    ) return
    const queue = saveQueueRef.current
    if (!queue || !isSamePreorderRequestContext(queue.context, context)) return
    submittingContextRef.current = context
    setSubmitting(true)
    const createPayload = () => ({
      storeCode: context.storeCode,
      expectedDraftRevision: queue.draftRevision,
      confirmNoDemand,
      items: snapshot.map((item) => ({ activationItemGuid: item.activationItemGuid, packCount: item.packCount })),
    })
    const observability = createPreorderSubmissionObservability({
      submissionId,
      activationGuid: context.activationGuid,
      storeCode: context.storeCode,
      action: confirmNoDemand ? 'abandon' : 'submit',
      initialRequestCounts: {
        draftPut: queue.currentRequestPromise ? 1 : 0,
        submitPost: 0,
        detailGet: 0,
        activeGet: 0,
      },
      ...measurePreorderSubmitPayload(createPayload()),
    })
    queue.submissionObservability = observability
    observability.record('confirm')
    try {
      // 提交前先取消尚未启动的 debounce，并直接等待真实 in-flight Promise。
      debouncedSaveRef.current?.cancel()
      debouncedSaveRef.current = null
      observability.record('wait-save-start', {
        hadInFlightSave: Boolean(queue.currentRequestPromise),
      })
      const currentSaveResult = await freezeSaveQueueForSubmission(queue)
      observability.record('wait-save-end', {
        outcome: currentSaveResult.status === 'saved'
          ? 'success'
          : currentSaveResult.status === 'failed' ? 'failed' : 'coordinated',
      })
      if (currentSaveResult.status === 'failed') {
        message.error(t('shop.preorder.submitFailed'))
        return
      }
      if (!isSamePreorderRequestContext(activeContextRef.current, context)) return
      if (!confirmNoDemand && getItemsSignature(snapshot) !== getItemsSignature(itemsRef.current)) {
        message.info(t('shop.preorder.draftConflictSubmitCancelled'))
        return
      }
      const outcome = await runPreorderSubmit({
        initialConflictDetail: currentSaveResult.status === 'conflict' ? currentSaveResult.detail : undefined,
        submit: async () => {
          observability.incrementRequest('submitPost')
          const requestPayload = createPayload()
          observability.updateRequestMetrics(measurePreorderSubmitPayload(requestPayload))
          observability.record('post-start')
          try {
            const result = await submitShopPreorder(context.activationGuid, requestPayload, submissionId)
            observability.record('post-end', { outcome: 'success' })
            return result
          } catch (error) {
            observability.record('post-end', { outcome: 'failed' })
            throw error
          }
        },
        loadDetail: () => {
          observability.incrementRequest('detailGet')
          return getShopPreorderActivation(context.activationGuid, context.storeCode)
        },
        isConflict: isPreorderDraftConflictError,
        onTerminal: (refreshed) => {
          if (!isSamePreorderRequestContext(activeContextRef.current, context)) return
          queue.draftRevision = refreshed.draftRevision
          queue.lastSavedSignature = getItemsSignature(refreshed.items)
          itemsRef.current = refreshed.items
          setDetail(refreshed)
          setItems(refreshed.items)
          setSaveState('saved')
          clearCurrentOwnerPendingDrafts(context, queue)
          hydratedContextGenerationRef.current = 0
          message.success(refreshed.orderStatus === 'NoDemand'
            ? t('shop.preorder.noDemandSuccess')
            : t('shop.preorder.submitSuccess'))
          observability.record('success-feedback', { outcome: 'terminal' })
          continueAfterTerminalSubmit(context, observability)
        },
        coordinateConflict: async (refreshed) => {
          if (!isSamePreorderRequestContext(activeContextRef.current, context)) return

          // 已有最新 detail 时先更新 revision，再让用户选择；不得用已知过期 revision 额外 PUT/GET。
          queue.draftRevision = refreshed.draftRevision
          queue.lastSavedSignature = getItemsSignature(refreshed.items)
          const conflictWriteId = writePendingPreorderDraft(context, snapshot, {
            ownerId: queue.ownerId,
            baseDraftRevision: refreshed.draftRevision,
            serverFingerprint: queue.lastSavedSignature,
          })
          replacePendingPreorderDraftWriteForOwner(queue, conflictWriteId)
          if (!conflictWriteId) queue.onDetachedSaveError()
          setDraftConflictResolving(true)

          const choice = await new Promise<OnlinePreorderDraftChoice | 'cancelled'>((resolve) => {
            let settled = false
            const finish = (value: OnlinePreorderDraftChoice | 'cancelled') => {
              if (settled) return
              settled = true
              queue.cancelConflictResolution = null
              resolve(value)
            }
            queue.cancelConflictResolution = () => finish('cancelled')
            const confirmation = modal.confirm({
              title: t('shop.preorder.onlineDraftConflictTitle'),
              content: t('shop.preorder.onlineDraftConflictDescription'),
              okText: t('shop.preorder.keepCurrentDraft'),
              cancelText: t('shop.preorder.useServerDraft'),
              keyboard: false,
              onOk: () => finish('local'),
              onCancel: () => finish('server'),
            })
            confirmDestroyRef.current = confirmation.destroy
          })
          confirmDestroyRef.current = null
          if (choice === 'cancelled' || !isSamePreorderRequestContext(activeContextRef.current, context)) return

          const resolution = resolveOnlinePreorderDraftConflict(refreshed, snapshot, choice)
          queue.draftRevision = resolution.draftRevision
          itemsRef.current = resolution.items
          setDetail(refreshed)
          setItems(resolution.items)
          if (!resolution.shouldSave) {
            clearCurrentOwnerPendingDrafts(context, queue)
            setSaveState('saved')
            return
          }

          // 用户选择本地草稿后才使用第一次 GET 的最新 revision 保存。
          submittingContextRef.current = null
          await saveDraft(resolution.items, context)
        },
      })
      if (!isSamePreorderRequestContext(activeContextRef.current, context)) return
      if (outcome === 'submitted') {
        clearCurrentOwnerPendingDrafts(context, queue)
        hydratedContextGenerationRef.current = 0
        message.success(confirmNoDemand ? t('shop.preorder.noDemandSuccess') : t('shop.preorder.submitSuccess'))
        observability.record('success-feedback', { outcome: 'success' })
        continueAfterTerminalSubmit(context, observability)
      } else if (outcome === 'coordinated') {
        message.info(t('shop.preorder.draftConflictSubmitCancelled'))
      } else if (outcome === 'failed') {
        message.error(t('shop.preorder.submitFailed'))
      }
    } catch {
      if (isSamePreorderRequestContext(activeContextRef.current, context)) {
        message.error(t('shop.preorder.submitFailed'))
      }
    } finally {
      if (isSamePreorderRequestContext(activeContextRef.current, context)) {
        setSubmitting(false)
        setSubmittingAction(null)
      }
      if (isSamePreorderRequestContext(submittingContextRef.current, context)) {
        submittingContextRef.current = null
      }
      if (queue.submissionObservability === observability) {
        queue.submissionObservability = null
      }
    }
  }

  const submitWithConfirm = () => {
    const context = activeContextRef.current
    if (!context || !isEditable || !items.some((item) => item.packCount > 0)) return
    // 确认框打开即冻结数量；snapshot 与随后 POST 之间不允许再产生未提交编辑。
    submittingContextRef.current = context
    setSubmitting(true)
    setSubmittingAction('submit')
    const snapshot = items.map((item) => ({ ...item }))
    const submissionId = createPreorderSubmissionId()
    const confirmation = modal.confirm({
      title: t('shop.preorder.confirmSubmitTitle'),
      content: t('shop.preorder.confirmSubmitDescription', { selectedCount, totalQuantity }),
      okText: t('shop.preorder.confirmSubmit'),
      cancelText: t('common.cancel'),
      onOk: () => submit(false, context, snapshot, submissionId),
      onCancel: () => {
        if (isSamePreorderRequestContext(submittingContextRef.current, context)) {
          submittingContextRef.current = null
          setSubmitting(false)
          setSubmittingAction(null)
        }
      },
    })
    confirmDestroyRef.current = confirmation.destroy
  }

  const abandonWithConfirm = () => {
    const context = activeContextRef.current
    if (!context || !isEditable) return
    // 放弃确认框打开后立即锁定编辑，有需求数量时也必须以全零 snapshot 提交。
    submittingContextRef.current = context
    setSubmitting(true)
    setSubmittingAction('abandon')
    const snapshot = createPreorderNoDemandSnapshot(items)
    const submissionId = createPreorderSubmissionId()
    const confirmationPhrase = t('shop.preorder.noDemandConfirmationPhrase')
    let noDemandConfirmationText = ''
    let confirmation: ReturnType<typeof modal.confirm>
    confirmation = modal.confirm({
      title: t('shop.preorder.confirmNoDemandTitle'),
      content: (
        <Space direction="vertical" size={12} style={{ width: '100%' }}>
          <Text>{t('shop.preorder.confirmNoDemandDescription')}</Text>
          <Text strong>
            {t('shop.preorder.noDemandConfirmationInstruction', { phrase: confirmationPhrase })}
          </Text>
          <Input
            autoFocus
            aria-label={t('shop.preorder.noDemandConfirmationPlaceholder')}
            placeholder={t('shop.preorder.noDemandConfirmationPlaceholder')}
            onChange={(event) => {
              noDemandConfirmationText = event.target.value
              // 未逐字输入当前语言的确认文本前，禁止放弃本期 Preorder。
              confirmation.update({
                okButtonProps: {
                  danger: true,
                  disabled: !isNoDemandConfirmationMatch(noDemandConfirmationText, confirmationPhrase),
                },
              })
            }}
          />
        </Space>
      ),
      okText: t('shop.preorder.confirmNoDemand'),
      cancelText: t('common.cancel'),
      okButtonProps: { danger: true, disabled: true },
      onOk: () => submit(true, context, snapshot, submissionId),
      onCancel: () => {
        if (isSamePreorderRequestContext(submittingContextRef.current, context)) {
          submittingContextRef.current = null
          setSubmitting(false)
          setSubmittingAction(null)
        }
      },
    })
    confirmDestroyRef.current = confirmation.destroy
  }

  const { selectedCount, totalQuantity, totalImportAmount } = useMemo(
    () => summarizePreorderItems(items),
    [items],
  )
  const filteredItems = useMemo(() => {
    const normalized = keyword.trim().toLowerCase()
    return items.filter((item) => {
      if (selectedOnly && item.packCount <= 0) return false
      if (!normalized) return true
      return `${item.itemNumber} ${item.productName}`.toLowerCase().includes(normalized)
    })
  }, [items, keyword, selectedOnly])

  const quantityControl = (item: PreorderActivationItem) => (
    <div className="shop-preorder-stepper">
      <Button aria-label={t('shop.preorder.decreasePackCount')} icon={<MinusOutlined />} disabled={!isEditable || item.packCount <= 0} onClick={() => updatePackCount(item.activationItemGuid, item.packCount - 1)} />
      <InputNumber aria-label={t('shop.preorder.packCountAria', { name: item.productName })} min={0} precision={0} controls={false} value={item.packCount} disabled={!isEditable} onChange={(value) => updatePackCount(item.activationItemGuid, Number(value ?? 0))} />
      <Button aria-label={t('shop.preorder.increasePackCount')} type="primary" icon={<PlusOutlined />} disabled={!isEditable} onClick={() => updatePackCount(item.activationItemGuid, item.packCount + 1)} />
    </div>
  )

  const columns: ColumnsType<PreorderActivationItem> = [
    { title: t('shop.preorder.product'), width: 360, render: (_, item) => <Space align="start"><Image src={item.productImage} width={64} height={64} style={{ objectFit: 'contain' }} /><div><Text strong>{item.productName}</Text><br /><Text type="secondary">{t('shop.preorder.itemNumber', { itemNumber: item.itemNumber })}</Text></div></Space> },
    { title: t('shop.preorder.importPrice'), dataIndex: 'importPrice', width: 100, align: 'right', render: (value) => `$${value.toFixed(2)}` },
    { title: t('shop.preorder.retailPrice'), dataIndex: 'retailPrice', width: 100, align: 'right', render: (value) => `$${value.toFixed(2)}` },
    { title: t('shop.preorder.moq'), dataIndex: 'minimumOrderQuantity', width: 80, align: 'right' },
    { title: t('shop.preorder.packCount'), width: 190, render: (_, item) => quantityControl(item) },
    { title: t('shop.preorder.totalUnits'), dataIndex: 'orderedQuantity', width: 90, align: 'right', render: (value) => <Text strong>{value}</Text> },
  ]

  if (!selectedStore) return <Empty description={t('shop.preorder.selectStoreFirst')} />
  if (!contextMatchesRoute || loading) return <div className="shop-preorder-loading"><Space direction="vertical" align="center"><Spin size="large" /><Text type="secondary">{t('shop.preorder.loading')}</Text></Space></div>
  if (!detail) return <Empty description={t('shop.preorder.notAccessible')} />

  const orderStatusLabel = detail.orderStatus ? t(`shop.preorder.orderStatus.${detail.orderStatus}`) : ''
  const estimatedArrivalDate = getPreorderDateDisplay(detail.estimatedArrivalDate)

  return (
    <div className="shop-preorder-page">
      <Card className="shop-preorder-hero">
        <Space direction="vertical" size={4}>
          <Space wrap><Title level={2}>{detail.templateName}</Title><Tag color="processing">{t('shop.preorder.period', { sequence: detail.sequenceNumber })}</Tag></Space>
          <Text>{t('shop.preorder.storeAndDeadline', { store: selectedStore.storeName || selectedStore.storeCode, deadline: preorderDateTimeFormatter.format(new Date(detail.endAtUtc)) })}</Text>
          {estimatedArrivalDate ? <Text>{t('shop.preorder.estimatedArrivalDate', { date: estimatedArrivalDate })}</Text> : null}
          <Text type={saveState === 'error' ? 'danger' : 'secondary'}><SaveOutlined /> {orderResponded ? t('shop.preorder.responded') : activationReadOnlyReason ? t('shop.preorder.saveReadOnly') : saveState === 'saving' ? t('shop.preorder.saveSaving') : saveState === 'error' ? t('shop.preorder.saveError') : t('shop.preorder.saveSaved')}</Text>
        </Space>
      </Card>
      {saveState === 'error' && isEditable ? <Alert type="error" showIcon message={t('shop.preorder.draftSaveFailed')} description={t('shop.preorder.draftSaveErrorDescription')} action={<Button onClick={() => void saveDraft(items)}>{t('shop.preorder.retry')}</Button>} /> : null}
      {returnedForRevision ? <Alert type="warning" showIcon message={t('shop.preorder.returnedTitle')} description={detail.warehouseNotes || t('shop.preorder.returnedDescription')} /> : null}
      {orderResponded ? <Alert type="success" showIcon message={t('shop.preorder.respondedTitle', { status: orderStatusLabel })} description={t('shop.preorder.respondedDescription')} /> : null}
      {!orderResponded && activationReadOnlyReason ? <Alert type="warning" showIcon message={t('shop.preorder.readOnlyTitle')} description={t(`shop.preorder.readOnlyReason.${activationReadOnlyReason}`)} /> : null}
      <Card className="shop-preorder-products">
        <div className="shop-preorder-toolbar">
          <Input.Search allowClear value={keyword} onChange={(event) => setKeyword(event.target.value)} placeholder={t('shop.preorder.searchPlaceholder')} />
          <Button type={selectedOnly ? 'primary' : 'default'} onClick={() => setSelectedOnly((value) => !value)}>{t('shop.preorder.selectedOnly', { count: selectedCount })}</Button>
        </div>
        {renderMode === 'desktop' ? (
          <Table className="shop-preorder-table" rowKey="activationItemGuid" dataSource={filteredItems} columns={columns} pagination={false} scroll={{ x: 950 }} locale={{ emptyText: t('shop.preorder.noMatchingProducts') }} />
        ) : (
          <div className="shop-preorder-cards">
            {filteredItems.length ? filteredItems.map((item) => <Card key={item.activationItemGuid} size="small" className={item.packCount ? 'selected' : ''}>
              <div className="shop-preorder-card-main"><Image src={item.productImage} width={76} height={76} style={{ objectFit: 'contain' }} /><div><Text strong>{item.productName}</Text><br /><Text type="secondary">{item.itemNumber}</Text><div><Text>{t('shop.preorder.importRetailPrices', { importPrice: item.importPrice.toFixed(2), retailPrice: item.retailPrice.toFixed(2) })}</Text></div><Text>{t('shop.preorder.moqValue', { value: item.minimumOrderQuantity })}</Text></div></div>
              <div className="shop-preorder-card-footer">{quantityControl(item)}<Text strong>{t('shop.preorder.unitCount', { count: item.orderedQuantity })}</Text></div>
            </Card>) : <Empty description={t('shop.preorder.noMatchingProducts')} />}
          </div>
        )}
      </Card>
      <div className="shop-preorder-summary">
        <div><Text type="secondary">{t('shop.preorder.selectedProducts')}</Text><strong>{selectedCount}</strong></div>
        <div><Text type="secondary">{t('shop.preorder.totalUnits')}</Text><strong>{totalQuantity}</strong></div>
        <div><Text type="secondary">{t('shop.preorder.estimatedImportAmount')}</Text><strong>${totalImportAmount.toFixed(2)}</strong></div>
        <div className="shop-preorder-actions">
          <Button danger size="large" loading={submitting && submittingAction === 'abandon'} disabled={!canStartPreorderSubmission(isEditable, 1)} icon={<StopOutlined />} onClick={abandonWithConfirm}>{t('shop.preorder.abandonCurrent')}</Button>
          <Button type="primary" size="large" loading={submitting && submittingAction === 'submit'} disabled={!canStartPreorderSubmission(isEditable, selectedCount)} icon={<CheckCircleOutlined />} onClick={submitWithConfirm}>{isEditable ? t('shop.preorder.submitCurrent') : t('shop.preorder.responded')}</Button>
        </div>
      </div>
    </div>
  )
}
