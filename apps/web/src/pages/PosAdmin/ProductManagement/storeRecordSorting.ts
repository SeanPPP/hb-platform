import type { ProductStoreRecordDto } from '../../../types/posProduct'

export function compareProductStoreRecordsByName(a: ProductStoreRecordDto, b: ProductStoreRecordDto): number {
  const leftName = a.storeName || a.storeCode || ''
  const rightName = b.storeName || b.storeCode || ''
  const nameResult = leftName.localeCompare(rightName)
  if (nameResult !== 0) return nameResult

  return (a.storeCode || '').localeCompare(b.storeCode || '')
}
