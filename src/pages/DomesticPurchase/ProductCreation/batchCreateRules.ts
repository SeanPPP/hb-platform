import { ProductCreationType } from '../../../types/domesticProductCreation'
import type { CreateBatchRequestItem } from '../../../types/domesticProductCreation'

export interface DraftSetSubItem {
  key: string
  productName?: string
  privateLabelPrice?: number | null
}

export interface DraftProductItem {
  key: string
  productName?: string
  productType: ProductCreationType
  privateLabelPrice?: number | null
  setQuantity?: number | null
  setPrice?: number | null
  createCount?: number | null
  subItems?: DraftSetSubItem[]
}

export interface DraftPreviewItem extends DraftProductItem {
  itemNumber: string
  parentPreviewKey?: string
}

export interface InvalidSetProduct {
  key: string
  index: number
}

export type BatchAddMode = 'append' | 'overwrite'
export type DraftKeyFactory = (prefix: string, index: number) => string

export const normalizeCreateCount = (value?: number | null) => Math.max(1, Math.floor(Number(value) || 1))

export const createDraftProductKey: DraftKeyFactory = (prefix: string, index: number) =>
  `${prefix}_${Date.now()}_${index}_${Math.random().toString(36).slice(2, 8)}`

export function createDraftSetSubItem(keyFactory: DraftKeyFactory = createDraftProductKey): DraftSetSubItem {
  return {
    key: keyFactory('sub', 0),
    productName: '',
  }
}

export function createDraftProduct(
  type: ProductCreationType,
  index: number,
  price?: number | null,
  keyFactory: DraftKeyFactory = createDraftProductKey,
): DraftProductItem {
  return {
    key: keyFactory('temp', index),
    productName: '',
    productType: type,
    privateLabelPrice: price ?? undefined,
    createCount: type === ProductCreationType.SET ? 1 : undefined,
    setQuantity: type === ProductCreationType.SET ? 1 : undefined,
    subItems: type === ProductCreationType.SET ? [createDraftSetSubItem(keyFactory)] : undefined,
  }
}

export function isMeaningfulSetSubItem(subItem: DraftSetSubItem): boolean {
  return Boolean(subItem.productName?.trim() || subItem.privateLabelPrice != null)
}

export function getValidSetSubItems(subItems?: DraftSetSubItem[]): DraftSetSubItem[] {
  return (subItems || []).filter(isMeaningfulSetSubItem)
}

export function findInvalidSetProduct(products: DraftProductItem[]): InvalidSetProduct | undefined {
  const invalidIndex = products.findIndex((product) => (
    product.productType === ProductCreationType.SET && getValidSetSubItems(product.subItems).length === 0
  ))

  if (invalidIndex < 0) return undefined
  return {
    key: products[invalidIndex].key,
    index: invalidIndex + 1,
  }
}

export function buildPreviewItems(products: DraftProductItem[], prefixCode: string): DraftPreviewItem[] {
  let itemIndex = 1

  return products.flatMap((product) => {
    if (product.productType !== ProductCreationType.SET) {
      return [{ ...product, itemNumber: `${prefixCode}${String(itemIndex++).padStart(4, '0')}` }]
    }

    const expandedRows: DraftPreviewItem[] = []
    const createCount = normalizeCreateCount(product.createCount)
    const validSubItems = getValidSetSubItems(product.subItems)
    for (let i = 0; i < createCount; i++) {
      const parentPreviewKey = `${product.key}_${i}`
      expandedRows.push({ ...product, key: parentPreviewKey, itemNumber: `${prefixCode}${String(itemIndex++).padStart(4, '0')}` })
      validSubItems.forEach((subItem) => {
        expandedRows.push({
          ...subItem,
          key: `${parentPreviewKey}_${subItem.key}`,
          productName: subItem.productName?.trim() || '',
          productType: ProductCreationType.SET_SUB_ITEM,
          privateLabelPrice: subItem.privateLabelPrice ?? undefined,
          itemNumber: `${prefixCode}${String(itemIndex++).padStart(4, '0')}`,
          parentPreviewKey,
        })
      })
    }
    return expandedRows
  })
}

export function applyBatchAddProducts({
  products,
  selectedRowKeys,
  expandedRowKeys,
  type,
  count,
  price,
  mode,
  createProduct = createDraftProduct,
}: {
  products: DraftProductItem[]
  selectedRowKeys: string[]
  expandedRowKeys: string[]
  type: ProductCreationType
  count: number
  price?: number | null
  mode: BatchAddMode
  createProduct?: (type: ProductCreationType, index: number, price?: number | null) => DraftProductItem
}): { products: DraftProductItem[]; selectedRowKeys: string[]; expandedRowKeys: string[] } {
  if (mode === 'append') {
    const newProducts = Array.from({ length: count }, (_, index) => createProduct(type, products.length + index, price))
    return {
      products: [...products, ...newProducts],
      selectedRowKeys,
      expandedRowKeys: [
        ...expandedRowKeys,
        ...newProducts.filter((item) => item.productType === ProductCreationType.SET).map((item) => item.key),
      ],
    }
  }

  // 覆盖模式必须同时覆盖数量和类型，避免选择“套装”时保留旧的普通行。
  const nextProducts = Array.from({ length: count }, (_, index) => createProduct(type, index, price))
  return {
    products: nextProducts,
    selectedRowKeys: [],
    expandedRowKeys: nextProducts.filter((item) => item.productType === ProductCreationType.SET).map((item) => item.key),
  }
}

export function buildCreateBatchItems(products: DraftProductItem[]): CreateBatchRequestItem[] {
  return products.map((product) => ({
    productName: product.productName?.trim() || undefined,
    productType: product.productType,
    privateLabelPrice: product.privateLabelPrice ?? undefined,
    setQuantity: product.setQuantity ?? undefined,
    setPrice: product.setPrice ?? undefined,
    createCount: product.productType === ProductCreationType.SET ? normalizeCreateCount(product.createCount) : undefined,
    subItems: product.productType === ProductCreationType.SET
      ? getValidSetSubItems(product.subItems).map((subItem) => ({
        productName: subItem.productName?.trim() || undefined,
        productType: ProductCreationType.SET_SUB_ITEM,
        privateLabelPrice: subItem.privateLabelPrice ?? undefined,
      }))
      : undefined,
  }))
}
