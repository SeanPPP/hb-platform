import ExcelJS from 'exceljs'
import { generateBarcodeImages } from '../utils/barcode'

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

const defaultExportOptions: ExportOptions = {
  includeLabelPrice: false,
  includeBarcodeImage: true,
  includeProductImage: false,
  fileName: '仓库商品',
}

const MAX_RETRIES = 2
const IMAGE_TIMEOUT_MS = 10_000
const RETRY_BASE_DELAY_MS = 1_500

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

async function fetchImageAsBase64SingleAttempt(url: string): Promise<string | null> {
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
      return null
    }
    const blob = await response.blob()
    return await new Promise<string>((resolve) => {
      const reader = new FileReader()
      reader.onloadend = () => resolve(reader.result as string)
      reader.onerror = () => resolve('')
      reader.readAsDataURL(blob)
    })
  } catch {}

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

  for (let attempt = 0; attempt <= retries; attempt++) {
    if (attempt > 0) {
      await delay(RETRY_BASE_DELAY_MS * attempt)
    }

    try {
      const controller = new AbortController()
      const timer = setTimeout(() => controller.abort(), IMAGE_TIMEOUT_MS)
      const response = await fetch(url, { method: 'HEAD', mode: 'cors', signal: controller.signal })
      clearTimeout(timer)
      if (response.status === 404) {
        return { data: null, reason: '404 Not Found' }
      }
    } catch {}

    const data = await fetchImageAsBase64SingleAttempt(url)
    if (data) {
      return { data, reason: null }
    }

    lastReason = `下载失败 (尝试 ${attempt + 1}/${retries + 1})`
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

export async function exportContainerDetailsToExcel(
  items: ContainerDetailExportItem[],
  options: ContainerExportOptions = {},
): Promise<void> {
  const workbook = new ExcelJS.Workbook()
  const worksheet = workbook.addWorksheet('货柜明细')

  populateContainerDetailsWorksheet(worksheet, items, options)
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

export function populateContainerDetailsWorksheet(
  worksheet: ExcelJS.Worksheet,
  items: ContainerDetailExportItem[],
  options: ContainerExportOptions = {},
) {
  const columns = options.columns?.length
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
    worksheet.addRow(item)
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
  return { headerRowNumber, dataStartRowNumber: headerRowNumber + 1 }
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
