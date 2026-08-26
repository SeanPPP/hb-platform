import { MAX_SET_CODE_DRAFT_ROWS } from './setCodeDraftLoader'

export type SetCodePasteField = 'setBarcode' | 'setRetailPrice'

export type SetCodeDraftRow = {
  id?: string
  _rowId?: string
  productCode?: string
  setItemNumber?: string
  setBarcode?: string
  setPurchasePrice?: number
  setRetailPrice?: number
  isActive?: boolean
}

export type SetCodeDraftEdit = {
  setItemNumber?: string
  setBarcode?: string
  setPurchasePrice?: number
  setRetailPrice?: number | null
}

export type SetCodeDraftEdits = Record<string, SetCodeDraftEdit>

export type SetCodePasteError = 'multiple_columns' | 'missing_target' | 'duplicate_barcode' | 'too_many_rows'

export type SetCodeColumnPasteResult = {
  rows: SetCodeDraftRow[]
  edits: SetCodeDraftEdits
  appliedCount: number
  skippedBlankCount: number
  addedCount: number
  invalidCount: number
  error?: SetCodePasteError
  duplicateBarcode?: string
  duplicateRowNumbers?: number[]
}

export type SetCodeDraftValidationResult =
  | { valid: true }
  | {
    valid: false
    reason: 'barcode_required' | 'retail_price_required' | 'duplicate_barcode'
    rowNumbers: number[]
    barcode?: string
  }

type DuplicateBarcode = {
  barcode: string
  rowNumbers: number[]
}

function emptyPasteResult(
  rows: SetCodeDraftRow[],
  edits: SetCodeDraftEdits,
  error?: SetCodePasteError,
): SetCodeColumnPasteResult {
  return {
    rows,
    edits,
    appliedCount: 0,
    skippedBlankCount: 0,
    addedCount: 0,
    invalidCount: 0,
    error,
  }
}

function getRowId(row: SetCodeDraftRow) {
  return row.id || row._rowId
}

function getEffectiveBarcode(row: SetCodeDraftRow, edits: SetCodeDraftEdits) {
  const rowId = getRowId(row)
  return String((rowId ? edits[rowId]?.setBarcode : undefined) ?? row.setBarcode ?? '').trim()
}

function getEffectiveRetailPrice(row: SetCodeDraftRow, edits: SetCodeDraftEdits) {
  const rowId = getRowId(row)
  const edit = rowId ? edits[rowId] : undefined
  return edit && Object.prototype.hasOwnProperty.call(edit, 'setRetailPrice')
    ? edit.setRetailPrice
    : row.setRetailPrice
}

function findDuplicateBarcode(rows: SetCodeDraftRow[], edits: SetCodeDraftEdits): DuplicateBarcode | undefined {
  const seen = new Map<string, { barcode: string; rowNumbers: number[] }>()

  rows.forEach((row, index) => {
    const barcode = getEffectiveBarcode(row, edits)
    if (!barcode) return

    const normalized = barcode.toLocaleLowerCase('en')
    const existing = seen.get(normalized)
    if (existing) {
      existing.rowNumbers.push(index + 1)
      return
    }
    seen.set(normalized, { barcode, rowNumbers: [index + 1] })
  })

  return Array.from(seen.values()).find((entry) => entry.rowNumbers.length > 1)
}

export function parseSetCodeClipboardColumn(text: string):
  | { ok: true; values: string[] }
  | { ok: false; reason: 'multiple_columns' } {
  const lines = text
    .replace(/\r\n/g, '\n')
    .replace(/\r/g, '\n')
    .split('\n')

  if (lines.some((line) => line.includes('\t'))) {
    return { ok: false, reason: 'multiple_columns' }
  }

  const values = lines.map((line) => line.trim())
  while (values.length > 0 && values[values.length - 1] === '') {
    values.pop()
  }

  return { ok: true, values }
}

export function parseSetCodeRetailPrice(value: string): number | undefined {
  const compact = value.trim().replace(/\s/g, '')
  const currencyMatches = compact.match(/[¥￥€£$₩₹]/g)
  if ((currencyMatches?.length ?? 0) > 1) {
    return undefined
  }

  const withoutCurrencySymbol = compact.replace(/^[¥￥€£$₩₹]/, '').replace(/[¥￥€£$₩₹]$/, '')
  if (/[¥￥€£$₩₹]/.test(withoutCurrencySymbol)) {
    return undefined
  }

  const normalizedThousands = withoutCurrencySymbol.replace(/，/g, ',')
  if (!/^(?:(?:(?:\d{1,3}(?:,\d{3})+)|\d+)(?:\.\d*)?|\.\d+)$/.test(normalizedThousands)) {
    return undefined
  }
  const normalized = normalizedThousands.replace(/,/g, '')

  // 金额必须按十进制字符串四舍五入，避免 10.075 等值受二进制浮点误差影响而少一分钱。
  const [rawIntegerPart, rawFractionPart = ''] = normalized.split('.')
  const integerPart = rawIntegerPart || '0'
  const fractionPart = rawFractionPart.padEnd(3, '0')
  const centsBeforeRounding = BigInt(integerPart) * 100n + BigInt(fractionPart.slice(0, 2))
  const roundedCents = fractionPart[2] >= '5' ? centsBeforeRounding + 1n : centsBeforeRounding
  const numericCents = Number(roundedCents)
  if (!Number.isSafeInteger(numericCents)) {
    return undefined
  }

  return numericCents / 100
}

