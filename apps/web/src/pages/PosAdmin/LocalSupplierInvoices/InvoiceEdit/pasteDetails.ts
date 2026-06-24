export interface ParsedPasteRow {
  itemNumber?: string
  barcode?: string
  additionalBarcodes?: string[]
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

export type PasteMultilineCellMode = 'merge' | 'smartSplit'

export interface ParsePasteTextOptions {
  normalizeRetailPrice?: boolean
  multilineCellMode?: PasteMultilineCellMode
}

export interface PasteMultilineCellAnalysis {
  hasMultilineCells: boolean
  unsafeRecordCount: number
}

interface ParsedPastedBarcode {
  barcode?: string
  additionalBarcodes: string[]
}

function normalizeCellLineBreaks(value: string) {
  return value.replace(/\r\n/g, '\n').replace(/\r/g, '\n')
}

function splitCellLines(value: string) {
  return normalizeCellLineBreaks(value).split('\n').map((line) => line.trim())
}

function mergeCellText(value?: string) {
  if (value === undefined) return undefined

  return normalizeCellLineBreaks(value)
    .replace(/\n+/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()
}

function hasCellLineBreak(value?: string) {
  return value !== undefined && /[\r\n]/.test(value)
}

function normalizeHeaderCell(value?: string) {
  return mergeCellText(value)
    ?.toLowerCase()
    .replace(/[^a-z0-9\u4e00-\u9fa5]/g, '')
}

function isHeaderCell(field: PasteFieldKey, value?: string) {
  const normalized = normalizeHeaderCell(value)
  if (!normalized) return false

  const headers: Record<Exclude<PasteFieldKey, 'skip'>, string[]> = {
    itemNumber: ['itemno', 'itemnumber', 'item', '货号'],
    barcode: ['barcode', '条码'],
    productName: ['description', 'desc', 'productname', '商品名称'],
    quantity: ['invoiceqty', 'qty', 'quantity', '数量'],
    purchasePrice: ['priceexgst', 'price', 'purchaseprice', '本次进货价', '进货价'],
    newAutoRetailPrice: ['newautoretailprice', '新自动零售价'],
    retailPrice: ['retailprice', '零售价'],
  }

  return field !== 'skip' && headers[field].includes(normalized)
}

function isPasteHeaderRow(cols: string[], fieldOrder: PasteFieldKey[]) {
  let mappedCells = 0
  let headerCells = 0

  fieldOrder.forEach((field, index) => {
    if (field === 'skip' || !cols[index]?.trim()) return

    mappedCells += 1
    if (isHeaderCell(field, cols[index])) {
      headerCells += 1
    }
  })

  // 关键位置：供应商 Excel 经常连同表头一起复制，表头不能作为一条“商品明细”提交到后台。
  return mappedCells > 0 && mappedCells === headerCells && headerCells >= 2
}

export function parsePasteCells(text: string): string[][] {
  if (!text.trim()) return []

  const normalized = normalizeCellLineBreaks(text)
  const rows: string[][] = []
  let row: string[] = []
  let cell = ''
  let inQuotedCell = false

  for (let index = 0; index < normalized.length; index += 1) {
    const char = normalized[index]
    const nextChar = normalized[index + 1]

    if (char === '"') {
      if (inQuotedCell && nextChar === '"') {
        cell += '"'
        index += 1
        continue
      }

      if (inQuotedCell || cell.length === 0) {
        inQuotedCell = !inQuotedCell
        continue
      }
    }

    if (char === '\t' && !inQuotedCell) {
      row.push(cell)
      cell = ''
      continue
    }

    if (char === '\n' && !inQuotedCell) {
      row.push(cell)
      rows.push(row)
      row = []
      cell = ''
      continue
    }

    cell += char
  }

  row.push(cell)
  rows.push(row)

  return rows.filter((currentRow) => currentRow.some((currentCell) => currentCell.trim()))
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

function parsePastedBarcode(value?: string): ParsedPastedBarcode {
  if (!value?.trim()) return { additionalBarcodes: [] }

  // 关键位置：Excel/扫码来源有时会把文本前导单引号或“条码”标签一起复制进条码列，提交前只保留真实条码值。
  const normalized = value
    .trim()
    .replace(/^'+/, '')
    .replace(/条码|barcode|bar\s*code|ean|upc/gi, ' ')
    .replace(/[\s:：]+/g, '')

  const barcodes: string[] = []
  const seen = new Set<string>()

  normalized
    .split(/[，,;；、]+/)
    .map((barcode) => barcode.trim())
    .filter(Boolean)
    .forEach((barcode) => {
      const key = barcode.toUpperCase()
      if (seen.has(key)) return
      seen.add(key)
      barcodes.push(barcode)
    })

  // 关键位置：供应商表格可能把多个条码塞进同一格，第一条是主条码，其余保留为待添加的副条码。
  const [barcode, ...additionalBarcodes] = barcodes
  return { barcode, additionalBarcodes }
}

function parsePastedItemNumber(value?: string) {
  if (!value?.trim()) return undefined

  // 关键位置：Excel 文本格式会把前导单引号一起带入货号，只清理开头，避免误删货号中间的合法字符。
  const normalized = value
    .trim()
    .replace(/^'+/, '')

  return normalized || undefined
}

function getSmartSplitPlan(cols: string[], fieldOrder: PasteFieldKey[]) {
  const businessCells = fieldOrder
    .map((field, index) => ({ field, value: cols[index] }))
    .filter(({ field, value }) => field !== 'skip' && Boolean(value?.trim()))

  const businessLineCounts = businessCells.map(({ value }) => splitCellLines(value ?? '').length)
  const hasBusinessMultiline = businessLineCounts.some((count) => count > 1)
  const splitCount = businessLineCounts[0] ?? 0
  const canSplit = businessCells.length > 1 && splitCount > 1 && businessLineCounts.every((count) => count === splitCount)

  return {
    canSplit,
    splitCount: canSplit ? splitCount : 1,
    hasBusinessMultiline,
  }
}

function createSmartSplitCols(cols: string[], fieldOrder: PasteFieldKey[], rowIndex: number) {
  return cols.map((value, index) => {
    const field = fieldOrder[index]
    if (field === 'skip' || !value?.trim()) return value

    return splitCellLines(value)[rowIndex] ?? value
  })
}

function parsePasteColumns(
  cols: string[],
  fieldOrder: PasteFieldKey[],
  options: ParsePasteTextOptions,
) {
  const row: ParsedPasteRow = {}

  fieldOrder.forEach((field, index) => {
    if (field === 'skip') return

    const value = mergeCellText(cols[index])
    if (field === 'quantity' || field === 'purchasePrice' || field === 'newAutoRetailPrice' || field === 'retailPrice') {
      const parsedNumber = parsePastedNumber(value)
      row[field] = field === 'retailPrice' && options.normalizeRetailPrice && parsedNumber !== undefined
        ? normalizePastedRetailPrice(parsedNumber)
        : parsedNumber
      return
    }

    if (field === 'barcode') {
      const parsedBarcode = parsePastedBarcode(value)
      row[field] = parsedBarcode.barcode
      row.additionalBarcodes = parsedBarcode.additionalBarcodes.length
        ? parsedBarcode.additionalBarcodes
        : undefined
      return
    }

    if (field === 'itemNumber') {
      row[field] = parsePastedItemNumber(value)
      return
    }

    row[field] = value || undefined
  })

  return row
}

export function analyzePasteMultilineCells(
  text: string,
  fieldOrder: PasteFieldKey[] = defaultPasteFieldOrder,
): PasteMultilineCellAnalysis {
  const rows = parsePasteCells(text)
  let hasMultilineCells = false
  let unsafeRecordCount = 0

  rows.forEach((cols) => {
    const hasAnyMultilineCell = cols.some((value) => hasCellLineBreak(value))
    if (!hasAnyMultilineCell) return

    hasMultilineCells = true
    const plan = getSmartSplitPlan(cols, fieldOrder)
    if (plan.hasBusinessMultiline && !plan.canSplit) {
      unsafeRecordCount += 1
    }
  })

  return { hasMultilineCells, unsafeRecordCount }
}

export function getPasteTextMaxColumnCount(text: string) {
  const rows = parsePasteCells(text)
  if (!rows.length) return 0

  return Math.max(...rows.map((row) => row.length))
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
  const rows = parsePasteCells(text)
  const multilineCellMode = options.multilineCellMode ?? 'merge'

  return rows.flatMap((cols) => {
    if (isPasteHeaderRow(cols, fieldOrder)) {
      return []
    }

    // 关键位置：Excel 会用引号包住“单元格内换行”，这里不能再把这些换行直接当成新记录。
    if (multilineCellMode === 'smartSplit') {
      const plan = getSmartSplitPlan(cols, fieldOrder)
      if (plan.canSplit) {
        return Array.from({ length: plan.splitCount }, (_, rowIndex) => (
          parsePasteColumns(createSmartSplitCols(cols, fieldOrder, rowIndex), fieldOrder, options)
        ))
      }
    }

    return [parsePasteColumns(cols, fieldOrder, options)]
  })
}
