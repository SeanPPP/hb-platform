import type { ApiResponse } from '../types/api'
import type {
  LocalPurchaseDashboardResponse,
  LocalPurchaseSupplierDetailResponse,
  LocalPurchaseSupplierSummary,
} from '../types/localPurchaseDashboard'
import request, { unwrapApiData } from '../utils/request'

const API_BASE = '/api/react/v1/local-purchase-dashboard'

type UnknownRecord = Record<string, unknown>

function asRecord(value: unknown): UnknownRecord {
  return value && typeof value === 'object' && !Array.isArray(value) ? value as UnknownRecord : {}
}

function readString(...values: unknown[]): string | undefined {
  const value = values.find((item) => typeof item === 'string' && item.trim())
  return typeof value === 'string' ? value.trim() : undefined
}

function readNumber(...values: unknown[]): number | undefined {
  for (const value of values) {
    if (typeof value === 'number' && Number.isFinite(value)) return value
    if (typeof value === 'string' && value.trim() && Number.isFinite(Number(value))) return Number(value)
  }
  return undefined
}

function readBoolean(...values: unknown[]): boolean | undefined {
  return values.find((value) => typeof value === 'boolean') as boolean | undefined
}

function readArray(...values: unknown[]): unknown[] {
  return values.find(Array.isArray) as unknown[] | undefined ?? []
}

function buildFallbackMonths(endMonth: string): string[] {
  const match = /^(\d{4})-(0[1-9]|1[0-2])$/.exec(endMonth)
  if (!match) return []
  const endAbsoluteMonth = Number(match[1]) * 12 + Number(match[2]) - 1
  return Array.from({ length: 12 }, (_, index) => {
    const absoluteMonth = endAbsoluteMonth + index - 11
    const year = Math.floor(absoluteMonth / 12)
    const month = (absoluteMonth % 12 + 12) % 12 + 1
    return `${year}-${String(month).padStart(2, '0')}`
  })
}

function readMonths(record: UnknownRecord, fallbackEndMonth: string): string[] {
  const rawMonths = readArray(record.months, record.Months, record.monthKeys, record.MonthKeys)
  const months = rawMonths
    .map((item) => typeof item === 'string'
      ? item.trim()
      : readString(asRecord(item).month, asRecord(item).Month))
    .filter((item): item is string => /^\d{4}-(0[1-9]|1[0-2])$/.test(item ?? ''))
  return months.length ? [...new Set(months)] : buildFallbackMonths(fallbackEndMonth)
}

function readSummaryRecord(record: UnknownRecord): UnknownRecord {
  return asRecord(record.summary ?? record.Summary)
}

function readWarehouseAmount(record: UnknownRecord): number | undefined {
  return readNumber(
    record.warehouseAmount,
    record.WarehouseAmount,
    record.warehouseTotal,
    record.WarehouseTotal,
    record.totalWarehouseAmount,
    record.TotalWarehouseAmount,
  )
}

function readLocalSupplierAmount(record: UnknownRecord): number | undefined {
  return readNumber(
    record.localSupplierAmount,
    record.LocalSupplierAmount,
    record.localSupplierTotal,
    record.LocalSupplierTotal,
    record.totalLocalSupplierAmount,
    record.TotalLocalSupplierAmount,
  )
}

function readTotalAmount(record: UnknownRecord): number | undefined {
  return readNumber(
    record.totalAmount,
    record.TotalAmount,
    record.grandTotal,
    record.GrandTotal,
  )
}

function readSalesAmount(record: UnknownRecord): number | undefined {
  return readNumber(record.salesAmount, record.SalesAmount)
}

function normalizeStoreMonth(raw: unknown) {
  const record = asRecord(raw)
  const month = readString(record.month, record.Month)
  if (!month) return null
  const warehouseAmount = readWarehouseAmount(record) ?? 0
  const localSupplierAmount = readLocalSupplierAmount(record) ?? 0
  return {
    month,
    warehouseAmount,
    localSupplierAmount,
    totalAmount: readTotalAmount(record) ?? warehouseAmount + localSupplierAmount,
    salesAmount: readSalesAmount(record) ?? 0,
  }
}

