import { SUPPORTED_APP_MENU_ROUTE_NAMES } from "./default-route";
import type { AppNavigationMenuItem } from "./types";

export function normalizeAppNavigationMenu(payload: unknown): AppNavigationMenuItem[] {
  if (!Array.isArray(payload)) {
    return [];
  }

  const seenRouteNames = new Set<string>();

  return payload
    .filter((item): item is Record<string, unknown> => Boolean(item) && typeof item === "object")
    .map((item) => ({
      routeName:
        (typeof item.routeName === "string" && item.routeName) ||
        (typeof item.RouteName === "string" && item.RouteName) ||
        "",
      titleKey:
        (typeof item.titleKey === "string" && item.titleKey) ||
        (typeof item.TitleKey === "string" && item.TitleKey) ||
        "",
      icon:
        (typeof item.icon === "string" && item.icon) ||
        (typeof item.Icon === "string" && item.Icon) ||
        "",
      permission:
        (typeof item.permission === "string" && item.permission) ||
        (typeof item.Permission === "string" && item.Permission) ||
        null,
      order: Number(item.order ?? item.Order ?? 0),
    }))
    .filter((item) => {
      if (
        !item.routeName ||
        !SUPPORTED_APP_MENU_ROUTE_NAMES.has(item.routeName) ||
        seenRouteNames.has(item.routeName)
      ) {
        return false;
      }

      seenRouteNames.add(item.routeName);
      return true;
    })
    .sort((left, right) => left.order - right.order);
}

export async function fetchAppNavigationMenu(): Promise<AppNavigationMenuItem[]> {
  const { apiClient } = await import("@/shared/api/client");
  const response = await apiClient.get("/navigation/app-menu");
  return normalizeAppNavigationMenu(response.data);
}
