import type { WarehouseCategoryNode } from '../../../services/warehouseCategoryService'
import type { WarehouseProductListItem } from '../../../services/warehouseProductService'

export interface WarehouseCategoryLookup {
  byGuid: Map<string, string>
  byName: Map<string, string[]>
  displayByGuid: Map<string, Record<WarehouseCategoryDisplayLanguage, string>>
  displayByName: Map<string, Record<WarehouseCategoryDisplayLanguage, string[]>>
  descendantGuidsByGuid: Map<string, Set<string>>
}

export type WarehouseCategoryDisplayLanguage = 'zh' | 'en'

function formatCategoryNodeName(node: WarehouseCategoryNode) {
  const name = node.categoryName.trim()
  const chineseName = node.chineseName?.trim()
  return [name, chineseName].filter(Boolean).join(' / ')
}

export function getWarehouseCategoryDisplayLanguage(language?: string): WarehouseCategoryDisplayLanguage {
  return language?.toLowerCase().startsWith('en') ? 'en' : 'zh'
}

export function formatWarehouseCategoryNodeName(
  node: Pick<WarehouseCategoryNode, 'categoryName' | 'chineseName'>,
  language?: string,
) {
  const displayLanguage = getWarehouseCategoryDisplayLanguage(language)
  const name = node.categoryName.trim()
  const chineseName = node.chineseName?.trim()
  return displayLanguage === 'en' ? name || chineseName || '' : chineseName || name || ''
}

function localizeCategoryPathText(path: string, language?: string) {
  const displayLanguage = getWarehouseCategoryDisplayLanguage(language)
  return path
    .split('>')
    .map((part) => {
      const candidates = part.split('/').map((item) => item.trim()).filter(Boolean)
      if (!candidates.length) return ''
      const preferred = displayLanguage === 'zh'
        ? candidates.find((item) => /[\u3400-\u9fff]/.test(item))
        : candidates.find((item) => !/[\u3400-\u9fff]/.test(item))
      return preferred || candidates[0]
    })
    .filter(Boolean)
    .join(' > ')
}

function addUniquePath(paths: string[] | undefined, path: string) {
  if (!path) {
    return [path].filter(Boolean)
  }
  if (!paths) {
    return [path]
  }
  return paths.includes(path) ? paths : [...paths, path]
}

function addLocalizedNamePath(
  lookup: WarehouseCategoryLookup,
  name: string | undefined,
  fullPath: string,
  localizedFullPath: Record<WarehouseCategoryDisplayLanguage, string>,
) {
  if (!name || !fullPath) return
  lookup.byName.set(name, addUniquePath(lookup.byName.get(name), fullPath))
  lookup.displayByName.set(name, {
    zh: addUniquePath(lookup.displayByName.get(name)?.zh, localizedFullPath.zh),
    en: addUniquePath(lookup.displayByName.get(name)?.en, localizedFullPath.en),
  })
}

export function buildWarehouseCategoryLookup(
  nodes: WarehouseCategoryNode[],
  parentPath: string[] = [],
  displayParentPath: Record<WarehouseCategoryDisplayLanguage, string[]> = { zh: [], en: [] },
  lookup: WarehouseCategoryLookup = {
    byGuid: new Map(),
    byName: new Map(),
    displayByGuid: new Map(),
    displayByName: new Map(),
    descendantGuidsByGuid: new Map(),
  },
): WarehouseCategoryLookup {
  for (const node of nodes) {
    const displayName = formatCategoryNodeName(node)
    const currentPath = [...parentPath, displayName].filter(Boolean)
    const fullPath = currentPath.join(' > ')
    const displayPath = {
      zh: [...displayParentPath.zh, formatWarehouseCategoryNodeName(node, 'zh')].filter(Boolean),
      en: [...displayParentPath.en, formatWarehouseCategoryNodeName(node, 'en')].filter(Boolean),
    }
    const localizedFullPath = {
      zh: displayPath.zh.join(' > '),
      en: displayPath.en.join(' > '),
    }
    const descendantGuids = new Set<string>()

    if (node.categoryGUID && fullPath) {
      lookup.byGuid.set(node.categoryGUID, fullPath)
      lookup.displayByGuid.set(node.categoryGUID, localizedFullPath)
      descendantGuids.add(node.categoryGUID)
    }

    // 后端列表当前可能只返回分类名，保留按中英文名称反查完整路径的兜底。
    addLocalizedNamePath(lookup, node.categoryName, fullPath, localizedFullPath)
    addLocalizedNamePath(lookup, node.chineseName, fullPath, localizedFullPath)

    buildWarehouseCategoryLookup(node.children || [], currentPath, displayPath, lookup)
    // 分类过滤选择父级时，需要包含所有层级的子孙分类商品。
    for (const child of node.children || []) {
      const childDescendantGuids = lookup.descendantGuidsByGuid.get(child.categoryGUID)
      childDescendantGuids?.forEach((guid) => descendantGuids.add(guid))
    }
    if (node.categoryGUID) {
      lookup.descendantGuidsByGuid.set(node.categoryGUID, descendantGuids)
    }
  }

  return lookup
}

export function getWarehouseProductCategoryTooltip(
  record: Pick<WarehouseProductListItem, 'categoryName' | 'warehouseCategoryGUID' | 'categoryPath'>,
  lookup: WarehouseCategoryLookup,
  language?: string,
) {
  const displayLanguage = getWarehouseCategoryDisplayLanguage(language)

  if (record.warehouseCategoryGUID) {
    const pathByGuid = lookup.displayByGuid.get(record.warehouseCategoryGUID)?.[displayLanguage]
    if (pathByGuid) {
      return pathByGuid
    }
  }

  const name = record.categoryName?.trim()
  if (!name) {
    return record.categoryPath ? localizeCategoryPathText(record.categoryPath, displayLanguage) : undefined
  }

  const pathsByName = lookup.byName.get(name)
  const displayPathsByName = lookup.displayByName.get(name)?.[displayLanguage]
  if (displayPathsByName?.length) {
    return displayPathsByName.join('；')
  }
  if (record.categoryPath) {
    return localizeCategoryPathText(record.categoryPath, displayLanguage)
  }
  if (pathsByName?.length) {
    return pathsByName.map((path) => localizeCategoryPathText(path, displayLanguage)).join('；')
  }

  return name
}
