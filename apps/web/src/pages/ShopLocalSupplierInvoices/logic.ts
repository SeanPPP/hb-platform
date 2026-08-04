import type {
  ShopLocalSupplierInvoiceGridQuery,
  ShopLocalSupplierInvoiceGridRequest,
  ShopLocalSupplierInvoiceGridTextFilter,
  ShopLocalSupplierInvoiceListPageSize,
} from '../../types/localSupplierInvoice'

const SHOP_INVOICE_LIST_PAGE_SIZES: readonly ShopLocalSupplierInvoiceListPageSize[] = [20, 50, 100]

function normalizePage(page: number | undefined) {
  return typeof page === 'number' && Number.isFinite(page) && page > 0 ? Math.trunc(page) : 1
}

function normalizePageSize(pageSize: number | undefined): ShopLocalSupplierInvoiceListPageSize {
  return SHOP_INVOICE_LIST_PAGE_SIZES.includes(pageSize as ShopLocalSupplierInvoiceListPageSize)
    ? (pageSize as ShopLocalSupplierInvoiceListPageSize)
    : 20
}

function buildTextFilter(
  value: string | undefined,
  type: ShopLocalSupplierInvoiceGridTextFilter['type'],
): ShopLocalSupplierInvoiceGridTextFilter | null {
  const filter = value?.trim()
  return filter ? { filterType: 'text', type, filter } : null
}

export function buildShopLocalSupplierInvoiceGridRequest(
  query: ShopLocalSupplierInvoiceGridQuery,
): ShopLocalSupplierInvoiceGridRequest {
  const page = normalizePage(query.page)
  const pageSize = normalizePageSize(query.pageSize)
  const startRow = (page - 1) * pageSize
  const filterModel: ShopLocalSupplierInvoiceGridRequest['filterModel'] = {}
  const storeFilter = buildTextFilter(query.storeCode, 'equals')
  const supplierFilter = buildTextFilter(query.supplierCode, 'equals')
  const productFilter = buildTextFilter(query.productKeyword, 'contains')

  if (storeFilter) filterModel.StoreCode = storeFilter
  if (supplierFilter) filterModel.SupplierCode = supplierFilter
  if (productFilter) filterModel.ProductKeyword = productFilter

  return {
    startRow,
    endRow: startRow + pageSize,
    pageSize,
    filterModel,
    sortModel: [{ colId: 'OrderDate', sort: 'desc' }],
  }
}
