export interface ParsedPasteRow {
  itemNumber?: string
  barcode?: string
  productName?: string
  quantity?: number
  purchasePrice?: number
  newAutoRetailPrice?: number
  retailPrice?: number
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

/** 粘贴数据解析：兼容 Excel 价格列中的 $, A$, AUD 等货币格式。 */
export function parsePasteText(text: string): ParsedPasteRow[] {
  if (!text.trim()) return []
  const lines = text.split('\n').filter((line) => line.trim())
  return lines.map((line) => {
    const cols = line.split('\t')
    return {
      itemNumber: cols[0]?.trim() || undefined,
      barcode: cols[1]?.trim() || undefined,
      productName: cols[2]?.trim() || undefined,
      quantity: parsePastedNumber(cols[3]),
      purchasePrice: parsePastedNumber(cols[4]),
      newAutoRetailPrice: parsePastedNumber(cols[5]),
      retailPrice: parsePastedNumber(cols[6]),
    }
  })
}
