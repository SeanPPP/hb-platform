import type { PreorderSubmitPayload } from '../../types/preorder'

export type PreorderSubmissionStage =
  | 'confirm'
  | 'wait-save-start'
  | 'wait-save-end'
  | 'post-start'
  | 'post-end'
  | 'success-feedback'
  | 'background-active-refresh-finish'

type PreorderSubmissionAction = 'submit' | 'abandon'
export type PreorderSubmissionRequestKind = 'draftPut' | 'submitPost' | 'detailGet' | 'activeGet'
export type PreorderSubmissionRequestCounts = Record<PreorderSubmissionRequestKind, number>

interface PreorderSubmissionObservabilityInput {
  submissionId: string
  activationGuid: string
  storeCode: string
  action: PreorderSubmissionAction
  itemCount: number
  requestBodyBytes: number
  initialRequestCounts: PreorderSubmissionRequestCounts
}

interface PreorderSubmissionStageData {
  outcome?: 'success' | 'failed' | 'terminal' | 'coordinated' | 'stale'
  hadInFlightSave?: boolean
}

export interface PreorderSubmissionLog {
  category: 'preorder-submit-observability'
  stage: PreorderSubmissionStage
  submissionId: string
  activationGuid: string
  storeCode: string
  action: PreorderSubmissionAction
  sequence: number
  requestCount: number
  requestCounts: PreorderSubmissionRequestCounts
  itemCount: number
  requestBodyBytes: number
  outcome?: PreorderSubmissionStageData['outcome']
  hadInFlightSave?: boolean
}

export function measurePreorderSubmitPayload(payload: PreorderSubmitPayload) {
  return {
    itemCount: payload.items.length,
    requestBodyBytes: new TextEncoder().encode(JSON.stringify(payload)).byteLength,
  }
}

export function createPreorderSubmissionObservability(
  input: PreorderSubmissionObservabilityInput,
  emit: (payload: PreorderSubmissionLog) => void = (payload) => {
    if (typeof console !== 'undefined') {
      // 可观测性保持纯本地，不能为了记录阶段额外制造网络请求。
      console.info('[shop-preorder-submit]', payload)
    }
  },
) {
  let sequence = 0
  let itemCount = input.itemCount
  let requestBodyBytes = input.requestBodyBytes
  const requestCounts = { ...input.initialRequestCounts }
  return {
    incrementRequest(kind: PreorderSubmissionRequestKind) {
      requestCounts[kind] += 1
    },
    updateRequestMetrics(metrics: { itemCount: number; requestBodyBytes: number }) {
      itemCount = metrics.itemCount
      requestBodyBytes = metrics.requestBodyBytes
    },
    record(stage: PreorderSubmissionStage, data: PreorderSubmissionStageData = {}) {
      sequence += 1
      // 严格白名单：只记录计数、阶段和关联 ID，不接受请求体、商品明细或凭证字段。
      emit({
        category: 'preorder-submit-observability',
        stage,
        submissionId: input.submissionId,
        activationGuid: input.activationGuid,
        storeCode: input.storeCode,
        action: input.action,
        sequence,
        requestCount: Object.values(requestCounts).reduce((sum, count) => sum + count, 0),
        requestCounts: { ...requestCounts },
        itemCount,
        requestBodyBytes,
        outcome: data.outcome,
        hadInFlightSave: data.hadInFlightSave,
      })
    },
  }
}

export type PreorderSubmissionObservability = ReturnType<typeof createPreorderSubmissionObservability>
