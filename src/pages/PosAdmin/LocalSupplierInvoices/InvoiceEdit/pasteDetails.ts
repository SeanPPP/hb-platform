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

export interface ParsePasteTextOptions {
  normalizeRetailPrice?: boolean
}

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

export function normalizePastedRetailPrice(price: number) {
  if (!Number.isFinite(price) || price < 3) return price

  const cents = Math.round(price * 100)
  const integerCents = Math.floor(cents / 100) * 100
  const decimalCents = cents - integerCents

  // 粘贴零售价按门店常用尾数归档：整数退 1 分，小数归到 .50 或 .99。
  if (decimalCents === 0) {
    return Number(((integerCents - 1) / 100).toFixed(2))
  }

  if (decimalCents <= 50) {
    return Number(((integerCents + 50) / 100).toFixed(2))
  }

  return Number(((integerCents + 99) / 100).toFixed(2))
}

/** 粘贴数据解析：兼容 Excel 价格列中的 $, A$, AUD 等货币格式。 */
export function parsePasteText(
  text: string,
  fieldOrder: PasteFieldKey[] = defaultPasteFieldOrder,
  options: ParsePasteTextOptions = {},
): ParsedPasteRow[] {
  if (!text.trim()) return []
  const lines = text.split('\n').filter((line) => line.trim())
  return lines.map((line) => {
    const cols = line.split('\t')
    const row: ParsedPasteRow = {}

    fieldOrder.forEach((field, index) => {
      if (field === 'skip') return

      const value = cols[index]
      if (field === 'quantity' || field === 'purchasePrice' || field === 'newAutoRetailPrice' || field === 'retailPrice') {
        const parsedNumber = parsePastedNumber(value)
        row[field] = field === 'retailPrice' && options.normalizeRetailPrice && parsedNumber !== undefined
          ? normalizePastedRetailPrice(parsedNumber)
          : parsedNumber
        return
      }

      row[field] = value?.trim() || undefined
    })

    return row
  })
}
