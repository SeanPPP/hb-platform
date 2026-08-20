import type {
  ProductSalesAnalysisEnvelope,
  ProductSalesAnalysisFilter,
  ProductSalesAnalysisOptions,
  ProductSalesAnalysisPaged,
  ProductSalesAnalysisPaging,
  ProductSalesAnalysisProduct,
  ProductSalesAnalysisScope,
  ProductSalesAnalysisSelection,
  ProductSalesAnalysisSupplier,
  ProductSalesBranch,
  ProductSalesDaily,
  ProductSalesMetrics,
  ProductSalesSummaryRow,
} from '../types/productSalesAnalysis'
import request from '../utils/request'

const API_BASE = '/api/react/v1/dashboard/product-sales-analysis'

export interface ProductSalesAnalysisQueryBehavior {
  allowNonFreshData?: boolean
}

type UnknownRecord = Record<string, unknown>

const DOMAIN_META_KEYS = new Set([
  'statisticStatus',
  'statisticMessage',
  'statisticUpdatedAt',
  'cacheVersion',
  'StatisticStatus',
  'StatisticMessage',
  'StatisticUpdatedAt',
  'CacheVersion',
])

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

function readNumber(value: unknown, fallback = 0): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : fallback
}

function readNullableNumber(value: unknown): number | null {
  if (value === null) {
    return null
  }
  return typeof value === 'number' && Number.isFinite(value) ? value : null
}

function readBoolean(value: unknown): boolean | undefined {
  return typeof value === 'boolean' ? value : undefined
}

function normalizeSupplier(raw: unknown): ProductSalesAnalysisSupplier | null {
  const record = asRecord(raw)
  if (!record) {
    return null
  }

  const code = readString(pick(record, 'code', 'Code'))
  if (!code) {
    return null
  }

  return {
    code,
    name: readString(pick(record, 'name', 'Name')),
  }
}

function normalizeSupplierArray(value: unknown): ProductSalesAnalysisSupplier[] {
  if (!Array.isArray(value)) {
    return []
  }

  return value
    .map(normalizeSupplier)
    .filter((item): item is ProductSalesAnalysisSupplier => item !== null)
}

function normalizeProduct(raw: unknown): ProductSalesAnalysisProduct | null {
  const record = asRecord(raw)
  if (!record) {
    return null
  }

  const productCode = readString(pick(record, 'productCode', 'ProductCode'))
  if (!productCode) {
    return null
  }

  return {
    productCode,
    itemNumber: readString(pick(record, 'itemNumber', 'ItemNumber')),
    barcode: readString(pick(record, 'barcode', 'Barcode')),
    productName: readString(pick(record, 'productName', 'ProductName')),
    englishName: readString(pick(record, 'englishName', 'EnglishName')),
    imageUrl: readString(pick(record, 'imageUrl', 'ImageUrl')),
    australianSuppliers: normalizeSupplierArray(
      pick(record, 'australianSuppliers', 'AustralianSuppliers'),
    ),
    chinaSuppliers: normalizeSupplierArray(pick(record, 'chinaSuppliers', 'ChinaSuppliers')),
    chinaSupplierUnmapped: readBoolean(pick(record, 'chinaSupplierUnmapped', 'ChinaSupplierUnmapped')),
  }
}

function normalizeMetrics(raw: unknown): ProductSalesMetrics {
  const record = asRecord(raw) ?? {}
  return {
    quantity: readNumber(pick(record, 'quantity', 'Quantity')),
    salesAmount: readNumber(pick(record, 'salesAmount', 'SalesAmount')),
    averageUnitPrice: readNullableNumber(pick(record, 'averageUnitPrice', 'AverageUnitPrice')),
  }
}

function normalizeSummaryRow(raw: unknown): ProductSalesSummaryRow | null {
  const record = asRecord(raw)
  if (!record) {
    return null
  }

  const product = normalizeProduct(record)
  if (!product) {
    return null
  }

  return {
    ...product,
    metrics: normalizeMetrics(pick(record, 'metrics', 'Metrics')),
  }
}

