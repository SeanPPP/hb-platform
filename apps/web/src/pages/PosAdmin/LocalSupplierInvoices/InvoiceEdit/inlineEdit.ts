import type { Key } from 'react'
import type {
  BatchEditFields,
  InvoiceDetailUpsertItemDto,
  LocalSupplierInvoiceItemDto,
} from '../../../../types/localSupplierInvoice'
import { discountRateToDecimal } from '../../../../utils/discountRate'
import {
  compareNullableNumbers,
  compareNullableText,
  filterBarcodeStatusColumn,
  filterBooleanColumn,
  filterProductStatusColumn,
  matchesActionTypeColumnFilter,
  matchesNumberColumnFilter,
  matchesTextColumnFilter,
  type NumberFilterField,
  type TextFilterField,
} from './tableColumnFilters'

export type InvoiceDetailInlineEditableField =
  | 'itemNumber'
  | 'barcode'
  | 'productName'
  | 'quantity'
  | 'purchasePrice'
  | 'retailPrice'
  | 'pricingFloatRate'
  | 'newAutoRetailPrice'
  | 'autoPricing'
  | 'isSpecialProduct'
  | 'discountRate'

export type InvoiceDetailInlineNavigationKey = 'ArrowUp' | 'ArrowDown'

export interface InvoiceDetailInlineNavigationTarget {
  detailGuid: string
  field: InvoiceDetailInlineEditableField
}

export type InvoiceDetailInlineColumnFilteredValues = Record<string, (Key | boolean)[] | null>

export interface InvoiceDetailInlineSortState {
  field: string
  order: 'ascend' | 'descend'
}

const textColumnFilterFields = new Set<string>(['itemNumber', 'barcode', 'productName'])

const numberColumnFilterFields = new Set<string>([
  'quantity',
  'lastPurchasePrice',
  'purchasePrice',
  'retailPrice',
  'pricingFloatRate',
  'newAutoRetailPrice',
  'discountRate',
  'amount',
])

function isTextColumnFilterField(field: string): field is TextFilterField {
  return textColumnFilterFields.has(field)
}

function isNumberColumnFilterField(field: string): field is NumberFilterField {
  return numberColumnFilterFields.has(field)
}

const numericFields = new Set<InvoiceDetailInlineEditableField>([
  'quantity',
  'purchasePrice',
  'retailPrice',
  'pricingFloatRate',
  'newAutoRetailPrice',
  'discountRate',
])

const textFields = new Set<InvoiceDetailInlineEditableField>([
  'itemNumber',
  'barcode',
  'productName',
])

function roundMoney(value: number) {
  return Math.round(value * 100) / 100
}

export function recalculateInvoiceDetailAmount(
  quantity?: number | null,
  purchasePrice?: number | null,
) {
  if (quantity == null || purchasePrice == null) return undefined
  return roundMoney(quantity * purchasePrice)
}

export function normalizeInvoiceDetailInlineValue(
  field: InvoiceDetailInlineEditableField,
  value: unknown,
) {
  if (textFields.has(field)) {
    return String(value ?? '').trim()
  }

  if (field === 'autoPricing' || field === 'isSpecialProduct') {
    return Boolean(value)
  }

  if (numericFields.has(field)) {
    const numericValue = Number(value)
    if (Number.isNaN(numericValue) || numericValue < 0) {
      throw new Error('请输入有效的非负数值')
    }
    return field === 'discountRate' ? discountRateToDecimal(numericValue) : numericValue
  }

  return value
}

export function applyInvoiceDetailInlineEdit(
  details: LocalSupplierInvoiceItemDto[],
  detailGuid: string,
  field: InvoiceDetailInlineEditableField,
  value: unknown,
) {
  return details.map((detail) => {
    if (detail.detailGUID !== detailGuid) return detail

    const nextDetail: LocalSupplierInvoiceItemDto = {
      ...detail,
      [field]: value,
    }

    if (field === 'quantity' || field === 'purchasePrice') {
      nextDetail.amount = recalculateInvoiceDetailAmount(
        nextDetail.quantity,
        nextDetail.purchasePrice,
      )
    }

    return nextDetail
  })
}

function matchesInvoiceDetailColumnFilter(
  record: LocalSupplierInvoiceItemDto,
  field: string,
  value: Key | boolean,
  rowActions: Record<string, number>,
) {
  if (isTextColumnFilterField(field)) return matchesTextColumnFilter(record, field, value)
  if (isNumberColumnFilterField(field)) return matchesNumberColumnFilter(record, field, value)
  if (field === 'autoPricing') return filterBooleanColumn(record.autoPricing, value)
  if (field === 'isSpecialProduct') return filterBooleanColumn(record.isSpecialProduct, value)
  if (field === 'existingProductCount') return filterProductStatusColumn(record, value)
  if (field === 'barcodeMatchCount') return filterBarcodeStatusColumn(record, value)
  if (field === 'action') return matchesActionTypeColumnFilter(record, value, rowActions)
  return true
}

