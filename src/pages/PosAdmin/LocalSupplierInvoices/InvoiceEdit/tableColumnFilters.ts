import type { Key } from 'react'
import type { LocalSupplierInvoiceItemDto } from '../../../../types/localSupplierInvoice'
import {
  getBarcodeStatusFilter,
  getProductStatusFilter,
  type BarcodeStatusFilter,
  type ProductStatusFilter,
} from './statusFilters'

export type TextFilterField = 'itemNumber' | 'barcode' | 'productName'

function isEmptyValue(value: unknown) {
  return value === undefined || value === null || value === ''
}

export function compareNullableNumbers(left?: number | null, right?: number | null) {
  const leftEmpty = isEmptyValue(left)
  const rightEmpty = isEmptyValue(right)

  if (leftEmpty && rightEmpty) return 0
  if (leftEmpty) return 1
  if (rightEmpty) return -1

  return Number(left) - Number(right)
}

export function compareNullableText(left?: string | null, right?: string | null) {
  const leftEmpty = isEmptyValue(left)
  const rightEmpty = isEmptyValue(right)

  if (leftEmpty && rightEmpty) return 0
  if (leftEmpty) return 1
  if (rightEmpty) return -1

  return String(left).localeCompare(String(right), undefined, {
    sensitivity: 'base',
    numeric: true,
  })
}

export function matchesTextColumnFilter(
  record: LocalSupplierInvoiceItemDto,
  field: TextFilterField,
  value: Key | boolean,
) {
  const keyword = String(value ?? '').trim().toLowerCase()
  if (!keyword) return true

  return String(record[field] ?? '').toLowerCase().includes(keyword)
}

export function filterBooleanColumn(actual: boolean | undefined | null, value: Key | boolean) {
  if (actual === undefined || actual === null) return false
  return actual === String(value).toLowerCase().includes('true')
}

export function filterProductStatusColumn(record: LocalSupplierInvoiceItemDto, value: Key | boolean) {
  return getProductStatusFilter(record) === (String(value) as ProductStatusFilter)
}

export function filterBarcodeStatusColumn(record: LocalSupplierInvoiceItemDto, value: Key | boolean) {
  return getBarcodeStatusFilter(record) === (String(value) as BarcodeStatusFilter)
}