function normalizeStore(raw: unknown, months: string[]) {
  const record = asRecord(raw)
  const storeCode = readString(record.storeCode, record.StoreCode)
  if (!storeCode) return null
  const rawMonthAmounts = readArray(
    record.monthlyAmounts,
    record.MonthlyAmounts,
    record.monthAmounts,
    record.MonthAmounts,
    record.months,
    record.Months,
  )
  const monthAmountMap = new Map(
    rawMonthAmounts
      .map(normalizeStoreMonth)
      .filter((item): item is NonNullable<ReturnType<typeof normalizeStoreMonth>> => item !== null)
      .map((item) => [item.month, item]),
  )
  const monthlyAmounts = months.map((month) => monthAmountMap.get(month) ?? {
    month,
    warehouseAmount: 0,
    localSupplierAmount: 0,
    totalAmount: 0,
    salesAmount: 0,
  })
  const calculatedWarehouseAmount = monthlyAmounts.reduce((sum, item) => sum + item.warehouseAmount, 0)
  const calculatedLocalSupplierAmount = monthlyAmounts.reduce((sum, item) => sum + item.localSupplierAmount, 0)
  return {
    storeCode,
    storeName: readString(record.storeName, record.StoreName) ?? storeCode,
    monthlyAmounts,
    warehouseAmount: readWarehouseAmount(record) ?? calculatedWarehouseAmount,
    localSupplierAmount: readLocalSupplierAmount(record) ?? calculatedLocalSupplierAmount,
    totalAmount:
      readTotalAmount(record) ?? calculatedWarehouseAmount + calculatedLocalSupplierAmount,
  }
}

function normalizeDashboardResponse(raw: unknown, requestedEndMonth: string): LocalPurchaseDashboardResponse {
  const record = asRecord(raw)
  const endMonth = readString(record.endMonth, record.EndMonth) ?? requestedEndMonth
  const months = readMonths(record, endMonth)
  const stores = readArray(record.stores, record.Stores, record.storeSummaries, record.StoreSummaries)
    .map((item) => normalizeStore(item, months))
    .filter((item): item is NonNullable<ReturnType<typeof normalizeStore>> => item !== null)
  const summary = readSummaryRecord(record)
  const calculatedWarehouseAmount = stores.reduce((sum, item) => sum + item.warehouseAmount, 0)
  const calculatedLocalSupplierAmount = stores.reduce((sum, item) => sum + item.localSupplierAmount, 0)
  const warehouseAmount = readWarehouseAmount(record) ?? readWarehouseAmount(summary) ?? calculatedWarehouseAmount
  const localSupplierAmount =
    readLocalSupplierAmount(record) ?? readLocalSupplierAmount(summary) ?? calculatedLocalSupplierAmount

  return {
    endMonth,
    months,
    warehouseAmount,
    // TotalAmount 已是最终未税金额，这里只做字段归一化，不执行任何 GST 换算。
    localSupplierAmount,
    totalAmount: readTotalAmount(record) ?? readTotalAmount(summary) ?? warehouseAmount + localSupplierAmount,
    stores,
  }
}

function normalizeSupplierMonth(raw: unknown) {
  const record = asRecord(raw)
  const month = readString(record.month, record.Month)
  if (!month) return null
  return {
    month,
    amount: readNumber(record.amount, record.Amount, record.totalAmount, record.TotalAmount) ?? 0,
  }
}

