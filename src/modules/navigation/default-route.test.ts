import { resolveDefaultTabRoute, resolveTabRouteCorrection } from "./default-route";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

assertEqual(
  resolveDefaultTabRoute({
    isDeviceMode: true,
    routeNames: ["home", "orders", "product-query", "settings"],
  }),
  "/(tabs)/product-query",
  "device-bound login defaults to product query"
);

assertEqual(
  resolveDefaultTabRoute({
    isDeviceMode: false,
    routeNames: ["home", "attendance", "settings"],
  }),
  "/(tabs)/attendance",
  "user login defaults to attendance"
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
  "/(tabs)/attendance",
  "startup home route redirects user sessions to attendance"
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
