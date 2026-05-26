import {
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
    routeNames: ["device-management", "settings"],
  }),
  "/(tabs)/settings",
  "device mode never defaults to device management"
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
  TAB_PATHS["store-vouchers"],
  "/(tabs)/store-vouchers",
  "store vouchers route is registered as a valid tab path"
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
    currentRouteName: "store-vouchers",
    hasAppliedDefaultRoute: true,
    isDeviceMode: false,
    routeNames: ["home", "store-vouchers", "settings"],
  }),
  null,
  "store vouchers route is allowed when app menu exposes it"
);
