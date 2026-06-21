import type { ProductStoreRecordDto } from '../../../types/posProduct'

function compareText(left?: string | null, right?: string | null): number {
  return (left || '').localeCompare(right || '')
}

function compareNumber(left?: number | null, right?: number | null): number {
  const leftMissing = left === undefined || left === null
  const rightMissing = right === undefined || right === null
  if (leftMissing && rightMissing) return 0
  if (leftMissing) return 1
  if (rightMissing) return -1

  return left - right
}

function compareBoolean(left?: boolean | null, right?: boolean | null): number {
  return Number(left ?? false) - Number(right ?? false)
}

function compareDateText(left?: string | null, right?: string | null): number {
  const leftTime = left ? new Date(left).getTime() : Number.POSITIVE_INFINITY
  const rightTime = right ? new Date(right).getTime() : Number.POSITIVE_INFINITY

  return leftTime - rightTime
}

export function compareProductStoreRecordsByStoreCode(a: ProductStoreRecordDto, b: ProductStoreRecordDto): number {
  return compareText(a.storeCode, b.storeCode)
}

export function compareProductStoreRecordsByName(a: ProductStoreRecordDto, b: ProductStoreRecordDto): number {
  const leftName = a.storeName || a.storeCode || ''
  const rightName = b.storeName || b.storeCode || ''
  const nameResult = leftName.localeCompare(rightName)
  if (nameResult !== 0) return nameResult

  return (a.storeCode || '').localeCompare(b.storeCode || '')
}

export function compareProductStoreRecordsByStoreProductCode(a: ProductStoreRecordDto, b: ProductStoreRecordDto): number {
  return compareText(a.storeProductCode, b.storeProductCode)
}

export function compareProductStoreRecordsByPurchasePrice(a: ProductStoreRecordDto, b: ProductStoreRecordDto): number {
  return compareNumber(a.purchasePrice, b.purchasePrice)
}

export function compareProductStoreRecordsByRetailPrice(a: ProductStoreRecordDto, b: ProductStoreRecordDto): number {
  return compareNumber(a.storeRetailPriceValue, b.storeRetailPriceValue)
}

export function compareProductStoreRecordsByDiscountRate(a: ProductStoreRecordDto, b: ProductStoreRecordDto): number {
  return compareNumber(a.discountRate, b.discountRate)
}

export function compareProductStoreRecordsByAutoPricing(a: ProductStoreRecordDto, b: ProductStoreRecordDto): number {
  return compareBoolean(a.isAutoPricing, b.isAutoPricing)
}

export function compareProductStoreRecordsBySpecialProduct(a: ProductStoreRecordDto, b: ProductStoreRecordDto): number {
  return compareBoolean(a.isSpecialProduct, b.isSpecialProduct)
}

export function compareProductStoreRecordsByActive(a: ProductStoreRecordDto, b: ProductStoreRecordDto): number {
  return compareBoolean(a.isActive, b.isActive)
}

export function compareProductStoreRecordsByUpdatedAt(a: ProductStoreRecordDto, b: ProductStoreRecordDto): number {
  return compareDateText(a.updatedAt, b.updatedAt)
}

export function compareProductStoreRecordsByUpdatedBy(a: ProductStoreRecordDto, b: ProductStoreRecordDto): number {
  return compareText(a.updatedBy, b.updatedBy)
}
