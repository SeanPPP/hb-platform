export type AppTabPath =
  | "/(tabs)/home"
  | "/(tabs)/orders"
  | "/(tabs)/cart"
  | "/(tabs)/warehouse"
  | "/(tabs)/domestic-purchase"
  | "/(tabs)/local-supplier-invoices"
  | "/(tabs)/installment-orders"
  | "/(tabs)/store-vouchers"
  | "/(tabs)/attendance-personal"
  | "/(tabs)/attendance-management"
  | "/(tabs)/product-query"
  | "/(tabs)/users"
  | "/(tabs)/employee-profile"
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
  "store-vouchers": "/(tabs)/store-vouchers",
  "attendance-personal": "/(tabs)/attendance-personal",
  "attendance-management": "/(tabs)/attendance-management",
  "product-query": "/(tabs)/product-query",
  users: "/(tabs)/users",
  "employee-profile": "/(tabs)/employee-profile",
  "device-management": "/(tabs)/device-management",
  settings: "/(tabs)/settings",
};

const DEVICE_MODE_BLOCKED_ROUTE_NAMES = new Set(["device-management"]);
const LEGACY_ATTENDANCE_ROUTE_NAME = "attendance";

export function expandAttendanceRouteNames(
  routeNames: Iterable<string>,
  canReviewAttendance: boolean
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
      if (canReviewAttendance) {
        pushUnique("attendance-management");
      }
      return;
    }

    if (routeName === "attendance-management" && !canReviewAttendance) {
      return;
    }

    pushUnique(routeName);
  });

  return expandedRouteNames;
}

function normalizeVisibleRouteNames(routeNames: Iterable<string>, isDeviceMode: boolean) {
  const orderedRouteNames = expandAttendanceRouteNames(routeNames, true);
  return isDeviceMode
    ? orderedRouteNames.filter((routeName) => !DEVICE_MODE_BLOCKED_ROUTE_NAMES.has(routeName))
    : orderedRouteNames;
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
  const orderedRouteNames = normalizeVisibleRouteNames(routeNames, isDeviceMode);
  const preferredRouteName = isDeviceMode ? "product-query" : "attendance-personal";

  if (orderedRouteNames.includes(preferredRouteName)) {
    return TAB_PATHS[preferredRouteName];
  }

  const firstVisibleRouteName = orderedRouteNames.find((routeName) => Boolean(TAB_PATHS[routeName]));
  return firstVisibleRouteName ? TAB_PATHS[firstVisibleRouteName] : TAB_PATHS.settings;
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

  const orderedRouteNames = normalizeVisibleRouteNames(routeNames, isDeviceMode);
  const visibleRouteNames = new Set(orderedRouteNames);
  const defaultRoute = resolveDefaultTabRoute({ isDeviceMode, routeNames: orderedRouteNames });
  const currentRoute = TAB_PATHS[currentRouteName];

  if (!currentRoute || !visibleRouteNames.has(currentRouteName)) {
    return defaultRoute;
  }

  if (!hasAppliedDefaultRoute && currentRouteName === "home" && currentRoute !== defaultRoute) {
    return defaultRoute;
  }

  return null;
}
