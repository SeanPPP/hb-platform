import { create } from "zustand";
import Constants from "expo-constants";
import { Platform } from "react-native";
import type { CurrentUser, AccessControl, LoginRequest } from "@/modules/auth/types";
import { buildAccess } from "@/shared/utils/access";
import { AppAsyncStorage } from "@/shared/storage/async-storage";
import { SecureStorage } from "@/shared/storage/secure";
import { queryClient } from "@/shared/api/query-client";
import { clearSensitiveQueryCache } from "@/modules/auth/sensitive-query-cache";
import { subscribeUnauthenticatedSession } from "@/modules/auth/auth-session-events";
import { stopAttendanceLocationTracking } from "@/modules/attendance/location-tracking-control";
import {
  loginApi,
  getCurrentUserApi,
  logoutApi,
} from "@/modules/auth/api";
import { STORE_SELECTION_STORAGE_KEY } from "@/modules/shop/types";
import { useCartStore } from "@/store/cart-store";
import { useAppNavigationStore } from "@/modules/navigation/store";
import { DeviceStorage } from "@/modules/device/storage";
import { useDeviceStore } from "@/store/device-store";
import {
  isIosReviewBuildEnabled,
  isIosReviewUsername,
  tryAuthenticateIosReview,
  type IosReviewBuildContext,
} from "@/modules/ios-review/config";
import {
  beginStandardAuthentication,
  clearIosReviewSession,
  configureIosReviewBuildGate,
  isIosReviewSessionActive,
  restoreIosReviewSession,
  setIosReviewSessionActive,
  type AuthSessionKind,
} from "@/modules/ios-review/session";
import { resetReviewData } from "@/modules/ios-review/data-store";
import { createIosReviewUser } from "@/modules/ios-review/identity";
import { IOS_REVIEW_MENU_ITEMS } from "@/modules/ios-review/menu";

export function getIosReviewBuildContext(): IosReviewBuildContext {
  return {
    platform: Platform.OS,
    buildProfile: Constants.expoConfig?.extra?.nativeAppBuildProfile,
    enabled: process.env.EXPO_PUBLIC_IOS_REVIEW_MODE_ENABLED,
    passwordSha256: process.env.EXPO_PUBLIC_IOS_REVIEW_PASSWORD_SHA256,
  };
}

const iosReviewBuildEnabled = isIosReviewBuildEnabled(
  getIosReviewBuildContext()
);
// 模块加载即建立同步 pre-auth 守卫，所有 React effect 和 API 请求都只能在其后运行。
configureIosReviewBuildGate(iosReviewBuildEnabled);
let clearLocalSessionInFlight: Promise<void> | null = null;

export async function waitForLocalSessionClear() {
  // 清理期间不允许新认证写入 token/marker，避免旧 finally 删除刚建立的会话。
  while (clearLocalSessionInFlight) {
    await clearLocalSessionInFlight;
  }
}

function setIosReviewMenu() {
  useAppNavigationStore.setState({
    items: [...IOS_REVIEW_MENU_ITEMS],
    isLoading: false,
    isReady: true,
    errorMessage: null,
  });
}

function createInvalidIosReviewCredentialsError() {
  const error = new Error("Invalid username or password") as Error & {
    code?: string;
  };
  error.code = "IOS_REVIEW_INVALID_CREDENTIALS";
  return error;
}

interface AuthState {
  user: CurrentUser | null;
  access: AccessControl;
  sessionKind: AuthSessionKind;
  iosReviewOfflineGuardActive: boolean;
  standardAuthIntent: "account" | "device" | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  login: (payload: LoginRequest) => Promise<void>;
  logout: () => Promise<void>;
  hydrateIosReviewSession: () => Promise<boolean>;
  restoreSession: () => Promise<boolean>;
  clearLocalSession: () => Promise<void>;
  clearAccountSessionForDeviceLogin: () => Promise<void>;
  beginStandardAuth: (kind?: "account" | "device") => void;
  rearmIosReviewPreAuth: () => void;
  performLocalSessionClear: () => Promise<void>;
  setSessionKind: (kind: AuthSessionKind) => void;
}

