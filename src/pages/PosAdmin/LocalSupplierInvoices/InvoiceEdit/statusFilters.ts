import type { LocalSupplierInvoiceItemDto } from '../../../../types/localSupplierInvoice'

export type PriceFilter = 'all' | 'up' | 'down'
export type ProductStatusFilter = 'notDetected' | 'exists' | 'notExists'
export type BarcodeStatusFilter = 'notDetected' | 'normal' | 'noMatch' | 'multiMatch'
export type StatusFilterValue<T extends string> = 'all' | T

export interface InvoiceDetailFilters {
  searchText: string
  priceFilter: PriceFilter
  productStatusFilter: StatusFilterValue<ProductStatusFilter>
  barcodeStatusFilter: StatusFilterValue<BarcodeStatusFilter>
}

export interface DetailStatusStats {
  product: Record<ProductStatusFilter, number>
  barcode: Record<BarcodeStatusFilter, number>
}

export function getProductStatusFilter(detail: LocalSupplierInvoiceItemDto): ProductStatusFilter {
  const count = detail.existingProductCount
  if (count === undefined || count === null) return 'notDetected'
  if (count > 0) return 'exists'
  return 'notExists'
}

export function getBarcodeStatusFilter(detail: LocalSupplierInvoiceItemDto): BarcodeStatusFilter {
  const status = detail.barcodeStatus
  const count = detail.barcodeMatchCount ?? 0
  if (status === undefined || status === null || status === 0) return 'notDetected'
  if (status === 1) return 'normal'
  if (count === 0) return 'noMatch'
  return 'multiMatch'
}

export function getDetailStatusStats(details: LocalSupplierInvoiceItemDto[]): DetailStatusStats {
  const stats: DetailStatusStats = {
    product: {
      notDetected: 0,
      exists: 0,
      notExists: 0,
    },
    barcode: {
      notDetected: 0,
      normal: 0,
      noMatch: 0,
      multiMatch: 0,
    },
  }

  details.forEach((item) => {
    stats.product[getProductStatusFilter(item)] += 1
    stats.barcode[getBarcodeStatusFilter(item)] += 1
  })

  return stats
}

export function toggleStatusFilter<T extends string>(
  currentFilter: StatusFilterValue<T>,
  nextFilter: T,
): StatusFilterValue<T> {
  return currentFilter === nextFilter ? 'all' : nextFilter
}

export function filterInvoiceDetails(
  details: LocalSupplierInvoiceItemDto[],
  filters: InvoiceDetailFilters,
): LocalSupplierInvoiceItemDto[] {
  let result = details

  const keyword = filters.searchText.trim().toLowerCase()
  if (keyword) {
    result = result.filter(
      (item) =>
        item.productCode?.toLowerCase().includes(keyword) ||
        item.itemNumber?.toLowerCase().includes(keyword) ||
        item.barcode?.toLowerCase().includes(keyword) ||
        item.productName?.toLowerCase().includes(keyword) ||
        item.storeProductCode?.toLowerCase().includes(keyword),
    )
  }

  if (filters.priceFilter === 'up') {
    result = result.filter((item) => hasPurchasePriceChanged(item, 'up'))
  } else if (filters.priceFilter === 'down') {
    result = result.filter((item) => hasPurchasePriceChanged(item, 'down'))
  }

  // 状态过滤与搜索、涨跌筛选按 AND 叠加。
  if (filters.productStatusFilter !== 'all') {
    result = result.filter((item) => getProductStatusFilter(item) === filters.productStatusFilter)
  }

  if (filters.barcodeStatusFilter !== 'all') {
    result = result.filter((item) => getBarcodeStatusFilter(item) === filters.barcodeStatusFilter)
  }

  return result
}

function hasPurchasePriceChanged(item: LocalSupplierInvoiceItemDto, direction: 'up' | 'down') {
  if (
    item.lastPurchasePrice === undefined ||
    item.lastPurchasePrice === null ||
    item.lastPurchasePrice <= 0 ||
    item.purchasePrice === undefined ||
    item.purchasePrice === null
  ) {
    return false
  }

  return direction === 'up'
    ? item.purchasePrice > item.lastPurchasePrice
    : item.purchasePrice < item.lastPurchasePrice
}
