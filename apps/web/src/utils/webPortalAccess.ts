import type { AccessControl } from '../types/auth'
import { P } from '../types/permissions'

type WebPortalAccess = Pick<
  AccessControl,
  | 'isAdmin'
  | 'canAccessDashboard'
  | 'canAccessOrderFront'
  | 'canManageWarehouse'
  | 'canManageWarehouseOrders'
  | 'canManageStoreOrderImportPriceVariance'
  | 'canViewContainers'
  | 'canViewProductMovementReport'
  | 'canManageLocalPurchase'
  | 'canEditLocalPurchase'
  | 'canManageSystemSettings'
  | 'canViewAppDownloads'
  | 'canViewOperationAudits'
  | 'hasPermission'
>

export const WEB_NO_ACCESS_PATH = '/web-access-denied'

type AdminEntryRule = {
  defaultPath: string
  targetPrefixes: readonly string[]
  canAccess: (access: BackendNavigationAccess) => boolean
}

export type BackendNavigationAccess = Omit<WebPortalAccess, 'canAccessOrderFront'>

// 与后端 NavigationService.HasBackendNavigationAccess 的权限集合保持同一入口语义。
const ADMIN_ENTRY_RULES: readonly AdminEntryRule[] = [
  {
    defaultPath: '/dashboard',
    targetPrefixes: ['/dashboard'],
    canAccess: (access) => access.canAccessDashboard,
  },
  {
    defaultPath: '/warehouse/store-orders',
    targetPrefixes: [
      '/warehouse/store-orders',
      '/warehouse/preorders',
      '/warehouse/store-order',
    ],
    canAccess: (access) => access.canManageWarehouseOrders,
  },
  {
    defaultPath: '/warehouse/store-order-import-price-variance',
    targetPrefixes: ['/warehouse/store-order-import-price-variance'],
    canAccess: (access) => access.canManageStoreOrderImportPriceVariance,
  },
  {
    defaultPath: '/warehouse/store-orders',
    // 旧 Warehouse.Manage 只覆盖其实际派生的仓库业务页，不能绕过价差或货柜叶子权限。
    targetPrefixes: [
      '/warehouse/products',
      '/warehouse/categories',
      '/warehouse/locations',
      '/warehouse/product-grade-management',
    ],
    canAccess: (access) => access.canManageWarehouse,
  },
  {
    defaultPath: '/warehouse/containers',
    targetPrefixes: [
      '/warehouse/containers',
      '/warehouse/container/detail',
      '/warehouse/container/allocation-sales',
    ],
    canAccess: (access) => access.canViewContainers,
  },
  {
    defaultPath: '/executive-sales-intelligence/product-movement-report',
    targetPrefixes: ['/executive-sales-intelligence/product-movement-report'],
    // Reports.View 兼容叶子页面访问，但后台导航入口与后端一致，仅认专用权限。
    canAccess: (access) => access.hasPermission(P.Reports.ProductMovementView),
  },
  {
    defaultPath: '/executive-sales-intelligence/purchase-amount-dashboard',
    targetPrefixes: [
      '/executive-sales-intelligence/purchase-amount-dashboard',
      '/pos-admin/local-supplier-invoices',
      '/pos-admin/local-supplier-purchase-sales-analysis',
      '/pos-admin/invoice-detail',
    ],
    canAccess: (access) => access.canManageLocalPurchase,
  },
  {
    defaultPath: '/system/invoice-email-settings',
    targetPrefixes: [
      '/system/invoice-email-settings',
      '/system/payment-terminal-settings',
      '/system/emergency-login-keys',
    ],
    canAccess: (access) => access.canManageSystemSettings,
  },
  {
    defaultPath: '/system/app-downloads',
    targetPrefixes: ['/system/app-downloads', '/system/wpf-versions'],
    canAccess: (access) => access.canViewAppDownloads,
  },
  {
    defaultPath: '/pos-admin/operation-logs',
    targetPrefixes: ['/pos-admin/operation-logs'],
    canAccess: (access) => access.canViewOperationAudits,
  },
]

function matchesRoutePrefix(target: string, prefix: string) {
  return (
    target === prefix ||
    target.startsWith(`${prefix}/`) ||
    target.startsWith(`${prefix}?`) ||
    target.startsWith(`${prefix}#`)
  )
}

export function hasBackendNavigationAccess(access: BackendNavigationAccess) {
  return access.isAdmin || ADMIN_ENTRY_RULES.some((rule) => rule.canAccess(access))
}

export function getDefaultWebPath(access: WebPortalAccess) {
  const adminEntry = ADMIN_ENTRY_RULES.find((rule) => rule.canAccess(access))
  if (adminEntry) {
    return adminEntry.defaultPath
  }
  if (access.canAccessOrderFront) {
    return '/shop'
  }
  return WEB_NO_ACCESS_PATH
}

export function resolveAuthorizedWebTarget(target: string | null | undefined, access: WebPortalAccess) {
  if (!target || !target.startsWith('/') || target.startsWith('//') || target === '/login') {
    return undefined
  }
  if (target === '/') {
    return target
  }
  if (target === WEB_NO_ACCESS_PATH) {
    return !hasBackendNavigationAccess(access) && !access.canAccessOrderFront ? target : undefined
  }
  if (/^\/shop(?:\/|[?#]|$)/.test(target)) {
    return access.canAccessOrderFront ? target : undefined
  }

  // 编辑页与只读详情共用进货单路径前缀，必须在父级查看权限匹配前单独核验编辑权限。
  if (/^\/pos-admin\/local-supplier-invoices\/[^/?#]+\/?(?:[?#]|$)/.test(target)) {
    return access.canEditLocalPurchase ? target : undefined
  }

  // 后台壳只负责放行入口，具体重定向目标仍按对应叶子权限核验。
  const authorizedRule = ADMIN_ENTRY_RULES.find(
    (rule) =>
      rule.canAccess(access) &&
      rule.targetPrefixes.some((prefix) => matchesRoutePrefix(target, prefix)),
  )
  if (authorizedRule) {
    return target
  }
  return access.isAdmin ? target : undefined
}
