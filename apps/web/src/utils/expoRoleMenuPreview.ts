import type { AccessControl } from '../types/auth'
import { P } from '../types/permissions'

export type ExpoAppMenuTranslate = (key: string, fallback?: string) => string

export interface ExpoAppMenuDefinition {
  routeName: string
  titleKey: string
  icon: string
  permissionCodes: string[]
  order: number
  zhTitle: string
  enTitle: string
  fixed?: boolean
}

export interface ExpoAppVisibleRoute extends ExpoAppMenuDefinition {
  path: string
  visible: boolean
  anyPermission: boolean
  readOnly: boolean
  locked: boolean
  inherited: boolean
  direct: boolean
  roleSources: string[]
  canAdd: boolean
  canRemove: boolean
  addPermissionCodes: string[]
  removePermissionCodes: string[]
}

export type ExpoAppDisplayTab =
  | {
      type: 'route'
      key: string
      route: ExpoAppVisibleRoute
    }
  | {
      type: 'store'
      key: 'store'
      zhTitle: string
      enTitle: string
      children: ExpoAppVisibleRoute[]
    }

export interface ExpoRoleMenuPreview {
  visibleRoutes: ExpoAppVisibleRoute[]
  allRoutes: ExpoAppVisibleRoute[]
  displayTabs: ExpoAppDisplayTab[]
  storeChildren: ExpoAppVisibleRoute[]
}

export type ExpoMenuVisibilityFilter = 'all' | 'visible' | 'hidden'

interface BuildExpoRoleMenuPreviewOptions {
  explicitPermissionCodes?: string[]
  readOnly?: boolean
}

export interface ExpoMenuInheritedSource {
  roleName: string
  permissionCodes: string[]
}

export interface BuildExpoUserMenuPreviewOptions {
  inheritedPermissionCodes: string[]
  directPermissionCodes: string[]
  assignablePermissionCodes: string[]
  inheritedSources?: ExpoMenuInheritedSource[]
  isSuperAdmin?: boolean
  implicitAllPermissions?: boolean
  readOnly?: boolean
}

export interface ExpoMenuPermissionMutationOptions {
  directPermissionCodes: string[]
  route: ExpoAppVisibleRoute
  assignablePermissionCodes: string[]
}

const MAX_VISIBLE_TABS = 4

const STORE_ROUTE_NAMES = new Set([
  'home',
  'orders',
  'cart',
  'product-query',
  'local-supplier-invoices',
  'installment-orders',
  'store-vouchers',
  'seasonal-cards',
])

const ATTENDANCE_MANAGEMENT_PERMISSION_CODES = [
  P.Attendance.ScheduleViewStore,
  P.Attendance.ScheduleEditManagedStore,
  P.Attendance.AvailabilityViewManagedStore,
  P.Attendance.PunchViewManagedStore,
  P.Attendance.ApprovalViewManagedStore,
  P.Attendance.ApprovalReviewManagedStore,
  P.Attendance.HolidayViewStore,
  P.Attendance.HolidayEditManagedStore,
  P.Attendance.LeaveViewManagedStore,
  P.Attendance.LeaveReviewManagedStore,
  P.Attendance.SettingsEdit,
  P.Attendance.AdminView,
]

const PERMISSION_ALIAS_GROUPS = [
  {
    canonicalCode: P.LocalPurchase.View,
    aliasCodes: ['LocalInvocie.View'],
  },
  {
    canonicalCode: P.Reports.ProductMovementView,
    aliasCodes: [P.Reports.View],
  },
  {
    canonicalCode: P.Warehouse.ManageProducts,
    aliasCodes: [P.Warehouse.Manage],
  },
] as const

const permissionAliasMap = new Map<string, string[]>()

PERMISSION_ALIAS_GROUPS.forEach(({ canonicalCode, aliasCodes }) => {
  const equivalentCodes = [canonicalCode, ...aliasCodes]
  equivalentCodes.forEach((permissionCode) => {
    permissionAliasMap.set(permissionCode.toLowerCase(), equivalentCodes)
  })
})

