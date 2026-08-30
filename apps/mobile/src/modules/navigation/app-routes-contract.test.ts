import assert from "node:assert/strict";
import { access, readFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { TAB_PATHS } from "./default-route";

const moduleDir = dirname(fileURLToPath(import.meta.url));
const mobileRoot = resolve(moduleDir, "../../..");

const EXISTING_REAL_PAGE_FILES = [
  "app/(auth)/login.tsx",
  "app/privacy.tsx",
  "app/(tabs)/home.tsx",
  "app/(tabs)/cart.tsx",
  "app/(tabs)/orders.tsx",
  "app/preorders/index.tsx",
  "app/preorders/[activationGuid].tsx",
  "app/(tabs)/installment-orders.tsx",
  "app/(tabs)/store-vouchers.tsx",
  "app/(tabs)/seasonal-cards.tsx",
  "app/(tabs)/warehouse.tsx",
  "app/containers/index.tsx",
  "app/containers/[containerGuid]/index.tsx",
  "app/(tabs)/product-query.tsx",
  "app/(tabs)/domestic-purchase.tsx",
  "app/(tabs)/local-supplier-invoices.tsx",
  "app/(tabs)/advertisements.tsx",
  "app/(tabs)/promotions.tsx",
  "app/(tabs)/reports.tsx",
  "app/(tabs)/attendance-personal.tsx",
  "app/(tabs)/attendance-management.tsx",
  "app/(tabs)/users.tsx",
  "app/staff/[userGuid].tsx",
  "app/users/[userGuid]/access.tsx",
  "app/users/[userGuid]/pos-terminal-permissions.tsx",
  "app/(tabs)/device-management.tsx",
  "app/(tabs)/employee-profile.tsx",
  "app/(tabs)/employee-profile-review.tsx",
  "app/employee-profile-review/[requestId].tsx",
  "app/(tabs)/settings.tsx",
] as const;

const EXISTING_TAB_PATHS = {
  home: "/(tabs)/home",
  orders: "/(tabs)/orders",
  cart: "/(tabs)/cart",
  warehouse: "/(tabs)/warehouse",
  "domestic-purchase": "/(tabs)/domestic-purchase",
  "local-supplier-invoices": "/(tabs)/local-supplier-invoices",
  "installment-orders": "/(tabs)/installment-orders",
  advertisements: "/(tabs)/advertisements",
  promotions: "/(tabs)/promotions",
  reports: "/(tabs)/reports",
  "store-vouchers": "/(tabs)/store-vouchers",
  "seasonal-cards": "/(tabs)/seasonal-cards",
  "attendance-personal": "/(tabs)/attendance-personal",
  "attendance-management": "/(tabs)/attendance-management",
  "product-query": "/(tabs)/product-query",
  users: "/(tabs)/users",
  "employee-profile": "/(tabs)/employee-profile",
  "employee-profile-review": "/(tabs)/employee-profile-review",
  "device-management": "/(tabs)/device-management",
  settings: "/(tabs)/settings",
} as const;

async function assertFileExists(relativePath: string) {
  await access(resolve(mobileRoot, relativePath));
}

async function run() {
  assert.equal(
    EXISTING_REAL_PAGE_FILES.length,
    30,
    "导航改版前的 30 个真实业务页面必须全部保留"
  );
  await Promise.all(EXISTING_REAL_PAGE_FILES.map(assertFileExists));
  await assertFileExists("app/(tabs)/workbench.tsx");

  for (const [routeName, expectedPath] of Object.entries(EXISTING_TAB_PATHS)) {
    assert.equal(
      TAB_PATHS[routeName],
      expectedPath,
      `既有深链 ${routeName} 不得因工作台改版改变`
    );
  }

  assert.equal(
    TAB_PATHS.workbench,
    "/(tabs)/workbench",
    "工作台是唯一允许新增的 Tab 路由"
  );
  assert.equal(
    "task-center" in TAB_PATHS,
    false,
    "生产路由表不得包含已移除的任务中心"
  );

  const [tabsLayout, appIndex, legacyAttendance, navigationSources, zhCommon, enCommon] =
    await Promise.all([
      readFile(resolve(mobileRoot, "app/(tabs)/_layout.tsx"), "utf8"),
      readFile(resolve(mobileRoot, "app/index.tsx"), "utf8"),
      readFile(resolve(mobileRoot, "app/(tabs)/attendance.tsx"), "utf8"),
      readFile(resolve(mobileRoot, "src/modules/navigation/default-route.ts"), "utf8"),
      readFile(resolve(mobileRoot, "src/locales/zh/common.json"), "utf8"),
      readFile(resolve(mobileRoot, "src/locales/en/common.json"), "utf8"),
    ]);

  assert.match(tabsLayout, /<Tabs\.Screen\s+name="workbench"/);
  assert.match(tabsLayout, /<Tabs\.Screen\s+name="home"/);
  assert.match(appIndex, /\/?\(tabs\)\/workbench/);
  assert.match(legacyAttendance, /\/?\(tabs\)\/attendance-personal/);
  assert.doesNotMatch(navigationSources, /task-center/);
  assert.doesNotMatch(tabsLayout, /name="task-center"/);

  const zhTabs = JSON.parse(zhCommon).tabs;
  const enTabs = JSON.parse(enCommon).tabs;
  assert.deepEqual(
    {
      workbench: zhTabs.workbench,
      scan: zhTabs.scan,
      checkIn: zhTabs.checkIn,
      reports: zhTabs.reports,
      me: zhTabs.me,
    },
    {
      workbench: "工作台",
      scan: "扫码查询",
      checkIn: "打卡",
      reports: "报表",
      me: "我的",
    },
    "中文一级导航文案必须精确匹配产品定义"
  );
  assert.deepEqual(
    {
      workbench: enTabs.workbench,
      scan: enTabs.scan,
      checkIn: enTabs.checkIn,
      reports: enTabs.reports,
      me: enTabs.me,
    },
    {
      workbench: "Home",
      scan: "Scan",
      checkIn: "Check in",
      reports: "Reports",
      me: "Me",
    },
    "英文一级导航文案必须精确匹配产品定义"
  );
}

void run();
