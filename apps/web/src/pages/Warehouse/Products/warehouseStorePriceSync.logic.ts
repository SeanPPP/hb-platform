export type WarehouseStorePriceSyncJobStatus =
  | 'Pending'
  | 'Running'
  | 'Succeeded'
  | 'PartiallySucceeded'
  | 'Failed'

export interface WarehouseStorePriceSyncTargetStore {
  storeCode: string
  storeName: string
}

export interface WarehouseStorePriceSyncPayload {
  productCodes: string[]
  applyToAllProducts: boolean
  targetStoreCodes: string[]
  syncToHq: boolean
}

export interface WarehouseStorePriceSyncError {
  stage?: string
  productCode?: string
  storeCode?: string
  code?: string
  message: string
}

export type WarehouseStorePriceSyncErrorInput = string | Partial<WarehouseStorePriceSyncError>

export interface WarehouseStorePriceSyncResult {
  requestedProductCount: number
  eligibleProductCount: number
  skippedProductCount: number
  targetStoreCount: number
  localCreatedCount: number
  localUpdatedCount: number
  hqCreatedCount: number
  hqUpdatedCount: number
  hqProvisionedProductCount: number
  errors: WarehouseStorePriceSyncErrorInput[]
}

export interface WarehouseStorePriceSyncJob {
  jobId: string
  status: WarehouseStorePriceSyncJobStatus
  isDuplicateRequest?: boolean
  createdAt?: string
  completedAt?: string
  message?: string
  result?: Partial<WarehouseStorePriceSyncResult>
}

export const WAREHOUSE_STORE_PRICE_SYNC_FIELD_MAPPINGS = [
  {
    source: 'importPrice',
    sourceLabelKey: 'warehouse.importPrice',
    target: 'purchasePrice',
    targetLabelKey: 'posAdmin.invoiceDetail.purchasePrice',
    fixedValue: undefined,
  },
  {
    source: 'retailPrice',
    sourceLabelKey: 'warehouse.retailPrice',
    target: 'storeRetailPrice',
    targetLabelKey: 'posAdmin.invoiceDetail.retailPrice',
    fixedValue: undefined,
  },
  {
    source: 'discountRate',
    sourceLabelKey: 'warehouse.discountRate',
    target: 'discountRate',
    targetLabelKey: 'warehouse.discountRate',
    fixedValue: 0,
  },
  {
    source: 'autoPricing',
    sourceLabelKey: 'warehouse.autoPricing',
    target: 'autoPricing',
    targetLabelKey: 'warehouse.autoPricing',
    fixedValue: false,
  },
] as const

export type WarehouseStorePriceSyncValidationError = 'targetStoreRequired'

function normalizeCodes(values: readonly (string | number)[] | undefined) {
  const seen = new Set<string>()
  return (values ?? []).reduce<string[]>((result, value) => {
    const code = String(value ?? '').trim()
    if (code && !seen.has(code)) {
      seen.add(code)
      result.push(code)
    }
    return result
  }, [])
}

export function buildWarehouseStorePriceSyncPayload(input: {
  productCodes?: readonly (string | number)[]
  targetStoreCodes: readonly string[]
  syncToHq?: boolean
}): WarehouseStorePriceSyncPayload {
  const productCodes = normalizeCodes(input.productCodes)
  const targetStoreCodes = normalizeCodes(input.targetStoreCodes)

  return {
    productCodes,
    // 空选择是明确的全量语义，不能把当前筛选条件或当前页商品带入请求。
    applyToAllProducts: productCodes.length === 0,
    targetStoreCodes,
    syncToHq: input.syncToHq === true,
  }
}

export function validateWarehouseStorePriceSyncInput(input: {
  productCodes?: readonly (string | number)[]
  targetStoreCodes: readonly string[]
}): WarehouseStorePriceSyncValidationError | null {
  return normalizeCodes(input.targetStoreCodes).length ? null : 'targetStoreRequired'
}

export function getWarehouseStorePriceSyncScopeSummary(input: {
  productCodes?: readonly (string | number)[]
  allProductCount?: number
  targetStoreCount: number
}) {
  const productCodes = normalizeCodes(input.productCodes)
  const isFullScope = productCodes.length === 0
  const productCount = isFullScope
    ? Math.max(0, Math.trunc(input.allProductCount ?? 0))
    : productCodes.length

  return {
    isFullScope,
    productCount,
    maxWriteCount: productCount * Math.max(0, Math.trunc(input.targetStoreCount)),
  }
}

export function isWarehouseStorePriceSyncTerminalStatus(
  status: WarehouseStorePriceSyncJobStatus,
) {
  return status === 'Succeeded' || status === 'PartiallySucceeded' || status === 'Failed'
}

function toCount(value: unknown) {
  const count = Number(value ?? 0)
  return Number.isFinite(count) && count >= 0 ? count : 0
}

function readRecord(value: unknown): Record<string, unknown> {
  return value && typeof value === 'object' && !Array.isArray(value)
    ? value as Record<string, unknown>
    : {}
}

function readOptionalText(...values: unknown[]) {
  for (const value of values) {
    if (typeof value === 'string' && value.trim()) return value.trim()
  }
  return undefined
}

export function normalizeWarehouseStorePriceSyncErrors(value: unknown): WarehouseStorePriceSyncError[] {
  if (!Array.isArray(value)) return []

  return value.map((error) => {
    if (typeof error === 'string') return { message: error }

    const record = readRecord(error)
    const normalized = {} as WarehouseStorePriceSyncError
    const stage = readOptionalText(record.stage, record.Stage)
    const productCode = readOptionalText(record.productCode, record.ProductCode)
    const storeCode = readOptionalText(record.storeCode, record.StoreCode)
    const code = readOptionalText(record.code, record.Code)
    if (stage) normalized.stage = stage
    if (productCode) normalized.productCode = productCode
    if (storeCode) normalized.storeCode = storeCode
    if (code) normalized.code = code
    normalized.message = readOptionalText(record.message, record.Message, record.code, record.Code) || 'Unknown error'
    return normalized
  })
}

export function summarizeWarehouseStorePriceSyncResult(
  job: Pick<WarehouseStorePriceSyncJob, 'status' | 'result'>,
) {
  const result = job.result ?? {}
  const errors = normalizeWarehouseStorePriceSyncErrors(result.errors)
  const failedCount = errors.filter((error) => error.code?.toUpperCase() !== 'MISSING_PRICE').length

  return {
    status: job.status,
    requestedProductCount: toCount(result.requestedProductCount),
    eligibleProductCount: toCount(result.eligibleProductCount),
    skippedProductCount: toCount(result.skippedProductCount),
    targetStoreCount: toCount(result.targetStoreCount),
    localCreatedCount: toCount(result.localCreatedCount),
    localUpdatedCount: toCount(result.localUpdatedCount),
    hqCreatedCount: toCount(result.hqCreatedCount),
    hqUpdatedCount: toCount(result.hqUpdatedCount),
    hqProvisionedProductCount: toCount(result.hqProvisionedProductCount),
    failedCount,
    errors,
  }
}
