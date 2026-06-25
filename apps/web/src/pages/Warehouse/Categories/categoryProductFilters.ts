import type { TFunction } from 'i18next'
import type { WarehouseCategoryNode } from '../../../services/warehouseCategoryService'
import { formatWarehouseCategoryNodeName } from '../Products/categoryPath'

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

export interface CategoryTreeSelectOption {
  title: string
  value: string
  key: string
  searchText: string
  children?: CategoryTreeSelectOption[]
}

function normalizeCategorySearchText(parts: Array<string | undefined>) {
  return Array.from(new Set(parts.map((part) => part?.trim()).filter(Boolean) as string[]))
    .join(' ')
    .toLowerCase()
}

function formatCategoryOptionName(node: WarehouseCategoryNode, language?: string) {
  if (language) {
    return formatWarehouseCategoryNodeName(node, language)
  }

  return `${node.categoryName}${node.chineseName ? ` / ${node.chineseName}` : ''}`
}

export function buildCategoryOptions(nodes: WarehouseCategoryNode[], level = 0, language?: string): CategorySelectOption[] {
  return nodes.flatMap((node) => [
    {
      value: node.categoryGUID,
      label: `${level > 0 ? `${'--'.repeat(level)} ` : ''}${formatCategoryOptionName(node, language)}`,
    },
    ...buildCategoryOptions(node.children || [], level + 1, language),
  ])
}

export function buildFilterCategoryOptions(nodes: WarehouseCategoryNode[], t: TFunction, language?: string): CategorySelectOption[] {
  return [
    { value: ALL_PRODUCTS_FILTER_KEY, label: t('warehouse.categories.allCategoryOption') },
    { value: UNCATEGORIZED_PRODUCTS_FILTER_KEY, label: t('warehouse.categories.uncategorizedOption', '未分类商品') },
    ...buildCategoryOptions(nodes, 0, language),
  ]
}

function buildCategoryTreeOptions(
  nodes: WarehouseCategoryNode[],
  language?: string,
  parentSearchParts: string[] = [],
): CategoryTreeSelectOption[] {
  return nodes.map((node) => {
    const title = formatCategoryOptionName(node, language)
    const currentSearchParts = [
      ...parentSearchParts,
      node.categoryName,
      node.chineseName,
      title,
    ].filter((part): part is string => Boolean(part?.trim()))
    const children = buildCategoryTreeOptions(node.children || [], language, currentSearchParts)

    return {
      title,
      value: node.categoryGUID,
      key: node.categoryGUID,
      // TreeSelect 搜索只看一个字段，这里把父级路径和中英文名称都压进去。
      searchText: normalizeCategorySearchText(currentSearchParts),
      children: children.length ? children : undefined,
    }
  })
}

export function buildFilterCategoryTreeOptions(
  nodes: WarehouseCategoryNode[],
  t: TFunction,
  language?: string,
): CategoryTreeSelectOption[] {
  const allLabel = t('warehouse.categories.allCategoryOption')
  const uncategorizedLabel = t('warehouse.categories.uncategorizedOption', '未分类商品')

  return [
    {
      title: allLabel,
      value: ALL_PRODUCTS_FILTER_KEY,
      key: ALL_PRODUCTS_FILTER_KEY,
      searchText: normalizeCategorySearchText([allLabel, 'all', '全部商品']),
    },
    {
      title: uncategorizedLabel,
      value: UNCATEGORIZED_PRODUCTS_FILTER_KEY,
      key: UNCATEGORIZED_PRODUCTS_FILTER_KEY,
      searchText: normalizeCategorySearchText([uncategorizedLabel, 'uncategorized', '未分类商品']),
    },
    ...buildCategoryTreeOptions(nodes, language),
  ]
}
