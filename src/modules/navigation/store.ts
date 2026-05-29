import { create } from "zustand";
import { fetchAppNavigationMenu } from "@/modules/navigation/api";
import type { AppNavigationMenuItem } from "@/modules/navigation/types";
import { SETTINGS_FALLBACK_ROUTE_NAME } from "@/modules/navigation/default-route";
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

export const useAppNavigationStore = create<AppNavigationState>((set) => ({
  items: [],
  isLoading: false,
  isReady: false,
  errorMessage: null,

  async fetchMenu() {
    set({ isLoading: true, errorMessage: null });
    try {
      const items = await fetchAppNavigationMenu();
      const nextItems = items.length ? items : SETTINGS_ONLY_MENU;
      set({
        items: nextItems,
        isLoading: false,
        isReady: true,
        errorMessage: null,
      });
      return nextItems;
    } catch (error) {
      const errorMessage = error instanceof Error
        ? error.message
        : i18n.t("common:errors.requestFailed");
      console.warn("[app-navigation] failed to load app menu", { error });
      set({
        items: SETTINGS_ONLY_MENU,
        isLoading: false,
        isReady: true,
        errorMessage,
      });
      return SETTINGS_ONLY_MENU;
    }
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
