import {
  getVisibleTabRouteNames,
  expandAttendanceRouteNames,
  resolveDefaultTabRoute,
  resolveTabRouteCorrection,
  TAB_PATHS,
} from "./default-route";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

const warehouseManagerWithoutMenu = {
  roleNames: ["WarehouseManager"],
  permissions: [],
  appMenu: [] as string[],
};

assertEqual(
  resolveDefaultTabRoute({
    isDeviceMode: true,
    routeNames: ["home", "orders", "product-query", "device-management", "settings"],
  }),
  "/(tabs)/product-query",
  "device-bound login defaults to product query"
);

assertEqual(
  resolveDefaultTabRoute({
    isDeviceMode: false,
    routeNames: warehouseManagerWithoutMenu.appMenu,
  }),
  "/(tabs)/settings",
  "WarehouseManager without permissions or app menu does not default to warehouse"
);

assertEqual(
  resolveDefaultTabRoute({
    isDeviceMode: false,
    routeNames: ["home", "attendance", "settings"],
  }),
  "/(tabs)/attendance-personal",
  "user login defaults to personal attendance"
);

assertEqual(
  resolveDefaultTabRoute({
    isDeviceMode: false,
    routeNames: ["home", "attendance-personal", "attendance-management", "settings"],
  }),
  "/(tabs)/attendance-personal",
  "split attendance menu defaults to personal attendance"
);

assertEqual(
  expandAttendanceRouteNames(["home", "attendance", "settings"], false).join(","),
  "home,attendance-personal,settings",
  "legacy attendance expands to personal attendance for normal users"
);

assertEqual(
  expandAttendanceRouteNames(["home", "attendance", "settings"], true).join(","),
  "home,attendance-personal,attendance-management,settings",
  "legacy attendance expands to personal and management attendance for managers"
);

assertEqual(
  getVisibleTabRouteNames({
    routeNames: ["home", "attendance", "device-management", "reports", "settings"],
    isDeviceMode: true,
    canViewAttendanceManagement: true,
  }).join(","),
  "home,attendance-personal,settings",
  "shared visible routes expand legacy attendance and hide management-only/report routes in device mode"
);

assertEqual(
  getVisibleTabRouteNames({
    routeNames: ["home", "attendance", "settings"],
    isDeviceMode: false,
    canViewAttendanceManagement: true,
  }).join(","),
  "home,attendance-personal,attendance-management,settings",
  "shared visible routes expand legacy attendance to management for users with management permission"
);

assertEqual(
  resolveDefaultTabRoute({
    isDeviceMode: false,
    routeNames: ["settings"],
  }),
  "/(tabs)/settings",
  "role-only sessions without app menu entries stay on settings"
);

assertEqual(
  resolveDefaultTabRoute({
    isDeviceMode: false,
    routeNames: ["home", "settings"],
  }),
  "/(tabs)/home",
  "missing preferred route falls back to first visible tab"
);

assertEqual(
  resolveDefaultTabRoute({
    isDeviceMode: true,
    routeNames: ["device-management", "reports", "settings"],
  }),
  "/(tabs)/settings",
  "device mode never defaults to device management or reports"
);

assertEqual(
  resolveDefaultTabRoute({
    isDeviceMode: false,
    routeNames: ["device-management", "settings"],
  }),
  "/(tabs)/device-management",
  "account sessions can fall back to device management"
);

assertEqual(
  resolveTabRouteCorrection({
    currentRouteName: "device-management",
    hasAppliedDefaultRoute: false,
    isDeviceMode: true,
    routeNames: ["device-management", "settings"],
  }),
  "/(tabs)/settings",
  "device mode redirects away from device management"
);

assertEqual(
  resolveTabRouteCorrection({
    currentRouteName: "reports",
    hasAppliedDefaultRoute: false,
    isDeviceMode: true,
    routeNames: ["reports", "settings"],
  }),
  "/(tabs)/settings",
  "device mode redirects away from reports"
);

assertEqual(
  resolveDefaultTabRoute({
    isDeviceMode: true,
    routeNames: [],
  }),
  "/(tabs)/settings",
  "empty navigation falls back to settings"
);

