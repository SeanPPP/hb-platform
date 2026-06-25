import type { StoreOrderDetail, StoreOrderDetailLine } from '../../../types/storeOrder'
import { formatStoreOrderVolume } from './volumeFormat'

export interface PickingListExcelTexts {
  sheetName: string
  orderNoLabel: string
  storeLabel: string
  orderDateLabel: string
  printTimeLabel: string
  remarksLabel: string
  totalSKULabel: string
  totalOrderQtyLabel: string
  totalShipQtyLabel: string
  totalOrderVolumeLabel: string
  detailHeaders: {
    index: string
    itemNumber: string
    location: string
    productName: string
    importPrice: string
    rrp: string
    innerPackCount: string
    orderQuantity: string
  }
}

export interface PickingListExcelMeta {
  orderNoText: string
  storeText: string
  orderDateText: string
  printTimeText: string
  totalOrderVolumeText: string
}

export interface PickingListExcelData {
  sheetName: string
  overviewRows: Array<[string, string]>
  detailHeader: string[]
  detailRows: Array<Array<string | number>>
  remarksRow?: [string, string]
  totalRows: Array<[string, string | number]>
}

export interface PickingListPdfPaginationOptions {
  pageHeightMm?: number
  pagePaddingTopMm?: number
  pagePaddingBottomMm?: number
  headerHeightMm?: number
  tableHeaderHeightMm?: number
  footerHeightMm?: number
  rowHeightMm?: number
  finalSummaryHeightMm?: number
}

export interface PickingListPdfPage {
  items: StoreOrderDetailLine[]
  startIndex: number
  hasHeader: true
  footerKind: 'pageNumber'
  showSummary: boolean
}

const DEFAULT_PDF_PAGINATION_OPTIONS: Required<PickingListPdfPaginationOptions> = {
  pageHeightMm: 297,
  pagePaddingTopMm: 6,
  pagePaddingBottomMm: 12,
  headerHeightMm: 20,
  tableHeaderHeightMm: 10,
  footerHeightMm: 12,
  // 默认行高与打印 CSS 的 9mm 明细行保持一致，避免 PDF 预分页后在行中间切断。
  rowHeightMm: 9,
  finalSummaryHeightMm: 22,
}

function formatExcelCurrency(value?: number) {
  return Number(Number(value ?? 0).toFixed(2))
}

function resolvePickingListRowsPerPdfPage(
  options: Required<PickingListPdfPaginationOptions>,
  shouldReserveSummarySpace: boolean,
) {
  const summaryHeight = shouldReserveSummarySpace ? options.finalSummaryHeightMm : 0
  const availableHeight = options.pageHeightMm
    - options.pagePaddingTopMm
    - options.pagePaddingBottomMm
    - options.headerHeightMm
    - options.tableHeaderHeightMm
    - options.footerHeightMm
    - summaryHeight

  return Math.max(1, Math.floor(availableHeight / options.rowHeightMm))
}

function resolveRowsBeforeSummaryPage(
  remaining: number,
  regularRowsPerPage: number,
  summaryRowsPerPage: number,
  shortSummaryTailRows: number,
) {
  // 尾页 1-2 行时可挤回前页；否则倒数第二页满排，最后一页保留剩余明细和汇总。
  const rowsBeforeSummaryPage = Math.min(regularRowsPerPage, Math.max(1, remaining - 1))
  const summaryRows = remaining - rowsBeforeSummaryPage
  const maxMergedSummaryRows = summaryRowsPerPage + shortSummaryTailRows
  return summaryRows <= shortSummaryTailRows && remaining <= maxMergedSummaryRows ? remaining : rowsBeforeSummaryPage
}

function resolvePickingDisplayQuantity(quantity: unknown, allocQuantity?: unknown) {
  const numericQuantity = Number(quantity)
  if (Number.isFinite(numericQuantity) && numericQuantity > 0) {
    return numericQuantity
  }

  const numericAllocQuantity = Number(allocQuantity)
  return Number.isFinite(numericAllocQuantity) && numericAllocQuantity > 0 ? numericAllocQuantity : null
}

// 统一管理配货单的派生展示逻辑，包数分子必须和“订货数”列的发货数兜底口径一致。
export function formatInnerPackCount(quantity: unknown, allocQuantity: unknown, minOrderQuantity?: number) {
  if (typeof minOrderQuantity !== 'number' || !Number.isFinite(minOrderQuantity) || minOrderQuantity <= 1) {
    return ''
  }

  const displayQuantity = resolvePickingDisplayQuantity(quantity, allocQuantity)
  if (displayQuantity === null) {
    return ''
  }

  const innerPackCount = displayQuantity / minOrderQuantity
  if (!Number.isFinite(innerPackCount)) {
    return ''
  }

  return Number.isInteger(innerPackCount) ? String(innerPackCount) : innerPackCount.toFixed(1)
}