function normalizeDaily(raw: unknown): ProductSalesDaily | null {
  const record = asRecord(raw)
  if (!record) {
    return null
  }

  const date = normalizeDailyDate(pick(record, 'date', 'Date'))
  if (!date) {
    return null
  }

  const metrics = normalizeMetrics(pick(record, 'metrics', 'Metrics') ?? record)
  return {
    date,
    ...metrics,
  }
}

const dailyDatePrefixPattern = /^(\d{4})-(\d{2})-(\d{2})/

function normalizeDailyDate(raw: unknown): string | null {
  const value = readString(raw)
  if (!value) {
    return null
  }

  const match = dailyDatePrefixPattern.exec(value)
  if (!match) {
    return null
  }

  const year = Number(match[1])
  const month = Number(match[2])
  const day = Number(match[3])
  const parsed = new Date(Date.UTC(year, month - 1, day))
  if (
    parsed.getUTCFullYear() !== year
    || parsed.getUTCMonth() !== month - 1
    || parsed.getUTCDate() !== day
  ) {
    return null
  }

  return `${match[1]}-${match[2]}-${match[3]}`
}

function normalizeBranch(raw: unknown): ProductSalesBranch | null {
  const record = asRecord(raw)
  if (!record) {
    return null
  }

  const branchCode = readString(pick(record, 'branchCode', 'BranchCode'))
  if (!branchCode) {
    return null
  }

  return {
    branchCode,
    branchName: readString(pick(record, 'branchName', 'BranchName')),
    metrics: normalizeMetrics(pick(record, 'metrics', 'Metrics')),
  }
}

function normalizePagedData<T>(raw: unknown, normalizeItem: (item: unknown) => T | null) {
  const record = asRecord(raw) ?? {}
  const items = Array.isArray(pick(record, 'items', 'Items'))
    ? (pick(record, 'items', 'Items') as unknown[])
        .map(normalizeItem)
        .filter((item): item is T => item !== null)
    : []

  return {
    items,
    total: readNumber(pick(record, 'total', 'Total')),
    pageNumber: readNumber(pick(record, 'pageNumber', 'PageNumber'), 1),
    pageSize: readNumber(pick(record, 'pageSize', 'PageSize'), 20),
  }
}

function hasDomainMeta(record: UnknownRecord): boolean {
  return [...DOMAIN_META_KEYS].some((key) => key in record)
}

function getDomainEnvelopeRecord(raw: unknown): { domain: UnknownRecord; data: unknown } {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) {
    throw new Error('响应格式非法')
  }

  let current = raw as UnknownRecord
  const success = current.success ?? current.isSuccess
  if (success === false) {
    const message = readString(current.message) ?? readString(current.Message) ?? '请求失败'
    throw new Error(message)
  }

  // 项目 HTTP 层可能再包一层 { success, data }，此处先剥掉外层，再识别领域信封。
  if (!hasDomainMeta(current) && 'data' in current && current.data) {
    const inner = asRecord(current.data)
    if (inner) {
      current = inner
    }
  }

  const domain = asRecord(current)
  if (!domain) {
    throw new Error('响应格式非法')
  }

  const data = pick(domain, 'data', 'Data')
  return { domain, data }
}

function readDomainMeta(domain: UnknownRecord) {
  return {
    statisticStatus: readString(pick(domain, 'statisticStatus', 'StatisticStatus')),
    statisticMessage: readString(pick(domain, 'statisticMessage', 'StatisticMessage')),
    statisticUpdatedAt: readString(pick(domain, 'statisticUpdatedAt', 'StatisticUpdatedAt')),
    cacheVersion: readString(pick(domain, 'cacheVersion', 'CacheVersion')),
  }
}

