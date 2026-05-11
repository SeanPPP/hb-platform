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

const defaultExportOptions: ExportOptions = {
  includeLabelPrice: false,
  includeBarcodeImage: true,
  includeProductImage: false,
  fileName: '仓库商品',
}

const MAX_RETRIES = 2

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

function loadImageViaElement(src: string, crossOrigin?: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const img = new Image()
    if (crossOrigin) {
      img.crossOrigin = crossOrigin
    }
    img.onload = () => resolve(img)
    img.onerror = () => reject(new Error(`Image load failed`))
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
  } catch {
    // crossOrigin 模式失败
  }

  try {
    const response = await fetch(url)
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
  } catch {
    // fetch 也失败
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

  for (let attempt = 0; attempt <= retries; attempt++) {
    if (attempt > 0) {
      await delay(500 * attempt)
    }

    try {
      const response = await fetch(url, { method: 'HEAD', mode: 'cors' })
      if (response.status === 404) {
        return { data: null, reason: '404 Not Found' }
      }
    } catch {
      // HEAD 检测失败不阻断，继续尝试下载
    }

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
