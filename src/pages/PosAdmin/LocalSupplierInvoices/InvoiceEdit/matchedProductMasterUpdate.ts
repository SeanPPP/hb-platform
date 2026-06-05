import type { LocalSupplierInvoiceDetailDto, LocalSupplierInvoiceItemDto } from '../../../../types/localSupplierInvoice'
import type { PosProductDto } from '../../../../types/posProduct'

export interface MatchedProductMasterUpdateTarget {
  itemNumber: string
  supplierCode: string
}

export type MatchedProductMasterUpdatePayload = PosProductDto & {
  productCategoryGUID?: string
}

type ProductWithBackendCategory = PosProductDto & {
  productCategoryGUID?: string
}

function normalizeText(value?: string | null) {
  return value?.trim() ?? ''
}

export function getMatchedProductMasterUpdateTarget(
  detail: Pick<LocalSupplierInvoiceItemDto, 'itemNumber' | 'supplierCode'>,
  invoice: Pick<LocalSupplierInvoiceDetailDto, 'supplierCode'> | null | undefined,
): MatchedProductMasterUpdateTarget {
  return {
    itemNumber: normalizeText(detail.itemNumber),
    supplierCode: normalizeText(detail.supplierCode) || normalizeText(invoice?.supplierCode),
  }
}

export function buildMatchedProductMasterUpdatePayload(
  product: ProductWithBackendCategory,
  detail: Pick<LocalSupplierInvoiceItemDto, 'itemNumber' | 'supplierCode'>,
  invoice: Pick<LocalSupplierInvoiceDetailDto, 'supplierCode'> | null | undefined,
): MatchedProductMasterUpdatePayload {
  const target = getMatchedProductMasterUpdateTarget(detail, invoice)

  if (!target.itemNumber) {
    throw new Error('当前明细缺少货号，无法更换匹配商品主档')
  }
  if (!target.supplierCode) {
    throw new Error('当前明细缺少供应商，无法更换匹配商品主档')
  }

  return {
    ...product,
    productCategoryGUID: product.categoryGuid ?? product.productCategoryGUID,
    itemNumber: target.itemNumber,
    localSupplierCode: target.supplierCode,
  }
}
