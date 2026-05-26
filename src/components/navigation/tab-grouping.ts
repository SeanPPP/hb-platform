export const MAX_VISIBLE_TABS = 4;
export const STORE_ROUTE_NAMES = new Set([
  "home",
  "orders",
  "cart",
  "product-query",
  "local-supplier-invoices",
]);

export type NavigationGroupRoute = {
  key: string;
  name: string;
  index: number;
};

export type NavigationDisplayTab<T extends NavigationGroupRoute = NavigationGroupRoute> =
  | {
      type: "route";
      key: string;
      route: T;
    }
  | {
      type: "store";
      key: "store";
      children: T[];
    };

export function buildNavigationDisplayTabs<T extends NavigationGroupRoute>(
  visibleRoutes: T[],
  maxVisibleTabs = MAX_VISIBLE_TABS
): NavigationDisplayTab<T>[] {
  if (visibleRoutes.length <= maxVisibleTabs) {
    return visibleRoutes.map((route) => ({
      type: "route",
      key: route.key,
      route,
    }));
  }

  const storeChildren = visibleRoutes.filter((route) => STORE_ROUTE_NAMES.has(route.name));

  if (!storeChildren.length) {
    return visibleRoutes.map((route) => ({
      type: "route",
      key: route.key,
      route,
    }));
  }

  let hasInsertedStore = false;
  const tabs: NavigationDisplayTab<T>[] = [];

  visibleRoutes.forEach((route) => {
    if (!STORE_ROUTE_NAMES.has(route.name)) {
      tabs.push({ type: "route", key: route.key, route });
      return;
    }

    if (hasInsertedStore) {
      return;
    }

    hasInsertedStore = true;
    tabs.push({ type: "store", key: "store", children: storeChildren });
  });

  return tabs;
}

export function isNavigationDisplayTabFocused(
  item: NavigationDisplayTab,
  activeIndex: number
) {
  return item.type === "store"
    ? item.children.some((route) => route.index === activeIndex)
    : item.route.index === activeIndex;
}