const TAB_PATHS: Record<string, string> = {
  home: '/(tabs)/home',
  orders: '/(tabs)/orders',
  cart: '/(tabs)/cart',
  warehouse: '/(tabs)/warehouse',
  'domestic-purchase': '/(tabs)/domestic-purchase',
  'local-supplier-invoices': '/(tabs)/local-supplier-invoices',
  'installment-orders': '/(tabs)/installment-orders',
  advertisements: '/(tabs)/advertisements',
  promotions: '/(tabs)/promotions',
  reports: '/(tabs)/reports',
  'store-vouchers': '/(tabs)/store-vouchers',
  'seasonal-cards': '/(tabs)/seasonal-cards',
  'attendance-personal': '/(tabs)/attendance-personal',
  'attendance-management': '/(tabs)/attendance-management',
  'product-query': '/(tabs)/product-query',
  users: '/(tabs)/users',
  'employee-profile': '/(tabs)/employee-profile',
  'employee-profile-review': '/(tabs)/employee-profile-review',
  'device-management': '/(tabs)/device-management',
  settings: '/(tabs)/settings',
}

const ROUTE_LABELS: Record<string, Pick<ExpoAppMenuDefinition, 'zhTitle' | 'enTitle'>> = {
  home: { zhTitle: '商品', enTitle: 'Home' },
  orders: { zhTitle: '订单', enTitle: 'Orders' },
  cart: { zhTitle: '购物车', enTitle: 'Cart' },
  warehouse: { zhTitle: '仓库', enTitle: 'Warehouse' },
  'domestic-purchase': { zhTitle: '中国采购', enTitle: 'China Purchase' },
  'local-supplier-invoices': { zhTitle: '澳洲进货', enTitle: 'AU Invoices' },
  advertisements: { zhTitle: '广告', enTitle: 'Advertisements' },
  promotions: { zhTitle: '促销', enTitle: 'Promotions' },
  reports: { zhTitle: '报表', enTitle: 'Reports' },
  'installment-orders': { zhTitle: '分期订单', enTitle: 'Installments' },
  'store-vouchers': { zhTitle: '门店代金券', enTitle: 'Vouchers' },
  'seasonal-cards': { zhTitle: '节日贺卡', enTitle: 'Seasonal Cards' },
  'attendance-personal': { zhTitle: '考勤', enTitle: 'Attendance' },
  'attendance-management': { zhTitle: '考勤管理', enTitle: 'Attendance Management' },
  'product-query': { zhTitle: '商品维护', enTitle: 'Products' },
  users: { zhTitle: '用户', enTitle: 'Users' },
  'employee-profile': { zhTitle: '员工', enTitle: 'Employee' },
  'employee-profile-review': { zhTitle: '员工资料审核', enTitle: 'Employee Profile Review' },
  'device-management': { zhTitle: '设备管理', enTitle: 'Devices' },
  settings: { zhTitle: '设置', enTitle: 'Settings' },
}

