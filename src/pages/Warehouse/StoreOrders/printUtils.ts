import html2canvas from 'html2canvas'
import jsPDF from 'jspdf'
import type { StoreDto } from '../../../types/store'

export interface StorePrintInfo {
  storeName?: string
  storeCode?: string
  address?: string
  contactPhone?: string
  contactEmail?: string
}

export function formatPrintDate(value?: string, withTime = true) {
  const target = value ? new Date(value) : new Date()
  if (Number.isNaN(target.getTime())) {
    return value || '--'
  }

  return withTime ? target.toLocaleString('zh-CN', { hour12: false }) : target.toLocaleDateString('zh-CN')
}

export function formatCurrency(value?: number) {
  return `$${Number(value ?? 0).toFixed(2)}`
}

export function sanitizeFileNamePart(value?: string) {
  const normalized = (value || '')
    .replace(/[\\/:*?"<>|]/g, '_')
    .replace(/\s+/g, ' ')
    .trim()
    .replace(/[\s_]+/g, '_')

  return normalized || '未知分店'
}

export function buildDocumentFileName(prefix: string, storeName: string | undefined, orderNo: string | undefined, extension: string) {
  const safePrefix = sanitizeFileNamePart(prefix)
  const safeStoreName = sanitizeFileNamePart(storeName)
  const safeOrderNo = sanitizeFileNamePart(orderNo || '未知订单')
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

export async function downloadElementAsPdf(element: HTMLElement, fileName: string) {
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
  const pageHeightInPx = (pdfHeight * imageWidth) / pdfWidth

  let renderedHeight = 0
  let pageIndex = 0

  while (renderedHeight < imageHeight) {
    const currentSliceHeight = Math.min(pageHeightInPx, imageHeight - renderedHeight)
    const sliceCanvas = document.createElement('canvas')
    sliceCanvas.width = imageWidth
    sliceCanvas.height = currentSliceHeight

    const context = sliceCanvas.getContext('2d')
    if (!context) {
      throw new Error('创建 PDF 临时画布失败')
    }

    context.fillStyle = '#ffffff'
    context.fillRect(0, 0, sliceCanvas.width, sliceCanvas.height)
    context.drawImage(
      canvas,
      0,
      renderedHeight,
      imageWidth,
      currentSliceHeight,
      0,
      0,
      imageWidth,
      currentSliceHeight,
    )

    const imageData = sliceCanvas.toDataURL('image/png')
    if (pageIndex > 0) {
      pdf.addPage()
    }

    const imageHeightInPdf = (currentSliceHeight * pdfWidth) / imageWidth
    pdf.addImage(imageData, 'PNG', 0, 0, pdfWidth, imageHeightInPdf)

    renderedHeight += currentSliceHeight
    pageIndex += 1
  }

  pdf.save(fileName)
}
