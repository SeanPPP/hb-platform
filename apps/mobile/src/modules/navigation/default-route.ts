export type AppTabPath =
  | "/(tabs)/home"
  | "/(tabs)/orders"
  | "/(tabs)/cart"
  | "/(tabs)/warehouse"
  | "/(tabs)/domestic-purchase"
  | "/(tabs)/local-supplier-invoices"
  | "/(tabs)/installment-orders"
  | "/(tabs)/advertisements"
  | "/(tabs)/promotions"
  | "/(tabs)/reports"
  | "/(tabs)/store-vouchers"
  | "/(tabs)/seasonal-cards"
  | "/(tabs)/attendance-personal"
  | "/(tabs)/attendance-management"
  | "/(tabs)/product-query"
  | "/(tabs)/users"
  | "/(tabs)/employee-profile"
  | "/(tabs)/employee-profile-review"
  | "/(tabs)/device-management"
  | "/(tabs)/settings";

export const TAB_PATHS: Record<string, AppTabPath> = {
  home: "/(tabs)/home",
  orders: "/(tabs)/orders",
  cart: "/(tabs)/cart",
  warehouse: "/(tabs)/warehouse",
  "domestic-purchase": "/(tabs)/domestic-purchase",
  "local-supplier-invoices": "/(tabs)/local-supplier-invoices",
  "installment-orders": "/(tabs)/installment-orders",
  advertisements: "/(tabs)/advertisements",
  promotions: "/(tabs)/promotions",
  reports: "/(tabs)/reports",
  "store-vouchers": "/(tabs)/store-vouchers",
  "seasonal-cards": "/(tabs)/seasonal-cards",
  "attendance-personal": "/(tabs)/attendance-personal",
  "attendance-management": "/(tabs)/attendance-management",
  "product-query": "/(tabs)/product-query",
  users: "/(tabs)/users",
  "employee-profile": "/(tabs)/employee-profile",
  "employee-profile-review": "/(tabs)/employee-profile-review",
  "device-management": "/(tabs)/device-management",
  settings: "/(tabs)/settings",
};

export const SUPPORTED_TAB_ROUTE_NAMES = new Set(Object.keys(TAB_PATHS));
export const SETTINGS_FALLBACK_ROUTE_NAME = "settings";

const DEVICE_MODE_BLOCKED_ROUTE_NAMES = new Set([
  "attendance-management",
  "employee-profile-review",
  "device-management",
  "reports",
]);
const LEGACY_ATTENDANCE_ROUTE_NAME = "attendance";
export const SUPPORTED_APP_MENU_ROUTE_NAMES = new Set([
  ...SUPPORTED_TAB_ROUTE_NAMES,
  LEGACY_ATTENDANCE_ROUTE_NAME,
]);

export function expandAttendanceRouteNames(
  routeNames: Iterable<string>,
  includeAttendanceManagement = false
) {
  const expandedRouteNames: string[] = [];
  const pushUnique = (routeName: string) => {
    if (!expandedRouteNames.includes(routeName)) {
      expandedRouteNames.push(routeName);
    }
  };

  Array.from(routeNames).forEach((routeName) => {
    if (routeName === LEGACY_ATTENDANCE_ROUTE_NAME) {
      pushUnique("attendance-personal");
      if (includeAttendanceManagement) {
        pushUnique("attendance-management");
      }
      return;
    }

    pushUnique(routeName);
  });

  return expandedRouteNames;
}

interface AccountTabRouteNamesOptions {
  canCreateOrder?: boolean;
  isWarehouseStaffOnly?: boolean;
}

export function filterAccountTabRouteNames(
  routeNames: Iterable<string>,
  { canCreateOrder = false, isWarehouseStaffOnly = false }: AccountTabRouteNamesOptions = {}
) {
  const orderedRouteNames = Array.from(routeNames);
  if (!isWarehouseStaffOnly || canCreateOrder) {
    return orderedRouteNames;
  }

  // 纯仓库员工只有显式 Orders.Create 才能进入自己的专用购物车。
  return orderedRouteNames.filter((routeName) => routeName !== "cart");
}

interface VisibleTabRouteNamesOptions {
  routeNames: Iterable<string>;
  isDeviceMode?: boolean;
  canViewAttendanceManagement?: boolean;
  canManageAttendance?: boolean;
}

export function getVisibleTabRouteNames({
  routeNames,
  isDeviceMode = false,
  canViewAttendanceManagement,
  canManageAttendance = false,
}: VisibleTabRouteNamesOptions) {
  const orderedRouteNames = expandAttendanceRouteNames(
    routeNames,
    canViewAttendanceManagement ?? canManageAttendance
  );
  return isDeviceMode
    ? orderedRouteNames.filter((routeName) => !DEVICE_MODE_BLOCKED_ROUTE_NAMES.has(routeName))
    : orderedRouteNames;
}

export function hasVisibleTabRoute(
  routeNames: Iterable<string>,
  routeName: string,
  options?: Omit<VisibleTabRouteNamesOptions, "routeNames">
) {
  return getVisibleTabRouteNames({ routeNames, ...options }).includes(routeName);
}

interface ResolveDefaultTabRouteOptions {
  isDeviceMode: boolean;
  routeNames: Iterable<string>;
}

interface ResolveTabRouteCorrectionOptions extends ResolveDefaultTabRouteOptions {
  currentRouteName: string | undefined;
  hasAppliedDefaultRoute: boolean;
}

export function resolveDefaultTabRoute({
  isDeviceMode,
  routeNames,
}: ResolveDefaultTabRouteOptions): AppTabPath {
  const orderedRouteNames = getVisibleTabRouteNames({ routeNames, isDeviceMode });
  const preferredRouteName = isDeviceMode ? "product-query" : "attendance-personal";

  if (orderedRouteNames.includes(preferredRouteName)) {
    return TAB_PATHS[preferredRouteName];
  }

  const firstVisibleRouteName = orderedRouteNames.find((routeName) => Boolean(TAB_PATHS[routeName]));
  return firstVisibleRouteName
    ? TAB_PATHS[firstVisibleRouteName]
    : TAB_PATHS[SETTINGS_FALLBACK_ROUTE_NAME];
}

export function resolveTabRouteCorrection({
  currentRouteName,
  hasAppliedDefaultRoute,
  isDeviceMode,
  routeNames,
}: ResolveTabRouteCorrectionOptions): AppTabPath | null {
  if (!currentRouteName) {
    return null;
  }

  const orderedRouteNames = getVisibleTabRouteNames({ routeNames, isDeviceMode });
  const visibleRouteNames = new Set(orderedRouteNames);
  const defaultRoute = resolveDefaultTabRoute({ isDeviceMode, routeNames: orderedRouteNames });
  const currentRoute = TAB_PATHS[currentRouteName];

  if (currentRouteName === LEGACY_ATTENDANCE_ROUTE_NAME) {
    return defaultRoute;
  }

  if (!currentRoute) {
    // 非 Tab 栈页面（例如货柜列表/明细）由根 Stack 接管，不能被 Tab 默认页纠偏抢走。
    return null;
  }

  if (!visibleRouteNames.has(currentRouteName)) {
    return defaultRoute;
  }

  if (!hasAppliedDefaultRoute && currentRouteName === "home" && currentRoute !== defaultRoute) {
    return defaultRoute;
  }

  return null;
}
