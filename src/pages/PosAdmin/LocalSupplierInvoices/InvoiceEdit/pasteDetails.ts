export interface ParsedPasteRow {
  itemNumber?: string
  barcode?: string
  productName?: string
  quantity?: number
  purchasePrice?: number
  newAutoRetailPrice?: number
  retailPrice?: number
}

export type PasteFieldKey =
  | 'itemNumber'
  | 'barcode'
  | 'productName'
  | 'quantity'
  | 'purchasePrice'
  | 'newAutoRetailPrice'
  | 'retailPrice'
  | 'skip'

export const defaultPasteFieldOrder: PasteFieldKey[] = [
  'itemNumber',
  'barcode',
  'productName',
  'quantity',
  'purchasePrice',
  'newAutoRetailPrice',
  'retailPrice',
]

function parsePastedNumber(value?: string) {
  if (!value?.trim()) return undefined

  const normalized = value
    .trim()
    .replace(/,/g, '')
    .replace(/\s+/g, '')
    .replace(/[^\d.-]/g, '')

  if (!normalized || normalized === '-' || normalized === '.' || normalized === '-.') {
    return undefined
  }

  const parsed = Number(normalized)
  return Number.isNaN(parsed) ? undefined : parsed
}

/** 粘贴数据解析：兼容 Excel 价格列中的 $, A$, AUD 等货币格式。 */
export function parsePasteText(text: string, fieldOrder: PasteFieldKey[] = defaultPasteFieldOrder): ParsedPasteRow[] {
  if (!text.trim()) return []
  const lines = text.split('\n').filter((line) => line.trim())
  return lines.map((line) => {
    const cols = line.split('\t')
    const row: ParsedPasteRow = {}

    fieldOrder.forEach((field, index) => {
      if (field === 'skip') return

      const value = cols[index]
      if (field === 'quantity' || field === 'purchasePrice' || field === 'newAutoRetailPrice' || field === 'retailPrice') {
        row[field] = parsePastedNumber(value)
        return
      }

      row[field] = value?.trim() || undefined
    })

    return row
  })
}
