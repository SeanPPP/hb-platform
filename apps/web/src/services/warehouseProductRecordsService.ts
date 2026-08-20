import type { ApiResponse } from '../types/api'
import type {
  WarehouseProductAllocationBranch,
  WarehouseProductAllocationQuery,
  WarehouseProductAllocationReport,
  WarehouseProductAllocationSummary,
  WarehouseProductContainerItem,
  WarehouseProductContainerQuery,
  WarehouseProductContainerReport,
  WarehouseProductContainerSummary,
  WarehouseProductRecordSummary,
} from '../types/warehouseProductRecords'
import request, { unwrapApiData } from '../utils/request'

const API_BASE = '/api/react/v1/warehouse-product-records'

type UnknownRecord = Record<string, unknown>

function asRecord(value: unknown): UnknownRecord | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    return null
  }
  return value as UnknownRecord
}

function pick(record: UnknownRecord, camelKey: string, pascalKey: string): unknown {
  if (record[camelKey] !== undefined) {
    return record[camelKey]
  }
  return record[pascalKey]
}

function readString(value: unknown): string | undefined {
  return typeof value === 'string' && value.trim() ? value : undefined
}

function readNullableString(value: unknown): string | null {
  const result = readString(value)
  return result ?? null
}

function readNullableDate(value: unknown): string | null {
  const result = readNullableString(value)
  const match = result?.match(/^(\d{4}-\d{2}-\d{2})/)
  return match?.[1] ?? result
}

function readNumber(value: unknown, fallback = 0): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : fallback
}

function readNullableNumber(value: unknown): number | null {
  return typeof value === 'number' && Number.isFinite(value) ? value : null
}

function readBoolean(value: unknown): boolean {
  return typeof value === 'boolean' ? value : false
}

function normalizeSummary(raw: unknown): WarehouseProductRecordSummary {
  const record = asRecord(raw) ?? {}
  return {
    productCode: readString(pick(record, 'productCode', 'ProductCode')) ?? '',
    itemNumber: readNullableString(pick(record, 'itemNumber', 'ItemNumber')),
    barcode: readNullableString(pick(record, 'barcode', 'Barcode')),
    productName: readNullableString(pick(record, 'productName', 'ProductName')),
    englishName: readNullableString(pick(record, 'englishName', 'EnglishName')),
    imageUrl: readNullableString(pick(record, 'imageUrl', 'ImageUrl')),
    isActive: readBoolean(pick(record, 'isActive', 'IsActive')),
  }
}

function normalizeContainerItem(raw: unknown): WarehouseProductContainerItem | null {
  const record = asRecord(raw)
  if (!record) {
    return null
  }

  const detailCode = readString(pick(record, 'detailCode', 'DetailCode'))
  const containerCode = readString(pick(record, 'containerCode', 'ContainerCode'))
  if (!detailCode || !containerCode) {
    return null
  }

  return {
    detailCode,
    containerCode,
    containerNumber: readNullableString(pick(record, 'containerNumber', 'ContainerNumber')),
    loadingDate: readNullableDate(pick(record, 'loadingDate', 'LoadingDate')),
    estimatedArrivalDate: readNullableDate(pick(record, 'estimatedArrivalDate', 'EstimatedArrivalDate')),
    actualArrivalDate: readNullableDate(pick(record, 'actualArrivalDate', 'ActualArrivalDate')),
    effectiveArrivalDate: readNullableDate(pick(record, 'effectiveArrivalDate', 'EffectiveArrivalDate')),
    status: readNullableNumber(pick(record, 'status', 'Status')),
    loadingPieces: readNullableNumber(pick(record, 'loadingPieces', 'LoadingPieces')),
    loadingQuantity: readNullableNumber(pick(record, 'loadingQuantity', 'LoadingQuantity')),
    domesticPrice: readNullableNumber(pick(record, 'domesticPrice', 'DomesticPrice')),
    importPrice: readNullableNumber(pick(record, 'importPrice', 'ImportPrice')),
    totalAmount: readNullableNumber(pick(record, 'totalAmount', 'TotalAmount')),
  }
}

function normalizeContainerSummary(raw: unknown): WarehouseProductContainerSummary {
  const record = asRecord(raw) ?? {}
  return {
    containerCount: readNumber(pick(record, 'containerCount', 'ContainerCount')),
    loadingPieces: readNumber(pick(record, 'loadingPieces', 'LoadingPieces')),
    loadingQuantity: readNumber(pick(record, 'loadingQuantity', 'LoadingQuantity')),
    totalAmount: readNumber(pick(record, 'totalAmount', 'TotalAmount')),
  }
}

