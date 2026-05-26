import type { TFunction } from 'i18next'
import ExcelJS from 'exceljs'
import { ProductCreationType } from '../../../types/domesticProductCreation'
import type { BatchDetail, BatchProductItem } from '../../../types/domesticProductCreation'
import { generateBarcodeImages } from '../../../utils/barcode'

interface ExportBatchDetailOptions {
  batchNumber: string
  t: TFunction
}

interface ExportRow {
  itemNumber: string
  barcode: string
  productName: string
  privateLabelPrice: number | string
  setQuantity: number | string
  setPrice: number | string
}

export function getExportableBatchItems(items: BatchProductItem[]) {
  return items
    .filter((item) => item.productType === ProductCreationType.SET || item.productType === ProductCreationType.NORMAL)
    .sort((a, b) => a.hbProductNo.localeCompare(b.hbProductNo))
}

function toExportRows(items: BatchProductItem[]): ExportRow[] {
  return getExportableBatchItems(items).map((item) => ({
    itemNumber: item.hbProductNo,
    barcode: item.barcode,
    productName: item.productName,
    privateLabelPrice: item.privateLabelPrice ?? '',
    setQuantity: item.setQuantity ?? '',
    setPrice: item.setPrice ?? '',
  }))
}

export async function exportProductCreationBatchToExcel(
  detail: BatchDetail,
  { batchNumber, t }: ExportBatchDetailOptions,
) {
  const workbook = new ExcelJS.Workbook()
  const worksheet = workbook.addWorksheet(t('productCreation.batchDetail', '批次明细'))
  worksheet.columns = [
    { header: t('productImport.hbProductNoCol', '货号'), key: 'itemNumber', width: 20 },
    { header: t('domesticProducts.barcode', '条码'), key: 'barcode', width: 18 },
    { header: t('domesticProducts.productName', '商品名称'), key: 'productName', width: 30 },
    { header: t('productCreation.privateLabelPrice', '贴牌价格'), key: 'privateLabelPrice', width: 12 },
    { header: t('productCreation.setQuantity', '套装数量'), key: 'setQuantity', width: 10 },
    { header: t('productCreation.setPrice', '套装价格'), key: 'setPrice', width: 12 },
    { header: t('productCreation.barcodeImage', '条码图片'), key: 'barcodeImage', width: 25 },
  ]

  const headerRow = worksheet.getRow(1)
  headerRow.height = 25
  headerRow.eachCell((cell) => {
    cell.font = { bold: true, color: { argb: 'FFFFFFFF' } }
    cell.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FF4472C4' } }
    cell.alignment = { horizontal: 'center', vertical: 'middle' }
  })

  const rows = toExportRows(detail.items)
  const barcodes = rows.map((item) => item.barcode).filter(Boolean)
  const barcodeMap = await generateBarcodeImages(barcodes, { width: 1, height: 40, displayValue: true })

  rows.forEach((item, index) => {
    const currentRow = worksheet.getRow(index + 2)
    currentRow.values = [
      item.itemNumber,
      item.barcode,
      item.productName,
      item.privateLabelPrice,
      item.setQuantity,
      item.setPrice,
      '',
    ]
    currentRow.height = 50
    if (index % 2 === 0) {
      currentRow.eachCell((cell) => {
        cell.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FFF9F9F9' } }
      })
    }
    if (item.barcode) {
      const barcodeData = barcodeMap.get(item.barcode)
      if (barcodeData) {
        const base64Image = barcodeData.split(',')[1]
        const imageId = workbook.addImage({ base64: base64Image, extension: 'png' })
        worksheet.addImage(imageId, {
          tl: { col: 6, row: index + 1 },
          br: { col: 7, row: index + 2 },
          editAs: 'oneCell',
        } as any)
      }
    }
  })

  worksheet.eachRow((row, rowNumber) => {
    if (rowNumber > 1) {
      row.eachCell({ includeEmpty: false }, (cell) => {
        cell.alignment = { vertical: 'middle' }
      })
    }
  })

  const buffer = await workbook.xlsx.writeBuffer()
  const blob = new Blob([buffer], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  const today = new Date()
  const dateStr = `${today.getFullYear()}${String(today.getMonth() + 1).padStart(2, '0')}${String(today.getDate()).padStart(2, '0')}`
  link.href = url
  link.download = t('productCreation.batchDetailFile', '批次明细_{{batchNumber}}_{{date}}.xlsx', {
    batchNumber,
    date: dateStr,
  })
  document.body.appendChild(link)
  link.click()
  document.body.removeChild(link)
  URL.revokeObjectURL(url)
}