assertEqual(
  resolveTabRouteCorrection({
    currentRouteName: "home",
    hasAppliedDefaultRoute: false,
    isDeviceMode: false,
    routeNames: ["home", "attendance", "settings"],
  }),
  "/(tabs)/attendance-personal",
  "startup home route redirects user sessions to personal attendance"
);

assertEqual(
  resolveTabRouteCorrection({
    currentRouteName: "attendance",
    hasAppliedDefaultRoute: true,
    isDeviceMode: false,
    routeNames: ["home", "attendance", "settings"],
  }),
  "/(tabs)/attendance-personal",
  "legacy attendance route redirects to personal attendance"
);

assertEqual(
  resolveTabRouteCorrection({
    currentRouteName: "home",
    hasAppliedDefaultRoute: true,
    isDeviceMode: false,
    routeNames: ["home", "attendance", "settings"],
  }),
  null,
  "manual home navigation is allowed after startup default was applied"
);

assertEqual(
  TAB_PATHS["local-supplier-invoices"],
  "/(tabs)/local-supplier-invoices",
  "local supplier invoices route is registered as a valid tab path"
);

assertEqual(
  TAB_PATHS["installment-orders"],
  "/(tabs)/installment-orders",
  "installment orders route is registered as a valid tab path"
);

assertEqual(
  TAB_PATHS.advertisements,
  "/(tabs)/advertisements",
  "advertisements route is registered as a valid tab path"
);

assertEqual(
  TAB_PATHS.promotions,
  "/(tabs)/promotions",
  "promotions route is registered as a valid tab path"
);

assertEqual(
  TAB_PATHS.reports,
  "/(tabs)/reports",
  "reports route is registered as a valid tab path"
);

assertEqual(
  TAB_PATHS["store-vouchers"],
  "/(tabs)/store-vouchers",
  "store vouchers route is registered as a valid tab path"
);

assertEqual(
  TAB_PATHS["seasonal-cards"],
  "/(tabs)/seasonal-cards",
  "seasonal cards route is registered as a valid tab path"
);

assertEqual(
  resolveTabRouteCorrection({
    currentRouteName: "local-supplier-invoices",
    hasAppliedDefaultRoute: true,
    isDeviceMode: false,
    routeNames: ["home", "local-supplier-invoices", "settings"],
  }),
  null,
  "local supplier invoices route is allowed when app menu exposes it"
);

assertEqual(
  resolveTabRouteCorrection({
    currentRouteName: "installment-orders",
    hasAppliedDefaultRoute: true,
    isDeviceMode: false,
    routeNames: ["home", "installment-orders", "settings"],
  }),
  null,
  "installment orders route is allowed when app menu exposes it"
);

assertEqual(
  resolveTabRouteCorrection({
    currentRouteName: "advertisements",
    hasAppliedDefaultRoute: true,
    isDeviceMode: false,
    routeNames: ["home", "advertisements", "settings"],
  }),
  null,
  "advertisements route is allowed when app menu exposes it"
);

assertEqual(
  resolveTabRouteCorrection({
    currentRouteName: "promotions",
    hasAppliedDefaultRoute: true,
    isDeviceMode: false,
    routeNames: ["home", "promotions", "settings"],
  }),
  null,
  "promotions route is allowed when app menu exposes it"
);

assertEqual(
  resolveTabRouteCorrection({
    currentRouteName: "reports",
    hasAppliedDefaultRoute: true,
    isDeviceMode: false,
    routeNames: ["home", "reports", "settings"],
  }),
  null,
  "reports route is allowed when app menu exposes it for account sessions"
);

assertEqual(
  resolveTabRouteCorrection({
    currentRouteName: "store-vouchers",
    hasAppliedDefaultRoute: true,
    isDeviceMode: false,
    routeNames: ["home", "store-vouchers", "settings"],
  }),
  null,
  "store vouchers route is allowed when app menu exposes it"
);

assertEqual(
  resolveTabRouteCorrection({
    currentRouteName: "seasonal-cards",
    hasAppliedDefaultRoute: true,
    isDeviceMode: false,
    routeNames: ["home", "seasonal-cards", "settings"],
  }),
  null,
  "seasonal cards route is allowed when app menu exposes it"
);
