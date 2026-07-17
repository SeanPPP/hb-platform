import assert from "node:assert/strict";
import { loadNavigationMenuWithRetry } from "./menu-loader";
import type { AppNavigationMenuItem } from "./types";

const settingsOnlyMenu: AppNavigationMenuItem[] = [
  {
    routeName: "settings",
    titleKey: "tabs.settings",
    icon: "account-circle-outline",
    permission: null,
    order: 60,
  },
];

const fullMenu: AppNavigationMenuItem[] = [
  {
    routeName: "reports",
    titleKey: "tabs.reports",
    icon: "chart-line",
    permission: "Reports.View",
    order: 20,
  },
  ...settingsOnlyMenu,
];

async function main() {
  let thrownAttempt = 0;
  const recoveredFromError = await loadNavigationMenuWithRetry({
    load: async () => {
      thrownAttempt += 1;
      if (thrownAttempt === 1) {
        throw new Error("temporary failure");
      }
      return fullMenu;
    },
    fallbackItems: settingsOnlyMenu,
    getCurrentItems: () => [],
    delay: async () => undefined,
  });
  assert.equal(thrownAttempt, 2, "首次异常后应再请求一次菜单");
  assert.equal(recoveredFromError.items, fullMenu, "第二次成功时应返回完整菜单");
  assert.equal(recoveredFromError.error, null, "重试成功后不应保留首次错误");

  let emptyAttempt = 0;
  const recoveredFromEmpty = await loadNavigationMenuWithRetry({
    load: async () => {
      emptyAttempt += 1;
      return emptyAttempt === 1 ? [] : fullMenu;
    },
    fallbackItems: settingsOnlyMenu,
    getCurrentItems: () => [],
    delay: async () => undefined,
  });
  assert.equal(emptyAttempt, 2, "首次返回空菜单后应再请求一次");
  assert.equal(recoveredFromEmpty.items, fullMenu, "空菜单重试成功后应返回完整菜单");

  let currentItems = settingsOnlyMenu;
  const preservedCurrentMenu = await loadNavigationMenuWithRetry({
    load: async () => {
      throw new Error("late failed request");
    },
    fallbackItems: settingsOnlyMenu,
    getCurrentItems: () => currentItems,
    delay: async () => {
      // 模拟并发请求已经成功写入完整菜单。
      currentItems = fullMenu;
    },
  });
  assert.equal(
    preservedCurrentMenu.items,
    fullMenu,
    "较晚失败的请求不能覆盖并发请求写入的完整菜单"
  );

  const finalFallback = await loadNavigationMenuWithRetry({
    load: async () => [],
    fallbackItems: settingsOnlyMenu,
    getCurrentItems: () => [],
    delay: async () => undefined,
  });
  assert.equal(finalFallback.items, settingsOnlyMenu, "最终失败且无旧菜单时应退化为设置页");
}

void main();
