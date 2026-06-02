import type { TFunction } from 'i18next'
import type { WarehouseCategoryNode } from '../../../services/warehouseCategoryService'

export const ALL_PRODUCTS_FILTER_KEY: string = '__ALL_PRODUCTS__'
export const UNCATEGORIZED_PRODUCTS_FILTER_KEY: string = '__UNCATEGORIZED_PRODUCTS__'

export type CategoryProductFilterMode =
  | { type: 'all'; categoryGuid?: undefined }
  | { type: 'uncategorized'; categoryGuid?: undefined }
  | { type: 'category'; categoryGuid: string }

export type CategoryProductQueryState = CategoryProductFilterMode | null

export function resolveCategoryProductFilterMode(value?: string): CategoryProductFilterMode {
  if (value === ALL_PRODUCTS_FILTER_KEY) {
    return { type: 'all' }
  }

  // 分类筛选被清空时，业务语义是查询未设置分类的商品，而不是回退到 ALL。
  if (!value || value === UNCATEGORIZED_PRODUCTS_FILTER_KEY) {
    return { type: 'uncategorized' }
  }

  return { type: 'category', categoryGuid: value }
}

export function hasExecutedCategoryProductQuery(
  state: CategoryProductQueryState,
): state is CategoryProductFilterMode {
  return state !== null
}

export interface CategorySelectOption {
  label: string
  value: string
}

export function buildCategoryOptions(nodes: WarehouseCategoryNode[], level = 0): CategorySelectOption[] {
  return nodes.flatMap((node) => [
    {
      value: node.categoryGUID,
      label: `${level > 0 ? `${'--'.repeat(level)} ` : ''}${node.categoryName}${node.chineseName ? ` / ${node.chineseName}` : ''}`,
    },
    ...buildCategoryOptions(node.children || [], level + 1),
  ])
}

export function buildFilterCategoryOptions(nodes: WarehouseCategoryNode[], t: TFunction): CategorySelectOption[] {
  return [
    { value: ALL_PRODUCTS_FILTER_KEY, label: t('warehouse.categories.allCategoryOption') },
    ...buildCategoryOptions(nodes),
  ]
}
