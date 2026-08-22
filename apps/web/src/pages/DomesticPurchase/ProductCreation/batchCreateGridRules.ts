import { ProductCreationType } from '../../../types/domesticProductCreation'
import {
  createDraftProduct,
  createDraftSetSubItem,
} from './batchCreateRules'
import type {
  DraftProductItem,
  DraftSetSubItem,
} from './batchCreateRules'

export type BatchCreatePasteField = 'productName' | 'privateLabelPrice'
export type BatchCreateEditableField =
  | BatchCreatePasteField
  | 'createCount'
  | 'setQuantity'
  | 'setPrice'
export type BatchCreateNavigationDirection = 'up' | 'down' | 'left' | 'right'
export type BatchCreatePasteError = 'multiple_columns' | 'missing_target'

export interface BatchCreateEditableCell {
  rowKey: string
  field: BatchCreateEditableField
}

export interface BatchCreateEditableRow {
  rowKey: string
  fields: readonly BatchCreateEditableField[]
}

export type BatchCreateClipboardColumn =
  | { ok: true; values: string[] }
  | { ok: false; reason: 'multiple_columns' }

export interface BatchCreateColumnPasteResult {
  products: DraftProductItem[]
  appliedCount: number
  clearedCount: number
  addedCount: number
  invalidCount: number
  error?: BatchCreatePasteError
}

type ProductFactory = (
  type: ProductCreationType,
  index: number,
  price?: number | null,
) => DraftProductItem

type SubItemFactory = () => DraftSetSubItem

const emptyPasteResult = (
  products: DraftProductItem[],
  error?: BatchCreatePasteError,
): BatchCreateColumnPasteResult => ({
  products,
  appliedCount: 0,
  clearedCount: 0,
  addedCount: 0,
  invalidCount: 0,
  error,
})

export function parseBatchCreateClipboardColumn(text: string): BatchCreateClipboardColumn {
  const lines = text
    .replace(/\r\n/g, '\n')
    .replace(/\r/g, '\n')
    .split('\n')

  if (lines.some((line) => line.includes('\t'))) {
    return { ok: false, reason: 'multiple_columns' }
  }

  const values = lines.map((line) => line.trim())
  // Excel 复制列时通常会在末尾附加换行；只移除尾部空行，保留中间空格的行位。
  while (values.length > 0 && values[values.length - 1] === '') {
    values.pop()
  }

  return { ok: true, values }
}

function parsePrivateLabelPrice(value: string): number | undefined {
  const normalized = value.replace(/[¥￥€£$₩₹,，\s]/g, '')
  if (!normalized) return undefined

  const parsed = Number(normalized)
  if (!Number.isFinite(parsed) || parsed < 0) return undefined
  return Math.round((parsed + Number.EPSILON) * 100) / 100
}

function applyColumnValue<T extends DraftProductItem | DraftSetSubItem>(
  item: T,
  field: BatchCreatePasteField,
  rawValue: string,
): { item: T; applied: number; cleared: number; invalid: number } {
  if (!rawValue) {
    return {
      item: {
        ...item,
        [field]: field === 'productName' ? '' : undefined,
      },
      applied: 0,
      cleared: 1,
      invalid: 0,
    }
  }

  if (field === 'productName') {
    return {
      item: { ...item, productName: rawValue },
      applied: 1,
      cleared: 0,
      invalid: 0,
    }
  }

  const price = parsePrivateLabelPrice(rawValue)
  if (price === undefined) {
    return { item, applied: 0, cleared: 0, invalid: 1 }
  }

  return {
    item: { ...item, privateLabelPrice: price },
    applied: 1,
    cleared: 0,
    invalid: 0,
  }
}

