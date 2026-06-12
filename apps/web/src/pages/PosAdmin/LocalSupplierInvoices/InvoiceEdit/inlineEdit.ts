import type {
  InvoiceDetailUpsertItemDto,
  LocalSupplierInvoiceItemDto,
} from '../../../../types/localSupplierInvoice'
import { discountRateToDecimal } from '../../../../utils/discountRate'

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
