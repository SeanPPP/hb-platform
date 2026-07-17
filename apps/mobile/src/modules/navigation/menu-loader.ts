import type { AppNavigationMenuItem } from "./types";

export interface NavigationMenuLoadResult {
  items: AppNavigationMenuItem[];
  error: unknown | null;
}

export interface NavigationMenuLoadOptions {
  load: () => Promise<AppNavigationMenuItem[]>;
  fallbackItems: AppNavigationMenuItem[];
  getCurrentItems: () => AppNavigationMenuItem[];
  delay?: (milliseconds: number) => Promise<void>;
  retryDelayMs?: number;
}

const MAX_LOAD_ATTEMPTS = 2;
const DEFAULT_RETRY_DELAY_MS = 150;

function wait(milliseconds: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

function hasNonFallbackItem(
  items: AppNavigationMenuItem[],
  fallbackItems: AppNavigationMenuItem[]
): boolean {
  const fallbackRouteNames = new Set(fallbackItems.map((item) => item.routeName));
  return items.some((item) => !fallbackRouteNames.has(item.routeName));
}

export async function loadNavigationMenuWithRetry({
  load,
  fallbackItems,
  getCurrentItems,
  delay = wait,
  retryDelayMs = DEFAULT_RETRY_DELAY_MS,
}: NavigationMenuLoadOptions): Promise<NavigationMenuLoadResult> {
  let lastError: unknown | null = null;

  for (let attempt = 0; attempt < MAX_LOAD_ATTEMPTS; attempt += 1) {
    try {
      const items = await load();
      if (items.length > 0) {
        return { items, error: null };
      }
    } catch (error) {
      lastError = error;
    }

    if (attempt < MAX_LOAD_ATTEMPTS - 1) {
      await delay(retryDelayMs);
    }
  }

  // 必须在所有尝试结束后读取，避免较晚失败的并发请求覆盖已成功写入的完整菜单。
  const currentItems = getCurrentItems();
  return {
    items: hasNonFallbackItem(currentItems, fallbackItems) ? currentItems : fallbackItems,
    error: lastError,
  };
}