export function deriveSetCodePurchasePrice({
  retailPrice,
  mainPurchasePrice,
  mainRetailPrice,
}: {
  retailPrice: number
  mainPurchasePrice: number
  mainRetailPrice: number
}): number | undefined {
  const toCents = (value: number) => {
    if (!Number.isFinite(value) || value < 0) return undefined
    const normalized = parseSetCodeRetailPrice(String(value))
    if (normalized === undefined) return undefined
    const numericCents = Math.round(normalized * 100)
    return Number.isSafeInteger(numericCents) ? BigInt(numericCents) : undefined
  }

  const retailCents = toCents(retailPrice)
  const mainPurchaseCents = toCents(mainPurchasePrice)
  const mainRetailCents = toCents(mainRetailPrice)
  if (retailCents === undefined || mainPurchaseCents === undefined || !mainRetailCents) {
    return undefined
  }

  // 金额比例按“分”做有理数 half-up，避免 toFixed 受二进制浮点误差影响而少一分钱。
  const numerator = retailCents * mainPurchaseCents
  const quotient = numerator / mainRetailCents
  const remainder = numerator % mainRetailCents
  const roundedCents = quotient + (remainder * 2n >= mainRetailCents ? 1n : 0n)
  const numericCents = Number(roundedCents)
  return Number.isSafeInteger(numericCents) ? numericCents / 100 : undefined
}

export function mergeSetCodeRetailPriceEdit(
  currentEdit: SetCodeDraftEdit | undefined,
  retailPrice: number | null,
  derivePurchasePrice?: (retailPrice: number) => number | undefined,
): SetCodeDraftEdit {
  const nextEdit: SetCodeDraftEdit = { ...currentEdit, setRetailPrice: retailPrice }
  if (retailPrice === null || !derivePurchasePrice) return nextEdit

  const purchasePrice = derivePurchasePrice(retailPrice)
  if (purchasePrice !== undefined) {
    nextEdit.setPurchasePrice = purchasePrice
  }
  return nextEdit
}

export function applySetCodeColumnPaste({
  rows,
  edits,
  startRowId,
  field,
  clipboardText,
  createRow,
  derivePurchasePrice,
  maxRows = MAX_SET_CODE_DRAFT_ROWS,
}: {
  rows: SetCodeDraftRow[]
  edits: SetCodeDraftEdits
  startRowId?: string
  field: SetCodePasteField
  clipboardText: string
  createRow: (rowIndex: number) => SetCodeDraftRow
  derivePurchasePrice?: (retailPrice: number) => number | undefined
  maxRows?: number
}): SetCodeColumnPasteResult {
  const parsed = parseSetCodeClipboardColumn(clipboardText)
  if (!parsed.ok) {
    return emptyPasteResult(rows, edits, parsed.reason)
  }
  if (parsed.values.length === 0) {
    return emptyPasteResult(rows, edits)
  }

  const startIndex = startRowId
    ? rows.findIndex((row) => getRowId(row) === startRowId)
    : 0
  if (startIndex < 0) {
    return emptyPasteResult(rows, edits, 'missing_target')
  }

  const requiredCount = startIndex + parsed.values.length
  const safeMaxRows = Number.isSafeInteger(maxRows) && maxRows >= 0
    ? maxRows
    : MAX_SET_CODE_DRAFT_ROWS
  if (Math.max(rows.length, requiredCount) > safeMaxRows) {
    return emptyPasteResult(rows, edits, 'too_many_rows')
  }

  const nextRows = [...rows]
  while (nextRows.length < requiredCount) {
    nextRows.push(createRow(nextRows.length))
  }

  const nextEdits: SetCodeDraftEdits = { ...edits }
  let appliedCount = 0
  let skippedBlankCount = 0
  let invalidCount = 0

  for (const [offset, rawValue] of parsed.values.entries()) {
    const targetRow = nextRows[startIndex + offset]
    const rowId = getRowId(targetRow)
    if (!rowId) {
      return emptyPasteResult(rows, edits, 'missing_target')
    }

    if (!rawValue) {
      skippedBlankCount += 1
      continue
    }

    if (field === 'setBarcode') {
      nextEdits[rowId] = { ...nextEdits[rowId], setBarcode: rawValue.trim() }
      appliedCount += 1
      continue
    }

    const retailPrice = parseSetCodeRetailPrice(rawValue)
    if (retailPrice === undefined) {
      invalidCount += 1
      continue
    }

    nextEdits[rowId] = mergeSetCodeRetailPriceEdit(
      nextEdits[rowId],
      retailPrice,
      derivePurchasePrice,
    )
    appliedCount += 1
  }

  if (field === 'setBarcode') {
    const duplicate = findDuplicateBarcode(nextRows, nextEdits)
    if (duplicate) {
      return {
        ...emptyPasteResult(rows, edits, 'duplicate_barcode'),
        duplicateBarcode: duplicate.barcode,
        duplicateRowNumbers: duplicate.rowNumbers,
      }
    }
  }

  return {
    rows: nextRows,
    edits: nextEdits,
    appliedCount,
    skippedBlankCount,
    addedCount: nextRows.length - rows.length,
    invalidCount,
  }
}

export function validateSetCodeDrafts(
  rows: SetCodeDraftRow[],
  edits: SetCodeDraftEdits,
): SetCodeDraftValidationResult {
  for (const [index, row] of rows.entries()) {
    if (!getEffectiveBarcode(row, edits)) {
      return { valid: false, reason: 'barcode_required', rowNumbers: [index + 1] }
    }

    const retailPrice = getEffectiveRetailPrice(row, edits)
    if (retailPrice === undefined || retailPrice === null) {
      return { valid: false, reason: 'retail_price_required', rowNumbers: [index + 1] }
    }
  }

  const duplicate = findDuplicateBarcode(rows, edits)
  if (duplicate) {
    return {
      valid: false,
      reason: 'duplicate_barcode',
      rowNumbers: duplicate.rowNumbers,
      barcode: duplicate.barcode,
    }
  }

  return { valid: true }
}
