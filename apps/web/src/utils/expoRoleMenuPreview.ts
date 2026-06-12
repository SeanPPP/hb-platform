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

const MAX_VISIBLE_TABS = 4

const STORE_ROUTE_NAMES = new Set([
  'home',
  'orders',
  'cart',
  'product-query',
  'local-supplier-invoices',
  'installment-orders',
  'store-vouchers',
])

const ATTENDANCE_PERSONAL_PERMISSION_CODES = [
  P.Attendance.ScheduleViewSelf,
  P.Attendance.AvailabilitySubmitSelf,
  P.Attendance.PunchSelf,
  P.Attendance.LeaveApplySelf,
]

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

const TAB_PATHS: Record<string, string> = {
  home: '/(tabs)/home',
  orders: '/(tabs)/orders',
  cart: '/(tabs)/cart',
  warehouse: '/(tabs)/warehouse',
  'domestic-purchase': '/(tabs)/domestic-purchase',
  'local-supplier-invoices': '/(tabs)/local-supplier-invoices',
  'installment-orders': '/(tabs)/installment-orders',
  'store-vouchers': '/(tabs)/store-vouchers',
  'attendance-personal': '/(tabs)/attendance-personal',
  'attendance-management': '/(tabs)/attendance-management',
  'product-query': '/(tabs)/product-query',
  users: '/(tabs)/users',
  'employee-profile': '/(tabs)/employee-profile',
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
  'installment-orders': { zhTitle: '分期订单', enTitle: 'Installments' },
  'store-vouchers': { zhTitle: '门店代金券', enTitle: 'Vouchers' },
  'attendance-personal': { zhTitle: '考勤', enTitle: 'Attendance' },
  'attendance-management': { zhTitle: '考勤管理', enTitle: 'Attendance Management' },
  'product-query': { zhTitle: '商品维护', enTitle: 'Products' },
  users: { zhTitle: '用户', enTitle: 'Users' },
  'employee-profile': { zhTitle: '员工', enTitle: 'Employee' },
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
    permissionCodes: [P.Orders.View],
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
    permissionCodes: [P.Warehouse.ManageProducts],
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
    permissionCodes: [P.LocalPurchase.View, 'LocalInvocie.View'],
    order: 46,
    ...ROUTE_LABELS['local-supplier-invoices'],
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
    icon: 'account-clock-outline',
    permissionCodes: ATTENDANCE_PERSONAL_PERMISSION_CODES,
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
    routeName: 'device-management',
    titleKey: 'tabs.deviceManagement',
    icon: 'cellphone-cog',
    permissionCodes: [P.DeviceRegistration.View],
    order: 59,
    ...ROUTE_LABELS['device-management'],
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
  const visible =
    definition.fixed ||
    definition.permissionCodes.length === 0 ||
    definition.permissionCodes.some((permissionCode) => access.hasPermission(permissionCode))

  return {
    ...definition,
    path: TAB_PATHS[definition.routeName],
    visible,
    anyPermission,
    readOnly,
    locked: Boolean(definition.fixed || readOnly),
    addPermissionCodes:
      !visible && definition.permissionCodes.length > 0
        ? [definition.permissionCodes.find((permissionCode) => !explicitPermissionCodeSet.has(permissionCode)) ?? definition.permissionCodes[0]]
        : [],
    removePermissionCodes: definition.permissionCodes,
  }
}

export function buildExpoRoleMenuPreview(
  access: AccessControl,
  _t?: ExpoAppMenuTranslate,
  options: BuildExpoRoleMenuPreviewOptions = {},
): ExpoRoleMenuPreview {
  const explicitPermissionCodeSet = new Set(options.explicitPermissionCodes ?? [])
  const readOnly = Boolean(options.readOnly)
  const allRoutes = EXPO_APP_MENU_DEFINITIONS
    .map((definition) => buildExpoRoute(definition, access, explicitPermissionCodeSet, readOnly))
    .sort((left, right) => left.order - right.order)

  const visibleRoutes = allRoutes.filter((route) => route.visible)
  const displayTabs = buildDisplayTabs(visibleRoutes)
  const storeTab = displayTabs.find((item): item is Extract<ExpoAppDisplayTab, { type: 'store' }> => item.type === 'store')

  return {
    visibleRoutes,
    allRoutes,
    displayTabs,
    storeChildren: storeTab?.children ?? [],
  }
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