const EXPO_APP_MENU_DEFINITIONS: ExpoAppMenuDefinition[] = [
  {
    routeName: 'home',
    titleKey: 'tabs.home',
    icon: 'home',
    permissionCodes: [P.Orders.Create],
    order: 10,
    ...ROUTE_LABELS.home,
  },
  {
    routeName: 'orders',
    titleKey: 'tabs.orders',
    icon: 'clipboard-list',
    permissionCodes: [
      P.OrderFront.View,
      P.Orders.View,
      P.Warehouse.ManageOrders,
      P.Warehouse.Manage,
    ],
    order: 20,
    ...ROUTE_LABELS.orders,
  },
  {
    routeName: 'cart',
    titleKey: 'tabs.cart',
    icon: 'cart-outline',
    permissionCodes: [P.Orders.Create],
    order: 30,
    ...ROUTE_LABELS.cart,
  },
  {
    routeName: 'warehouse',
    titleKey: 'tabs.warehouse',
    icon: 'warehouse',
    permissionCodes: [P.Warehouse.ManageProducts, P.Container.View],
    order: 40,
    ...ROUTE_LABELS.warehouse,
  },
  {
    routeName: 'domestic-purchase',
    titleKey: 'tabs.domesticPurchase',
    icon: 'shopping-outline',
    permissionCodes: [P.DomesticPurchase.ManageProducts],
    order: 45,
    ...ROUTE_LABELS['domestic-purchase'],
  },
  {
    routeName: 'local-supplier-invoices',
    titleKey: 'tabs.localSupplierInvoices',
    icon: 'receipt-text-outline',
    permissionCodes: [P.LocalPurchase.MobileView, P.LocalPurchase.View],
    order: 46,
    ...ROUTE_LABELS['local-supplier-invoices'],
  },
  {
    routeName: 'advertisements',
    titleKey: 'tabs.advertisements',
    icon: 'image-multiple',
    permissionCodes: [P.Advertisements.View],
    order: 47,
    ...ROUTE_LABELS.advertisements,
  },
  {
    routeName: 'promotions',
    titleKey: 'tabs.promotions',
    icon: 'ticket-percent-outline',
    permissionCodes: [P.Promotions.View],
    order: 48,
    ...ROUTE_LABELS.promotions,
  },
  {
    routeName: 'product-query',
    titleKey: 'tabs.productQuery',
    icon: 'barcode-scan',
    permissionCodes: [P.StoreProducts.View],
    order: 50,
    ...ROUTE_LABELS['product-query'],
  },
  {
    routeName: 'installment-orders',
    titleKey: 'tabs.installmentOrders',
    icon: 'cash-clock',
    permissionCodes: [P.InstallmentOrders.View],
    order: 51,
    ...ROUTE_LABELS['installment-orders'],
  },
  {
    routeName: 'store-vouchers',
    titleKey: 'tabs.storeVouchers',
    icon: 'ticket-percent-outline',
    permissionCodes: [P.StoreVouchers.View],
    order: 52,
    ...ROUTE_LABELS['store-vouchers'],
  },
  {
    routeName: 'attendance-personal',
    titleKey: 'tabs.attendancePersonal',
    icon: 'calendar-clock',
    permissionCodes: [P.Attendance.ScheduleViewSelf],
    order: 55,
    ...ROUTE_LABELS['attendance-personal'],
  },
  {
    routeName: 'attendance-management',
    titleKey: 'tabs.attendanceManagement',
    icon: 'calendar-clock',
    permissionCodes: ATTENDANCE_MANAGEMENT_PERMISSION_CODES,
    order: 56,
    ...ROUTE_LABELS['attendance-management'],
  },
  {
    routeName: 'seasonal-cards',
    titleKey: 'tabs.seasonalCards',
    icon: 'gift-outline',
    permissionCodes: [
      'SeasonalCards.Remaining.ViewManagedStore',
      'SeasonalCards.Remaining.SubmitManagedStore',
    ],
    order: 56,
    ...ROUTE_LABELS['seasonal-cards'],
  },
  {
    routeName: 'users',
    titleKey: 'tabs.users',
    icon: 'account-group-outline',
    permissionCodes: [P.Users.View],
    order: 57,
    ...ROUTE_LABELS.users,
  },
  {
    routeName: 'employee-profile',
    titleKey: 'tabs.employeeProfile',
    icon: 'card-account-details-outline',
    permissionCodes: [P.EmployeeProfiles.View],
    order: 58,
    ...ROUTE_LABELS['employee-profile'],
  },
  {
    routeName: 'employee-profile-review',
    titleKey: 'tabs.employeeProfileReview',
    icon: 'account-check-outline',
    permissionCodes: ['EmployeeProfiles.ReviewSensitiveManagedStore'],
    order: 58,
    ...ROUTE_LABELS['employee-profile-review'],
  },
  {
    routeName: 'device-management',
    titleKey: 'tabs.deviceManagement',
    icon: 'cellphone-cog',
    permissionCodes: [P.DeviceRegistration.View],
    order: 59,
    ...ROUTE_LABELS['device-management'],
  },
  {
    routeName: 'reports',
    titleKey: 'tabs.reports',
    icon: 'chart-box-outline',
    permissionCodes: [P.Reports.ProductMovementView],
    order: 59,
    ...ROUTE_LABELS.reports,
  },
  {
    routeName: 'settings',
    titleKey: 'tabs.settings',
    icon: 'account-circle-outline',
    permissionCodes: [],
    order: 60,
    fixed: true,
    ...ROUTE_LABELS.settings,
  },
]