export function buildInvoiceDetailInlineNavigationDetails(
  details: LocalSupplierInvoiceItemDto[],
  columnFilteredValues: InvoiceDetailInlineColumnFilteredValues,
  rowActions: Record<string, number> = {},
  sortState: InvoiceDetailInlineSortState | null = null,
) {
  const activeFilters = Object.entries(columnFilteredValues).filter(([, values]) => Boolean(values?.length))
  const filtered = activeFilters.length
    ? details.filter((record) =>
        activeFilters.every(([field, values]) =>
          values?.some((value) => matchesInvoiceDetailColumnFilter(record, field, value, rowActions)),
        ),
      )
    : details

  if (!sortState) return filtered

  const direction = sortState.order === 'descend' ? -1 : 1
  const sortField = sortState.field
  if (isTextColumnFilterField(sortField)) {
    return [...filtered].sort((left, right) =>
      direction * compareNullableText(left[sortField], right[sortField]),
    )
  }
  if (isNumberColumnFilterField(sortField)) {
    return [...filtered].sort((left, right) =>
      direction * compareNullableNumbers(left[sortField], right[sortField]),
    )
  }

  return filtered
}

export function resolveInvoiceDetailInlineNavigation(
  details: LocalSupplierInvoiceItemDto[],
  currentDetailGuid: string,
  field: InvoiceDetailInlineEditableField,
  key: InvoiceDetailInlineNavigationKey,
): InvoiceDetailInlineNavigationTarget | null {
  const currentIndex = details.findIndex((detail) => detail.detailGUID === currentDetailGuid)
  if (currentIndex < 0) return null

  // 方向键只在调用方传入的明细列表中上下移动，避免跳出当前录入范围。
  const nextIndex = key === 'ArrowUp' ? currentIndex - 1 : currentIndex + 1
  const target = details[nextIndex]
  if (!target?.detailGUID) return null

  return {
    detailGuid: target.detailGUID,
    field,
  }
}

export function applyInvoiceDetailBatchEdit(
  details: LocalSupplierInvoiceItemDto[],
  detailGuids: readonly string[],
  editFields: BatchEditFields,
) {
  const targetGuidSet = new Set(detailGuids)

  return details.map((detail) => {
    if (!targetGuidSet.has(detail.detailGUID)) return detail

    const nextDetail: LocalSupplierInvoiceItemDto = { ...detail }

    // 批量编辑只应用用户勾选的字段；0 和 false 都是有效业务值，不能用 truthy 判断。
    if (editFields.updatePurchasePrice && editFields.purchasePrice !== undefined) {
      nextDetail.purchasePrice = editFields.purchasePrice
      nextDetail.amount = recalculateInvoiceDetailAmount(nextDetail.quantity, editFields.purchasePrice)
    }
    if (editFields.updateRetailPrice && editFields.retailPrice !== undefined) {
      nextDetail.retailPrice = editFields.retailPrice
    }
    if (editFields.updateIsAutoPricing && editFields.isAutoPricing !== undefined) {
      nextDetail.autoPricing = editFields.isAutoPricing
    }
    if (editFields.updateIsSpecialProduct && editFields.isSpecialProduct !== undefined) {
      nextDetail.isSpecialProduct = editFields.isSpecialProduct
    }
    if (editFields.updateDiscountRate && editFields.discountRate !== undefined) {
      nextDetail.discountRate = editFields.discountRate
    }

    return nextDetail
  })
}

export function buildInvoiceDetailSaveItems(
  details: LocalSupplierInvoiceItemDto[],
): InvoiceDetailUpsertItemDto[] {
  return details.map((detail) => ({
    detailGUID: detail.detailGUID,
    itemNumber: detail.itemNumber,
    barcode: detail.barcode,
    productName: detail.productName,
    quantity: detail.quantity,
    purchasePrice: detail.purchasePrice,
    retailPrice: detail.retailPrice,
    amount: detail.amount,
    autoPricing: detail.autoPricing,
    pricingFloatRate: detail.pricingFloatRate,
    newAutoRetailPrice: detail.newAutoRetailPrice,
    isSpecialProduct: detail.isSpecialProduct,
    discountRate: detail.discountRate,
  }))
}
