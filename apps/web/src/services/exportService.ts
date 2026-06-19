import ExcelJS from 'exceljs'
import { generateBarcodeImages } from '../utils/barcode'
import { reportExternalFetchError } from '../utils/centerLogClient'
import type { ProductGradeListItem } from '../types/productGrade'

export interface ExportOptions {
  includeLabelPrice?: boolean
  includeBarcodeImage?: boolean
  includeProductImage?: boolean
  fileName?: string
  onProgress?: (progress: number, message: string) => void
}

export interface ExportProductItem {
  itemNumber: string
  barcode?: string
  name: string
  labelPrice?: number
  productImage?: string
}

export interface ExportResult {
  failedProductImages: Array<{ itemNumber: string; url: string; reason: string }>
}

export interface ProductGradeExportOptions {
  includeProductImage?: boolean
  fileName?: string
  onProgress?: (progress: number, message: string) => void
}

export interface ProductGradeWorksheetOptions {
  includeProductImage?: boolean
}

export interface ContainerDetailExportItem {
  [key: string]: string | number | boolean | undefined
}

export type ContainerDetailExportValueType = 'text' | 'number' | 'integer' | 'money' | 'volume'

export interface ContainerDetailExportColumn {
  header: string
  key: string
  width: number
  valueType?: ContainerDetailExportValueType
  currencySymbol?: '$' | '¥'
}

export interface ContainerExportSummaryCell {
  label: string
  value: string | number
  valueType?: ContainerDetailExportValueType
}

export interface ContainerExportSummary {
  title: string
  rows: ContainerExportSummaryCell[][]
}

export interface ContainerExportOptions {
  columns?: ContainerDetailExportColumn[]
  includeImages?: boolean
  summary?: ContainerExportSummary
  fileName?: string
  onProgress?: (progress: number, message: string) => void
}

interface ContainerExportPreparedImages {
  barcodeImageMap: Map<string, string>
  productImageMap: Map<number, string>
  failedProductImageRows: Set<number>
}

export interface ContainerDetailPdfLayout {
  orientation: 'p' | 'l'
  pageWidthPx: number
  pageHeightPx: number
  pdfWidthMm: number
  pdfHeightMm: number
  rowsPerPageWithImages: number
  rowsPerPageWithoutImages: number
}

const defaultExportOptions: ExportOptions = {
  includeLabelPrice: false,
  includeBarcodeImage: true,
  includeProductImage: false,
  fileName: '仓库商品',
}

const defaultProductGradeExportOptions: Required<Pick<ProductGradeExportOptions, 'includeProductImage' | 'fileName'>> = {
  includeProductImage: true,
  fileName: '商品等级',
}

const MAX_RETRIES = 2
const IMAGE_TIMEOUT_MS = 10_000
const RETRY_BASE_DELAY_MS = 1_500
const IMAGE_PROXY_ALLOWED_HOSTS = new Set([
  'hotbargain-yw-2023-1300114625.cos.ap-shanghai.myqcloud.com',
  'hb-sales-2019-1300114625.cos.ap-singapore.myqcloud.com',
])

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

function loadImageViaElement(src: string, crossOrigin?: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const img = new Image()
    const timer = setTimeout(() => {
      img.onload = null
      img.onerror = null
      img.src = ''
      reject(new Error('Image load timeout'))
    }, IMAGE_TIMEOUT_MS)
    if (crossOrigin) {
      img.crossOrigin = crossOrigin
    }
    img.onload = () => {
      clearTimeout(timer)
      resolve(img)
    }
    img.onerror = () => {
      clearTimeout(timer)
      reject(new Error('Image load failed'))
    }
    img.src = src
  })
}

function drawImageToCanvas(img: HTMLImageElement): string {
  const canvas = document.createElement('canvas')
  canvas.width = img.naturalWidth || img.width
  canvas.height = img.naturalHeight || img.height
  const ctx = canvas.getContext('2d')!
  ctx.drawImage(img, 0, 0)
  return canvas.toDataURL('image/png')
}

function normalizeImageDownloadUrl(url: string) {
  const trimmedUrl = url.trim()
  if (!trimmedUrl) return null
  if (trimmedUrl.startsWith('data:') || trimmedUrl.startsWith('blob:')) return trimmedUrl

  try {
    const baseUrl = typeof window !== 'undefined' ? window.location.origin : undefined
    return new URL(trimmedUrl, baseUrl).toString()
  } catch {
    return trimmedUrl
  }
}

function isHttpImageUrl(url: string) {
  try {
    const parsedUrl = new URL(url)
    return parsedUrl.protocol === 'http:' || parsedUrl.protocol === 'https:'
  } catch {
    return false
  }
}

function canUseImageProxy(url: URL) {
  return IMAGE_PROXY_ALLOWED_HOSTS.has(url.hostname.toLowerCase())
}

export function getImageDownloadCandidates(url: string) {
  const normalizedUrl = normalizeImageDownloadUrl(url)
  if (!normalizedUrl) return []

  if (!isHttpImageUrl(normalizedUrl)) return [normalizedUrl]

  try {
    const parsedUrl = new URL(normalizedUrl)
    const currentOrigin = typeof window !== 'undefined' ? window.location.origin : ''
    if (parsedUrl.origin === currentOrigin) {
      return [normalizedUrl]
    }

    if (!canUseImageProxy(parsedUrl)) {
      // 未知跨域图片直接跳过，避免导出时由操作员浏览器直连内网/link-local/恶意地址。
      return []
    }

    return [
      normalizedUrl,
      `/api/react/v1/image-proxy?url=${encodeURIComponent(parsedUrl.toString())}`,
    ]
  } catch {}

  return []
}