export function applyParentColumnPaste({
  products,
  startProductKey,
  field,
  clipboardText,
  createProduct = createDraftProduct,
}: {
  products: DraftProductItem[]
  startProductKey?: string
  field: BatchCreatePasteField
  clipboardText: string
  createProduct?: ProductFactory
}): BatchCreateColumnPasteResult {
  const parsed = parseBatchCreateClipboardColumn(clipboardText)
  if (!parsed.ok) return emptyPasteResult(products, parsed.reason)
  if (parsed.values.length === 0) return emptyPasteResult(products)

  const startIndex = startProductKey
    ? products.findIndex((product) => product.key === startProductKey)
    : 0
  if (startIndex < 0) return emptyPasteResult(products, 'missing_target')

  const nextProducts = [...products]
  const requiredCount = startIndex + parsed.values.length
  while (nextProducts.length < requiredCount) {
    nextProducts.push(createProduct(ProductCreationType.NORMAL, nextProducts.length))
  }

  let appliedCount = 0
  let clearedCount = 0
  let invalidCount = 0
  parsed.values.forEach((rawValue, offset) => {
    const targetIndex = startIndex + offset
    const nextValue = applyColumnValue(nextProducts[targetIndex], field, rawValue)
    nextProducts[targetIndex] = nextValue.item
    appliedCount += nextValue.applied
    clearedCount += nextValue.cleared
    invalidCount += nextValue.invalid
  })

  return {
    products: nextProducts,
    appliedCount,
    clearedCount,
    addedCount: nextProducts.length - products.length,
    invalidCount,
  }
}

export function applySubItemColumnPaste({
  products,
  setKey,
  startSubItemKey,
  field,
  clipboardText,
  createSubItem = createDraftSetSubItem,
}: {
  products: DraftProductItem[]
  setKey: string
  startSubItemKey?: string
  field: BatchCreatePasteField
  clipboardText: string
  createSubItem?: SubItemFactory
}): BatchCreateColumnPasteResult {
  const parsed = parseBatchCreateClipboardColumn(clipboardText)
  if (!parsed.ok) return emptyPasteResult(products, parsed.reason)
  if (parsed.values.length === 0) return emptyPasteResult(products)

  const setIndex = products.findIndex((product) => (
    product.key === setKey && product.productType === ProductCreationType.SET
  ))
  if (setIndex < 0) return emptyPasteResult(products, 'missing_target')

  const sourceSubItems = products[setIndex].subItems || []
  const startIndex = startSubItemKey
    ? sourceSubItems.findIndex((subItem) => subItem.key === startSubItemKey)
    : 0
  if (startIndex < 0) return emptyPasteResult(products, 'missing_target')

  const nextSubItems = [...sourceSubItems]
  const requiredCount = startIndex + parsed.values.length
  while (nextSubItems.length < requiredCount) {
    nextSubItems.push(createSubItem())
  }

  let appliedCount = 0
  let clearedCount = 0
  let invalidCount = 0
  parsed.values.forEach((rawValue, offset) => {
    const targetIndex = startIndex + offset
    const nextValue = applyColumnValue(nextSubItems[targetIndex], field, rawValue)
    nextSubItems[targetIndex] = nextValue.item
    appliedCount += nextValue.applied
    clearedCount += nextValue.cleared
    invalidCount += nextValue.invalid
  })

  const nextProducts = [...products]
  nextProducts[setIndex] = {
    ...products[setIndex],
    subItems: nextSubItems,
    setQuantity: nextSubItems.length,
  }

  return {
    products: nextProducts,
    appliedCount,
    clearedCount,
    addedCount: nextSubItems.length - sourceSubItems.length,
    invalidCount,
  }
}

export function getNextBatchCreateEditableCell({
  rows,
  current,
  direction,
}: {
  rows: readonly BatchCreateEditableRow[]
  current: BatchCreateEditableCell
  direction: BatchCreateNavigationDirection
}): BatchCreateEditableCell | undefined {
  const rowIndex = rows.findIndex((row) => row.rowKey === current.rowKey)
  if (rowIndex < 0) return undefined

  const currentRow = rows[rowIndex]
  const fieldIndex = currentRow.fields.indexOf(current.field)
  if (fieldIndex < 0) return undefined

  if (direction === 'left' || direction === 'right') {
    const nextFieldIndex = fieldIndex + (direction === 'left' ? -1 : 1)
    const nextField = currentRow.fields[nextFieldIndex]
    return nextField ? { rowKey: current.rowKey, field: nextField } : undefined
  }

  const rowStep = direction === 'up' ? -1 : 1
  for (let candidateIndex = rowIndex + rowStep; candidateIndex >= 0 && candidateIndex < rows.length; candidateIndex += rowStep) {
    if (rows[candidateIndex].fields.includes(current.field)) {
      return { rowKey: rows[candidateIndex].rowKey, field: current.field }
    }
  }

  return undefined
}
