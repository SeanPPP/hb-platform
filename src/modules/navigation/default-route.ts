export type AppTabPath =
  | "/(tabs)/home"
  | "/(tabs)/orders"
  | "/(tabs)/cart"
  | "/(tabs)/warehouse"
  | "/(tabs)/domestic-purchase"
  | "/(tabs)/attendance"
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
  attendance: "/(tabs)/attendance",
  "product-query": "/(tabs)/product-query",
  users: "/(tabs)/users",
  "employee-profile": "/(tabs)/employee-profile",
  "device-management": "/(tabs)/device-management",
  settings: "/(tabs)/settings",
};

const DEVICE_MODE_BLOCKED_ROUTE_NAMES = new Set(["device-management"]);

function normalizeVisibleRouteNames(routeNames: Iterable<string>, isDeviceMode: boolean) {
  const orderedRouteNames = Array.from(routeNames);
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
  const preferredRouteName = isDeviceMode ? "product-query" : "attendance";

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
