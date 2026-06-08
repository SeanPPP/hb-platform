import type { Key } from 'react'

export type SupplierImageBatchScope = 'supplier' | 'selected'

export function getDefaultSupplierImageBatchScope(selectedRowKeys: Key[]): SupplierImageBatchScope {
  return selectedRowKeys.length > 0 ? 'selected' : 'supplier'
}

export function buildSupplierImageBatchScopeRequest(
  scope: SupplierImageBatchScope,
  selectedRowKeys: Key[],
): { productCodes?: string[] } {
  if (scope !== 'selected') {
    return {}
  }

  const productCodes = selectedRowKeys.map(String).filter(Boolean)
  if (!productCodes.length) {
    throw new Error('请先选择商品')
  }

  return { productCodes }
}
