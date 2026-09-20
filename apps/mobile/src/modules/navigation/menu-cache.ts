/**
 * 底部菜单的本地缓存（仅设备注册绑定会话使用）。
 *
 * 冷启动断网时 /navigation/app-menu 不可达，若没有缓存只能回退到「仅设置页」，
 * 商品查询 tab 就不可见。缓存按 scopeKey（设备 hardwareId + 账号 GUID）隔离，
 * 避免换绑或换账号后拿到别人的菜单。
 */
import { AppAsyncStorage } from "@/shared/storage/async-storage";
import type { AppNavigationMenuItem } from "./types";

export const NAVIGATION_MENU_CACHE_KEY = "@app-navigation/menu/v1";

export interface CachedNavigationMenu {
  scopeKey: string;
  items: AppNavigationMenuItem[];
  savedAtIso: string;
}

export interface NavigationMenuCacheStorage {
  load(): Promise<CachedNavigationMenu | null>;
  save(value: CachedNavigationMenu): Promise<void>;
  clear(): Promise<void>;
}

export const asyncStorageNavigationMenuCache: NavigationMenuCacheStorage = {
  load: () => AppAsyncStorage.getObject<CachedNavigationMenu>(NAVIGATION_MENU_CACHE_KEY),
  save: (value) => AppAsyncStorage.setObject(NAVIGATION_MENU_CACHE_KEY, value),
  clear: () => AppAsyncStorage.removeItem(NAVIGATION_MENU_CACHE_KEY),
};

export function buildNavigationMenuScopeKey(input: {
  sessionKind: string | null | undefined;
  hardwareId: string | null | undefined;
  userGuid: string | null | undefined;
}): string | null {
  if (input.sessionKind !== "device" && input.sessionKind !== "deviceAccount") {
    return null;
  }
  const hardwareId = input.hardwareId?.trim();
  if (!hardwareId) {
    return null;
  }
  return input.sessionKind === "deviceAccount"
    ? `deviceAccount:${hardwareId}:${input.userGuid?.trim() ?? ""}`
    : `device:${hardwareId}`;
}

/** 仅当缓存存在、scopeKey 匹配且含可用菜单时返回缓存菜单。 */
export function resolveCachedNavigationMenu(
  cached: CachedNavigationMenu | null,
  scopeKey: string | null,
): AppNavigationMenuItem[] | null {
  if (!cached || !scopeKey || cached.scopeKey !== scopeKey) {
    return null;
  }
  if (!Array.isArray(cached.items) || cached.items.length === 0) {
    return null;
  }
  return cached.items;
}
