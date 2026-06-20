import type {
  LocalSupplierInvoiceImportColumnMapping,
  LocalSupplierInvoiceImportField,
  LocalSupplierInvoiceImportPreviewLine,
  LocalSupplierInvoiceImportSourceColumn,
} from '../../../types/localSupplierInvoice'

export const REQUIRED_IMPORT_FIELDS: LocalSupplierInvoiceImportField[] = [
  'itemNumber',
  'barcode',
  'productName',
  'quantity',
  'price',
]

export interface LocalSupplierInvoiceImportPreviewDisplayLine {
  key: string
  rowNumber?: number
  itemNumber: string
  barcode: string
  productName: string
  quantity?: number
  price?: number
  amount?: number
  rawValues: Record<string, string | null | undefined>
}

const FIELD_TO_MAPPING_KEY: Record<LocalSupplierInvoiceImportField, keyof LocalSupplierInvoiceImportColumnMapping> = {
  itemNumber: 'itemNumberColumnKey',
  barcode: 'barcodeColumnKey',
  productName: 'productNameColumnKey',
  quantity: 'quantityColumnKey',
  price: 'priceColumnKey',
}

function normalizeColumnKey(value: string | null | undefined): string | null {
  return typeof value === 'string' && value.trim() ? value.trim() : null
}

function sanitizeCellValue(value: string | null | undefined): string {
  return typeof value === 'string' ? value.trim() : ''
}

function parseNumericCell(value: string | undefined): number | undefined {
  const sanitized = sanitizeCellValue(value)
  if (!sanitized) {
    return undefined
  }

  const normalized = sanitized
    .replace(/\s+/g, '')
    .replace(/[$￥€,]/g, '')
    .replace(/\((.*)\)/, '-$1')

  if (!/^[-+]?\d+(\.\d+)?$/.test(normalized)) {
    return undefined
  }

  const parsed = Number(normalized)
  return Number.isFinite(parsed) ? parsed : undefined
}

function readMappedCell(
  rawValues: Record<string, string | null | undefined>,
  mapping: LocalSupplierInvoiceImportColumnMapping,
  field: LocalSupplierInvoiceImportField,
): string {
  const columnKey = normalizeColumnKey(mapping[FIELD_TO_MAPPING_KEY[field]])
  return columnKey === null ? '' : sanitizeCellValue(rawValues[columnKey])
}

export function normalizeImportColumnMapping(
  mapping?: LocalSupplierInvoiceImportColumnMapping,
): LocalSupplierInvoiceImportColumnMapping {
  return {
    itemNumberColumnKey: normalizeColumnKey(mapping?.itemNumberColumnKey),
    barcodeColumnKey: normalizeColumnKey(mapping?.barcodeColumnKey),
    productNameColumnKey: normalizeColumnKey(mapping?.productNameColumnKey),
    quantityColumnKey: normalizeColumnKey(mapping?.quantityColumnKey),
    priceColumnKey: normalizeColumnKey(mapping?.priceColumnKey),
  }
}

export function hasRequiredImportMappings(mapping?: LocalSupplierInvoiceImportColumnMapping): boolean {
  const normalized = normalizeImportColumnMapping(mapping)
  return REQUIRED_IMPORT_FIELDS.every((field) => normalized[FIELD_TO_MAPPING_KEY[field]] !== null)
}

export function getMissingImportMappings(mapping?: LocalSupplierInvoiceImportColumnMapping): LocalSupplierInvoiceImportField[] {
  const normalized = normalizeImportColumnMapping(mapping)
  return REQUIRED_IMPORT_FIELDS.filter((field) => normalized[FIELD_TO_MAPPING_KEY[field]] === null)
}

export function hasDuplicateImportMappings(mapping?: LocalSupplierInvoiceImportColumnMapping): boolean {
  const normalized = normalizeImportColumnMapping(mapping)
  const selectedColumns = REQUIRED_IMPORT_FIELDS
    .map((field) => normalized[FIELD_TO_MAPPING_KEY[field]])
    .filter((value): value is string => value !== null)

  return new Set(selectedColumns).size !== selectedColumns.length
}

export function isLegacyExcelFileName(fileName: string): boolean {
  return /\.xls$/i.test(fileName) && !/\.(xlsx|xlsm)$/i.test(fileName)
}

export function resolveSourceColumnSampleValue(
  column: LocalSupplierInvoiceImportSourceColumn,
  lines: LocalSupplierInvoiceImportPreviewLine[],
): string {
  if (column.sampleValue?.trim()) {
    return column.sampleValue.trim()
  }

  for (const line of lines) {
    const candidate = sanitizeCellValue(line.rawValues?.[column.key])
    if (candidate) {
      return candidate
    }
  }

  return ''
}

export function buildImportPreviewLines(
  lines: LocalSupplierInvoiceImportPreviewLine[],
  mapping?: LocalSupplierInvoiceImportColumnMapping,
): LocalSupplierInvoiceImportPreviewDisplayLine[] {
  const normalized = normalizeImportColumnMapping(mapping)

  return lines.map((line, index) => {
    const itemNumber = readMappedCell(line.rawValues, normalized, 'itemNumber')
    const barcode = readMappedCell(line.rawValues, normalized, 'barcode')
    const productName = readMappedCell(line.rawValues, normalized, 'productName')
    const quantity = parseNumericCell(readMappedCell(line.rawValues, normalized, 'quantity'))
    const price = parseNumericCell(readMappedCell(line.rawValues, normalized, 'price'))
    const amount = quantity !== undefined && price !== undefined ? Number((quantity * price).toFixed(2)) : undefined

    return {
      key: `${line.rowNumber ?? index}-${Object.values(line.rawValues).join('|')}`,
      rowNumber: line.rowNumber,
      itemNumber,
      barcode,
      productName,
      quantity,
      price,
      amount,
      rawValues: line.rawValues,
    }
  })
}
