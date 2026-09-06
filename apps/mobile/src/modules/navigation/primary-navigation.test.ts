import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import {
  buildPrimaryNavigation,
  resolvePrimaryNavigationAction,
} from "./primary-navigation";
import { buildWorkbenchSections } from "./workbench";

const localeRoot = resolve(
  dirname(fileURLToPath(import.meta.url)),
  "../../locales"
);
const zhWorkbench = JSON.parse(
  readFileSync(resolve(localeRoot, "zh/screens/workbench.json"), "utf8")
);
const enWorkbench = JSON.parse(
  readFileSync(resolve(localeRoot, "en/screens/workbench.json"), "utf8")
);
const zhOrders = JSON.parse(
  readFileSync(resolve(localeRoot, "zh/screens/orders.json"), "utf8")
);
const enOrders = JSON.parse(
  readFileSync(resolve(localeRoot, "en/screens/orders.json"), "utf8")
);

assert.equal(zhWorkbench.routes.orders, "HB订单", "中文工作台必须显示 HB订单");
assert.equal(enWorkbench.routes.orders, "HB Orders", "英文工作台必须显示 HB Orders");
assert.equal(zhOrders.title, "订单列表", "中文订单业务页标题不得随工作台入口改名");
assert.equal(enOrders.title, "Orders", "英文订单业务页标题不得随工作台入口改名");

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
    },
    {
      key: "scan",
      targetRouteName: "product-query",
      labelKey: "tabs.scan",
      active: false,
    },
    {
      key: "attendance",
      targetRouteName: "attendance-personal",
      labelKey: "tabs.checkIn",
      active: false,
    },
    {
      key: "reports",
      targetRouteName: "reports",
      labelKey: "tabs.reports",
      active: false,
    },
    {
      key: "me",
      targetRouteName: "settings",
      labelKey: "tabs.me",
      active: false,
    },
  ],
  "全权限账号必须按工作台、扫码查询、打卡、报表、我的顺序显示五项"
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

const primaryItemsForNavigationActions = buildPrimaryNavigation({
  activeRouteName: "workbench",
  visibleRouteNames: fullMenu,
});
assert.equal(
  primaryItemsForNavigationActions.every((item) => !("locked" in item)),
  true,
  "可见一级导航不得再携带锁定展示状态"
);

function primaryItem(key: "workbench" | "scan" | "attendance" | "reports" | "me") {
  const item = primaryItemsForNavigationActions.find((candidate) => candidate.key === key);
  assert.ok(item, `必须存在 ${key} 一级入口`);
  return item;
}

assert.equal(
  resolvePrimaryNavigationAction("workbench", primaryItem("workbench")),
  "none",
  "已在工作台根页时重复点击不得产生重复导航"
);
assert.equal(
  resolvePrimaryNavigationAction("orders", primaryItem("workbench")),
  "dismiss-to",
  "工作台二级业务页点击工作台必须清理子栈并回到工作台根页"
);
assert.equal(
  resolvePrimaryNavigationAction("reports", primaryItem("workbench")),
  "dismiss-to",
  "报表页点击工作台必须清理报表栈并回到工作台根页"
);
assert.equal(
  resolvePrimaryNavigationAction(
    "attendance-management",
    buildPrimaryNavigation({
      activeRouteName: "attendance-management",
      visibleRouteNames: fullMenu,
    }).find((item) => item.key === "attendance")!
  ),
  "dismiss-to",
  "考勤管理作为打卡上下文子页时必须返回个人考勤根页"
);
assert.equal(
  resolvePrimaryNavigationAction(
    "employee-profile",
    buildPrimaryNavigation({
      activeRouteName: "employee-profile",
      visibleRouteNames: fullMenu,
    }).find((item) => item.key === "me")!
  ),
  "dismiss-to",
  "个人资料作为我的上下文子页时必须返回设置根页"
);
assert.equal(
  resolvePrimaryNavigationAction("orders", primaryItem("scan")),
  "navigate",
  "跨一级入口时必须导航到目标首页，而不能错误地清理当前业务栈"
);
assert.equal(
  resolvePrimaryNavigationAction("orders", primaryItem("reports")),
  "navigate",
  "业务页进入报表必须导航到现有报表根页"
);
assert.equal(
  resolvePrimaryNavigationAction(
    "reports",
    buildPrimaryNavigation({
      activeRouteName: "reports",
      visibleRouteNames: fullMenu,
    }).find((item) => item.key === "reports")!
  ),
  "none",
  "已在报表根页时重复点击不得产生重复导航"
);

