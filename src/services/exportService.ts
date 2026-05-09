import ExcelJS from 'exceljs'
import { generateBarcodeImages } from '../utils/barcode'

export interface ExportOptions {
  includeLabelPrice?: boolean
  fileName?: string
  onProgress?: (progress: number, message: string) => void
}

export interface ExportProductItem {
  itemNumber: string
  barcode?: string
  name: string
  labelPrice?: number
}

const defaultExportOptions: ExportOptions = {
  includeLabelPrice: false,
  fileName: '仓库商品',
}

export async function exportDomesticProductsToExcel(
  products: ExportProductItem[],
  options: ExportOptions = defaultExportOptions,
): Promise<void> {
  const mergedOptions = { ...defaultExportOptions, ...options }
  const workbook = new ExcelJS.Workbook()
  const worksheet = workbook.addWorksheet('仓库商品')

  const columns: Array<{ header: string; key: string; width: number }> = [
    { header: '货号', key: 'itemNumber', width: 18 },
    { header: '条码', key: 'barcode', width: 22 },
    { header: '条码图片', key: 'barcodeImage', width: 24 },
    { header: '名称', key: 'name', width: 32 },
  ]

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

  const barcodes = products.map((item) => item.barcode).filter((value): value is string => Boolean(value))
  mergedOptions.onProgress?.(20, '正在生成条码图片...')
  const barcodeMap = await generateBarcodeImages(barcodes, {
    width: 3,
    height: 100,
    displayValue: true,
    fontSize: 14,
    margin: 5,
  })

  mergedOptions.onProgress?.(40, '正在写入商品数据...')

  products.forEach((product, index) => {
    const rowIndex = index + 2
    const row = worksheet.getRow(rowIndex)
    row.values = {
      itemNumber: product.itemNumber || '',
      barcode: product.barcode || '',
      barcodeImage: '',
      name: product.name || '',
      ...(mergedOptions.includeLabelPrice ? { labelPrice: product.labelPrice || 0 } : {}),
    }
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

    if (product.barcode) {
      const barcodeData = barcodeMap.get(product.barcode)
      if (barcodeData) {
        const imageId = workbook.addImage({
          base64: barcodeData.split(',')[1],
          extension: 'png',
        })

        worksheet.addImage(imageId, {
          tl: { col: 2, row: rowIndex - 1 },
          br: { col: 3, row: rowIndex },
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
}
