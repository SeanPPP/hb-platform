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
}

export type PrimaryNavigationAction = "none" | "dismiss-to" | "navigate";

export function resolvePrimaryNavigationAction(
  activeRouteName: string | undefined,
  item: PrimaryNavigationItem
): PrimaryNavigationAction {
  if (activeRouteName === item.targetRouteName) {
    return "none";
  }

  if (item.key === "workbench" || item.active) {
    return "dismiss-to";
  }

  return "navigate";
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
  const visiblePrimaryKeys = new Set<PrimaryNavigationKey>(["workbench", "me"]);

  if (visibleRoutes.has("product-query")) {
    visiblePrimaryKeys.add("scan");
  }
  if (!isDeviceMode && visibleRoutes.has("attendance-personal")) {
    visiblePrimaryKeys.add("attendance");
  }
  if (!isDeviceMode && visibleRoutes.has("reports")) {
    visiblePrimaryKeys.add("reports");
  }

  const requestedActiveKey = resolveActivePrimaryKey(activeRouteName);
  const activeKey = visiblePrimaryKeys.has(requestedActiveKey)
    ? requestedActiveKey
    : "workbench";
  const items: PrimaryNavigationItem[] = [
    {
      key: "workbench",
      targetRouteName: "workbench",
      labelKey: "tabs.workbench",
      icon: "view-dashboard-outline",
      active: activeKey === "workbench",
    },
  ];

  // 底栏只呈现当前会话真正可进入的主入口；工作台和我的保留为本地安全壳。
  if (visiblePrimaryKeys.has("scan")) {
    items.push({
      key: "scan",
      targetRouteName: "product-query",
      labelKey: "tabs.scan",
      icon: "barcode-scan",
      active: activeKey === "scan",
    });
  }

  if (visiblePrimaryKeys.has("attendance")) {
    items.push({
      key: "attendance",
      targetRouteName: "attendance-personal",
      labelKey: "tabs.checkIn",
      icon: "clock-outline",
      active: activeKey === "attendance",
    });
  }

  if (visiblePrimaryKeys.has("reports")) {
    items.push({
      key: "reports",
      targetRouteName: "reports",
      labelKey: "tabs.reports",
      icon: "chart-box-outline",
      active: activeKey === "reports",
    });
  }

  items.push({
    key: "me",
    targetRouteName: "settings",
    labelKey: "tabs.me",
    icon: "account-circle-outline",
    active: activeKey === "me",
  });

  return items;
}
