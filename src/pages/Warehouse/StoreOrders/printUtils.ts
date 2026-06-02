import type { StoreDto } from '../../../types/store'

export interface StorePrintInfo {
  storeName?: string
  storeCode?: string
  address?: string
  contactPhone?: string
  contactEmail?: string
}

interface DocumentFileNameFallbackTexts {
  unknownOrder: string
  unknownStore: string
}

interface DownloadPdfOptions {
  createCanvasContextErrorMessage?: string
  avoidBreakOffsets?: number[]
}

export const PDF_IMAGE_FORMAT = 'JPEG'
export const PDF_IMAGE_MIME_TYPE = 'image/jpeg'
export const PDF_IMAGE_QUALITY = 0.95

export interface PdfSlicePlanItem {
  offsetY: number
  height: number
}

function normalizePrintLocale(locale?: string) {
  // 打印只需要当前需求中的中英文格式，其他语种先按中文兜底，避免输出不一致。
  return locale?.toLowerCase().startsWith('en') ? 'en-US' : 'zh-CN'
}

export function formatPrintDate(value?: string, withTime = true, locale?: string) {
  const target = value ? new Date(value) : new Date()
  if (Number.isNaN(target.getTime())) {
    return value || '--'
  }

  const printLocale = normalizePrintLocale(locale)
  return withTime ? target.toLocaleString(printLocale, { hour12: false }) : target.toLocaleDateString(printLocale)
}

export function formatCurrency(value?: number) {
  return `$${Number(value ?? 0).toFixed(2)}`
}

