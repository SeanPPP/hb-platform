import type { PermissionCategoryDto, PermissionDto } from '../../../types/role'
import { sortRolePermissionCategories } from './rolePermissionCategories'

function assertArrayEqual<T>(actual: T[], expected: T[], message: string) {
  const actualText = JSON.stringify(actual)
  const expectedText = JSON.stringify(expected)
  if (actualText !== expectedText) {
    throw new Error(`${message}. Expected: ${expectedText}, received: ${actualText}`)
  }
}

function createPermission(name: string, category: string): PermissionDto {
  return {
    name,
    displayName: name,
    category,
    isSystemPermission: false,
    createdAt: '2026-07-16T00:00:00Z',
  }
}

function createCategory(
  category: string,
  displayName: string,
  permissionNames: string[],
): PermissionCategoryDto {
  return {
    category,
    displayName,
    permissions: permissionNames.map((name) => createPermission(name, category)),
  }
}

const ordinaryFirst = createCategory('System.Users', '用户管理', ['Permissions.Users.View'])
const ordinarySecond = createCategory('PosProducts', 'POS 管理', ['Permissions.PosProducts.View'])
const posPayment = createCategory(
  'PosTerminal.Payment',
  'POS 收款',
  ['Permissions.PosTerminal.Payment.TakePayment'],
)
const posSales = createCategory(
  'PosTerminal.Sales',
  'POS 销售',
  ['Permissions.PosTerminal.Sales.CreateOrder'],
)
const posAudit = createCategory(
  'PosTerminal.Audit',
  'POS 审计',
  ['Permissions.PosTerminal.Audit.View'],
)
const unknownPosFirst = createCategory(
  'PosTerminal.FutureOne',
  '未来 POS 一',
  ['Permissions.PosTerminal.FutureOne.View'],
)
const unknownPosSecond = createCategory(
  'PosTerminal.FutureTwo',
  '未来 POS 二',
  ['Permissions.PosTerminal.FutureTwo.Manage'],
)

const knownPosModuleOrder = [
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
]

const reversedKnownPosCategories = [...knownPosModuleOrder]
  .reverse()
  .map((module) =>
    createCategory(
      `PosTerminal.${module}`,
      `任意显示名-${module}`,
      [`Permissions.PosTerminal.${module}.View`],
    ),
  )

assertArrayEqual(
  sortRolePermissionCategories(reversedKnownPosCategories).map(
    (category) => category.permissions[0]?.name.split('.')[2],
  ),
  knownPosModuleOrder,
  '已知 POS 前端分类应按权限代码模块排序，不依赖中文显示名',
)

const mixedCategories = [
  ordinaryFirst,
  posAudit,
  unknownPosFirst,
  ordinarySecond,
  posPayment,
  unknownPosSecond,
  posSales,
]
const originalSnapshot = [...mixedCategories]
const originalSerialization = JSON.stringify(mixedCategories)
const sortedCategories = sortRolePermissionCategories(mixedCategories)

assertArrayEqual(
  sortedCategories.map((category) => category.displayName),
  ['POS 销售', 'POS 收款', 'POS 审计', '未来 POS 一', '未来 POS 二', '用户管理', 'POS 管理'],
  '乱序分类应将 POS 前端权限按固定业务顺序连续置顶',
)

assertArrayEqual(
  sortedCategories.slice(3, 5),
  [unknownPosFirst, unknownPosSecond],
  '未知 POS 前端分类应位于已知分类之后并保持 API 原相对顺序',
)

assertArrayEqual(
  sortedCategories.slice(5),
  [ordinaryFirst, ordinarySecond],
  '普通分类应保持 API 原相对顺序，Permissions.PosProducts 仍属于普通分类',
)

assertArrayEqual(
  mixedCategories,
  originalSnapshot,
  '排序不应修改输入数组',
)

if (JSON.stringify(mixedCategories) !== originalSerialization) {
  throw new Error('排序不应修改输入分类内容')
}

if (sortedCategories === mixedCategories) {
  throw new Error('排序函数应返回新数组')
}

const categoryWithMixedPermissions = createCategory(
  'Mixed',
  '混合权限分类',
  ['Permissions.Users.View', 'Permissions.PosTerminal.Settings.Manage'],
)

assertArrayEqual(
  sortRolePermissionCategories([ordinaryFirst, categoryWithMixedPermissions]),
  [categoryWithMixedPermissions, ordinaryFirst],
  '分类内任一权限代码命中 PosTerminal 前缀就应视为 POS 前端分类',
)
