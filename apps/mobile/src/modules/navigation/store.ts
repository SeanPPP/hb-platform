import { create } from "zustand";
import { fetchAppNavigationMenu } from "@/modules/navigation/api";
import type { AppNavigationMenuItem } from "@/modules/navigation/types";
import { SETTINGS_FALLBACK_ROUTE_NAME } from "@/modules/navigation/default-route";
import {
  getNavigationMenuRecoveryDelay,
  loadNavigationMenuWithRetry,
} from "@/modules/navigation/menu-loader";
import {
  asyncStorageNavigationMenuCache,
  buildNavigationMenuScopeKey,
  resolveCachedNavigationMenu,
} from "@/modules/navigation/menu-cache";
import { i18n } from "@/shared/i18n/i18n";

/**
 * 从已落盘的会话标记与设备会话推导缓存作用域。
 *
 * 设备绑定账号登录会在 sessionKind / user 落到内存之前就把菜单拉完，那一刻按内存态
 * 根本算不出 key，导致读不到也写不进缓存。而此时 marker 与设备会话早已写盘
 * （markDeviceAccountSession / saveCurrentUser 都排在 loadNavigationMenu 之前），
 * 所以落盘态是这个阶段唯一可靠的来源。
 */
async function resolvePersistedNavigationMenuScopeKey(): Promise<string | null> {
  const [{ getAuthSessionMarker }, { DeviceStorage }, { SecureStorage }] = await Promise.all([
    import("@/modules/device-activation/auth-session-marker"),
    import("@/modules/device/storage"),
    import("@/shared/storage/secure"),
  ]);
  const [marker, session] = await Promise.all([
    getAuthSessionMarker().catch(() => null),
    DeviceStorage.getSession().catch(() => null),
  ]);
  if (marker === "account") {
    // 普通账号登录不参与离线，也不写缓存。
    return null;
  }
  const hardwareId = session?.hardwareId;
  if (!hardwareId) {
    return null;
  }
  if (marker === "deviceAccount") {
    const user = await SecureStorage.getUser<{ userGUID?: string }>().catch(() => null);
    return buildNavigationMenuScopeKey({
      sessionKind: "deviceAccount",
      hardwareId,
      userGuid: user?.userGUID,
    });
  }
  // 无 marker + 有可用设备会话 = 纯设备模式。
  return buildNavigationMenuScopeKey({ sessionKind: "device", hardwareId, userGuid: null });
}

/**
 * 计算菜单缓存作用域：仅设备注册绑定会话（device / deviceAccount）参与缓存。
 * auth-store / device-store 反向依赖本模块，故在调用时动态加载，避免循环导入。
 * 内存态未落定时回退到落盘态，保证读缓存与写缓存始终用同一个 key。
 */
async function resolveNavigationMenuScopeKey(): Promise<string | null> {
  try {
    const [{ useAuthStore }, { useDeviceStore }] = await Promise.all([
      import("@/store/auth-store"),
      import("@/store/device-store"),
    ]);
    const auth = useAuthStore.getState();
    const device = useDeviceStore.getState().session;
    const fromMemory = buildNavigationMenuScopeKey({
      sessionKind: auth.sessionKind,
      hardwareId: device?.hardwareId,
      userGuid: auth.user?.userGUID,
    });
    if (fromMemory) {
      return fromMemory;
    }
    return await resolvePersistedNavigationMenuScopeKey();
  } catch {
    return null;
  }
}

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

interface ResetMenuOptions {
  /**
   * 同一设备同一绑定重建会话（设备绑定账号登录 / 恢复）时保留菜单缓存。
   * 登出、解绑、换账号仍必须清掉，避免下个会话拿到上个会话的菜单。
   */
  keepMenuCache?: boolean;
}

interface AppNavigationState {
  items: AppNavigationMenuItem[];
  isLoading: boolean;
  isReady: boolean;
  errorMessage: string | null;
  /**
   * 当前 items 是否来自本地缓存兜底（菜单请求失败，但设备类会话读到了上次的菜单）。
   * 这种情况下菜单是可用的，不算错误；后台恢复重试仍在进行。
   */
  servedFromCache: boolean;
  fetchMenu: (options?: FetchMenuOptions) => Promise<AppNavigationMenuItem[]>;
  replaceMenu: (items: AppNavigationMenuItem[]) => void;
  reset: (options?: ResetMenuOptions) => void;
}

export const useAppNavigationStore = create<AppNavigationState>((set, get) => ({
  items: [],
  isLoading: false,
  isReady: false,
  errorMessage: null,
  servedFromCache: false,

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
    // 设备类会话：断网时用上次成功的菜单作为回退，而不是只剩设置页。
    const cacheScopeKey = await resolveNavigationMenuScopeKey();
    const cachedItems = cacheScopeKey
      ? resolveCachedNavigationMenu(
          await asyncStorageNavigationMenuCache.load().catch(() => null),
          cacheScopeKey
        )
      : null;
    const { items: nextItems, error } = await loadNavigationMenuWithRetry({
      load: () => fetchAppNavigationMenu(requestController.signal),
      fallbackItems: cachedItems ?? SETTINGS_ONLY_MENU,
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
    const servedFromCache = error !== null && cachedItems !== null && nextItems === cachedItems;

    if (error !== null) {
      console.warn("[app-navigation] failed to load app menu", { error, servedFromCache });
    }

    if (error === null && cacheScopeKey && hasUsableCurrentMenu) {
      void asyncStorageNavigationMenuCache
        .save({ scopeKey: cacheScopeKey, items: nextItems, savedAtIso: new Date().toISOString() })
        .catch(() => undefined);
    }

    if (error !== null && (!hasUsableCurrentMenu || servedFromCache)) {
      // 登录或恢复期间的短暂网络失败不应把本次会话永久锁死在设置页；
      // 缓存菜单只是临时回退，联网后仍要刷新为服务端最新菜单。
      scheduleNavigationRecovery(generation);
    } else {
      recoveryAttempt = 0;
    }

    set({
      items: nextItems,
      isLoading: false,
      isReady: true,
      // 缓存兜底时菜单是可用的，不能报错：工作台会因为 errorMessage 非空而清空整个
      // 功能列表并盖上「功能菜单加载失败」，而底部 tab 却用缓存菜单正常显示，两处
      // 自相矛盾。后台重试已由 scheduleNavigationRecovery 负责，用户无需处理。
      errorMessage: servedFromCache ? null : errorMessage,
      servedFromCache,
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
      servedFromCache: false,
    });
  },

  reset(options = {}) {
    requestGeneration += 1;
    cancelScheduledRecovery();
    cancelActiveRequest();
    recoveryAttempt = 0;
    if (!options.keepMenuCache) {
      // 登出/解绑/换账号时清掉菜单缓存，避免下个会话拿到上个会话的菜单。
      void asyncStorageNavigationMenuCache.clear().catch(() => undefined);
    }
    set({
      items: [],
      isLoading: false,
      isReady: false,
      errorMessage: null,
      servedFromCache: false,
    });
  },
}));
