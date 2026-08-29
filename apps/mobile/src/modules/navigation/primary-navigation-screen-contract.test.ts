import assert from "node:assert/strict";
import { access, readFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const moduleDir = dirname(fileURLToPath(import.meta.url));
const mobileRoot = resolve(moduleDir, "../../..");

async function readMobileSource(relativePath: string) {
  return readFile(resolve(mobileRoot, relativePath), "utf8");
}

async function run() {
  const [tabsLayout, primaryTabBar, primaryNavigation, workbenchRoute, workbenchScreen, attendancePersonal, attendanceManagement, legacyAttendance] =
    await Promise.all([
      readMobileSource("app/(tabs)/_layout.tsx"),
      readMobileSource("src/components/navigation/PrimaryTabBar.tsx"),
      readMobileSource("src/modules/navigation/primary-navigation.ts"),
      readMobileSource("app/(tabs)/workbench.tsx"),
      readMobileSource("src/modules/workbench/workbench-screen.tsx"),
      readMobileSource("app/(tabs)/attendance-personal.tsx"),
      readMobileSource("app/(tabs)/attendance-management.tsx"),
      readMobileSource("app/(tabs)/attendance.tsx"),
    ]);

  assert.match(tabsLayout, /<Tabs\.Screen\s+name="workbench"/);
  assert.match(tabsLayout, /<Tabs\.Screen\s+name="home"/);
  assert.match(tabsLayout, /<Tabs\.Screen\s+name="attendance-personal"/);
  assert.match(tabsLayout, /<Tabs\.Screen\s+name="attendance-management"/);
  assert.doesNotMatch(tabsLayout, /<Tabs\.Screen\s+name="task-center"/);
  assert.match(tabsLayout, /PrimaryTabBar/);
  assert.match(tabsLayout, /awaitingPreferredDefaultRoute/);
  assert.match(tabsLayout, /resolvePreferredDefaultTabRoute/);
  assert.doesNotMatch(tabsLayout, /ScrollableTabBar|tab-grouping/);

  assert.match(primaryTabBar, /buildPrimaryNavigation/);
  assert.match(primaryTabBar, /locked/);
  assert.match(primaryTabBar, /accessibilityRole="button"/);
  assert.doesNotMatch(
    primaryTabBar,
    /buildNavigationDisplayTabs/,
    "固定四入口不得回退到旧的动态门店折叠导航"
  );
  assert.doesNotMatch(primaryTabBar, /task-center/);

  await assert.rejects(
    () => access(resolve(mobileRoot, "src/components/navigation/ScrollableTabBar.tsx")),
    { code: "ENOENT" },
    "旧的动态 ScrollableTabBar 必须删除，避免被后续页面重新引用"
  );
  await assert.rejects(
    () => access(resolve(mobileRoot, "src/components/navigation/tab-grouping.ts")),
    { code: "ENOENT" },
    "旧的 Tab 分组模型必须删除，避免重新生成门店折叠入口"
  );

  assert.match(primaryNavigation, /key:\s*"workbench"/);
  assert.match(primaryNavigation, /key:\s*"scan"/);
  assert.match(primaryNavigation, /key:\s*"attendance"/);
  assert.match(primaryNavigation, /key:\s*"me"/);
  assert.match(primaryNavigation, /targetRouteName:\s*"attendance-personal"/);
  assert.match(primaryNavigation, /isDeviceMode/);
  assert.match(primaryNavigation, /locked:/);
  assert.doesNotMatch(primaryNavigation, /task-center/);

  assert.match(workbenchRoute, /<WorkbenchScreen\s*\/>/);
  assert.match(workbenchScreen, /buildWorkbenchSections/);
  assert.match(workbenchScreen, /router\.(navigate|push)/);
  assert.match(attendancePersonal, /<AttendanceScreen\s+mode="personal"/);
  assert.match(attendanceManagement, /<AttendanceScreen\s+mode="management"/);
  assert.match(legacyAttendance, /\/?\(tabs\)\/attendance-personal/);
}

void run();
