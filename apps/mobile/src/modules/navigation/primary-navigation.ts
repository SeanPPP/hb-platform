export type PrimaryNavigationKey =
  | "workbench"
  | "scan"
  | "attendance"
  | "reports"
  | "me";

export interface PrimaryNavigationItem {
  key: PrimaryNavigationKey;
  targetRouteName:
    | "workbench"
    | "product-query"
    | "attendance-personal"
    | "reports"
    | "settings";
  labelKey:
    | "tabs.workbench"
    | "tabs.scan"
    | "tabs.checkIn"
    | "tabs.reports"
    | "tabs.me";
  icon:
    | "view-dashboard-outline"
    | "barcode-scan"
    | "clock-outline"
    | "chart-box-outline"
    | "account-circle-outline";
  active: boolean;
  locked: boolean;
}

interface BuildPrimaryNavigationOptions {
  activeRouteName?: string;
  visibleRouteNames: Iterable<string>;
  isDeviceMode?: boolean;
}

const ATTENDANCE_CONTEXT_ROUTE_NAMES = new Set([
  "attendance",
  "attendance-personal",
  "attendance-management",
]);
const ME_CONTEXT_ROUTE_NAMES = new Set(["employee-profile", "settings"]);

function resolveActivePrimaryKey(routeName: string | undefined): PrimaryNavigationKey {
  if (routeName === "product-query") {
    return "scan";
  }

  if (routeName && ATTENDANCE_CONTEXT_ROUTE_NAMES.has(routeName)) {
    return "attendance";
  }

  if (routeName === "reports") {
    return "reports";
  }

  if (routeName && ME_CONTEXT_ROUTE_NAMES.has(routeName)) {
    return "me";
  }

  return "workbench";
}

export function buildPrimaryNavigation({
  activeRouteName,
  visibleRouteNames,
  isDeviceMode = false,
}: BuildPrimaryNavigationOptions): PrimaryNavigationItem[] {
  const visibleRoutes = new Set(visibleRouteNames);
  const activeKey = resolveActivePrimaryKey(activeRouteName);

  return [
    {
      key: "workbench",
      targetRouteName: "workbench",
      labelKey: "tabs.workbench",
      icon: "view-dashboard-outline",
      active: activeKey === "workbench",
      locked: false,
    },
    {
      key: "scan",
      targetRouteName: "product-query",
      labelKey: "tabs.scan",
      icon: "barcode-scan",
      active: activeKey === "scan",
      locked: !visibleRoutes.has("product-query"),
    },
    {
      key: "attendance",
      targetRouteName: "attendance-personal",
      labelKey: "tabs.checkIn",
      icon: "clock-outline",
      active: activeKey === "attendance",
      locked: isDeviceMode || !visibleRoutes.has("attendance-personal"),
    },
    {
      key: "reports",
      targetRouteName: "reports",
      labelKey: "tabs.reports",
      icon: "chart-box-outline",
      active: activeKey === "reports",
      locked: isDeviceMode || !visibleRoutes.has("reports"),
    },
    {
      key: "me",
      targetRouteName: "settings",
      labelKey: "tabs.me",
      icon: "account-circle-outline",
      active: activeKey === "me",
      locked: false,
    },
  ];
}
