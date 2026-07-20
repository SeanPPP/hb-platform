import type {
  LocalPurchaseMonthAmount,
  LocalPurchaseSupplierSummary,
  LocalPurchaseStoreSummary,
} from '../../../types/localPurchaseDashboard'

export interface PurchaseMonthRow {
  month: string
}

export type PurchaseReportViewState = 'loading' | 'error' | 'empty' | 'ready'

export interface PurchaseReportViewStateInput {
  loading: boolean
  hasError: boolean
  hasReport: boolean
  hasRows: boolean
}

export function resolvePurchaseReportViewState(
  input: PurchaseReportViewStateInput,
): PurchaseReportViewState {
  // 错误优先于加载和空态；只有成功取得报表后才允许展示 Empty。
  if (input.hasError) return 'error'
  if (!input.hasReport || input.loading) return 'loading'
  return input.hasRows ? 'ready' : 'empty'
}

export function getPurchaseMonthColumnLayout() {
  return {
    key: 'month' as const,
    fixed: 'left' as const,
    width: 112,
  }
}

export function getPurchaseMatrixScroll(storeCount: number) {
  return {
    x: 112 + storeCount * 184,
    y: 'max(320px, calc(100vh - 520px))',
  }
}

export function getSupplierDetailScroll(monthCount: number) {
  return { x: 220 + monthCount * 132 + 148 }
}

export function buildRollingMonths(endMonth: string): string[] {
  const match = /^(\d{4})-(0[1-9]|1[0-2])$/.exec(endMonth)
  if (!match) {
    throw new Error('结束月份必须是 YYYY-MM')
  }

  const endYear = Number(match[1])
  const endMonthIndex = Number(match[2]) - 1

  // 只做年月整数运算，避免浏览器时区把月边界偏移到前一天。
  return Array.from({ length: 12 }, (_, index) => {
    const offset = index - 11
    const absoluteMonth = endYear * 12 + endMonthIndex + offset
    const year = Math.floor(absoluteMonth / 12)
    const month = (absoluteMonth % 12 + 12) % 12 + 1
    return `${year}-${String(month).padStart(2, '0')}`
  })
}

export function sortPurchaseMonthsDescending(months: string[]): string[] {
  // 接口和归一化层继续保留原始月份顺序，仅在表格展示层复制后倒序。
  return [...months].sort((left, right) => right.localeCompare(left))
}

export function buildPurchaseMonthRows(months: string[]): PurchaseMonthRow[] {
  return sortPurchaseMonthsDescending(months).map((month) => ({ month }))
}

export function filterPurchaseStores(
  stores: LocalPurchaseStoreSummary[],
  selectedStoreCodes: string[],
): LocalPurchaseStoreSummary[] {
  if (selectedStoreCodes.length === 0) return stores

  const selectedCodes = new Set(selectedStoreCodes)
  return stores.filter((store) => selectedCodes.has(store.storeCode))
}

const emptyMonthAmount: LocalPurchaseMonthAmount = {
  month: '',
  warehouseAmount: 0,
  localSupplierAmount: 0,
  totalAmount: 0,
  salesAmount: 0,
}

export function getPurchaseStoreMonthAmount(
  store: LocalPurchaseStoreSummary,
  month: string,
): LocalPurchaseMonthAmount {
  const amount = store.monthlyAmounts.find((item) => item.month === month)
  // 兼容营业额字段上线前的缓存响应；返回新对象也避免读取辅助函数改写源数组。
  return {
    ...emptyMonthAmount,
    month,
    ...amount,
    salesAmount: amount?.salesAmount ?? 0,
  }
}

export function sortPurchaseSuppliers(
  suppliers: LocalPurchaseSupplierSummary[],
): LocalPurchaseSupplierSummary[] {
  return [...suppliers].sort((left, right) => {
    if (left.isWarehouse !== right.isWarehouse) {
      return left.isWarehouse ? -1 : 1
    }
    return right.totalAmount - left.totalAmount || left.supplierName.localeCompare(right.supplierName)
  })
}

export interface LatestRequestGuard {
  begin: () => number
  isLatest: (requestId: number) => boolean
  invalidate: () => void
}

export function createLatestRequestGuard(): LatestRequestGuard {
  let latestRequestId = 0

  return {
    begin() {
      latestRequestId += 1
      return latestRequestId
    },
    isLatest(requestId) {
      return latestRequestId === requestId
    },
    invalidate() {
      latestRequestId += 1
    },
  }
}

// 金额矩阵会高频调用格式化逻辑，复用实例避免每个单元格重复构造格式化器。
const purchaseAmountFormatter = new Intl.NumberFormat('en-AU', {
  style: 'currency',
  currency: 'AUD',
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
})

export function formatPurchaseAmount(value: number | null | undefined): string {
  if (value == null || !Number.isFinite(value)) return '--'
  return purchaseAmountFormatter.format(value)
}

export function getSupplierDisplayName(
  supplier: LocalPurchaseSupplierSummary,
  labels: { warehouse: string; unassigned: string },
): string {
  if (supplier.isWarehouse) return labels.warehouse
  if (supplier.isUnassigned) return labels.unassigned
  return supplier.supplierName
}