export function formatPickingOrderQuantity(quantity: unknown, allocQuantity?: unknown) {
  // 订货数为空或为 0 时，用发货数兜底显示；兜底值也为空时保持空白。
  return resolvePickingDisplayQuantity(quantity, allocQuantity) ?? ''
}

export function buildPickingListExcelData(
  order: StoreOrderDetail,
  items: StoreOrderDetailLine[],
  texts: PickingListExcelTexts,
  meta: PickingListExcelMeta = {
    orderNoText: order.orderNo || order.orderGUID || '',
    storeText: order.storeCode || '',
    orderDateText: order.orderDate || '',
    printTimeText: '',
    totalOrderVolumeText:
      typeof order.totalOrderVolume === 'number'
        ? formatStoreOrderVolume(order.totalOrderVolume)
        : typeof order.totalVolume === 'number'
          ? formatStoreOrderVolume(order.totalVolume)
          : '--',
  },
): PickingListExcelData {
  return {
    sheetName: texts.sheetName,
    overviewRows: [
      [texts.orderNoLabel, meta.orderNoText],
      [texts.storeLabel, meta.storeText],
      [texts.orderDateLabel, meta.orderDateText],
      [texts.printTimeLabel, meta.printTimeText],
    ],
    detailHeader: [
      texts.detailHeaders.index,
      texts.detailHeaders.itemNumber,
      texts.detailHeaders.location,
      texts.detailHeaders.productName,
      texts.detailHeaders.importPrice,
      texts.detailHeaders.rrp,
      texts.detailHeaders.innerPackCount,
      texts.detailHeaders.orderQuantity,
    ],
    detailRows: items.map((item, index) => [
      index + 1,
      item.itemNumber || '',
      item.locationCode || '',
      item.productName || '',
      formatExcelCurrency(item.importPrice),
      item.rrp === undefined || item.rrp === null ? '' : formatExcelCurrency(item.rrp),
      formatInnerPackCount(item.quantity, item.allocQuantity, item.minOrderQuantity),
      formatPickingOrderQuantity(item.quantity, item.allocQuantity),
    ]),
    remarksRow: order.remarks ? [texts.remarksLabel, order.remarks] : undefined,
    totalRows: [
      [texts.totalSKULabel, order.totalSKU ?? items.length],
      [texts.totalOrderQtyLabel, order.totalQuantity],
      [texts.totalShipQtyLabel, order.totalAllocQuantity ?? 0],
      [texts.totalOrderVolumeLabel, meta.totalOrderVolumeText],
    ],
  }
}

export function buildPickingListPdfPages(
  items: StoreOrderDetailLine[],
  hasSummary: boolean,
  paginationOptions: PickingListPdfPaginationOptions = {},
): PickingListPdfPage[] {
  const options = {
    ...DEFAULT_PDF_PAGINATION_OPTIONS,
    ...paginationOptions,
  }
  const regularRowsPerPage = resolvePickingListRowsPerPdfPage(options, false)
  const summaryRowsPerPage = resolvePickingListRowsPerPdfPage(options, hasSummary)
  const shortSummaryTailRows = 2
  const pages: PickingListPdfPage[] = []

  let startIndex = 0
  while (startIndex < items.length) {
    const remaining = items.length - startIndex
    const canFitRestWithSummary = remaining <= summaryRowsPerPage
    const shouldBalanceTailPages = hasSummary && !canFitRestWithSummary && remaining <= regularRowsPerPage + summaryRowsPerPage
    // 尾页需要显示备注和汇总时，短尾可挤回前页；否则倒数第二页尽量满排。
    const rowsForPage = canFitRestWithSummary
      ? summaryRowsPerPage
      : shouldBalanceTailPages
        ? resolveRowsBeforeSummaryPage(remaining, regularRowsPerPage, summaryRowsPerPage, shortSummaryTailRows)
        : regularRowsPerPage
    const pageItems = items.slice(startIndex, startIndex + rowsForPage)

    pages.push({
      items: pageItems,
      startIndex,
      hasHeader: true,
      footerKind: 'pageNumber',
      showSummary: hasSummary && canFitRestWithSummary,
    })
    startIndex += pageItems.length
  }

  if (pages.length === 0) {
    pages.push({
      items: [],
      startIndex: 0,
      hasHeader: true,
      footerKind: 'pageNumber',
      showSummary: hasSummary,
    })
  }

  // 最后一页必须承载备注和汇总，避免汇总区在 PDF 中被切断或丢失。
  pages.forEach((page, pageIndex) => {
    page.showSummary = hasSummary && pageIndex === pages.length - 1
  })

  return pages
}