function normalizeContainerReport(raw: unknown): WarehouseProductContainerReport {
  const record = asRecord(raw) ?? {}
  const items = Array.isArray(pick(record, 'items', 'Items'))
    ? (pick(record, 'items', 'Items') as unknown[])
        .map(normalizeContainerItem)
        .filter((item): item is WarehouseProductContainerItem => item !== null)
    : []

  return {
    totalCount: readNumber(pick(record, 'totalCount', 'TotalCount')),
    pageNumber: readNumber(pick(record, 'pageNumber', 'PageNumber'), 1),
    pageSize: readNumber(pick(record, 'pageSize', 'PageSize'), 20),
    summary: normalizeContainerSummary(pick(record, 'summary', 'Summary')),
    items,
  }
}

function normalizeAllocationBranch(raw: unknown): WarehouseProductAllocationBranch | null {
  const record = asRecord(raw)
  if (!record) {
    return null
  }

  const rawStoreCode = pick(record, 'storeCode', 'StoreCode')
  if (typeof rawStoreCode !== 'string') {
    return null
  }
  // 空编码是后端保留的“未匹配分店（无编码）”业务分组，不能在标准化阶段丢弃。
  const storeCode = rawStoreCode.trim()

  return {
    storeCode,
    storeName: readNullableString(pick(record, 'storeName', 'StoreName')),
    isActive: readBoolean(pick(record, 'isActive', 'IsActive')),
    allocationQuantity: readNumber(pick(record, 'allocationQuantity', 'AllocationQuantity')),
    allocationAmount: readNumber(pick(record, 'allocationAmount', 'AllocationAmount')),
    orderCount: readNumber(pick(record, 'orderCount', 'OrderCount')),
    firstAllocationDate: readNullableDate(pick(record, 'firstAllocationDate', 'FirstAllocationDate')),
    lastAllocationDate: readNullableDate(pick(record, 'lastAllocationDate', 'LastAllocationDate')),
  }
}

function normalizeAllocationSummary(raw: unknown): WarehouseProductAllocationSummary {
  const record = asRecord(raw) ?? {}
  return {
    allocationQuantity: readNumber(pick(record, 'allocationQuantity', 'AllocationQuantity')),
    allocationAmount: readNumber(pick(record, 'allocationAmount', 'AllocationAmount')),
    orderCount: readNumber(pick(record, 'orderCount', 'OrderCount')),
  }
}

function normalizeAllocationReport(raw: unknown): WarehouseProductAllocationReport {
  const record = asRecord(raw) ?? {}
  const branches = Array.isArray(pick(record, 'branches', 'Branches'))
    ? (pick(record, 'branches', 'Branches') as unknown[])
        .map(normalizeAllocationBranch)
        .filter((item): item is WarehouseProductAllocationBranch => item !== null)
    : []

  return {
    summary: normalizeAllocationSummary(pick(record, 'summary', 'Summary')),
    branches,
  }
}

function recordsUrl(productCode: string, suffix: string) {
  return `${API_BASE}/${encodeURIComponent(productCode)}/${suffix}`
}

export async function queryWarehouseProductRecordSummary(
  productCode: string,
  signal?: AbortSignal,
): Promise<WarehouseProductRecordSummary> {
  const response = await request<ApiResponse<WarehouseProductRecordSummary>>(
    recordsUrl(productCode, 'summary'),
    { method: 'GET', signal },
  )
  return normalizeSummary(unwrapApiData(response))
}

export async function queryWarehouseProductContainers(
  productCode: string,
  query: WarehouseProductContainerQuery,
  signal?: AbortSignal,
): Promise<WarehouseProductContainerReport> {
  const response = await request.post<ApiResponse<WarehouseProductContainerReport>>(
    recordsUrl(productCode, 'containers/query'),
    query,
    { signal },
  )
  return normalizeContainerReport(unwrapApiData(response))
}

export async function queryWarehouseProductAllocations(
  productCode: string,
  query: WarehouseProductAllocationQuery,
  signal?: AbortSignal,
): Promise<WarehouseProductAllocationReport> {
  const response = await request.post<ApiResponse<WarehouseProductAllocationReport>>(
    recordsUrl(productCode, 'allocations/query'),
    query,
    { signal },
  )
  return normalizeAllocationReport(unwrapApiData(response))
}
