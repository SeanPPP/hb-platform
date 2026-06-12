import type { Key } from 'react'
import type {
  BatchExecuteActionsResult,
  BatchExecuteExpectedAction,
  LocalSupplierInvoiceItemDto,
} from '../../../../types/localSupplierInvoice'
import { DetailAction } from '../../../../types/localSupplierInvoice'
import { RequestError } from '../../../../utils/request'

export interface BatchExecuteActionCounts {
  selectedCount: number
  createProductCount: number
}

export interface BatchExecuteConfirmLabels {
  title: string
  content: string
  createProductNotice: string
  okText: string
  cancelText: string
}

export interface BatchExecuteConfirmText {
  title: string
  content: string
  okText: string
  cancelText: string
}

export interface BatchExecuteSnapshot {
  selectedCount: number
  detailGuids: string[]
  expectedActions: BatchExecuteExpectedAction[]
  confirmedCreateProductCount: number
  confirmedAt?: string
}

export interface BatchExecuteErrorFeedback {
  message: string
  details: string[]
  failure?: BatchExecuteActionsResult
}

function getCurrentDetailAction(
  detail: Pick<LocalSupplierInvoiceItemDto, 'detailGUID' | 'activityType'>,
  rowActions: Record<string, number>,
) {
  return rowActions[detail.detailGUID] ?? detail.activityType ?? DetailAction.None
}

function renderTemplate(template: string, values: Record<string, number | string>) {
  return Object.entries(values).reduce(
    (result, [key, value]) => result.replace(new RegExp(`{{${key}}}`, 'g'), String(value)),
    template,
  )
}

function normalizeBatchExecuteFailure(error: unknown): BatchExecuteActionsResult | undefined {
  if (!(error instanceof RequestError)) return undefined

  const payload = error.payload as { data?: unknown; details?: unknown } | undefined
  const candidate = (payload?.details ?? payload?.data) as Partial<BatchExecuteActionsResult> | undefined
  if (!candidate || typeof candidate !== 'object') return undefined

  return {
    createdProducts: Number(candidate.createdProducts ?? 0),
    updatedPurchasePrices: Number(candidate.updatedPurchasePrices ?? 0),
    updatedItemNumbers: Number(candidate.updatedItemNumbers ?? 0),
    addedMultiCodes: Number(candidate.addedMultiCodes ?? 0),
    skipped: Number(candidate.skipped ?? 0),
    failed: Number(candidate.failed ?? 0),
    errors: Array.isArray(candidate.errors) ? candidate.errors.map(String) : [],
  }
}

export function countSelectedBatchExecuteActions(
  selectedRowKeys: Key[],
  details: Array<Pick<LocalSupplierInvoiceItemDto, 'detailGUID' | 'activityType'>>,
  rowActions: Record<string, number>,
): BatchExecuteActionCounts {
  const selectedKeys = new Set(selectedRowKeys.map(String))
  const selectedDetails = details.filter((item) => selectedKeys.has(item.detailGUID))

  return {
    selectedCount: selectedKeys.size,
    createProductCount: selectedDetails.filter((item) => {
      // 页面内存中的 rowActions 代表用户刚刚修改但可能尚未重新加载的操作类型。
      const currentAction = getCurrentDetailAction(item, rowActions)
      return currentAction === DetailAction.CreateProduct
    }).length,
  }
}

export function buildBatchExecuteSnapshot({
  selectedRowKeys,
  details,
  rowActions,
  confirmedAt,
}: {
  selectedRowKeys: Key[]
  details: Array<Pick<LocalSupplierInvoiceItemDto, 'detailGUID' | 'activityType'>>
  rowActions: Record<string, number>
  confirmedAt?: string
}): BatchExecuteSnapshot {
  const detailMap = new Map(details.map((item) => [item.detailGUID, item]))
  const detailGuids = selectedRowKeys.map(String)
  const expectedActions: BatchExecuteExpectedAction[] = detailGuids.flatMap((detailGuid) => {
    const detail = detailMap.get(detailGuid)
    if (!detail) {
      return []
    }

    const action = getCurrentDetailAction(detail, rowActions)
    return [{
      detailGuid,
      action,
      // 这里保留明细原始 activityType，方便后端按确认当刻做契约校验。
      activityType: detail.activityType ?? action,
    }]
  })

  return {
    selectedCount: detailGuids.length,
    detailGuids: [...detailGuids],
    expectedActions,
    confirmedCreateProductCount: expectedActions.filter((item) => item.action === DetailAction.CreateProduct).length,
    confirmedAt,
  }
}

export function constrainSelectedRowKeysToVisibleDetails(
  selectedRowKeys: Key[],
  visibleDetails: Array<Pick<LocalSupplierInvoiceItemDto, 'detailGUID'>>,
): Key[] {
  const visibleKeys = new Set(visibleDetails.map((item) => item.detailGUID))
  const nextSelectedRowKeys = selectedRowKeys.filter((key) => visibleKeys.has(String(key)))

  if (nextSelectedRowKeys.length === selectedRowKeys.length) {
    return selectedRowKeys
  }

  return nextSelectedRowKeys
}

export function buildBatchExecuteConfirmText({
  selectedCount,
  createProductCount,
  labels,
}: BatchExecuteActionCounts & { labels: BatchExecuteConfirmLabels }): BatchExecuteConfirmText {
  const lines = [
    renderTemplate(labels.content, { count: selectedCount }),
  ]

  if (createProductCount > 0) {
    lines.push(renderTemplate(labels.createProductNotice, { count: createProductCount }))
  }

  return {
    title: labels.title,
    content: lines.join('\n'),
    okText: labels.okText,
    cancelText: labels.cancelText,
  }
}

export function getBatchExecuteErrorFeedback(error: unknown, fallbackMessage: string): BatchExecuteErrorFeedback {
  const failure = normalizeBatchExecuteFailure(error)

  return {
    message: error instanceof Error ? error.message : fallbackMessage,
    details: failure?.errors ?? [],
    failure,
  }
}