export const useAuthStore = create<AuthState>((set, get) => ({
  user: null,
  access: buildAccess(null),
  sessionKind: "account",
  iosReviewOfflineGuardActive: isIosReviewSessionActive(),
  standardAuthIntent: null,
  isAuthenticated: false,
  isLoading: false,

  async login(payload) {
    const isReviewUsername = isIosReviewUsername(payload.username);
    if (isReviewUsername) {
      // 必须在第一个 await 前同步恢复 fail-closed；设备模式残留不能放行审核凭据。
      get().rearmIosReviewPreAuth();
    }
    await waitForLocalSessionClear();
    if (isReviewUsername) {
      // 在途清理可能于等待期间改变 gate，认证判断前再次同步隔离。
      get().rearmIosReviewPreAuth();
    }
    // 关键位置：审核账号必须在真实认证请求之前完成本地分流。
    const reviewAuthentication = tryAuthenticateIosReview({
      username: payload.username,
      password: payload.password,
      buildContext: getIosReviewBuildContext(),
    });
    if (reviewAuthentication.status === "invalid-password") {
      set({ isLoading: false });
      throw createInvalidIosReviewCredentialsError();
    }
    if (reviewAuthentication.status === "authenticated") {
      set({ isLoading: true });
      // 仅清审核页面可见的设备态，保留 DeviceStorage 中真实设备绑定。
      useDeviceStore.setState({
        session: null,
        isReady: true,
        isLoading: false,
      });
      try {
        // 切入离线审核会话前先停止普通账号可能遗留的后台定位任务。
        await stopAttendanceLocationTracking({ force: true }).catch((error) => {
          console.warn("[ios-review] 停止遗留后台定位失败", error);
        });
        // 审核会话只保留独立 marker，先清除可能残留的真实账号令牌。
        await SecureStorage.clearAll();
        queryClient.clear();
        useCartStore.getState().reset();
        resetReviewData();
        await setIosReviewSessionActive();
        const user = createIosReviewUser();
        setIosReviewMenu();
        set({
          user,
          access: buildAccess(user),
          sessionKind: "iosReview",
          iosReviewOfflineGuardActive: true,
          standardAuthIntent: null,
          isAuthenticated: true,
          isLoading: false,
        });
        return;
      } catch (error) {
        await clearIosReviewSession().catch(() => undefined);
        set({ isLoading: false });
        throw error;
      }
    }

    get().beginStandardAuth();
    set({ isLoading: true });
    try {
      const tokenRes = await loginApi({
        ...payload,
        // 登录密码输入框容易混入首尾空格，提交前统一按用户实际输入意图归一化。
        password: payload.password.trim(),
        passwordFormat: "raw",
      });
      await SecureStorage.setToken(tokenRes.accessToken);
      await SecureStorage.setRefreshToken(tokenRes.refreshToken);

      const user = await getCurrentUserApi();
      await SecureStorage.setUser(user);

      set({
        user,
        access: buildAccess(user),
        sessionKind: "account",
        iosReviewOfflineGuardActive: false,
        standardAuthIntent: null,
        isAuthenticated: true,
        isLoading: false,
      });
      await useAppNavigationStore.getState().fetchMenu();
    } catch (error) {
      await SecureStorage.clearAll().catch(() => undefined);
      get().rearmIosReviewPreAuth();
      set({
        user: null,
        access: buildAccess(null),
        sessionKind: "account",
        isAuthenticated: false,
        isLoading: false,
      });
      throw error;
    }
  },

  async logout() {
    if (get().sessionKind === "iosReview") {
      await get().clearLocalSession();
      return;
    }

    try {
      const rt = await SecureStorage.getRefreshToken();
      if (rt) {
        await logoutApi(rt);
      }
    } finally {
      await get().clearLocalSession();
    }
  },

  async hydrateIosReviewSession() {
    const buildContext = getIosReviewBuildContext();
    const buildEnabled = isIosReviewBuildEnabled(buildContext);
    configureIosReviewBuildGate(buildEnabled);
    set({ iosReviewOfflineGuardActive: isIosReviewSessionActive() });
    if (!buildEnabled) {
      // 非目标构建主动删除 marker，避免审核会话跨构建配置泄漏。
      await clearIosReviewSession().catch((error) => {
        console.warn("[ios-review] 清理非目标构建 marker 失败", error);
      });
      set({ iosReviewOfflineGuardActive: false });
      return false;
    }

    const restored = await restoreIosReviewSession();
    if (!restored) {
      // 仅本地检查既有普通 token/device；存在候选会话时才允许原恢复链路触网。
      const [storedToken, storedDevice] = await Promise.all([
        SecureStorage.getToken().catch(() => null),
        DeviceStorage.getSession().catch(() => null),
      ]);
      const hasStoredDeviceSession = Boolean(
        storedDevice?.hardwareId && storedDevice.authCode && storedDevice.storeCode
      );
      if (storedToken || hasStoredDeviceSession) {
        get().beginStandardAuth(
          hasStoredDeviceSession ? "device" : "account"
        );
      } else {
        set({ iosReviewOfflineGuardActive: true });
      }
      set({ isLoading: false });
      return false;
    }

    // marker 已确认后立即标记审核会话，后续任一步失败都会走 fail-closed 清理分支。
    set({
      sessionKind: "iosReview",
      iosReviewOfflineGuardActive: true,
      standardAuthIntent: null,
    });
    // marker 恢复后 active 已为 true，因此这里显式 force 清理上次普通会话遗留任务。
    await stopAttendanceLocationTracking({ force: true }).catch((error) => {
      console.warn("[ios-review] 恢复会话时停止遗留后台定位失败", error);
    });
    await SecureStorage.clearAll();
    const user = createIosReviewUser();
    setIosReviewMenu();
    set({
      user,
      access: buildAccess(user),
      sessionKind: "iosReview",
      iosReviewOfflineGuardActive: true,
      standardAuthIntent: null,
      isAuthenticated: true,
      isLoading: false,
    });
    return true;
  },

  async restoreSession() {
    await waitForLocalSessionClear();
    set({ isLoading: true });
    try {
      if (await get().hydrateIosReviewSession()) {
        return true;
      }
      if (isIosReviewSessionActive()) {
        set({ isLoading: false });
        return false;
      }

      const token = await SecureStorage.getToken();
      if (!token) {
        // 已保存设备验证失败后可能已放行全局 gate；无账号可恢复时必须重新 fail-closed。
        get().rearmIosReviewPreAuth();
        set({ isLoading: false });
        return false;
      }

      const user = await getCurrentUserApi();
      await SecureStorage.setUser(user);

      set({
        user,
        access: buildAccess(user),
        sessionKind: "account",
        iosReviewOfflineGuardActive: false,
        standardAuthIntent: null,
        isAuthenticated: true,
        isLoading: false,
      });
      await useAppNavigationStore.getState().fetchMenu();
      return true;
    } catch {
      await get().clearLocalSession();
      return false;
    }
  },

  async clearLocalSession() {
    if (clearLocalSessionInFlight) {
      return clearLocalSessionInFlight;
    }

    const clearOperation = get().performLocalSessionClear();
    clearLocalSessionInFlight = clearOperation;
    try {
      await clearOperation;
    } finally {
      if (clearLocalSessionInFlight === clearOperation) {
        clearLocalSessionInFlight = null;
      }
    }
  },

  async performLocalSessionClear() {
    const wasIosReview = get().sessionKind === "iosReview";
    const preservesStoredDeviceAuthentication =
      !wasIosReview && get().standardAuthIntent === "device";
    if (preservesStoredDeviceAuthentication) {
      set({ standardAuthIntent: null });
    } else {
      get().rearmIosReviewPreAuth();
    }

    // 先同步清空查询与认证 UI，整个异步清理窗口都保持 fail-closed。
    if (wasIosReview) {
      queryClient.clear();
    } else {
      clearSensitiveQueryCache(queryClient);
    }
    useCartStore.getState().reset();
    useAppNavigationStore.getState().reset();
    set({
      user: null,
      access: buildAccess(null),
      sessionKind: preservesStoredDeviceAuthentication ? "device" : "account",
      iosReviewOfflineGuardActive:
        iosReviewBuildEnabled || isIosReviewSessionActive(),
      standardAuthIntent: null,
      isAuthenticated: false,
      isLoading: false,
    });

    await stopAttendanceLocationTracking(
      wasIosReview ? { force: true } : undefined
    ).catch((error) => {
      console.warn("[attendance-location] 停止后台定位失败", error);
    });

    try {
      await clearIosReviewSession();
    } catch (error) {
      console.warn("[ios-review] 删除审核会话 marker 失败", error);
    } finally {
      // marker 清理失败也不能阻断真实账号令牌和本地认证残留清理。
      await SecureStorage.clearAll().catch((error) => {
        console.warn("[auth] 清理真实账号令牌失败", error);
      });
      await AppAsyncStorage.removeItem(STORE_SELECTION_STORAGE_KEY).catch(
        (error) => {
          console.warn("[auth] 清理门店选择失败", error);
        }
      );
      if (wasIosReview) {
        // 只清审核态的内存展示，保留用户原有真实设备绑定供普通设备登录恢复。
        useDeviceStore.setState({
          session: null,
          isReady: true,
          isLoading: false,
        });
      }
    }

    if (preservesStoredDeviceAuthentication) {
      // 清理审核 marker 会重新启用 pre-auth 守卫；设备会话恢复前必须再次显式放行。
      beginStandardAuthentication();
      set({
        sessionKind: "device",
        iosReviewOfflineGuardActive:
          iosReviewBuildEnabled || isIosReviewSessionActive(),
        standardAuthIntent: null,
      });
    }
  },

  async clearAccountSessionForDeviceLogin() {
    await waitForLocalSessionClear();
    // 专用于设备登录切换：清账号 token，但不重置审核 pre-auth gate 或持久设备绑定。
    clearSensitiveQueryCache(queryClient);
    useCartStore.getState().reset();
    useAppNavigationStore.getState().reset();
    await SecureStorage.clearAll();
    await AppAsyncStorage.removeItem(STORE_SELECTION_STORAGE_KEY);
    set({
      user: null,
      access: buildAccess(null),
      sessionKind: "device",
      iosReviewOfflineGuardActive:
        iosReviewBuildEnabled || isIosReviewSessionActive(),
      standardAuthIntent: null,
      isAuthenticated: false,
      isLoading: false,
    });
  },

  beginStandardAuth(kind = "account") {
    beginStandardAuthentication();
    set({
      // review 构建只放行显式认证请求；Root 副作用等待账号或设备真正认证成功。
      iosReviewOfflineGuardActive:
        iosReviewBuildEnabled || isIosReviewSessionActive(),
      standardAuthIntent: kind,
    });
  },

  rearmIosReviewPreAuth() {
    configureIosReviewBuildGate(iosReviewBuildEnabled);
    set({
      iosReviewOfflineGuardActive: isIosReviewSessionActive(),
      standardAuthIntent: null,
    });
  },

  setSessionKind(kind) {
    set({
      sessionKind: kind,
      ...(kind === "device"
        ? {
            iosReviewOfflineGuardActive: false,
            standardAuthIntent: null,
          }
        : {}),
    });
  },
}));

subscribeUnauthenticatedSession(() => {
  void useAuthStore.getState().clearLocalSession();
});
