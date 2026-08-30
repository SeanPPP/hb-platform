import assert from "node:assert/strict";
import { buildPrimaryNavigation } from "./primary-navigation";
import { buildWorkbenchSections } from "./workbench";

function compactPrimaryItems(
  activeRouteName: string | undefined,
  visibleRouteNames: string[],
  isDeviceMode = false
) {
  return buildPrimaryNavigation({
    activeRouteName,
    visibleRouteNames,
    isDeviceMode,
  }).map((item) => ({
    key: item.key,
    targetRouteName: item.targetRouteName,
    labelKey: item.labelKey,
    icon: item.icon,
    active: item.active,
    locked: item.locked,
  }));
}

const fullMenu = [
  "home",
  "orders",
  "cart",
  "product-query",
  "warehouse",
  "domestic-purchase",
  "local-supplier-invoices",
  "advertisements",
  "promotions",
  "reports",
  "attendance-personal",
  "attendance-management",
  "users",
  "employee-profile-review",
  "device-management",
  "employee-profile",
  "settings",
];

const accountPrimaryItems = compactPrimaryItems("workbench", fullMenu);
assert.deepEqual(
  accountPrimaryItems.map(({ icon: _icon, ...item }) => item),
  [
    {
      key: "workbench",
      targetRouteName: "workbench",
      labelKey: "tabs.workbench",
      active: true,
      locked: false,
    },
    {
      key: "scan",
      targetRouteName: "product-query",
      labelKey: "tabs.scan",
      active: false,
      locked: false,
    },
    {
      key: "attendance",
      targetRouteName: "attendance-personal",
      labelKey: "tabs.checkIn",
      active: false,
      locked: false,
    },
    {
      key: "reports",
      targetRouteName: "reports",
      labelKey: "tabs.reports",
      active: false,
      locked: false,
    },
    {
      key: "me",
      targetRouteName: "settings",
      labelKey: "tabs.me",
      active: false,
      locked: false,
    },
  ],
  "一级导航必须固定为工作台、扫码查询、打卡、报表、我的五项"
);
assert.equal(
  accountPrimaryItems.every((item) => typeof item.icon === "string" && item.icon.length > 0),
  true,
  "五个一级入口必须都声明图标"
);
assert.match(
  accountPrimaryItems[2]?.icon ?? "",
  /clock/,
  "打卡入口必须使用时钟语义图标"
);
assert.equal(
  accountPrimaryItems[3]?.icon,
  "chart-box-outline",
  "报表入口必须使用清晰的报表语义图标"
);

assert.equal(
  compactPrimaryItems("orders", fullMenu)[0]?.active,
  true,
  "订单等业务页必须回归工作台上下文"
);
assert.equal(
  compactPrimaryItems("product-query", fullMenu)[1]?.active,
  true,
  "商品查询页必须高亮扫码查询"
);
assert.equal(
  compactPrimaryItems("attendance-management", fullMenu)[2]?.active,
  true,
  "考勤管理页也必须高亮打卡，而不是改变打卡的个人考勤目标"
);
assert.equal(
  compactPrimaryItems("reports", fullMenu)[3]?.active,
  true,
  "报表中心必须高亮独立的报表入口"
);
assert.equal(
  compactPrimaryItems("employee-profile", fullMenu)[4]?.active,
  true,
  "个人资料页必须高亮我的"
);

const devicePrimaryItems = compactPrimaryItems(
  "product-query",
  [...fullMenu, "attendance-personal"],
  true
);
assert.equal(devicePrimaryItems[1]?.active, true, "设备模式扫码查询必须保持可用且高亮");
assert.deepEqual(
  devicePrimaryItems[2] && (({ icon: _icon, ...item }) => item)(devicePrimaryItems[2]),
  {
    key: "attendance",
    targetRouteName: "attendance-personal",
    labelKey: "tabs.checkIn",
    active: false,
    locked: true,
  },
  "设备模式即使收到个人考勤菜单，也必须锁定打卡入口"
);
assert.equal(
  devicePrimaryItems[3]?.locked,
  true,
  "设备模式即使收到报表菜单，也必须锁定报表入口"
);

const attendanceUnavailableItems = compactPrimaryItems(
  "workbench",
  fullMenu.filter((routeName) => routeName !== "attendance-personal")
);
assert.equal(
  attendanceUnavailableItems[2]?.locked,
  true,
  "只有考勤管理权限但没有个人考勤菜单时，不得解锁固定个人打卡目标"
);

const reportsUnavailableItems = compactPrimaryItems(
  "workbench",
  fullMenu.filter((routeName) => routeName !== "reports")
);
assert.equal(
  reportsUnavailableItems[3]?.locked,
  true,
  "没有报表菜单时必须锁定固定报表入口"
);

const sparseSections = buildWorkbenchSections([
  "orders",
  "warehouse",
  "reports",
  "users",
]);
assert.deepEqual(
  sparseSections.map((section) => ({
    key: section.key,
    itemRouteNames: section.items.map((item) => item.routeName),
  })),
  [
    { key: "sales-product", itemRouteNames: ["orders"] },
    { key: "warehouse-purchase", itemRouteNames: ["warehouse"] },
    { key: "operations-reports", itemRouteNames: ["reports"] },
    { key: "people-management", itemRouteNames: ["users"] },
  ],
  "工作台仅按显式可见路由显示四类业务入口"
);

const visibleSectionRoutes = sparseSections.flatMap((section) =>
  section.items.map((item) => item.routeName)
);
assert.deepEqual(
  visibleSectionRoutes,
  ["orders", "warehouse", "reports", "users"],
  "工作台不得从角色或默认配置推断未授权入口"
);
assert.deepEqual(
  buildWorkbenchSections([]),
  [],
  "空菜单必须 fail-closed，不得泄露任何工作台业务入口"
);
assert.deepEqual(
  buildWorkbenchSections(["task-center", "unknown-route"]),
  [],
  "任务中心和未知路由不得成为工作台入口"
);