function normalizeOptions(raw: unknown): ProductSalesAnalysisOptions {
  const record = asRecord(raw) ?? {}
  return {
    australianSuppliers: normalizeSupplierArray(
      pick(record, 'australianSuppliers', 'AustralianSuppliers'),
    ),
    chinaSuppliers: normalizeSupplierArray(pick(record, 'chinaSuppliers', 'ChinaSuppliers')),
  }
}

function normalizeCandidates(raw: unknown): ProductSalesAnalysisPaged<ProductSalesAnalysisProduct> {
  return normalizePagedData(raw, normalizeProduct)
}

function normalizeSummary(raw: unknown): ProductSalesAnalysisPaged<ProductSalesSummaryRow> {
  return normalizePagedData(raw, normalizeSummaryRow)
}

function normalizeDailyList(raw: unknown): ProductSalesDaily[] {
  if (!Array.isArray(raw)) {
    return []
  }
  return raw
    .map(normalizeDaily)
    .filter((item): item is ProductSalesDaily => item !== null)
}

function normalizeBranches(raw: unknown): ProductSalesBranch[] {
  if (!Array.isArray(raw)) {
    return []
  }
  return raw
    .map(normalizeBranch)
    .filter((item): item is ProductSalesBranch => item !== null)
}

function unwrapEnvelope<T>(
  raw: unknown,
  normalizeData: (data: unknown) => T,
): ProductSalesAnalysisEnvelope<T> {
  const { domain, data } = getDomainEnvelopeRecord(raw)
  return {
    ...readDomainMeta(domain),
    data: normalizeData(data),
  }
}

function clearNonFreshData<T>(
  envelope: ProductSalesAnalysisEnvelope<T>,
  emptyData: T,
  behavior?: ProductSalesAnalysisQueryBehavior,
): ProductSalesAnalysisEnvelope<T> {
  if (behavior?.allowNonFreshData) {
    return envelope
  }
  // fail-closed：只有状态明确为 Fresh（大小写不敏感）才保留数据，
  // 缺失/空白/未知/Pending/Stale/Failed 一律清空并对外归一化为 Pending。
  const isFresh = typeof envelope.statisticStatus === 'string'
    && envelope.statisticStatus.trim().toLowerCase() === 'fresh'
  if (!isFresh) {
    return { ...envelope, statisticStatus: 'Pending', data: emptyData }
  }
  return envelope
}

function buildBody(
  filter: ProductSalesAnalysisFilter,
  selection: ProductSalesAnalysisSelection,
  scope?: ProductSalesAnalysisScope,
  extra: Record<string, unknown> = {},
) {
  return {
    filter: {
      startDate: filter.startDate,
      endDate: filter.endDate,
      keyword: filter.keyword,
      australianSupplierCodes: filter.australianSupplierCodes,
      chinaSupplierCodes: filter.chinaSupplierCodes,
    },
    selection,
    ...(scope ? { scope } : {}),
    ...extra,
  }
}

export async function getProductSalesAnalysisOptions(
  filter: ProductSalesAnalysisFilter,
  signal?: AbortSignal,
): Promise<ProductSalesAnalysisEnvelope<ProductSalesAnalysisOptions>> {
  const response = await request(
    `${API_BASE}/options`,
    {
      method: 'GET',
      signal,
      params: {
        startDate: filter.startDate,
        endDate: filter.endDate,
        keyword: filter.keyword,
        australianSupplierCodes: filter.australianSupplierCodes,
        chinaSupplierCodes: filter.chinaSupplierCodes,
      },
    },
  )

  return clearNonFreshData(unwrapEnvelope(response, normalizeOptions), {
    australianSuppliers: [],
    chinaSuppliers: [],
  })
}