function uniquePermissionCodes(permissionCodes: Iterable<string>): string[] {
  const uniqueCodes: string[] = []
  const normalizedCodes = new Set<string>()

  Array.from(permissionCodes).forEach((permissionCode) => {
    const normalizedCode = permissionCode.toLowerCase()
    if (normalizedCodes.has(normalizedCode)) return
    normalizedCodes.add(normalizedCode)
    uniqueCodes.push(permissionCode)
  })

  return uniqueCodes
}

function getAcceptedPermissionCodes(definition: ExpoAppMenuDefinition): string[] {
  return uniquePermissionCodes(
    [
      ...definition.permissionCodes,
      ...definition.permissionCodes.flatMap(
        (permissionCode) => permissionAliasMap.get(permissionCode.toLowerCase()) ?? [],
      ),
    ],
  )
}

function toNormalizedPermissionSet(permissionCodes: Iterable<string>): Set<string> {
  return new Set(Array.from(permissionCodes, (permissionCode) => permissionCode.toLowerCase()))
}

function includesPermission(permissionCodeSet: ReadonlySet<string>, permissionCode: string): boolean {
  return permissionCodeSet.has(permissionCode.toLowerCase())
}

function hasAnyPermission(
  permissionCodeSet: ReadonlySet<string>,
  permissionCodes: Iterable<string>,
): boolean {
  return Array.from(permissionCodes).some((permissionCode) =>
    includesPermission(permissionCodeSet, permissionCode),
  )
}

function buildDisplayTabs(visibleRoutes: ExpoAppVisibleRoute[]): ExpoAppDisplayTab[] {
  if (visibleRoutes.length <= MAX_VISIBLE_TABS) {
    return visibleRoutes.map((route) => ({
      type: 'route',
      key: route.routeName,
      route,
    }))
  }

  const storeChildren = visibleRoutes.filter((route) => STORE_ROUTE_NAMES.has(route.routeName))
  if (!storeChildren.length) {
    return visibleRoutes.map((route) => ({
      type: 'route',
      key: route.routeName,
      route,
    }))
  }

  let hasInsertedStore = false
  const tabs: ExpoAppDisplayTab[] = []
  visibleRoutes.forEach((route) => {
    if (!STORE_ROUTE_NAMES.has(route.routeName)) {
      tabs.push({ type: 'route', key: route.routeName, route })
      return
    }
    if (hasInsertedStore) {
      return
    }
    hasInsertedStore = true
    tabs.push({
      type: 'store',
      key: 'store',
      zhTitle: '门店',
      enTitle: 'Store',
      children: storeChildren,
    })
  })
  return tabs
}

function buildExpoRoute(
  definition: ExpoAppMenuDefinition,
  access: AccessControl,
  explicitPermissionCodeSet: Set<string>,
  readOnly: boolean,
): ExpoAppVisibleRoute {
  const anyPermission = definition.permissionCodes.length > 1
  const acceptedPermissionCodes = getAcceptedPermissionCodes(definition)
  const visible =
    definition.fixed ||
    definition.permissionCodes.length === 0 ||
    acceptedPermissionCodes.some((permissionCode) => access.hasPermission(permissionCode))
  const direct = acceptedPermissionCodes.some((permissionCode) =>
    includesPermission(explicitPermissionCodeSet, permissionCode),
  )
  const locked = Boolean(definition.fixed || readOnly)

  return {
    ...definition,
    path: TAB_PATHS[definition.routeName],
    visible,
    anyPermission,
    readOnly,
    locked,
    inherited: false,
    direct,
    roleSources: [],
    canAdd: !visible && !locked && definition.permissionCodes.length > 0,
    canRemove: visible && !locked && acceptedPermissionCodes.length > 0,
    addPermissionCodes:
      !visible && definition.permissionCodes.length > 0
        ? [
            definition.permissionCodes.find(
              (permissionCode) => !includesPermission(explicitPermissionCodeSet, permissionCode),
            ) ?? definition.permissionCodes[0],
          ]
        : [],
    removePermissionCodes: acceptedPermissionCodes,
  }
}

