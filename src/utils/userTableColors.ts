import { getStableTagColor } from './tagColors'

const ROLE_COLOR_MAP: Record<string, string> = {
  Admin: 'red',
  管理员: 'red',
  Manager: 'orange',
  经理: 'orange',
  WarehouseManager: 'gold',
  仓库经理: 'gold',
  StoreManager: 'green',
  店长: 'green',
  StoreStaff: 'cyan',
  店铺员工: 'cyan',
  WarehouseStaff: 'blue',
  仓库员工: 'blue',
  Order: 'purple',
  订货员: 'purple',
  User: 'default',
  用户: 'default',
}

export function getRoleColor(roleName: string): string {
  return ROLE_COLOR_MAP[roleName] || 'default'
}

export function getStoreColor(storeName: string): string {
  return getStableTagColor(storeName)
}
