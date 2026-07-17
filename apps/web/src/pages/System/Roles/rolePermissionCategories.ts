import type { PermissionCategoryDto } from '../../../types/role'

const POS_TERMINAL_PERMISSION_PREFIX = 'Permissions.PosTerminal.'

const POS_TERMINAL_MODULE_ORDER = new Map(
  [
    'Sales',
    'Payment',
    'Returns',
    'SpecialProducts',
    'History',
    'DailyClose',
    'Installments',
    'Settings',
    'CashDrawer',
    'Receipt',
    'CustomerDisplay',
    'System',
    'Audit',
  ].map((module, index) => [module, index]),
)

interface RankedCategory {
  category: PermissionCategoryDto
  originalIndex: number
  group: number
  knownModuleOrder: number
}

function getPosTerminalModule(permissionName: string): string | undefined {
  if (!permissionName.startsWith(POS_TERMINAL_PERMISSION_PREFIX)) return undefined
  return permissionName.slice(POS_TERMINAL_PERMISSION_PREFIX.length).split('.')[0]
}

export function sortRolePermissionCategories(
  categories: PermissionCategoryDto[],
): PermissionCategoryDto[] {
  const rankedCategories: RankedCategory[] = categories.map((category, originalIndex) => {
    const posModules = category.permissions
      .map((permission) => getPosTerminalModule(permission.name))
      .filter((module): module is string => module !== undefined)

    if (posModules.length === 0) {
      return { category, originalIndex, group: 2, knownModuleOrder: Number.MAX_SAFE_INTEGER }
    }

    const knownModuleOrders = posModules
      .map((module) => POS_TERMINAL_MODULE_ORDER.get(module))
      .filter((order): order is number => order !== undefined)

    // 已知 POS 模块按业务顺序置顶；未来新增模块紧随其后，并依靠原索引保持 API 顺序。
    return {
      category,
      originalIndex,
      group: knownModuleOrders.length > 0 ? 0 : 1,
      knownModuleOrder:
        knownModuleOrders.length > 0 ? Math.min(...knownModuleOrders) : Number.MAX_SAFE_INTEGER,
    }
  })

  return rankedCategories
    .sort(
      (left, right) =>
        left.group - right.group ||
        left.knownModuleOrder - right.knownModuleOrder ||
        left.originalIndex - right.originalIndex,
    )
    .map(({ category }) => category)
}
