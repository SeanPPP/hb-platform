import dayjs, { type Dayjs } from 'dayjs'

export const TRANSPARENT_IMAGE_FALLBACK =
  'data:image/gif;base64,R0lGODlhAQABAAD/ACwAAAAAAQABAAACADs='
export const DEFAULT_PRODUCT_IMAGE_BASE_URL =
  'https://hotbargain-yw-2023-1300114625.cos.ap-shanghai.myqcloud.com/YW200'
export const DEFAULT_PURCHASE_SALES_ANALYSIS_PAGE_SIZE = 100
export const PURCHASE_SALES_ANALYSIS_PAGE_SIZE_OPTIONS = [50, 100, 200] as const
export const PURCHASE_SALES_ANALYSIS_DEFAULT_SORT_BY = 'latestPurchaseDate'
export const PURCHASE_SALES_ANALYSIS_DEFAULT_SORT_ORDER = 'desc' as const
export const PURCHASE_SALES_ANALYSIS_SORT_FIELDS = [
  'itemNumber',
  'productName',
  'latestPurchaseDate',
  'previousPurchaseDate',
  'purchaseIntervalDays',
  'salesBetweenPurchases',
  'salesQty30',
  'salesQty60',
  'salesQty90',
] as const

export type PurchaseSalesAnalysisSorterOrder = 'ascend' | 'descend' | undefined | null

export function getDefaultPurchaseSalesAnalysisDateRange(referenceDate: Dayjs = dayjs()) {
  return [referenceDate.subtract(180, 'day'), referenceDate] as [Dayjs, Dayjs]
}

export function normalizePurchaseSalesAnalysisPageSize(value?: number) {
  return PURCHASE_SALES_ANALYSIS_PAGE_SIZE_OPTIONS.includes(
    value as (typeof PURCHASE_SALES_ANALYSIS_PAGE_SIZE_OPTIONS)[number],
  )
    ? value!
    : DEFAULT_PURCHASE_SALES_ANALYSIS_PAGE_SIZE
}

export function toPurchaseSalesAnalysisSort(
  sortBy?: string,
  sorterOrder?: PurchaseSalesAnalysisSorterOrder,
) {
  const isAllowedSortField = PURCHASE_SALES_ANALYSIS_SORT_FIELDS.includes(
    sortBy as (typeof PURCHASE_SALES_ANALYSIS_SORT_FIELDS)[number],
  )

  if (!sortBy || !sorterOrder || !isAllowedSortField) {
    return {
      sortBy: PURCHASE_SALES_ANALYSIS_DEFAULT_SORT_BY,
      sortOrder: PURCHASE_SALES_ANALYSIS_DEFAULT_SORT_ORDER,
    }
  }

  return {
    sortBy,
    sortOrder: sorterOrder === 'ascend' ? ('asc' as const) : ('desc' as const),
  }
}

export function buildDefaultProductImageUrl(itemNumber?: string | null, productCode?: string | null) {
  const imageKey = String(itemNumber || productCode || '').trim()
  return imageKey
    ? `${DEFAULT_PRODUCT_IMAGE_BASE_URL}/${encodeURIComponent(imageKey)}.jpg`
    : ''
}

export function buildPurchaseSalesAnalysisImageSourceChain(
  productImage?: string | null,
  itemNumber?: string | null,
  productCode?: string | null,
) {
  const chain = [
    String(productImage || '').trim(),
    buildDefaultProductImageUrl(itemNumber, productCode),
    TRANSPARENT_IMAGE_FALLBACK,
  ].filter(Boolean)

  return Array.from(new Set(chain))
}
