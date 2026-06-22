import type { AccessControl } from '../types/auth'
import { P } from '../types/permissions'

export type WebMenuPreviewTranslate = (key: string, fallback?: string) => string

interface WebMenuPreviewRoute {
  path: string
  title: string
  accessKey?: keyof AccessControl
  children?: WebMenuPreviewRoute[]
}

export interface WebMenuPreviewNode {
  key: string
  title: string
  path: string
  accessKey?: keyof AccessControl
  permissionCodes: string[]
  visible: boolean
  edit: {
    canAdd: boolean
    canRemove: boolean
    isReadOnly: boolean
    isFixed: boolean
    addPermissionCodes: string[]
    removePermissionCodes: string[]
    reason?: 'group' | 'read-only' | 'fixed'
  }
  children?: WebMenuPreviewNode[]
}

export type WebMenuVisibilityFilter = 'all' | 'visible' | 'hidden'

interface BuildWebRoleMenuPreviewOptions {
  includeHidden?: boolean
  explicitPermissionCodes?: string[]
  readOnly?: boolean
}

const accessKeyPermissionMap: Partial<Record<keyof AccessControl, string[]>> = {
  isAdmin: [],
  canAccessDashboard: [P.Dashboard.View],
  canAccessOrderFront: [P.OrderFront.View],
  canReadStore: [P.Stores.View],
  canViewEmployeeProfiles: [P.EmployeeProfiles.View],
  canViewSystemLogs: [P.System.ViewLogs],
  canManageScheduledTasks: [P.System.ManageScheduledTasks],
  canManageSystemSettings: [P.System.ManageSettings],
  canViewAppDownloads: [P.System.ViewAppDownloads],
  canReadUser: [P.Users.View],
  canReadRole: [P.Roles.View],
  canViewDeviceRegistration: [P.DeviceRegistration.View, P.DeviceRegistration.Manage],
  canManageDomesticSuppliers: [P.DomesticPurchase.ManageSuppliers],
  canReadProduct: [P.Products.View],
  canManageDomesticPrefixCodes: [P.DomesticPurchase.ManagePrefixCodes],
  canManageDomesticProducts: [P.DomesticPurchase.ManageProducts],
  canManageWarehouseOrders: [P.Warehouse.ManageOrders, P.Warehouse.Manage],
  canViewContainers: [P.Container.View],
  canManageWarehouseProducts: [P.Warehouse.ManageProducts, P.Warehouse.Manage],
  canManageWarehouseCategories: [P.Warehouse.ManageCategories, P.Warehouse.Manage],
  canManageWarehouseLocations: [P.Warehouse.ManageLocations, P.Warehouse.Manage],
  canViewReports: [P.Reports.View],
  canViewSalesIntelligence: [P.Reports.View, P.Reports.ProductMovementView],
  canViewProductMovementReport: [P.Reports.ProductMovementView, P.Reports.View],
  canViewAustralianSuppliers: [P.AustralianSuppliers.View],
  canViewPosProducts: [P.PosProducts.View, P.PosProducts.Manage],
  canManageStoreProducts: [P.StoreProducts.View],
  canManagePricing: [P.PricingStrategy.View],
  canManagePromotions: [P.Promotions.View],
  canManageAdvertisements: [P.Advertisements.View],
  canViewAttendanceSchedule: [P.Attendance.AdminView, P.Attendance.ScheduleViewStore],
  canManageStoreOps: [P.Store.ManageOperations],
  canReadOrder: [P.Orders.View],
  canManageLocalPurchase: [P.LocalPurchase.View, 'LocalInvocie.View'],
  canEditLocalPurchase: [P.LocalPurchase.Edit, 'LocalInvocie.Edit'],
}