function buildExpoUserRoute(
  definition: ExpoAppMenuDefinition,
  {
    inheritedPermissionCodeSet,
    directPermissionCodeSet,
    assignablePermissionCodeSet,
    inheritedSources,
    implicitAllPermissions,
    readOnly,
  }: {
    inheritedPermissionCodeSet: ReadonlySet<string>
    directPermissionCodeSet: ReadonlySet<string>
    assignablePermissionCodeSet: ReadonlySet<string>
    inheritedSources: ExpoMenuInheritedSource[]
    implicitAllPermissions: boolean
    readOnly: boolean
  },
): ExpoAppVisibleRoute {
  const acceptedPermissionCodes = getAcceptedPermissionCodes(definition)
  const inherited = hasAnyPermission(inheritedPermissionCodeSet, acceptedPermissionCodes)
  const direct = hasAnyPermission(directPermissionCodeSet, acceptedPermissionCodes)
  const visible =
    Boolean(definition.fixed) ||
    definition.permissionCodes.length === 0 ||
    implicitAllPermissions ||
    inherited ||
    direct
  const addPermissionCode = acceptedPermissionCodes.find((permissionCode) =>
    includesPermission(assignablePermissionCodeSet, permissionCode),
  )
  const removablePermissionCodes = acceptedPermissionCodes.filter(
    (permissionCode) =>
      includesPermission(directPermissionCodeSet, permissionCode) &&
      includesPermission(assignablePermissionCodeSet, permissionCode),
  )
  const canAdd = Boolean(!visible && !readOnly && addPermissionCode)
  const canRemove = Boolean(visible && !readOnly && removablePermissionCodes.length > 0)
  const roleSources = uniquePermissionCodes(
    inheritedSources
      .filter((source) => {
        const sourcePermissionCodeSet = toNormalizedPermissionSet(source.permissionCodes)
        return hasAnyPermission(sourcePermissionCodeSet, acceptedPermissionCodes)
      })
      .map((source) => source.roleName),
  )

  return {
    ...definition,
    path: TAB_PATHS[definition.routeName],
    visible,
    anyPermission: definition.permissionCodes.length > 1,
    readOnly,
    locked: Boolean(definition.fixed || readOnly || (!canAdd && !canRemove)),
    inherited,
    direct,
    roleSources,
    canAdd,
    canRemove,
    addPermissionCodes: canAdd && addPermissionCode ? [addPermissionCode] : [],
    removePermissionCodes: canRemove ? removablePermissionCodes : [],
  }
}

function buildExpoMenuPreview(allRoutes: ExpoAppVisibleRoute[]): ExpoRoleMenuPreview {
  const visibleRoutes = allRoutes.filter((route) => route.visible)
  const displayTabs = buildDisplayTabs(visibleRoutes)
  const storeTab = displayTabs.find(
    (item): item is Extract<ExpoAppDisplayTab, { type: 'store' }> => item.type === 'store',
  )

  return {
    visibleRoutes,
    allRoutes,
    displayTabs,
    storeChildren: storeTab?.children ?? [],
  }
}

