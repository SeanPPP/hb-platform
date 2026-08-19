import type { StoreOrderProductActivityFilter } from '../../types/storeOrder'

export interface ProductActivityHistoryRequestIdentityInput {
  open: boolean
  storeCode: string | null | undefined
  productCode: string | null | undefined
  page: number
  recordType: StoreOrderProductActivityFilter
  retryVersion: number
}

export interface ProductActivityHistoryRequestToken {
  identity: string
  version: number
}

export interface ProductActivityHistoryRequestCoordinator {
  activate: (identity: string | null) => void
  invalidate: (identity: string | null) => void
  begin: (identity: string) => ProductActivityHistoryRequestToken | null
  isCurrent: (token: ProductActivityHistoryRequestToken) => boolean
}

export function getProductActivityHistoryRequestIdentity({
  open,
  storeCode,
  productCode,
  page,
  recordType,
  retryVersion,
}: ProductActivityHistoryRequestIdentityInput): string | null {
  if (!open || !storeCode || !productCode) {
    return null
  }

  return JSON.stringify([storeCode, productCode, page, recordType, retryVersion])
}

export function createProductActivityHistoryRequestCoordinator(): ProductActivityHistoryRequestCoordinator {
  let activeIdentity: string | null = null
  let version = 0

  return {
    activate(identity) {
      activeIdentity = identity
      version += 1
    },
    invalidate(identity) {
      if (activeIdentity === identity) {
        activeIdentity = null
        version += 1
      }
    },
    begin(identity) {
      if (activeIdentity !== identity) {
        return null
      }

      return { identity, version }
    },
    isCurrent(token) {
      return activeIdentity === token.identity && version === token.version
    },
  }
}

export interface RunProductActivityHistoryRequestOptions<TResult> {
  coordinator: ProductActivityHistoryRequestCoordinator
  identity: string | null
  request: () => Promise<TResult>
  signal?: AbortSignal
  onSuccess: (result: TResult) => void
  onError: (error: unknown) => void
}

function isAbortError(error: unknown, signal?: AbortSignal): boolean {
  if (error instanceof Error && error.name === 'AbortError') {
    return true
  }

  return Boolean(signal?.aborted)
}

export async function runProductActivityHistoryRequest<TResult>({
  coordinator,
  identity,
  request,
  signal,
  onSuccess,
  onError,
}: RunProductActivityHistoryRequestOptions<TResult>): Promise<void> {
  if (!identity) {
    return
  }

  const token = coordinator.begin(identity)
  if (!token) {
    return
  }

  try {
    const result = await request()
    // 身份（门店、商品、页码、筛选、重试代次）变化后，旧成功响应不能覆盖当前弹窗。
    if (coordinator.isCurrent(token)) {
      onSuccess(result)
    }
  } catch (error) {
    // 主动取消不视为失败；AbortError 不能污染新实体/新请求的状态。
    if (isAbortError(error, signal)) {
      return
    }

    // 错误也要走同一代次校验，避免旧请求把新内容错误地切成失败态。
    if (coordinator.isCurrent(token)) {
      onError(error)
    }
  }
}
