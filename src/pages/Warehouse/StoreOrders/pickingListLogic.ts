import type { StoreOrderDetail, StoreOrderDetailLine } from '../../../types/storeOrder'

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
    allocQuantity: string
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
  // 默认行高按实际打印预览校准，让普通明细页容纳 28 行，减少页尾空白。
  rowHeightMm: 8.4,
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

// 统一管理配货单的派生展示逻辑，避免组件里散落业务格式化判断。
export function formatInnerPackCount(orderQuantity: number, minOrderQuantity?: number) {
  if (!Number.isFinite(orderQuantity)) {
    return ''
  }

  if (typeof minOrderQuantity !== 'number' || !Number.isFinite(minOrderQuantity) || minOrderQuantity <= 1) {
    return ''
  }

  const innerPackCount = orderQuantity / minOrderQuantity
  if (!Number.isFinite(innerPackCount)) {
    return ''
  }

  return Number.isInteger(innerPackCount) ? String(innerPackCount) : innerPackCount.toFixed(1)
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
        ? order.totalOrderVolume.toFixed(4)
        : typeof order.totalVolume === 'number'
          ? order.totalVolume.toFixed(4)
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
      texts.detailHeaders.allocQuantity,
    ],
    detailRows: items.map((item, index) => [
      index + 1,
      item.itemNumber || '',
      item.locationCode || '',
      item.productName || '',
      formatExcelCurrency(item.importPrice),
      item.rrp === undefined || item.rrp === null ? '' : formatExcelCurrency(item.rrp),
      formatInnerPackCount(item.quantity, item.minOrderQuantity),
      Number(item.quantity || 0),
      Number(item.allocQuantity ?? 0),
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
  const pages: PickingListPdfPage[] = []

  let startIndex = 0
  while (startIndex < items.length) {
    const remaining = items.length - startIndex
    const canFitRestWithSummary = remaining <= summaryRowsPerPage
    const shouldSplitBeforeSummary = hasSummary && !canFitRestWithSummary && remaining <= regularRowsPerPage
    // 尾页需要显示备注和汇总时，最多只放汇总页容量；否则 26-28 行会挤占汇总区。
    const rowsForPage = canFitRestWithSummary
      ? summaryRowsPerPage
      : shouldSplitBeforeSummary
        ? Math.max(1, remaining - summaryRowsPerPage)
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