export function sanitizeFileNamePart(value: string) {
  const normalized = (value || '')
    .replace(/[\\/:*?"<>|]/g, '_')
    .replace(/\s+/g, ' ')
    .trim()
    .replace(/[\s_]+/g, '_')

  return normalized
}

export function buildDocumentFileName(
  prefix: string,
  storeName: string | undefined,
  orderNo: string | undefined,
  extension: string,
  fallbackTexts: DocumentFileNameFallbackTexts,
) {
  // 文件名中的未知文案交给调用方注入翻译，工具函数只负责清洗与拼接。
  const unknownStoreText = fallbackTexts.unknownStore
  const unknownOrderText = fallbackTexts.unknownOrder
  const safePrefix = sanitizeFileNamePart(prefix)
  const safeStoreName = sanitizeFileNamePart(storeName || unknownStoreText)
  const safeOrderNo = sanitizeFileNamePart(orderNo || unknownOrderText)
  return `${safePrefix}_${safeStoreName}_${safeOrderNo}.${extension}`
}

export function resolveStorePrintInfo(storeCode?: string, store?: StoreDto | null): StorePrintInfo {
  return {
    storeName: store?.storeName || storeCode || '--',
    storeCode: storeCode || store?.storeCode,
    address: store?.address,
    contactPhone: store?.contactPhone,
    contactEmail: store?.contactEmail,
  }
}

export function buildPdfSlicePlan(imageHeight: number, pageHeightInPx: number, avoidBreakOffsets: number[] = []): PdfSlicePlanItem[] {
  const normalizedImageHeight = Math.max(0, Math.floor(imageHeight))
  if (!Number.isFinite(normalizedImageHeight) || normalizedImageHeight <= 0) {
    return []
  }

  // 切片高度必须是正整数像素，避免浮点高度造成空切片和损坏的图片数据。
  const normalizedPageHeight = Math.max(1, Math.floor(pageHeightInPx))
  const normalizedBreakOffsets = Array.from(
    new Set(
      avoidBreakOffsets
        .filter((offset) => Number.isFinite(offset))
        .map((offset) => Math.floor(offset))
        .filter((offset) => offset > 0 && offset <= normalizedImageHeight),
    ),
  ).sort((left, right) => left - right)
  const slices: PdfSlicePlanItem[] = []

  let offsetY = 0
  while (offsetY < normalizedImageHeight) {
    const defaultEndY = Math.min(offsetY + normalizedPageHeight, normalizedImageHeight)
    const candidateBreakOffsets = normalizedBreakOffsets.filter((breakOffset) => breakOffset > offsetY && breakOffset <= defaultEndY)
    const boundaryEndY = candidateBreakOffsets[candidateBreakOffsets.length - 1]
    const endY = boundaryEndY ?? defaultEndY
    const height = Math.max(1, endY - offsetY)
    slices.push({ offsetY, height })
    offsetY += height
  }

  return slices
}

export function paintPdfSlice(
  context: CanvasRenderingContext2D,
  sourceCanvas: HTMLCanvasElement,
  imageWidth: number,
  slice: PdfSlicePlanItem,
) {
  context.fillStyle = '#ffffff'
  context.fillRect(0, 0, imageWidth, slice.height)
  context.drawImage(
    sourceCanvas,
    0,
    slice.offsetY,
    imageWidth,
    slice.height,
    0,
    0,
    imageWidth,
    slice.height,
  )
}

export function getPdfSliceImageData(sliceCanvas: HTMLCanvasElement) {
  return sliceCanvas.toDataURL(PDF_IMAGE_MIME_TYPE, PDF_IMAGE_QUALITY)
}

export function collectElementBreakOffsets(root: HTMLElement, rowSelector: string, footerSelector?: string) {
  const rootTop = root.getBoundingClientRect().top
  const rows = Array.from(root.querySelectorAll<HTMLElement>(rowSelector))
  const footer = footerSelector ? root.querySelector<HTMLElement>(footerSelector) : null

  // 这些偏移会被换算到 canvas 像素，用来让 PDF 切页尽量落在完整内容块之间。
  const offsets = rows.map((row) => row.getBoundingClientRect().top - rootTop)
  if (footer) {
    offsets.push(footer.getBoundingClientRect().top - rootTop)
  }

  offsets.push(root.scrollHeight)
  return offsets.filter((offset) => Number.isFinite(offset) && offset > 0)
}

export async function downloadElementAsPdf(element: HTMLElement, fileName: string, options?: DownloadPdfOptions) {
  const [{ default: html2canvas }, { default: jsPDF }] = await Promise.all([import('html2canvas'), import('jspdf')])
  const canvas = await html2canvas(element, {
    scale: 2,
    useCORS: true,
    logging: false,
    backgroundColor: '#ffffff',
  })

  const pdf = new jsPDF('p', 'mm', 'a4')
  const pdfWidth = 210
  const pdfHeight = 297
  const imageWidth = canvas.width
  const imageHeight = canvas.height
  const canvasScaleY = element.scrollHeight > 0 ? canvas.height / element.scrollHeight : 1
  const avoidBreakOffsets = options?.avoidBreakOffsets?.map((offset) => offset * canvasScaleY) ?? []
  const slicePlan = buildPdfSlicePlan(imageHeight, (pdfHeight * imageWidth) / pdfWidth, avoidBreakOffsets)

  slicePlan.forEach((slice, pageIndex) => {
    const sliceCanvas = document.createElement('canvas')
    sliceCanvas.width = imageWidth
    sliceCanvas.height = slice.height

    const context = sliceCanvas.getContext('2d')
    if (!context) {
      // 这里允许页面传入国际化错误文案，避免工具层写死提示语言。
      throw new Error(options?.createCanvasContextErrorMessage || '创建 PDF 临时画布失败')
    }

    paintPdfSlice(context, canvas, imageWidth, slice)

    const imageData = getPdfSliceImageData(sliceCanvas)
    if (pageIndex > 0) {
      pdf.addPage()
    }

    const imageHeightInPdf = (slice.height * pdfWidth) / imageWidth
    pdf.addImage(imageData, PDF_IMAGE_FORMAT, 0, 0, pdfWidth, imageHeightInPdf)
  })

  pdf.save(fileName)
}
