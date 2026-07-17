import { create } from "zustand";
import { fetchAppNavigationMenu } from "@/modules/navigation/api";
import type { AppNavigationMenuItem } from "@/modules/navigation/types";
import { SETTINGS_FALLBACK_ROUTE_NAME } from "@/modules/navigation/default-route";
import { loadNavigationMenuWithRetry } from "@/modules/navigation/menu-loader";
import { i18n } from "@/shared/i18n/i18n";

const SETTINGS_ONLY_MENU: AppNavigationMenuItem[] = [
  {
    routeName: SETTINGS_FALLBACK_ROUTE_NAME,
    titleKey: "tabs.settings",
    icon: "account-circle-outline",
    permission: null,
    order: 60,
  },
];

interface AppNavigationState {
  items: AppNavigationMenuItem[];
  isLoading: boolean;
  isReady: boolean;
  errorMessage: string | null;
  fetchMenu: () => Promise<AppNavigationMenuItem[]>;
  reset: () => void;
}

export const useAppNavigationStore = create<AppNavigationState>((set, get) => ({
  items: [],
  isLoading: false,
  isReady: false,
  errorMessage: null,

  async fetchMenu() {
    set({ isLoading: true, errorMessage: null });
    const { items: nextItems, error } = await loadNavigationMenuWithRetry({
      load: fetchAppNavigationMenu,
      fallbackItems: SETTINGS_ONLY_MENU,
      // 每次最终降级前读取最新状态，保留并发请求已经取得的完整菜单。
      getCurrentItems: () => get().items,
    });
    const errorMessage = error === null
      ? null
      : error instanceof Error
        ? error.message
        : i18n.t("common:errors.requestFailed");

    if (error !== null) {
      console.warn("[app-navigation] failed to load app menu", { error });
    }

    set({
      items: nextItems,
      isLoading: false,
      isReady: true,
      errorMessage,
    });
    return nextItems;
  },

  reset() {
    set({
      items: [],
      isLoading: false,
      isReady: false,
      errorMessage: null,
    });
  },
}));
