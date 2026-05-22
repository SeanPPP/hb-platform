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
  settings: "/(tabs)/settings",
};

interface ResolveDefaultTabRouteOptions {
  isDeviceMode: boolean;
  routeNames: Iterable<string>;
}

export function resolveDefaultTabRoute({
  isDeviceMode,
  routeNames,
}: ResolveDefaultTabRouteOptions): AppTabPath {
  const orderedRouteNames = Array.from(routeNames);
  const preferredRouteName = isDeviceMode ? "product-query" : "attendance";

  if (orderedRouteNames.includes(preferredRouteName)) {
    return TAB_PATHS[preferredRouteName];
  }

  const firstVisibleRouteName = orderedRouteNames.find((routeName) => Boolean(TAB_PATHS[routeName]));
  return firstVisibleRouteName ? TAB_PATHS[firstVisibleRouteName] : TAB_PATHS.settings;
}
