import { apiClient } from "@/shared/api/client";
import type { AppNavigationMenuItem } from "@/modules/navigation/types";

function normalizeAppNavigationMenu(payload: unknown): AppNavigationMenuItem[] {
  if (!Array.isArray(payload)) {
    return [];
  }

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
    .filter((item) => Boolean(item.routeName))
    .sort((left, right) => left.order - right.order);
}

export async function fetchAppNavigationMenu(): Promise<AppNavigationMenuItem[]> {
  const response = await apiClient.get("/navigation/app-menu");
  return normalizeAppNavigationMenu(response.data);
}