async function fetchImageAsBase64SingleAttempt(
  url: string,
  options: { reportUrl?: string; suppressReport?: boolean } = {},
): Promise<string | null> {
  const reportUrl = options.reportUrl ?? url
  try {
    const img = await loadImageViaElement(url, 'anonymous')
    return drawImageToCanvas(img)
  } catch {}

  try {
    const controller = new AbortController()
    const timer = setTimeout(() => controller.abort(), IMAGE_TIMEOUT_MS)
    const response = await fetch(url, { signal: controller.signal })
    clearTimeout(timer)
    if (!response.ok) {
      if (!options.suppressReport) {
        reportExternalFetchError({
          url: reportUrl,
          method: 'GET',
          statusCode: response.status,
          error: new Error(`Image download failed: ${response.status}`),
          responsePayload: {
            message: response.statusText || `HTTP ${response.status}`,
          },
          properties: {
            assetType: 'product-image',
          },
        })
      }
      return null
    }
    const blob = await response.blob()
    return await new Promise<string>((resolve) => {
      const reader = new FileReader()
      reader.onloadend = () => resolve(reader.result as string)
      reader.onerror = () => resolve('')
      reader.readAsDataURL(blob)
    })
  } catch (error) {
    if (!options.suppressReport) {
      reportExternalFetchError({
        url: reportUrl,
        method: 'GET',
        error,
        properties: {
          assetType: 'product-image',
        },
      })
    }
  }

  try {
    const img = await loadImageViaElement(url)
    return drawImageToCanvas(img)
  } catch {
    return null
  }
}

async function fetchImageAsBase64WithRetry(
  url: string,
  retries = MAX_RETRIES,
): Promise<{ data: string | null; reason: string | null }> {
  let lastReason = 'unknown'
  const downloadCandidates = getImageDownloadCandidates(url)
  if (!downloadCandidates.length) {
    return { data: null, reason: '无效图片地址' }
  }
  const primaryUrl = downloadCandidates[0]

  for (let attempt = 0; attempt <= retries; attempt++) {
    let headFailureReason: string | null = null
    if (attempt > 0) {
      await delay(RETRY_BASE_DELAY_MS * attempt)
    }

    if (isHttpImageUrl(primaryUrl)) {
      try {
        const controller = new AbortController()
        const timer = setTimeout(() => controller.abort(), IMAGE_TIMEOUT_MS)
        const response = await fetch(primaryUrl, { method: 'HEAD', mode: 'cors', signal: controller.signal })
        clearTimeout(timer)
        if (response.status === 404) {
          reportExternalFetchError({
            url,
            method: 'HEAD',
            statusCode: response.status,
            error: new Error('Image HEAD failed: 404'),
            responsePayload: {
              message: '404 Not Found',
            },
            properties: {
              assetType: 'product-image',
              attempt: attempt + 1,
            },
          })
          // 少数供应商站点 HEAD 会误报 404，仍继续尝试 GET 和后端代理兜底。
          headFailureReason = '404 Not Found'
        }
      } catch (error) {
        reportExternalFetchError({
          url,
          method: 'HEAD',
          error,
          properties: {
            assetType: 'product-image',
            attempt: attempt + 1,
          },
        })
      }
    }

    for (const [candidateIndex, downloadUrl] of downloadCandidates.entries()) {
      const data = await fetchImageAsBase64SingleAttempt(downloadUrl, {
        reportUrl: url,
        suppressReport: candidateIndex < downloadCandidates.length - 1,
      })
      if (data) {
        return { data, reason: null }
      }
    }

    lastReason = headFailureReason || `下载失败 (尝试 ${attempt + 1}/${retries + 1})`
  }

  return { data: null, reason: lastReason }
}