const devicePrimaryItems = compactPrimaryItems(
  "product-query",
  [...fullMenu, "attendance-personal"],
  true
);
assert.deepEqual(
  devicePrimaryItems.map((item) => item.key),
  ["workbench", "scan", "me"],
  "设备模式即使收到个人考勤和报表菜单，也必须完全隐藏两个入口"
);
assert.equal(
  devicePrimaryItems.find((item) => item.key === "scan")?.active,
  true,
  "设备模式扫码查询必须保持可用且高亮"
);

const attendanceUnavailableItems = compactPrimaryItems(
  "workbench",
  fullMenu.filter((routeName) => routeName !== "attendance-personal")
);
assert.deepEqual(
  attendanceUnavailableItems.map((item) => item.key),
  ["workbench", "scan", "reports", "me"],
  "只有考勤管理权限但没有个人考勤菜单时，不得显示个人打卡入口"
);

const attendanceManagementOnlyItems = compactPrimaryItems(
  "attendance-management",
  ["workbench", "attendance-management", "settings"]
);
assert.deepEqual(
  attendanceManagementOnlyItems.map((item) => ({
    key: item.key,
    active: item.active,
  })),
  [
    { key: "workbench", active: true },
    { key: "me", active: false },
  ],
  "个人打卡主入口不可见时，合法考勤管理页必须回落到工作台上下文"
);

const reportsUnavailableItems = compactPrimaryItems(
  "workbench",
  fullMenu.filter((routeName) => routeName !== "reports")
);
assert.deepEqual(
  reportsUnavailableItems.map((item) => item.key),
  ["workbench", "scan", "attendance", "me"],
  "没有报表菜单时必须完全隐藏报表入口"
);

const orderAccountItems = compactPrimaryItems("workbench", [
  "home",
  "orders",
  "cart",
  "product-query",
  "local-supplier-invoices",
  "settings",
]);
assert.deepEqual(
  orderAccountItems.map((item) => item.key),
  ["workbench", "scan", "me"],
  "无打卡与报表权限的账号只能显示工作台、扫码查询和我的"
);

assert.deepEqual(
  compactPrimaryItems("workbench", ["home", "settings"]).map((item) => item.key),
  ["workbench", "me"],
  "没有任何可用业务主入口时只显示两个本地安全壳"
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

const salesAndSupplierInvoiceSections = buildWorkbenchSections([
  "local-supplier-invoices",
  "warehouse",
  "orders",
]);
assert.deepEqual(
  salesAndSupplierInvoiceSections.map((section) => ({
    key: section.key,
    itemRouteNames: section.items.map((item) => item.routeName),
  })),
  [
    {
      key: "sales-product",
      itemRouteNames: ["orders", "local-supplier-invoices"],
    },
    { key: "warehouse-purchase", itemRouteNames: ["warehouse"] },
  ],
  "供应商发票必须归入销售与商品并紧跟 HB订单，仓库与采购不得再包含该入口"
);
assert.deepEqual(
  buildWorkbenchSections(["local-supplier-invoices"]).map((section) => ({
    key: section.key,
    itemRouteNames: section.items.map((item) => item.routeName),
  })),
  [
    {
      key: "sales-product",
      itemRouteNames: ["local-supplier-invoices"],
    },
  ],
  "只有供应商发票权限时必须只生成销售与商品分组"
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
