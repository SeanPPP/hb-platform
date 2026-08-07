import { create } from "zustand";
import { fetchAppNavigationMenu } from "@/modules/navigation/api";
import type { AppNavigationMenuItem } from "@/modules/navigation/types";
import { SETTINGS_FALLBACK_ROUTE_NAME } from "@/modules/navigation/default-route";
import {
  getNavigationMenuRecoveryDelay,
  loadNavigationMenuWithRetry,
} from "@/modules/navigation/menu-loader";
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

let recoveryTimer: ReturnType<typeof setTimeout> | null = null;
let recoveryAttempt = 0;
let requestGeneration = 0;
let activeRequestController: AbortController | null = null;

function cancelScheduledRecovery() {
  if (recoveryTimer) {
    clearTimeout(recoveryTimer);
    recoveryTimer = null;
  }
}

function cancelActiveRequest() {
  activeRequestController?.abort();
  activeRequestController = null;
}

function scheduleNavigationRecovery(expectedGeneration: number) {
  cancelScheduledRecovery();
  const delay = getNavigationMenuRecoveryDelay(recoveryAttempt);
  recoveryAttempt += 1;
  recoveryTimer = setTimeout(() => {
    recoveryTimer = null;
    if (requestGeneration !== expectedGeneration) {
      return;
    }
    void useAppNavigationStore.getState().fetchMenu({ background: true });
  }, delay);
}

interface FetchMenuOptions {
  background?: boolean;
}

interface AppNavigationState {
  items: AppNavigationMenuItem[];
  isLoading: boolean;
  isReady: boolean;
  errorMessage: string | null;
  fetchMenu: (options?: FetchMenuOptions) => Promise<AppNavigationMenuItem[]>;
  replaceMenu: (items: AppNavigationMenuItem[]) => void;
  reset: () => void;
}

export const useAppNavigationStore = create<AppNavigationState>((set, get) => ({
  items: [],
  isLoading: false,
  isReady: false,
  errorMessage: null,

  async fetchMenu(options = {}) {
    const generation = requestGeneration + 1;
    requestGeneration = generation;
    cancelScheduledRecovery();
    cancelActiveRequest();
    const requestController = new AbortController();
    activeRequestController = requestController;
    set(options.background
      ? { errorMessage: null }
      : { isLoading: true, errorMessage: null });
    const { items: nextItems, error } = await loadNavigationMenuWithRetry({
      load: () => fetchAppNavigationMenu(requestController.signal),
      fallbackItems: SETTINGS_ONLY_MENU,
      // 每次最终降级前读取最新状态，保留并发请求已经取得的完整菜单。
      getCurrentItems: () => get().items,
      isCancelled: () =>
        requestController.signal.aborted || requestGeneration !== generation,
    });
    if (activeRequestController === requestController) {
      activeRequestController = null;
    }

    // 登出、账号切换或 Review 菜单替换后，旧请求不得再写回或重启恢复任务。
    if (requestGeneration !== generation) {
      return get().items;
    }

    const errorMessage = error === null
      ? null
      : error instanceof Error
        ? error.message
        : i18n.t("common:errors.requestFailed");
    const hasUsableCurrentMenu = nextItems.some(
      (item) => item.routeName !== SETTINGS_FALLBACK_ROUTE_NAME
    );

    if (error !== null) {
      console.warn("[app-navigation] failed to load app menu", { error });
    }

    if (error !== null && !hasUsableCurrentMenu) {
      // 登录或恢复期间的短暂网络失败不应把本次会话永久锁死在设置页。
      scheduleNavigationRecovery(generation);
    } else {
      recoveryAttempt = 0;
    }

    set({
      items: nextItems,
      isLoading: false,
      isReady: true,
      errorMessage,
    });
    return nextItems;
  },

  replaceMenu(items) {
    requestGeneration += 1;
    cancelScheduledRecovery();
    cancelActiveRequest();
    recoveryAttempt = 0;
    set({
      items,
      isLoading: false,
      isReady: true,
      errorMessage: null,
    });
  },

  reset() {
    requestGeneration += 1;
    cancelScheduledRecovery();
    cancelActiveRequest();
    recoveryAttempt = 0;
    set({
      items: [],
      isLoading: false,
      isReady: false,
      errorMessage: null,
    });
  },
}));