const webMenuPreviewRoutes: WebMenuPreviewRoute[] = [
  { path: '/dashboard', title: 'menu.dashboard', accessKey: 'canAccessDashboard' },
  {
    path: '/system',
    title: 'menu.system',
    children: [
      { path: '/system/stores', title: 'menu.systemStores', accessKey: 'canReadStore' },
      { path: '/system/employee-profiles', title: 'menu.systemEmployeeProfiles', accessKey: 'canViewEmployeeProfiles' },
      { path: '/system/center-logs', title: 'menu.systemCenterLogs', accessKey: 'canViewSystemLogs' },
      { path: '/system/scheduled-statistics', title: 'menu.scheduledStatistics', accessKey: 'canManageScheduledTasks' },
      { path: '/system/invoice-email-settings', title: 'menu.invoiceEmailSettings', accessKey: 'canManageSystemSettings' },
      { path: '/system/users', title: 'menu.systemUsers', accessKey: 'canReadUser' },
      { path: '/system/roles', title: 'menu.systemRoles', accessKey: 'canReadRole' },
      { path: '/system/permissions', title: 'menu.systemPermissions', accessKey: 'canReadRole' },
      { path: '/system/device-registration', title: 'menu.deviceRegistration', accessKey: 'canViewDeviceRegistration' },
      { path: '/system/app-downloads', title: 'menu.appDownloads', accessKey: 'canViewAppDownloads' },
    ],
  },
  {
    path: '/domestic-purchase',
    title: 'menu.domesticPurchase',
    children: [
      { path: '/domestic-purchase/china-suppliers', title: 'menu.chinaSuppliers', accessKey: 'canManageDomesticSuppliers' },
      { path: '/domestic-purchase/domestic-products', title: 'menu.domesticProducts', accessKey: 'canReadProduct' },
      { path: '/domestic-purchase/prefix-code-management', title: 'menu.prefixCodeManagement', accessKey: 'canManageDomesticPrefixCodes' },
      { path: '/domestic-purchase/product-creation', title: 'menu.productCreation', accessKey: 'canManageDomesticProducts' },
      { path: '/domestic-purchase/product-import', title: 'menu.productImport', accessKey: 'canManageDomesticProducts' },
    ],
  },
  {
    path: '/warehouse',
    title: 'menu.warehouse',
    children: [
      { path: '/warehouse/store-orders', title: 'menu.storeOrders', accessKey: 'canManageWarehouseOrders' },
      { path: '/warehouse/containers', title: 'menu.containers', accessKey: 'canViewContainers' },
      { path: '/warehouse/products', title: 'menu.warehouseProducts', accessKey: 'canManageWarehouseProducts' },
      { path: '/warehouse/categories', title: 'menu.categories', accessKey: 'canManageWarehouseCategories' },
      { path: '/warehouse/locations', title: 'menu.warehouseLocations', accessKey: 'canManageWarehouseLocations' },
      { path: '/warehouse/product-grade-management', title: 'menu.productGradeManagement', accessKey: 'canManageWarehouseProducts' },
    ],
  },
  {
    path: '/executive-sales-intelligence',
    title: 'menu.executiveSalesIntelligence',
    accessKey: 'canViewSalesIntelligence',
    children: [
      { path: '/executive-sales-intelligence/overview', title: 'menu.salesData', accessKey: 'canViewReports' },
      { path: '/executive-sales-intelligence/sales-detail-v2', title: 'menu.salesDetail', accessKey: 'canViewReports' },
      { path: '/executive-sales-intelligence/product-movement-report', title: 'menu.productMovementReport', accessKey: 'canViewProductMovementReport' },
    ],
  },
  {
    path: '/pos-admin',
    title: 'menu.posAdmin',
    children: [
      { path: '/pos-admin/suppliers', title: 'menu.suppliers', accessKey: 'canViewAustralianSuppliers' },
      { path: '/pos-admin/products', title: 'menu.productManagement', accessKey: 'canViewPosProducts' },
      { path: '/pos-admin/store-product-price', title: 'menu.storeProductPrice', accessKey: 'canManageStoreProducts' },
      { path: '/pos-admin/pricing-strategies', title: 'menu.pricingStrategies', accessKey: 'canManagePricing' },
      { path: '/pos-admin/promotions', title: 'menu.promotions', accessKey: 'canManagePromotions' },
      { path: '/pos-admin/advertisements', title: 'menu.advertisements', accessKey: 'canManageAdvertisements' },
      { path: '/pos-admin/schedule-attendance', title: 'menu.scheduleAttendance', accessKey: 'canViewAttendanceSchedule' },
      { path: '/pos-admin/cash-register-users', title: 'menu.cashRegisterUsers', accessKey: 'canManageStoreOps' },
      { path: '/pos-admin/sales-orders', title: 'menu.salesOrders', accessKey: 'canReadOrder' },
      { path: '/pos-admin/local-supplier-invoices', title: 'menu.localSupplierInvoices', accessKey: 'canManageLocalPurchase' },
    ],
  },
]

const warehouseStaffVisibleMenuPaths = new Set(['/warehouse', '/warehouse/store-orders'])

