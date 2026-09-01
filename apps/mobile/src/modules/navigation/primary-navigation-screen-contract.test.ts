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
  const [shellLayout, primaryTabBar, primaryNavigation, workbenchRoute, workbenchScreen, attendancePersonal, attendanceManagement, legacyAttendance, containerLayout, userLayout] =
    await Promise.all([
      readMobileSource("app/(shell)/_layout.tsx"),
      readMobileSource("src/components/navigation/PrimaryTabBar.tsx"),
      readMobileSource("src/modules/navigation/primary-navigation.ts"),
      readMobileSource("app/(shell)/workbench.tsx"),
      readMobileSource("src/modules/workbench/workbench-screen.tsx"),
      readMobileSource("app/(shell)/attendance-personal.tsx"),
      readMobileSource("app/(shell)/attendance-management.tsx"),
      readMobileSource("app/(shell)/attendance.tsx"),
      readMobileSource("app/(shell)/containers/_layout.tsx"),
      readMobileSource("app/(shell)/users/_layout.tsx"),
    ]);

  assert.match(shellLayout, /initialRouteName:\s*"workbench"/);
  assert.match(shellLayout, /<Stack\b/);
  assert.match(shellLayout, /gestureEnabled:\s*true/);
  assert.doesNotMatch(shellLayout, /\bTabs\b/);
  assert.doesNotMatch(shellLayout, /task-center/);
  assert.match(shellLayout, /PrimaryTabBar/);
  assert.match(shellLayout, /awaitingPreferredDefaultRoute/);
  assert.match(shellLayout, /resolvePreferredDefaultTabRoute/);
  assert.match(shellLayout, /nextPath === TAB_PATHS\.workbench/);
  assert.match(shellLayout, /router\.dismissTo\(nextPath/);
  assert.match(shellLayout, /withAnchor:\s*true/);
  assert.doesNotMatch(shellLayout, /ScrollableTabBar|tab-grouping/);
  assert.match(containerLayout, /gestureEnabled:\s*true/);
  assert.match(userLayout, /gestureEnabled:\s*true/);

  assert.match(primaryTabBar, /buildPrimaryNavigation/);
  assert.match(primaryTabBar, /resolvePrimaryNavigationAction/);
  assert.match(primaryTabBar, /locked/);
  assert.match(primaryTabBar, /accessibilityRole="button"/);
  assert.match(primaryTabBar, /router\.dismissTo/);
  assert.match(primaryTabBar, /router\.navigate/);
  assert.match(
    primaryTabBar,
    /const action = resolvePrimaryNavigationAction[\s\S]*?if \(action === "none"\)[\s\S]*?item\.targetRouteName === "reports"[\s\S]*?markReportHubNavigationStart\(\)[\s\S]*?router\.(?:dismissTo|navigate)/,
    "底栏确实进入报告路由时必须记录一次由活动页签认领的点击起点"
  );
  assert.match(
    primaryTabBar,
    /numberOfLines=\{2\}/,
    "五列窄屏必须允许放大后的一级导航标签换行"
  );
  assert.match(
    primaryTabBar,
    /paddingHorizontal:\s*2/,
    "五列一级导航需要保留足够的标签可用宽度"
  );
  assert.doesNotMatch(
    primaryTabBar,
    /buildNavigationDisplayTabs/,
    "固定五入口不得回退到旧的动态门店折叠导航"
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
  assert.match(primaryNavigation, /key:\s*"reports"/);
  assert.match(primaryNavigation, /key:\s*"me"/);
  assert.match(primaryNavigation, /targetRouteName:\s*"attendance-personal"/);
  assert.match(primaryNavigation, /targetRouteName:\s*"reports"/);
  assert.match(primaryNavigation, /"none"\s*\|\s*"dismiss-to"\s*\|\s*"navigate"/);
  assert.match(primaryNavigation, /resolvePrimaryNavigationAction/);
  assert.match(primaryNavigation, /isDeviceMode/);
  assert.match(primaryNavigation, /locked:/);
  assert.doesNotMatch(primaryNavigation, /task-center/);

  assert.match(workbenchRoute, /<WorkbenchScreen\s*\/>/);
  assert.match(workbenchScreen, /buildWorkbenchSections/);
  assert.match(workbenchScreen, /router\.push/);
  assert.doesNotMatch(
    workbenchScreen,
    /router\.navigate/,
    "工作台功能必须 push 到 Shell Stack，才能由原生手势逐级返回"
  );
  assert.match(attendancePersonal, /<AttendanceScreen\s+mode="personal"/);
  assert.match(attendanceManagement, /<AttendanceScreen\s+mode="management"/);
  assert.match(legacyAttendance, /\/?\(shell\)\/attendance-personal/);
}

void run();
