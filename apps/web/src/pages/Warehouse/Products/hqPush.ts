import type { WarehouseProductListItem } from '../../../services/warehouseProductService'
import type {
  PushProductsToHqRequest,
  PushProductsToHqUpdateField,
} from '../../../types/posProduct'

export function areWarehouseProductCodeSelectionsEqual(
  previous: readonly string[],
  current: readonly string[],
): boolean {
  if (previous.length !== current.length) return false

  const sortedPrevious = [...previous].sort()
  const sortedCurrent = [...current].sort()
  return sortedPrevious.every((productCode, index) => productCode === sortedCurrent[index])
}

export function buildWarehouseProductHqPushPayload(
  products: readonly WarehouseProductListItem[],
  selectedProductCodes: readonly string[],
  updateFields: readonly PushProductsToHqUpdateField[],
  targetStoreCodes?: readonly string[],
): PushProductsToHqRequest {
  const productByCode = new Map(products.map((product) => [product.productCode, product]))
  const productCodes = selectedProductCodes.map(String)
  const items = productCodes.flatMap((productCode) => {
    const product = productByCode.get(productCode)
    if (!product) return []

    return [{
      productCode: product.productCode,
      localSupplierCode: product.localSupplierCode,
      itemNumber: product.itemNumber,
      productName: product.name,
      englishName: product.nameEn,
      barcode: product.barcode,
      imageUrl: product.productImage,
      domesticPrice: product.domesticPrice,
      importPrice: product.importPrice,
      // 仓库列表的 labelPrice 对应后端 WarehouseProduct.OEMPrice。
      oemPrice: product.labelPrice,
      isNewProduct: false,
    }]
  })

  return {
    productCodes,
    targetStoreCodes: targetStoreCodes ? [...targetStoreCodes] : undefined,
    items,
    updateFields: [...updateFields],
  }
}