export function buildExpoRoleMenuPreview(
  access: AccessControl,
  _t?: ExpoAppMenuTranslate,
  options: BuildExpoRoleMenuPreviewOptions = {},
): ExpoRoleMenuPreview {
  const explicitPermissionCodeSet = toNormalizedPermissionSet(options.explicitPermissionCodes ?? [])
  const readOnly = Boolean(options.readOnly)
  const allRoutes = EXPO_APP_MENU_DEFINITIONS
    .map((definition) => buildExpoRoute(definition, access, explicitPermissionCodeSet, readOnly))
    .sort((left, right) => left.order - right.order)

  return buildExpoMenuPreview(allRoutes)
}

export function buildExpoUserMenuPreview({
  inheritedPermissionCodes,
  directPermissionCodes,
  assignablePermissionCodes,
  inheritedSources = [],
  isSuperAdmin = false,
  implicitAllPermissions = false,
  readOnly = false,
}: BuildExpoUserMenuPreviewOptions): ExpoRoleMenuPreview {
  const hasImplicitAllPermissions = isSuperAdmin || implicitAllPermissions
  const isReadOnly = readOnly || hasImplicitAllPermissions
  const inheritedPermissionCodeSet = toNormalizedPermissionSet(inheritedPermissionCodes)
  const directPermissionCodeSet = toNormalizedPermissionSet(directPermissionCodes)
  const assignablePermissionCodeSet = toNormalizedPermissionSet(assignablePermissionCodes)

  const allRoutes = EXPO_APP_MENU_DEFINITIONS
    .map((definition) =>
      buildExpoUserRoute(definition, {
        inheritedPermissionCodeSet,
        directPermissionCodeSet,
        assignablePermissionCodeSet,
        inheritedSources,
        implicitAllPermissions: hasImplicitAllPermissions,
        readOnly: isReadOnly,
      }),
    )
    // 权限目录是操作者可分配范围的唯一白名单；隐藏且不可分配的入口不应泄露为完整预览。
    .filter(
      (route) =>
        route.visible ||
        Boolean(route.fixed) ||
        hasAnyPermission(assignablePermissionCodeSet, getAcceptedPermissionCodes(route)),
    )
    .sort((left, right) => left.order - right.order)

  return buildExpoMenuPreview(allRoutes)
}

export function addExpoMenuPermission({
  directPermissionCodes,
  route,
  assignablePermissionCodes,
}: ExpoMenuPermissionMutationOptions): string[] {
  const currentDirectPermissionCodes = uniquePermissionCodes(directPermissionCodes)
  if (route.visible || route.fixed || route.readOnly) return currentDirectPermissionCodes

  const assignablePermissionCodeSet = toNormalizedPermissionSet(assignablePermissionCodes)
  const addPermissionCode = getAcceptedPermissionCodes(route).find((permissionCode) =>
    includesPermission(assignablePermissionCodeSet, permissionCode),
  )
  if (!addPermissionCode) return currentDirectPermissionCodes

  return uniquePermissionCodes([...currentDirectPermissionCodes, addPermissionCode])
}

export function removeExpoMenuDirectPermissions({
  directPermissionCodes,
  route,
  assignablePermissionCodes,
}: ExpoMenuPermissionMutationOptions): string[] {
  const currentDirectPermissionCodes = uniquePermissionCodes(directPermissionCodes)
  if (!route.visible || route.fixed || route.readOnly) return currentDirectPermissionCodes

  const acceptedPermissionCodeSet = toNormalizedPermissionSet(getAcceptedPermissionCodes(route))
  const assignablePermissionCodeSet = toNormalizedPermissionSet(assignablePermissionCodes)

  return currentDirectPermissionCodes.filter(
    (permissionCode) =>
      !acceptedPermissionCodeSet.has(permissionCode.toLowerCase()) ||
      !assignablePermissionCodeSet.has(permissionCode.toLowerCase()),
  )
}

export function filterExpoRoutesByVisibility(
  routes: ExpoAppVisibleRoute[],
  filter: ExpoMenuVisibilityFilter,
): ExpoAppVisibleRoute[] {
  if (filter === 'visible') {
    return routes.filter((route) => route.visible)
  }
  if (filter === 'hidden') {
    return routes.filter((route) => !route.visible)
  }
  return routes
}
