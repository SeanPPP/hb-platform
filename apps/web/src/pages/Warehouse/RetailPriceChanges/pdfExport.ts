import type {
  RetailPriceChangeItem,
  RetailPriceChangesFilters,
  RetailPriceChangesPagedResult,
} from './logic'

export const RETAIL_PRICE_CHANGES_PDF_PAGE_SIZE = 100

export interface RetailPriceChangesPdfRow {
  [key: string]: string | number | boolean | undefined
  productImage: string
  itemNumber: string
  barcode: string
  barcodeImage: string
  latestRetailPrice: number | undefined
  lastPriceChangedAt: string
}

type RetailPriceChangesPageLoader = (
  pageNumber: number,
  pageSize: number,
) => Promise<RetailPriceChangesPagedResult>

export async function collectRetailPriceChangesForPdf(
  loadPage: RetailPriceChangesPageLoader,
): Promise<RetailPriceChangeItem[]> {
  const firstPage = await loadPage(1, RETAIL_PRICE_CHANGES_PDF_PAGE_SIZE)
  const expectedTotal = Math.max(firstPage.total, firstPage.items.length)
  const allItems = [...firstPage.items]
  const totalPages = Math.ceil(expectedTotal / RETAIL_PRICE_CHANGES_PDF_PAGE_SIZE)

  // API 每页最多 100 条；按稳定排序逐页读取，确保 PDF 不受当前表格分页限制。
  for (let pageNumber = 2; pageNumber <= totalPages; pageNumber += 1) {
    const page = await loadPage(pageNumber, RETAIL_PRICE_CHANGES_PDF_PAGE_SIZE)
    allItems.push(...page.items)
  }

  return allItems.slice(0, expectedTotal)
}

export function mapRetailPriceChangesPdfRows(
  items: RetailPriceChangeItem[],
  formatDateTime: (value: string | undefined) => string,
): RetailPriceChangesPdfRow[] {
  return items.map((item) => ({
    productImage: item.productImage || '',
    itemNumber: item.itemNumber || '--',
    barcode: item.barcode || '',
    barcodeImage: item.barcode || '',
    latestRetailPrice: item.latestRetailPrice ?? undefined,
    lastPriceChangedAt: formatDateTime(item.lastPriceChangedAtUtc),
  }))
}

export function buildRetailPriceChangesPdfFileName(
  baseName: string,
  filters: Pick<RetailPriceChangesFilters, 'startDate' | 'endDate'>,
) {
  const safeBaseName = baseName.replace(/[\\/:*?"<>|]+/g, '-').replace(/\s+/g, ' ').trim()
  return `${safeBaseName || 'warehouse-retail-price-changes'}_${filters.startDate}_${filters.endDate}`
}
