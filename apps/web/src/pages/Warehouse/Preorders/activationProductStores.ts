import type { PreorderStoreProductQuantity } from '../../../types/preorder'

const storeSortOptions: Intl.CollatorOptions = { numeric: true, sensitivity: 'base' }

/**
 * 仅选出当前商品的正数订货记录，并保留接口原始明细，不做状态过滤或聚合。
 */
export function getActivationProductStores(
  quantities: readonly PreorderStoreProductQuantity[],
  activationItemGuid: string,
) {
  return quantities
    .filter((quantity) => quantity.activationItemGuid === activationItemGuid && quantity.orderedQuantity > 0)
    .sort((left, right) => (
      left.storeCode.localeCompare(right.storeCode, undefined, storeSortOptions)
      || left.storeName.localeCompare(right.storeName, undefined, storeSortOptions)
      || left.storeGuid.localeCompare(right.storeGuid, undefined, storeSortOptions)
    ))
}
