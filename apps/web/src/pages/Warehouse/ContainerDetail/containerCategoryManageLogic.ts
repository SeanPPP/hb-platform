import type { WarehouseCategoryNode } from '../../../services/warehouseCategoryService'
import { formatWarehouseCategoryNodeName } from '../Products/categoryPath'

export type ContainerCategoryChangeKind = 'create' | 'update' | 'delete'

export interface ContainerCategoryChange {
  kind: ContainerCategoryChangeKind
  categoryGuid: string
  fallbackCategoryGuid: string | undefined
}

export interface ContainerCategoryParentOption {
  label: string
  value: string
}

export interface ContainerCategorySelectionState {
  managedCategoryGuid: string | undefined
  activeTargetCategoryGuid: string | undefined
}

export interface ContainerCategoryMutationOutcome {
  change: ContainerCategoryChange
  tree?: WarehouseCategoryNode[]
  refreshError?: unknown
}

export function findContainerCategory(
  nodes: WarehouseCategoryNode[],
  targetGuid?: string,
): WarehouseCategoryNode | undefined {
  if (!targetGuid) {
    return undefined
  }

  for (const node of nodes) {
    if (node.categoryGUID === targetGuid) {
      return node
    }

    const matched = findContainerCategory(node.children || [], targetGuid)
    if (matched) {
      return matched
    }
  }

  return undefined
}

export function collectContainerCategoryDescendantGuids(node?: WarehouseCategoryNode): string[] {
  if (!node) {
    return []
  }

  return (node.children || []).flatMap((child) => [
    child.categoryGUID,
    ...collectContainerCategoryDescendantGuids(child),
  ])
}

export function collectContainerCategoryExpandedGuids(
  nodes: WarehouseCategoryNode[],
  maxLevel: number,
  level = 1,
): string[] {
  if (level > maxLevel) {
    return []
  }

  return nodes.flatMap((node) => [
    node.categoryGUID,
    ...collectContainerCategoryExpandedGuids(node.children || [], maxLevel, level + 1),
  ])
}

export function buildContainerCategoryParentOptions(
  nodes: WarehouseCategoryNode[],
  editingCategoryGuid?: string,
  language?: string,
  level = 0,
): ContainerCategoryParentOption[] {
  const editingCategory = findContainerCategory(nodes, editingCategoryGuid)
  const excludedGuids = new Set([
    ...(editingCategory ? [editingCategory.categoryGUID] : []),
    ...collectContainerCategoryDescendantGuids(editingCategory),
  ])

  const buildOptions = (
    currentNodes: WarehouseCategoryNode[],
    currentLevel: number,
  ): ContainerCategoryParentOption[] => currentNodes.flatMap((node) => [
    ...(excludedGuids.has(node.categoryGUID)
      ? []
      : [{
          value: node.categoryGUID,
          label: `${currentLevel ? `${'--'.repeat(currentLevel)} ` : ''}${formatWarehouseCategoryNodeName(node, language)}`,
        }]),
    ...buildOptions(node.children || [], currentLevel + 1),
  ])

  return buildOptions(nodes, level)
}

export function resolveContainerCategorySelectionAfterRefresh(
  nodes: WarehouseCategoryNode[],
  currentManagedCategoryGuid: string | undefined,
  activeTargetCategoryGuid: string | undefined,
  change: ContainerCategoryChange,
): ContainerCategorySelectionState {
  const categoryExists = (categoryGuid?: string) => Boolean(findContainerCategory(nodes, categoryGuid))
  let managedCategoryGuid: string | undefined

  if (change.kind === 'delete') {
    managedCategoryGuid = categoryExists(change.fallbackCategoryGuid)
      ? change.fallbackCategoryGuid
      : undefined
  } else if (categoryExists(change.categoryGuid)) {
    managedCategoryGuid = change.categoryGuid
  } else if (categoryExists(change.fallbackCategoryGuid)) {
    managedCategoryGuid = change.fallbackCategoryGuid
  } else if (categoryExists(currentManagedCategoryGuid)) {
    managedCategoryGuid = currentManagedCategoryGuid
  }

  let nextActiveTargetCategoryGuid = categoryExists(activeTargetCategoryGuid)
    ? activeTargetCategoryGuid
    : undefined

  if (change.kind === 'create' && categoryExists(change.categoryGuid)) {
    nextActiveTargetCategoryGuid = change.categoryGuid
  } else if (change.kind === 'delete' && activeTargetCategoryGuid === change.categoryGuid) {
    // 删除当前分配目标时必须清空，不能静默改投父分类。
    nextActiveTargetCategoryGuid = undefined
  }

  return {
    managedCategoryGuid,
    activeTargetCategoryGuid: nextActiveTargetCategoryGuid,
  }
}

export function resolveContainerCategoryTargetAfterMutation(
  activeTargetCategoryGuid: string | undefined,
  change: ContainerCategoryChange,
): string | undefined {
  if (change.kind === 'create') {
    return change.categoryGuid
  }

  if (change.kind === 'delete' && activeTargetCategoryGuid === change.categoryGuid) {
    return undefined
  }

  return activeTargetCategoryGuid
}

export async function executeContainerCategoryMutation(
  mutation: () => Promise<ContainerCategoryChange>,
  refreshTree: () => Promise<WarehouseCategoryNode[]>,
  onCommitted: (change: ContainerCategoryChange) => void,
): Promise<ContainerCategoryMutationOutcome> {
  const change = await mutation()

  // 写操作一旦成功就先同步目标选择，避免后续刷新失败时重复新增或提交已删除 GUID。
  onCommitted(change)

  try {
    return {
      change,
      tree: await refreshTree(),
    }
  } catch (refreshError) {
    return {
      change,
      refreshError,
    }
  }
}
