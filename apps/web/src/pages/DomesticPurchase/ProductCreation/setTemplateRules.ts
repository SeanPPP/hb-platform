import { ProductCreationType } from '../../../types/domesticProductCreation'
import type { SetProductTemplateDetail, SetProductTemplatePayload } from '../../../types/domesticProductCreation'
import type { DraftProductItem } from './batchCreateRules'

type DraftKeyFactory = (prefix: string, index: number) => string

const createDraftKey: DraftKeyFactory = (prefix, index) =>
  `${prefix}_${Date.now()}_${index}_${Math.random().toString(36).slice(2, 8)}`

export function createSetDraftFromTemplate(
  template: SetProductTemplateDetail,
  index: number,
  keyFactory: DraftKeyFactory = createDraftKey,
): DraftProductItem {
  const subItems = [...template.subItems]
    .sort((left, right) => left.sortOrder - right.sortOrder)
    .map((subItem, subIndex) => ({
      key: keyFactory('template-sub', index * 1000 + subIndex),
      productName: subItem.productName,
      privateLabelPrice: subItem.privateLabelPrice,
    }))

  return {
    key: keyFactory('template-set', index),
    productName: template.setProductName,
    productType: ProductCreationType.SET,
    // 套用模板产生独立草稿，父级价格不会跟随模板带入。
    createCount: 1,
    setQuantity: subItems.length,
    subItems,
  }
}

export function applySetTemplateDraft(
  products: DraftProductItem[],
  templateDraft: DraftProductItem,
  automaticPlaceholderKey?: string,
): DraftProductItem[] {
  const hasDraftContent = (product: DraftProductItem) => Boolean(
    product.productName?.trim()
    || product.privateLabelPrice != null
    || product.setPrice != null
    || product.subItems?.some((subItem) => subItem.productName?.trim() || subItem.privateLabelPrice != null)
  )

  // 仅清理系统自动创建且仍为空白的首行，手动新增的空白行必须保留。
  const retainedProducts = products.filter((product) => (
    product.key !== automaticPlaceholderKey || hasDraftContent(product)
  ))
  return [...retainedProducts, templateDraft]
}

export type SetTemplateValidationError = 'missing_set_product_name' | 'missing_sub_items' | 'missing_sub_item_name' | 'missing_sub_item_price' | 'invalid_sub_item_price'

export function validateSetTemplateProduct(product: DraftProductItem): SetTemplateValidationError | undefined {
  if (!product.productName?.trim()) return 'missing_set_product_name'
  if (!product.subItems?.length) return 'missing_sub_items'

  for (const subItem of product.subItems) {
    if (!subItem.productName?.trim()) return 'missing_sub_item_name'
    if (subItem.privateLabelPrice == null) return 'missing_sub_item_price'
    if (subItem.privateLabelPrice < 0) return 'invalid_sub_item_price'
  }
  return undefined
}

export function buildSetProductTemplatePayload(
  supplierCode: string,
  templateName: string,
  product: DraftProductItem,
  isEnabled = true,
): SetProductTemplatePayload {
  const validationError = validateSetTemplateProduct(product)
  if (validationError) throw new Error(validationError)

  const subItems = (product.subItems || []).map((subItem) => {
    const price = subItem.privateLabelPrice
    if (price == null || price < 0) throw new Error('invalid_sub_item_price')
    return { productName: subItem.productName?.trim() || '', privateLabelPrice: price }
  })

  return {
    supplierCode,
    templateName: templateName.trim(),
    setProductName: product.productName?.trim() || '',
    isEnabled,
    subItems,
  }
}