export async function queryProductSalesCandidates(
  filter: ProductSalesAnalysisFilter,
  selection: ProductSalesAnalysisSelection,
  paging: ProductSalesAnalysisPaging,
  signal?: AbortSignal,
): Promise<ProductSalesAnalysisEnvelope<ProductSalesAnalysisPaged<ProductSalesAnalysisProduct>>> {
  const response = await request(
    `${API_BASE}/candidates`,
    {
      method: 'POST',
      signal,
      data: {
        ...buildBody(filter, selection),
        pageNumber: paging.pageNumber,
        pageSize: paging.pageSize,
        sortBy: paging.sortBy,
        sortDirection: paging.sortDirection,
      },
    },
  )

  return clearNonFreshData(unwrapEnvelope(response, normalizeCandidates), {
    items: [],
    total: 0,
    pageNumber: paging.pageNumber,
    pageSize: paging.pageSize,
  })
}

export async function queryProductSalesSummary(
  filter: ProductSalesAnalysisFilter,
  selection: ProductSalesAnalysisSelection,
  scope: ProductSalesAnalysisScope,
  paging: ProductSalesAnalysisPaging,
  signal?: AbortSignal,
  behavior?: ProductSalesAnalysisQueryBehavior,
): Promise<ProductSalesAnalysisEnvelope<ProductSalesAnalysisPaged<ProductSalesSummaryRow>>> {
  const response = await request(
    `${API_BASE}/summary`,
    {
      method: 'POST',
      signal,
      params: behavior?.allowNonFreshData ? { allowNonFreshData: true } : undefined,
      data: {
        ...buildBody(filter, selection, scope),
        pageNumber: paging.pageNumber,
        pageSize: paging.pageSize,
        sortBy: paging.sortBy,
        sortDirection: paging.sortDirection,
      },
    },
  )

  return clearNonFreshData(unwrapEnvelope(response, normalizeSummary), {
    items: [],
    total: 0,
    pageNumber: paging.pageNumber,
    pageSize: paging.pageSize,
  }, behavior)
}

export async function queryProductSalesDaily(
  filter: ProductSalesAnalysisFilter,
  selection: ProductSalesAnalysisSelection,
  scope: ProductSalesAnalysisScope,
  signal?: AbortSignal,
  behavior?: ProductSalesAnalysisQueryBehavior,
): Promise<ProductSalesAnalysisEnvelope<ProductSalesDaily[]>> {
  const response = await request(
    `${API_BASE}/product-daily`,
    {
      method: 'POST',
      signal,
      params: behavior?.allowNonFreshData ? { allowNonFreshData: true } : undefined,
      data: buildBody(filter, selection, scope),
    },
  )

  return clearNonFreshData(unwrapEnvelope(response, normalizeDailyList), [], behavior)
}

export async function queryProductSalesBranches(
  filter: ProductSalesAnalysisFilter,
  selection: ProductSalesAnalysisSelection,
  scope: ProductSalesAnalysisScope,
  signal?: AbortSignal,
  behavior?: ProductSalesAnalysisQueryBehavior,
): Promise<ProductSalesAnalysisEnvelope<ProductSalesBranch[]>> {
  const response = await request(
    `${API_BASE}/branches`,
    {
      method: 'POST',
      signal,
      params: behavior?.allowNonFreshData ? { allowNonFreshData: true } : undefined,
      data: buildBody(filter, selection, scope),
    },
  )

  return clearNonFreshData(unwrapEnvelope(response, normalizeBranches), [], behavior)
}

export async function queryProductSalesBranchDaily(
  filter: ProductSalesAnalysisFilter,
  selection: ProductSalesAnalysisSelection,
  scope: ProductSalesAnalysisScope,
  branchCode: string,
  signal?: AbortSignal,
  behavior?: ProductSalesAnalysisQueryBehavior,
): Promise<ProductSalesAnalysisEnvelope<ProductSalesDaily[]>> {
  const response = await request(
    `${API_BASE}/branch-daily`,
    {
      method: 'POST',
      signal,
      params: behavior?.allowNonFreshData ? { allowNonFreshData: true } : undefined,
      data: buildBody(filter, selection, scope, { branchCode }),
    },
  )

  return clearNonFreshData(unwrapEnvelope(response, normalizeDailyList), [], behavior)
}
