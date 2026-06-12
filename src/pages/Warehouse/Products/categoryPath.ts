import type { WarehouseCategoryNode } from '../../../services/warehouseCategoryService'
import type { WarehouseProductListItem } from '../../../services/warehouseProductService'

export interface WarehouseCategoryLookup {
  byGuid: Map<string, string>
  byName: Map<string, string[]>
  descendantGuidsByGuid: Map<string, Set<string>>
}

function formatCategoryNodeName(node: WarehouseCategoryNode) {
  const name = node.categoryName.trim()
  const chineseName = node.chineseName?.trim()
  return [name, chineseName].filter(Boolean).join(' / ')
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

export function buildWarehouseCategoryLookup(
  nodes: WarehouseCategoryNode[],
  parentPath: string[] = [],
  lookup: WarehouseCategoryLookup = { byGuid: new Map(), byName: new Map(), descendantGuidsByGuid: new Map() },
): WarehouseCategoryLookup {
  for (const node of nodes) {
    const displayName = formatCategoryNodeName(node)
    const currentPath = [...parentPath, displayName].filter(Boolean)
    const fullPath = currentPath.join(' > ')
    const descendantGuids = new Set<string>()

    if (node.categoryGUID && fullPath) {
      lookup.byGuid.set(node.categoryGUID, fullPath)
      descendantGuids.add(node.categoryGUID)
    }

    // 后端列表当前可能只返回 categoryName，保留按名称反查完整路径的兜底。
    if (node.categoryName && fullPath) {
      lookup.byName.set(node.categoryName, addUniquePath(lookup.byName.get(node.categoryName), fullPath))
    }

    buildWarehouseCategoryLookup(node.children || [], currentPath, lookup)
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
) {
  if (record.categoryPath) {
    return record.categoryPath
  }

  if (record.warehouseCategoryGUID) {
    const pathByGuid = lookup.byGuid.get(record.warehouseCategoryGUID)
    if (pathByGuid) {
      return pathByGuid
    }
  }

  const name = record.categoryName?.trim()
  if (!name) {
    return undefined
  }

  const pathsByName = lookup.byName.get(name)
  if (pathsByName?.length) {
    return pathsByName.join('；')
  }

  return name
}