function normalizeSupplier(raw: unknown, months: string[]) {
  const record = asRecord(raw)
  const supplierCode = readString(record.supplierCode, record.SupplierCode)
  const rawSourceType = readString(record.sourceType, record.SourceType)?.toLocaleUpperCase()
  const sourceCode = readString(record.sourceCode, record.SourceCode) ?? supplierCode ?? 'UNASSIGNED'
  const explicitIsWarehouse = readBoolean(record.isWarehouse, record.IsWarehouse)
  const isUnassigned = readBoolean(record.isUnassigned, record.IsUnassigned) ?? false
  // 来源类型是业务判定依据，不能把编码恰好为 WAREHOUSE_ORDER 的真实供应商误认成虚拟仓库行。
  const isWarehouse = explicitIsWarehouse ?? rawSourceType === 'WAREHOUSE_ORDER'
  const sourceType: LocalPurchaseSupplierSummary['sourceType'] = isWarehouse
    ? 'WAREHOUSE_ORDER'
    : 'LOCAL_SUPPLIER'
  const rawMonthAmounts = readArray(
    record.monthlyAmounts,
    record.MonthlyAmounts,
    record.monthAmounts,
    record.MonthAmounts,
    record.months,
    record.Months,
  )
  const monthAmountMap = new Map(
    rawMonthAmounts
      .map(normalizeSupplierMonth)
      .filter((item): item is NonNullable<ReturnType<typeof normalizeSupplierMonth>> => item !== null)
      .map((item) => [item.month, item]),
  )
  const monthlyAmounts = months.map((month) => monthAmountMap.get(month) ?? { month, amount: 0 })
  return {
    // 虚拟未匹配来源与真实业务编码可能同为 UNASSIGNED，行键必须包含显式身份。
    rowKey: `${sourceType}:${isUnassigned}:${sourceCode}`,
    sourceCode,
    sourceType,
    supplierCode,
    supplierName: readString(record.supplierName, record.SupplierName, record.sourceName, record.SourceName)
      ?? (isWarehouse ? 'WAREHOUSE_ORDER' : supplierCode ?? sourceCode),
    isWarehouse,
    isUnassigned,
    monthlyAmounts,
    totalAmount: readTotalAmount(record) ?? monthlyAmounts.reduce((sum, item) => sum + item.amount, 0),
  }
}

function normalizeSupplierDetailResponse(
  raw: unknown,
  requestedStoreCode: string,
  requestedEndMonth: string,
): LocalPurchaseSupplierDetailResponse {
  const record = asRecord(raw)
  const endMonth = readString(record.endMonth, record.EndMonth) ?? requestedEndMonth
  const storeCode = readString(record.storeCode, record.StoreCode) ?? requestedStoreCode
  const months = readMonths(record, endMonth)
  const suppliers = readArray(
    record.suppliers,
    record.Suppliers,
    record.sources,
    record.Sources,
    record.supplierSummaries,
    record.SupplierSummaries,
  ).map((item) => normalizeSupplier(item, months))
  const summary = readSummaryRecord(record)
  const warehouseAmount = readWarehouseAmount(record)
    ?? readWarehouseAmount(summary)
    ?? suppliers.filter((item) => item.isWarehouse).reduce((sum, item) => sum + item.totalAmount, 0)
  const localSupplierAmount = readLocalSupplierAmount(record)
    ?? readLocalSupplierAmount(summary)
    ?? suppliers.filter((item) => !item.isWarehouse).reduce((sum, item) => sum + item.totalAmount, 0)

  return {
    storeCode,
    storeName: readString(record.storeName, record.StoreName) ?? storeCode,
    endMonth,
    months,
    warehouseAmount,
    localSupplierAmount,
    totalAmount: readTotalAmount(record) ?? readTotalAmount(summary) ?? warehouseAmount + localSupplierAmount,
    suppliers,
  }
}

export async function getLocalPurchaseDashboard(
  endMonth: string,
  signal?: AbortSignal,
): Promise<LocalPurchaseDashboardResponse> {
  const response = await request.get<ApiResponse<LocalPurchaseDashboardResponse> | LocalPurchaseDashboardResponse>(
    API_BASE,
    { params: { endMonth }, signal },
  )
  return normalizeDashboardResponse(unwrapApiData(response), endMonth)
}

export async function getLocalPurchaseSupplierDetails(
  storeCode: string,
  endMonth: string,
  signal?: AbortSignal,
): Promise<LocalPurchaseSupplierDetailResponse> {
  const response = await request.get<ApiResponse<LocalPurchaseSupplierDetailResponse> | LocalPurchaseSupplierDetailResponse>(
    `${API_BASE}/stores/${encodeURIComponent(storeCode)}/suppliers`,
    { params: { endMonth }, signal },
  )
  return normalizeSupplierDetailResponse(unwrapApiData(response), storeCode, endMonth)
}

export const __localPurchaseDashboardServiceTestOnly = {
  normalizeDashboardResponse,
  normalizeSupplierDetailResponse,
}