function isWarehouseStaffNavigationLimited(access: AccessControl) {
  return (
    access.isWarehouseStaff &&
    !access.isAdmin &&
    !access.isWarehouseManager &&
    (access.hasRole('WarehouseStaff') || access.hasRole('仓库员工'))
  )
}

function canAccessRoute(route: WebMenuPreviewRoute, access: AccessControl) {
  const accessKey = route.accessKey
  if (!accessKey) {
    return true
  }
  return access[accessKey] === true
}

function buildAddPermissionCodes(
  route: WebMenuPreviewRoute,
  permissionCodes: string[],
  explicitPermissionCodeSet: Set<string>,
): string[] {
  if (!route.accessKey || !permissionCodes.length) {
    return []
  }

  const nextCodes = [permissionCodes[0]]
  if (route.path !== '/dashboard' && !explicitPermissionCodeSet.has(P.Dashboard.View)) {
    nextCodes.push(P.Dashboard.View)
  }

  return nextCodes
}

function buildPreviewNodes(
  routes: WebMenuPreviewRoute[],
  access: AccessControl,
  t: WebMenuPreviewTranslate,
  options: BuildWebRoleMenuPreviewOptions,
): WebMenuPreviewNode[] {
  const explicitPermissionCodeSet = new Set(options.explicitPermissionCodes ?? [])
  const limitWarehouseStaffNavigation = isWarehouseStaffNavigationLimited(access)

  return routes.flatMap((route) => {
    if (limitWarehouseStaffNavigation && !warehouseStaffVisibleMenuPaths.has(route.path)) {
      return []
    }

    const children = route.children ? buildPreviewNodes(route.children, access, t, options) : undefined
    const hasChildren = Boolean(children?.length)
    const hasSelfAccess = canAccessRoute(route, access)
    const hasVisibleChildren = Boolean(children?.some((child) => child.visible))
    const visible = route.children ? hasVisibleChildren : hasSelfAccess

    if (!options.includeHidden && !visible) {
      return []
    }

    if (options.includeHidden && !hasSelfAccess && !hasChildren && !route.accessKey) {
      return []
    }

    const permissionCodes = route.accessKey ? getAccessKeyPermissionCodes(route.accessKey) : []
    const addPermissionCodes = buildAddPermissionCodes(route, permissionCodes, explicitPermissionCodeSet)
    const isEditableRoute = permissionCodes.length > 0
    const isReadOnly = Boolean(options.readOnly)

    const node: WebMenuPreviewNode = {
      key: route.path,
      title: t(route.title, route.title),
      path: route.path,
      permissionCodes,
      visible,
      edit: {
        canAdd: !visible && isEditableRoute && !isReadOnly,
        canRemove: hasSelfAccess && isEditableRoute && !isReadOnly,
        isReadOnly,
        isFixed: false,
        addPermissionCodes,
        removePermissionCodes: permissionCodes,
        reason: isReadOnly ? 'read-only' : (!isEditableRoute ? 'group' : undefined),
      },
    }
    if (route.accessKey) {
      node.accessKey = route.accessKey
    }
    if (children?.length) {
      node.children = children
    }
    return [node]
  })
}

export function getAccessKeyPermissionCodes(accessKey: keyof AccessControl): string[] {
  return accessKeyPermissionMap[accessKey] ?? []
}

export function buildWebRoleMenuPreview(
  access: AccessControl,
  t: WebMenuPreviewTranslate,
  options: BuildWebRoleMenuPreviewOptions = {},
): WebMenuPreviewNode[] {
  return buildPreviewNodes(webMenuPreviewRoutes, access, t, options)
}

export function filterWebMenuNodesByVisibility(
  nodes: WebMenuPreviewNode[],
  filter: WebMenuVisibilityFilter,
): WebMenuPreviewNode[] {
  if (filter === 'all') {
    return nodes
  }

  const expectedVisible = filter === 'visible'

  return nodes.flatMap((node) => {
    const children = node.children ? filterWebMenuNodesByVisibility(node.children, filter) : undefined
    if (node.visible !== expectedVisible && !children?.length) {
      return []
    }

    const { children: _children, ...nodeWithoutChildren } = node
    const nextNode: WebMenuPreviewNode = { ...nodeWithoutChildren }
    if (children?.length) {
      nextNode.children = children
    }
    return [nextNode]
  })
}
