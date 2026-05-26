import type { AccessControl } from '../types/auth'
import { P } from '../types/permissions'

export type ExpoAppMenuTranslate = (key: string, fallback?: string) => string

export interface ExpoAppMenuDefinition {
  routeName: string
  titleKey: string
  icon: string
  permission?: string
  order: number
  zhTitle: string
  enTitle: string
}

export interface ExpoAppVisibleRoute extends ExpoAppMenuDefinition {
  path: string
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
  displayTabs: ExpoAppDisplayTab[]
  storeChildren: ExpoAppVisibleRoute[]
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
    permission: P.Orders.Create,
    order: 10,
    ...ROUTE_LABELS.home,
  },
  {
    routeName: 'orders',
    titleKey: 'tabs.orders',
    icon: 'clipboard-list',
    permission: P.Orders.View,
    order: 20,
    ...ROUTE_LABELS.orders,
  },
  {
    routeName: 'cart',
    titleKey: 'tabs.cart',
    icon: 'cart-outline',
    permission: P.Orders.Create,
    order: 30,
    ...ROUTE_LABELS.cart,
  },
  {
    routeName: 'warehouse',
    titleKey: 'tabs.warehouse',
    icon: 'warehouse',
    permission: P.Warehouse.ManageProducts,
    order: 40,
    ...ROUTE_LABELS.warehouse,
  },
  {
    routeName: 'domestic-purchase',
    titleKey: 'tabs.domesticPurchase',
    icon: 'shopping-outline',
    permission: P.DomesticPurchase.ManageProducts,
    order: 45,
    ...ROUTE_LABELS['domestic-purchase'],
  },
  {
    routeName: 'local-supplier-invoices',
    titleKey: 'tabs.localSupplierInvoices',
    icon: 'receipt-text-outline',
    permission: P.LocalPurchase.View,
    order: 46,
    ...ROUTE_LABELS['local-supplier-invoices'],
  },
  {
    routeName: 'product-query',
    titleKey: 'tabs.productQuery',
    icon: 'barcode-scan',
    permission: P.StoreProducts.View,
    order: 50,
    ...ROUTE_LABELS['product-query'],
  },
  {
    routeName: 'installment-orders',
    titleKey: 'tabs.installmentOrders',
    icon: 'cash-clock',
    permission: P.InstallmentOrders.View,
    order: 51,
    ...ROUTE_LABELS['installment-orders'],
  },
  {
    routeName: 'store-vouchers',
    titleKey: 'tabs.storeVouchers',
    icon: 'ticket-percent-outline',
    permission: P.StoreVouchers.View,
    order: 52,
    ...ROUTE_LABELS['store-vouchers'],
  },
  {
    routeName: 'attendance',
    titleKey: 'tabs.attendance',
    icon: 'calendar-clock',
    permission: P.Attendance.ScheduleViewSelf,
    order: 55,
    zhTitle: '考勤',
    enTitle: 'Attendance',
  },
  {
    routeName: 'users',
    titleKey: 'tabs.users',
    icon: 'account-group-outline',
    permission: P.Users.View,
    order: 56,
    ...ROUTE_LABELS.users,
  },
  {
    routeName: 'employee-profile',
    titleKey: 'tabs.employeeProfile',
    icon: 'card-account-details-outline',
    permission: P.EmployeeProfiles.View,
    order: 57,
    ...ROUTE_LABELS['employee-profile'],
  },
  {
    routeName: 'device-management',
    titleKey: 'tabs.deviceManagement',
    icon: 'cellphone-cog',
    permission: P.DeviceRegistration.View,
    order: 58,
    ...ROUTE_LABELS['device-management'],
  },
  {
    routeName: 'settings',
    titleKey: 'tabs.settings',
    icon: 'account-circle-outline',
    order: 60,
    ...ROUTE_LABELS.settings,
  },
]

function expandExpoRoute(definition: ExpoAppMenuDefinition): ExpoAppVisibleRoute[] {
  if (definition.routeName !== 'attendance') {
    return [{ ...definition, path: TAB_PATHS[definition.routeName] }]
  }

  return [
    {
      ...definition,
      routeName: 'attendance-personal',
      titleKey: 'tabs.attendancePersonal',
      icon: 'account-clock-outline',
      path: TAB_PATHS['attendance-personal'],
      ...ROUTE_LABELS['attendance-personal'],
    },
  ]
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

export function buildExpoRoleMenuPreview(
  access: AccessControl,
  _t?: ExpoAppMenuTranslate,
): ExpoRoleMenuPreview {
  const visibleRoutes = EXPO_APP_MENU_DEFINITIONS
    .filter((definition) => !definition.permission || access.hasPermission(definition.permission))
    .flatMap(expandExpoRoute)

  const displayTabs = buildDisplayTabs(visibleRoutes)
  const storeTab = displayTabs.find((item): item is Extract<ExpoAppDisplayTab, { type: 'store' }> => item.type === 'store')

  return {
    visibleRoutes,
    displayTabs,
    storeChildren: storeTab?.children ?? [],
  }
}