export async function exportDomesticProductsToExcel(
  products: ExportProductItem[],
  options: ExportOptions = defaultExportOptions,
): Promise<ExportResult> {
  const mergedOptions = { ...defaultExportOptions, ...options }
  const workbook = new ExcelJS.Workbook()
  const worksheet = workbook.addWorksheet('仓库商品')

  const failedProductImages: ExportResult['failedProductImages'] = []

  const columns: Array<{ header: string; key: string; width: number }> = [
    { header: '货号', key: 'itemNumber', width: 18 },
    { header: '条码', key: 'barcode', width: 22 },
  ]

  if (mergedOptions.includeBarcodeImage) {
    columns.push({ header: '条码图片', key: 'barcodeImage', width: 24 })
  }

  if (mergedOptions.includeProductImage) {
    columns.push({ header: '商品图片', key: 'productImageCol', width: 20 })
  }

  columns.push({ header: '名称', key: 'name', width: 32 })

  if (mergedOptions.includeLabelPrice) {
    columns.push({ header: '零售', key: 'labelPrice', width: 14 })
  }

  worksheet.columns = columns

  const headerRow = worksheet.getRow(1)
  headerRow.height = 32
  headerRow.eachCell((cell) => {
    cell.font = { bold: true, color: { argb: 'FFFFFFFF' } }
    cell.fill = {
      type: 'pattern',
      pattern: 'solid',
      fgColor: { argb: 'FF4472C4' },
    }
    cell.alignment = { horizontal: 'center', vertical: 'middle' }
  })

  let barcodeMap = new Map<string, string>()
  if (mergedOptions.includeBarcodeImage) {
    const barcodes = products.map((item) => item.barcode).filter((value): value is string => Boolean(value))
    mergedOptions.onProgress?.(10, '正在生成条码图片...')
    barcodeMap = await generateBarcodeImages(barcodes, {
      width: 3,
      height: 100,
      displayValue: true,
      fontSize: 14,
      margin: 5,
    })
  }

  let productImageMap = new Map<number, string>()
  const failedProductImageRows = new Set<number>()
  if (mergedOptions.includeProductImage) {
    mergedOptions.onProgress?.(20, '正在下载商品图片...')
    const imageEntries = products
      .map((item, index) => ({ url: item.productImage, itemNumber: item.itemNumber, index }))
      .filter((entry) => Boolean(entry.url))

    let downloaded = 0
    const total = imageEntries.length || 1
    const batchSize = 5
    for (let i = 0; i < imageEntries.length; i += batchSize) {
      const batch = imageEntries.slice(i, i + batchSize)
      const results = await Promise.all(
        batch.map(async (entry) => {
          const { data, reason } = await fetchImageAsBase64WithRetry(entry.url!)
          if (!data && reason) {
            failedProductImages.push({ itemNumber: entry.itemNumber, url: entry.url!, reason })
            failedProductImageRows.add(entry.index)
          }
          return { index: entry.index, data }
        }),
      )
      for (const result of results) {
        if (result.data) {
          productImageMap.set(result.index, result.data)
        }
      }
      downloaded += batch.length
      mergedOptions.onProgress?.(
        20 + Math.floor((downloaded / total) * 20),
        `正在下载商品图片 (${downloaded}/${total})...`,
      )
    }
  }

  const barcodeImageColIndex = columns.findIndex((col) => col.key === 'barcodeImage')
  const productImageColIndex = columns.findIndex((col) => col.key === 'productImageCol')

  mergedOptions.onProgress?.(40, '正在写入商品数据...')

  products.forEach((product, index) => {
    const rowIndex = index + 2
    const row = worksheet.getRow(rowIndex)

    const values: Record<string, string | number> = {
      itemNumber: product.itemNumber || '',
      barcode: product.barcode || '',
      name: product.name || '',
    }

    if (mergedOptions.includeBarcodeImage) {
      values.barcodeImage = ''
    }
    if (mergedOptions.includeProductImage) {
      values.productImageCol = ''
    }
    if (mergedOptions.includeLabelPrice) {
      values.labelPrice = product.labelPrice || 0
    }

    row.values = values
    row.height = 60
    row.eachCell((cell) => {
      cell.alignment = { horizontal: 'center', vertical: 'middle' }
    })

    if (index % 2 === 0) {
      row.eachCell((cell) => {
        cell.fill = {
          type: 'pattern',
          pattern: 'solid',
          fgColor: { argb: 'FFF9F9F9' },
        }
      })
    }

    if (mergedOptions.includeBarcodeImage && product.barcode) {
      const barcodeData = barcodeMap.get(product.barcode)
      if (barcodeData && barcodeImageColIndex >= 0) {
        const imageId = workbook.addImage({
          base64: barcodeData.split(',')[1],
          extension: 'png',
        })
        worksheet.addImage(imageId, {
          tl: { col: barcodeImageColIndex, row: rowIndex - 1 },
          br: { col: barcodeImageColIndex + 1, row: rowIndex },
          editAs: 'oneCell',
        } as any)
      }
    }

    if (mergedOptions.includeProductImage && productImageColIndex >= 0) {
      const imageData = productImageMap.get(index)
      if (imageData) {
        const ext = imageData.includes('image/jpeg') ? 'jpeg' : 'png'
        const imageId = workbook.addImage({
          base64: imageData.split(',')[1],
          extension: ext,
        })
        worksheet.addImage(imageId, {
          tl: { col: productImageColIndex, row: rowIndex - 1 },
          br: { col: productImageColIndex + 1, row: rowIndex },
          editAs: 'oneCell',
        } as any)
      } else if (failedProductImageRows.has(index)) {
        const cell = row.getCell(productImageColIndex + 1)
        cell.value = '图片下载失败'
        cell.font = { color: { argb: 'FFFF0000' }, size: 9 }
      }
    }

    mergedOptions.onProgress?.(
      40 + Math.floor(((index + 1) / Math.max(products.length, 1)) * 50),
      `正在处理第 ${index + 1}/${products.length} 条数据...`,
    )
  })

  if (mergedOptions.includeLabelPrice) {
    worksheet.getColumn('labelPrice').eachCell({ includeEmpty: false }, (cell, rowNumber) => {
      if (rowNumber > 1) {
        cell.numFmt = '$#,##0.00'
      }
    })
  }

  mergedOptions.onProgress?.(95, '正在生成 Excel 文件...')
  const buffer = await workbook.xlsx.writeBuffer()
  mergedOptions.onProgress?.(100, '导出完成')

  const blob = new Blob([buffer], {
    type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = `${mergedOptions.fileName || '仓库商品'}_${new Date().toISOString().split('T')[0]}.xlsx`
  document.body.appendChild(link)
  link.click()
  document.body.removeChild(link)
  URL.revokeObjectURL(url)

  return { failedProductImages }
}

export async function exportProductGradesToExcel(
  products: ProductGradeListItem[],
  options: ProductGradeExportOptions = {},
): Promise<ExportResult> {
  const mergedOptions = { ...defaultProductGradeExportOptions, ...options }
  const workbook = new ExcelJS.Workbook()
  const worksheet = workbook.addWorksheet('商品等级')

  const { productImageColIndex } = populateProductGradesWorksheet(worksheet, products, {
    includeProductImage: mergedOptions.includeProductImage,
  })

  const failedProductImages: ExportResult['failedProductImages'] = []
  const failedProductImageRows = new Set<number>()
  const productImageMap = new Map<number, string>()

  if (mergedOptions.includeProductImage && productImageColIndex >= 0) {
    mergedOptions.onProgress?.(20, '正在下载商品图片...')
    const imageEntries = products
      .map((item, index) => ({
        url: item.productImage,
        itemNumber: item.hbProductNo || item.productCode,
        index,
      }))
      .filter((entry) => Boolean(entry.url))

    let downloaded = 0
    const total = imageEntries.length || 1
    const batchSize = 5
    for (let i = 0; i < imageEntries.length; i += batchSize) {
      const batch = imageEntries.slice(i, i + batchSize)
      const results = await Promise.all(
        batch.map(async (entry) => {
          const { data, reason } = await fetchImageAsBase64WithRetry(entry.url!)
          if (!data && reason) {
            failedProductImages.push({ itemNumber: entry.itemNumber, url: entry.url!, reason })
            failedProductImageRows.add(entry.index)
          }
          return { index: entry.index, data }
        }),
      )
      for (const result of results) {
        if (result.data) {
          productImageMap.set(result.index, result.data)
        }
      }
      downloaded += batch.length
      mergedOptions.onProgress?.(
        20 + Math.floor((downloaded / total) * 30),
        `正在下载商品图片 (${downloaded}/${total})...`,
      )
    }
  }

  mergedOptions.onProgress?.(55, '正在写入商品等级数据...')
  products.forEach((_, index) => {
    const rowIndex = index + 2
    const row = worksheet.getRow(rowIndex)
    if (mergedOptions.includeProductImage && productImageColIndex >= 0) {
      const imageData = productImageMap.get(index)
      if (imageData) {
        const ext = imageData.includes('image/jpeg') ? 'jpeg' : 'png'
        const imageId = workbook.addImage({
          base64: imageData.split(',')[1],
          extension: ext,
        })
        worksheet.addImage(imageId, {
          tl: { col: productImageColIndex, row: rowIndex - 1 },
          br: { col: productImageColIndex + 1, row: rowIndex },
          editAs: 'oneCell',
        } as any)
      } else if (failedProductImageRows.has(index)) {
        const cell = row.getCell(productImageColIndex + 1)
        cell.value = '图片下载失败'
        cell.font = { color: { argb: 'FFFF0000' }, size: 9 }
      }
    }

    if (index % 100 === 0) {
      mergedOptions.onProgress?.(
        55 + Math.floor(((index + 1) / Math.max(products.length, 1)) * 35),
        `正在处理第 ${index + 1}/${products.length} 条数据...`,
      )
    }
  })

  mergedOptions.onProgress?.(95, '正在生成 Excel 文件...')
  const buffer = await workbook.xlsx.writeBuffer()
  mergedOptions.onProgress?.(100, '导出完成')

  const blob = new Blob([buffer], {
    type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = `${mergedOptions.fileName || '商品等级'}_${new Date().toISOString().split('T')[0]}.xlsx`
  document.body.appendChild(link)
  link.click()
  document.body.removeChild(link)
  URL.revokeObjectURL(url)

  return { failedProductImages }
}

export function populateProductGradesWorksheet(
  worksheet: ExcelJS.Worksheet,
  products: ProductGradeListItem[],
  options: ProductGradeWorksheetOptions = {},
) {
  const includeProductImage = options.includeProductImage ?? true
  const columns: Array<{ header: string; key: string; width: number; valueType?: 'moneyRmb' | 'moneyAud' }> = [
    { header: '供应商', key: 'supplierName', width: 24 },
    { header: '供应商编码', key: 'supplierCode', width: 14 },
    { header: '货号', key: 'hbProductNo', width: 18 },
    { header: '条码', key: 'barcode', width: 20 },
  ]

  if (includeProductImage) {
    columns.push({ header: '商品图片', key: 'productImageCol', width: 18 })
  }

  columns.push(
    { header: '商品名称', key: 'productName', width: 32 },
    { header: '等级', key: 'grade', width: 10 },
    { header: '国内价 RMB', key: 'domesticPrice', width: 14, valueType: 'moneyRmb' },
    { header: '进口价 AUD', key: 'importPrice', width: 14, valueType: 'moneyAud' },
    { header: '零售价 AUD', key: 'oemPrice', width: 14, valueType: 'moneyAud' },
  )

  worksheet.columns = columns

  const headerRow = worksheet.getRow(1)
  headerRow.height = 28
  headerRow.eachCell((cell) => {
    cell.font = { bold: true, color: { argb: 'FFFFFFFF' } }
    cell.fill = {
      type: 'pattern',
      pattern: 'solid',
      fgColor: { argb: 'FF1677FF' },
    }
    cell.alignment = { horizontal: 'center', vertical: 'middle' }
  })

  products.forEach((product, index) => {
    const row = worksheet.getRow(index + 2)
    // Excel 行数据只写业务值，图片二进制由导出函数在同一列覆盖插入。
    row.values = {
      supplierName: product.supplierName || '',
      supplierCode: product.supplierCode || '',
      hbProductNo: product.hbProductNo || '',
      barcode: product.barcode || '',
      productImageCol: '',
      productName: product.productName || '',
      grade: product.grade || '',
      domesticPrice: toExportNumber(product.domesticPrice),
      importPrice: toExportNumber(product.importPrice),
      oemPrice: toExportNumber(product.oemPrice),
    }
    row.height = includeProductImage ? 58 : 24
    row.eachCell((cell) => {
      cell.alignment = { horizontal: 'center', vertical: 'middle', wrapText: true }
    })
  })

  columns.forEach((column) => {
    const numFmt = getProductGradeExportNumberFormat(column.valueType)
    if (!numFmt) return
    worksheet.getColumn(column.key).eachCell({ includeEmpty: false }, (cell, rowNumber) => {
      if (rowNumber > 1) {
        cell.numFmt = numFmt
      }
    })
  })

  worksheet.views = [{ state: 'frozen', ySplit: 1 }]
  return {
    productImageColIndex: columns.findIndex((column) => column.key === 'productImageCol'),
  }
}

function toExportNumber(value?: number | null) {
  if (value === undefined || value === null || Number.isNaN(Number(value))) return ''
  return Number(value)
}

function getProductGradeExportNumberFormat(valueType?: 'moneyRmb' | 'moneyAud') {
  if (valueType === 'moneyRmb') return '¥#,##0.00'
  if (valueType === 'moneyAud') return '$#,##0.00'
  return undefined
}

export async function exportContainerDetailsToExcel(
  items: ContainerDetailExportItem[],
  options: ContainerExportOptions = {},
): Promise<void> {
  const workbook = new ExcelJS.Workbook()
  const worksheet = workbook.addWorksheet('货柜明细')
  const columns = getContainerDetailWorksheetColumns(options)
  const includeBarcodeImages = columns.some((column) => column.key === 'barcodeImage')
  const includeProductImages = columns.some((column) => column.key === 'productImage')
  const hasImageColumns = includeBarcodeImages || includeProductImages

  const preparedImages = hasImageColumns
    ? await prepareContainerDetailExportImages(
        items,
        withContainerExportProgressRange(options, 10, 55),
        { includeBarcodeImages, includeProductImages },
      )
    : createEmptyContainerExportPreparedImages()
  if (hasImageColumns) {
    options.onProgress?.(55, '图片准备完成，正在写入货柜明细...')
  }

  const worksheetInfo = populateContainerDetailsWorksheet(
    worksheet,
    items,
    hasImageColumns ? withContainerExportProgressRange(options, 55, 90) : options,
  )
  addContainerDetailWorksheetImages(workbook, worksheet, items, worksheetInfo, preparedImages)
  options.onProgress?.(95, '正在生成 Excel 文件...')
  const buffer = await workbook.xlsx.writeBuffer()

  const blob = new Blob([buffer], {
    type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = `${options.fileName || '货柜明细'}_${new Date().toISOString().split('T')[0]}.xlsx`
  document.body.appendChild(link)
  link.click()
  document.body.removeChild(link)
  URL.revokeObjectURL(url)
  options.onProgress?.(100, '导出完成')
}

export function mapContainerExportProgress(progress: number, start: number, end: number) {
  const safeProgress = Math.max(0, Math.min(100, Number.isFinite(progress) ? progress : 0))
  return start + Math.floor((safeProgress / 100) * (end - start))
}

function withContainerExportProgressRange(
  options: ContainerExportOptions,
  start: number,
  end: number,
): ContainerExportOptions {
  if (!options.onProgress) {
    return options
  }

  return {
    ...options,
    onProgress: (progress, message) => {
      options.onProgress?.(mapContainerExportProgress(progress, start, end), message)
    },
  }
}

function createEmptyContainerExportPreparedImages(): ContainerExportPreparedImages {
  return {
    barcodeImageMap: new Map(),
    productImageMap: new Map(),
    failedProductImageRows: new Set(),
  }
}

export async function exportContainerDetailsToPdf(
  items: ContainerDetailExportItem[],
  options: ContainerExportOptions = {},
): Promise<void> {
  const columns = getContainerDetailWorksheetColumns(options)
  const includeBarcodeImages = columns.some((column) => column.key === 'barcodeImage')
  const includeProductImages = columns.some((column) => column.key === 'productImage')
  const preparedImages = await prepareContainerDetailExportImages(items, options, {
    includeBarcodeImages,
    includeProductImages,
  })
  const layout = resolveContainerDetailPdfLayout(columns)

  options.onProgress?.(70, '正在生成 PDF 页面...')
  const root = buildContainerDetailPdfRoot(items, columns, preparedImages, layout, options.summary)
  document.body.appendChild(root)

  try {
    const [{ default: html2canvas }, { default: jsPDF }] = await Promise.all([import('html2canvas'), import('jspdf')])
    const pdf = new jsPDF(layout.orientation, 'mm', 'a4')
    const pages = Array.from(root.querySelectorAll<HTMLElement>('.container-detail-export-pdf-page'))

    for (const [pageIndex, pageElement] of pages.entries()) {
      const canvas = await html2canvas(pageElement, {
        // PDF 通过页面截图写入，使用更高倍率和 PNG 避免条码边缘被压缩到不可扫码。
        scale: 3,
        useCORS: true,
        logging: false,
        backgroundColor: '#ffffff',
      })
      const imageData = canvas.toDataURL('image/png')
      if (pageIndex > 0) {
        pdf.addPage()
      }
      pdf.addImage(imageData, 'PNG', 0, 0, layout.pdfWidthMm, layout.pdfHeightMm)
      options.onProgress?.(
        75 + Math.floor(((pageIndex + 1) / Math.max(pages.length, 1)) * 20),
        `正在写入 PDF 第 ${pageIndex + 1}/${pages.length} 页...`,
      )
    }

    pdf.save(`${options.fileName || '货柜明细'}_${new Date().toISOString().split('T')[0]}.pdf`)
    options.onProgress?.(100, 'PDF 导出完成')
  } finally {
    root.remove()
  }
}

export function resolveContainerDetailPdfLayout(columns: ContainerDetailExportColumn[]): ContainerDetailPdfLayout {
  // 少列 PDF 竖向更适合阅读；列数较多时保留横向，避免内容被压得过窄。
  if (columns.length <= 6) {
    return {
      orientation: 'p',
      pageWidthPx: 793,
      pageHeightPx: 1122,
      pdfWidthMm: 210,
      pdfHeightMm: 297,
      rowsPerPageWithImages: 15,
      rowsPerPageWithoutImages: 36,
    }
  }

  return {
    orientation: 'l',
    pageWidthPx: 1122,
    pageHeightPx: 793,
    pdfWidthMm: 297,
    pdfHeightMm: 210,
    rowsPerPageWithImages: 11,
    rowsPerPageWithoutImages: 18,
  }
}

export function populateContainerDetailsWorksheet(
  worksheet: ExcelJS.Worksheet,
  items: ContainerDetailExportItem[],
  options: ContainerExportOptions = {},
) {
  const columns = getContainerDetailWorksheetColumns(options)
  const barcodeImageColIndex = columns.findIndex((column) => column.key === 'barcodeImage')
  const productImageColIndex = columns.findIndex((column) => column.key === 'productImage')
  const hasImageColumn = barcodeImageColIndex >= 0 || productImageColIndex >= 0

  worksheet.columns = columns.map((column) => ({
    key: column.key,
    width: column.width,
  }))

  const headerRowNumber = writeContainerExportSummary(worksheet, columns.length, options.summary)
  const headerRow = worksheet.getRow(headerRowNumber)
  headerRow.height = 28
  columns.forEach((column, index) => {
    const cell = headerRow.getCell(index + 1)
    cell.value = column.header
    cell.font = { bold: true, color: { argb: 'FFFFFFFF' } }
    cell.fill = {
      type: 'pattern',
      pattern: 'solid',
      fgColor: { argb: 'FF1677FF' },
    }
    cell.alignment = { horizontal: 'center', vertical: 'middle' }
  })

  options.onProgress?.(20, '正在写入货柜明细...')
  items.forEach((item, index) => {
    const row = worksheet.addRow(item)
    if (hasImageColumn) {
      row.height = 58
      row.eachCell((cell) => {
        cell.alignment = { horizontal: 'center', vertical: 'middle', wrapText: true }
      })
    }
    if (index % 100 === 0) {
      options.onProgress?.(20 + Math.floor((index / Math.max(items.length, 1)) * 70), `正在处理第 ${index + 1}/${items.length} 条...`)
    }
  })

  columns.forEach((column) => {
    const numFmt = getContainerExportColumnNumberFormat(column)
    if (!numFmt) return
    worksheet.getColumn(column.key).eachCell({ includeEmpty: false }, (cell, rowNumber) => {
      if (rowNumber > headerRowNumber) {
        cell.numFmt = numFmt
      }
    })
  })

  worksheet.views = [{ state: 'frozen', ySplit: headerRowNumber }]
  return {
    headerRowNumber,
    dataStartRowNumber: headerRowNumber + 1,
    barcodeImageColIndex,
    productImageColIndex,
  }
}

function getContainerDetailWorksheetColumns(options: ContainerExportOptions) {
  return options.columns?.length
    ? options.columns
    : [
        { header: '序号', key: 'index', width: 8, valueType: 'integer' as const },
        { header: '货号', key: 'itemNumber', width: 18, valueType: 'text' as const },
        { header: '中文名称', key: 'productName', width: 36, valueType: 'text' as const },
        { header: '英文名称', key: 'englishName', width: 36, valueType: 'text' as const },
        { header: '件数', key: 'containerPieces', width: 12, valueType: 'integer' as const },
        { header: '总装柜数', key: 'containerQuantity', width: 12, valueType: 'integer' as const },
        { header: '单件体积', key: 'unitVolume', width: 12, valueType: 'volume' as const },
        { header: '总体积', key: 'totalVolume', width: 12, valueType: 'volume' as const },
        { header: '中包数', key: 'middlePackQuantity', width: 12, valueType: 'integer' as const },
        { header: '国内价格', key: 'domesticPrice', width: 12, valueType: 'money' as const, currencySymbol: '¥' as const },
        { header: '贴牌价格', key: 'oemPrice', width: 12, valueType: 'money' as const },
      ]
}

async function prepareContainerDetailExportImages(
  items: ContainerDetailExportItem[],
  options: ContainerExportOptions,
  imageOptions: { includeBarcodeImages: boolean; includeProductImages: boolean },
): Promise<ContainerExportPreparedImages> {
  const barcodeImageMap = new Map<string, string>()
  const productImageMap = new Map<number, string>()
  const failedProductImageRows = new Set<number>()

  if (imageOptions.includeBarcodeImages) {
    const barcodes = Array.from(new Set(items.map((item) => String(item.barcodeImage || item.barcode || '').trim()).filter(Boolean)))
    if (barcodes.length) {
      options.onProgress?.(25, '正在生成条码图片...')
      // PDF 条码需要保持可扫码，源图优先生成得更清晰，再由页面按比例缩放。
      const generated = await generateBarcodeImages(barcodes, { width: 3, height: 74, fontSize: 13, margin: 8 })
      generated.forEach((value, key) => barcodeImageMap.set(key, value))
    }
  }

  if (imageOptions.includeProductImages) {
    const imageEntries = items
      .map((item, index) => ({
        index,
        itemNumber: String(item.itemNumber || item.index || index + 1),
        url: String(item.productImage || '').trim(),
      }))
      .filter((entry) => Boolean(entry.url))

    if (imageEntries.length) {
      options.onProgress?.(35, '正在下载商品图片...')
      const batchSize = 5
      let downloaded = 0
      for (let i = 0; i < imageEntries.length; i += batchSize) {
        const batch = imageEntries.slice(i, i + batchSize)
        const results = await Promise.all(
          batch.map(async (entry) => {
            const { data, reason } = await fetchImageAsBase64WithRetry(entry.url)
            if (!data && reason) {
              failedProductImageRows.add(entry.index)
            }
            return { index: entry.index, data }
          }),
        )
        results.forEach((result) => {
          if (result.data) {
            productImageMap.set(result.index, result.data)
          }
        })
        downloaded += batch.length
        options.onProgress?.(
          35 + Math.floor((downloaded / Math.max(imageEntries.length, 1)) * 30),
          `正在下载商品图片 (${downloaded}/${imageEntries.length})...`,
        )
      }
    }
  }

  return { barcodeImageMap, productImageMap, failedProductImageRows }
}

function addContainerDetailWorksheetImages(
  workbook: ExcelJS.Workbook,
  worksheet: ExcelJS.Worksheet,
  items: ContainerDetailExportItem[],
  worksheetInfo: ReturnType<typeof populateContainerDetailsWorksheet>,
  preparedImages: ContainerExportPreparedImages,
) {
  items.forEach((item, index) => {
    const rowNumber = worksheetInfo.dataStartRowNumber + index
    const row = worksheet.getRow(rowNumber)

    if (worksheetInfo.barcodeImageColIndex >= 0) {
      const barcode = String(item.barcodeImage || item.barcode || '').trim()
      const barcodeData = barcode ? preparedImages.barcodeImageMap.get(barcode) : ''
      if (barcodeData) {
        const imageId = workbook.addImage({
          base64: barcodeData.split(',')[1],
          extension: 'png',
        })
        worksheet.addImage(imageId, {
          tl: { col: worksheetInfo.barcodeImageColIndex, row: rowNumber - 1 },
          br: { col: worksheetInfo.barcodeImageColIndex + 1, row: rowNumber },
          editAs: 'oneCell',
        } as any)
      }
    }

    if (worksheetInfo.productImageColIndex >= 0) {
      const imageData = preparedImages.productImageMap.get(index)
      const cell = row.getCell(worksheetInfo.productImageColIndex + 1)
      if (imageData) {
        const ext = imageData.includes('image/jpeg') ? 'jpeg' : 'png'
        const imageId = workbook.addImage({
          base64: imageData.split(',')[1],
          extension: ext,
        })
        worksheet.addImage(imageId, {
          tl: { col: worksheetInfo.productImageColIndex, row: rowNumber - 1 },
          br: { col: worksheetInfo.productImageColIndex + 1, row: rowNumber },
          editAs: 'oneCell',
        } as any)
      } else if (preparedImages.failedProductImageRows.has(index)) {
        cell.value = '图片下载失败'
        cell.font = { color: { argb: 'FFFF0000' }, size: 9 }
      }
    }
  })
}

function buildContainerDetailPdfRoot(
  items: ContainerDetailExportItem[],
  columns: ContainerDetailExportColumn[],
  preparedImages: ContainerExportPreparedImages,
  layout: ContainerDetailPdfLayout,
  summary?: ContainerExportSummary,
) {
  const root = document.createElement('div')
  root.style.position = 'fixed'
  root.style.left = '-10000px'
  root.style.top = '0'
  root.style.width = `${layout.pageWidthPx}px`
  root.style.background = '#ffffff'
  root.style.zIndex = '-1'

  const hasImageColumn = columns.some((column) => column.key === 'barcodeImage' || column.key === 'productImage')
  const rowsPerPage = hasImageColumn ? layout.rowsPerPageWithImages : layout.rowsPerPageWithoutImages
  const pages: ContainerDetailExportItem[][] = []
  for (let i = 0; i < items.length; i += rowsPerPage) {
    pages.push(items.slice(i, i + rowsPerPage))
  }
  if (!pages.length) {
    pages.push([])
  }

  root.innerHTML = pages.map((pageItems, pageIndex) => (
    buildContainerDetailPdfPageHtml({
      pageItems,
      pageIndex,
      totalPages: pages.length,
      firstRowIndex: pageIndex * rowsPerPage,
      columns,
      preparedImages,
      layout,
      summary: pageIndex === 0 ? summary : undefined,
    })
  )).join('')

  return root
}

function buildContainerDetailPdfPageHtml({
  pageItems,
  pageIndex,
  totalPages,
  firstRowIndex,
  columns,
  preparedImages,
  layout,
  summary,
}: {
  pageItems: ContainerDetailExportItem[]
  pageIndex: number
  totalPages: number
  firstRowIndex: number
  columns: ContainerDetailExportColumn[]
  preparedImages: ContainerExportPreparedImages
  layout: ContainerDetailPdfLayout
  summary?: ContainerExportSummary
}) {
  const columnCount = Math.max(columns.length, 1)
  const summaryHtml = summary ? buildContainerDetailPdfSummaryHtml(summary, columnCount) : ''
  const headerHtml = columns.map((column) => `<th>${escapeContainerDetailPdfHtml(column.header)}</th>`).join('')
  const bodyHtml = pageItems.map((item, index) => {
    const rowIndex = firstRowIndex + index
    const cells = columns.map((column) => buildContainerDetailPdfCellHtml(column, item, rowIndex, preparedImages)).join('')
    return `<tr>${cells}</tr>`
  }).join('')

  return `
    <section class="container-detail-export-pdf-page" style="
      box-sizing:border-box;
      width:${layout.pageWidthPx}px;
      min-height:${layout.pageHeightPx}px;
      padding:24px;
      background:#fff;
      color:#1f2937;
      font-family:-apple-system,BlinkMacSystemFont,'Segoe UI','PingFang SC','Microsoft YaHei',Arial,sans-serif;
    ">
      ${buildContainerDetailPdfStyleHtml()}
      ${summaryHtml}
      <table class="container-detail-export-pdf-table">
        <thead><tr>${headerHtml}</tr></thead>
        <tbody>${bodyHtml}</tbody>
      </table>
      <div class="container-detail-export-pdf-page-number">${pageIndex + 1} / ${totalPages}</div>
    </section>
  `
}

function buildContainerDetailPdfStyleHtml() {
  return `
    <style>
      .container-detail-export-pdf-summary-title {
        font-size: 18px;
        font-weight: 700;
        margin-bottom: 8px;
      }
      .container-detail-export-pdf-summary {
        width: 100%;
        border-collapse: collapse;
        margin-bottom: 10px;
        table-layout: fixed;
      }
      .container-detail-export-pdf-summary td {
        border: 1px solid #d9e2ef;
        padding: 5px 7px;
        font-size: 12px;
        line-height: 1.18;
      }
      .container-detail-export-pdf-summary-label {
        background: #eaf3ff;
        font-weight: 700;
        width: 11%;
      }
      .container-detail-export-pdf-table {
        width: 100%;
        border-collapse: collapse;
        table-layout: fixed;
      }
      .container-detail-export-pdf-table th {
        background: #1677ff;
        color: #fff;
        border: 1px solid #0f5fd4;
        padding: 5px;
        font-size: 11px;
        line-height: 1.15;
        text-align: center;
      }
      .container-detail-export-pdf-table td {
        border: 1px solid #d9e2ef;
        padding: 3px 5px;
        font-size: 10px;
        line-height: 1.15;
        text-align: center;
        vertical-align: middle;
        word-break: break-word;
      }
      .container-detail-export-pdf-table tr:nth-child(even) td {
        background: #fafafa;
      }
      .container-detail-export-pdf-image {
        width: 138px;
        max-width: 100%;
        height: 58px;
        object-fit: contain;
        image-rendering: auto;
      }
      .container-detail-export-pdf-product-image {
        max-width: 54px;
        max-height: 54px;
        object-fit: contain;
      }
      .container-detail-export-pdf-failed {
        color: #d4380d;
        font-size: 10px;
      }
      .container-detail-export-pdf-page-number {
        margin-top: 10px;
        color: #667085;
        font-size: 10px;
        text-align: right;
      }
    </style>
  `
}

function buildContainerDetailPdfSummaryHtml(summary: ContainerExportSummary, columnCount: number) {
  const rows = summary.rows.map((summaryRow) => {
    const cells = summaryRow.map((item) => `
      <td class="container-detail-export-pdf-summary-label">${escapeContainerDetailPdfHtml(item.label)}</td>
      <td>${escapeContainerDetailPdfHtml(formatContainerDetailPdfValue(item.value, item.valueType))}</td>
    `).join('')
    return `<tr>${cells}</tr>`
  }).join('')

  return `
    <div class="container-detail-export-pdf-summary-title">${escapeContainerDetailPdfHtml(summary.title)}</div>
    <table class="container-detail-export-pdf-summary" data-column-count="${columnCount}">
      <tbody>${rows}</tbody>
    </table>
  `
}

function buildContainerDetailPdfCellHtml(
  column: ContainerDetailExportColumn,
  item: ContainerDetailExportItem,
  rowIndex: number,
  preparedImages: ContainerExportPreparedImages,
) {
  if (column.key === 'barcodeImage') {
    const barcode = String(item.barcodeImage || item.barcode || '').trim()
    const imageData = barcode ? preparedImages.barcodeImageMap.get(barcode) : ''
    return imageData
      ? `<td><img class="container-detail-export-pdf-image" src="${escapeContainerDetailPdfAttribute(imageData)}" /></td>`
      : '<td></td>'
  }

  if (column.key === 'productImage') {
    const imageData = preparedImages.productImageMap.get(rowIndex)
    if (imageData) {
      return `<td><img class="container-detail-export-pdf-product-image" src="${escapeContainerDetailPdfAttribute(imageData)}" /></td>`
    }
    if (preparedImages.failedProductImageRows.has(rowIndex)) {
      return '<td><span class="container-detail-export-pdf-failed">图片下载失败</span></td>'
    }
    return '<td></td>'
  }

  return `<td>${escapeContainerDetailPdfHtml(formatContainerDetailPdfValue(item[column.key], column.valueType, column.currencySymbol))}</td>`
}

function formatContainerDetailPdfValue(
  value: string | number | boolean | undefined,
  valueType?: ContainerDetailExportValueType,
  currencySymbol: '$' | '¥' = '$',
) {
  if (value === undefined || value === null || value === '') return ''
  if (typeof value === 'number') {
    if (valueType === 'integer') return value.toLocaleString('zh-CN', { maximumFractionDigits: 0 })
    if (valueType === 'money') return `${currencySymbol}${value.toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
    if (valueType === 'volume') return value.toLocaleString('zh-CN', { minimumFractionDigits: 4, maximumFractionDigits: 4 })
    return value.toLocaleString('zh-CN')
  }
  return String(value)
}

function escapeContainerDetailPdfHtml(value: string) {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;')
}

function escapeContainerDetailPdfAttribute(value: string) {
  return escapeContainerDetailPdfHtml(value).replace(/`/g, '&#96;')
}

function writeContainerExportSummary(
  worksheet: ExcelJS.Worksheet,
  columnCount: number,
  summary?: ContainerExportSummary,
) {
  if (!summary) {
    return 1
  }

  const titleRow = worksheet.getRow(1)
  titleRow.getCell(1).value = summary.title
  titleRow.getCell(1).font = { bold: true, size: 14 }
  titleRow.getCell(1).alignment = { vertical: 'middle' }
  titleRow.height = 24
  worksheet.mergeCells(1, 1, 1, Math.max(columnCount, 1))

  summary.rows.forEach((summaryRow, rowIndex) => {
    const row = worksheet.getRow(rowIndex + 2)
    summaryRow.forEach((item, itemIndex) => {
      const labelCell = row.getCell(itemIndex * 2 + 1)
      const valueCell = row.getCell(itemIndex * 2 + 2)
      labelCell.value = item.label
      labelCell.font = { bold: true }
      labelCell.fill = {
        type: 'pattern',
        pattern: 'solid',
        fgColor: { argb: 'FFEAF3FF' },
      }
      valueCell.value = item.value
      const valueNumFmt = getContainerExportNumberFormat(item.valueType)
      if (valueNumFmt) {
        valueCell.numFmt = valueNumFmt
      }
      labelCell.alignment = { horizontal: 'center', vertical: 'middle' }
      valueCell.alignment = { horizontal: 'left', vertical: 'middle' }
    })
  })

  // 主表信息后留一行空白，让导出的明细表头在视觉上和主表区分开。
  return summary.rows.length + 3
}

function getContainerExportNumberFormat(valueType?: ContainerDetailExportValueType, currencySymbol: '$' | '¥' = '$') {
  if (valueType === 'integer') return '#,##0'
  if (valueType === 'money') return `${currencySymbol}#,##0.00`
  if (valueType === 'volume') return '#,##0.0000'
  if (valueType === 'number') return '#,##0.####'
  return undefined
}

function getContainerExportColumnNumberFormat(column: ContainerDetailExportColumn) {
  // 国内价格导出按人民币显示；其它金额列默认沿用美元格式。
  const currencySymbol = column.currencySymbol ?? (column.key === 'domesticPrice' ? '¥' : '$')
  return getContainerExportNumberFormat(column.valueType, currencySymbol)
}
