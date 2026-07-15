import { create } from "zustand";
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

interface AuthState {
  user: CurrentUser | null;
  access: AccessControl;
  isAuthenticated: boolean;
  isLoading: boolean;
  login: (payload: LoginRequest) => Promise<void>;
  logout: () => Promise<void>;
  restoreSession: () => Promise<boolean>;
  clearLocalSession: () => Promise<void>;
}

export const useAuthStore = create<AuthState>((set, get) => ({
  user: null,
  access: buildAccess(null),
  isAuthenticated: false,
  isLoading: false,

  async login(payload) {
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
        isAuthenticated: true,
        isLoading: false,
      });
      await useAppNavigationStore.getState().fetchMenu();
    } catch (error) {
      set({ isLoading: false });
      throw error;
    }
  },

  async logout() {
    try {
      const rt = await SecureStorage.getRefreshToken();
      if (rt) {
        await logoutApi(rt);
      }
    } finally {
      await get().clearLocalSession();
    }
  },

  async restoreSession() {
    set({ isLoading: true });
    try {
      const token = await SecureStorage.getToken();
      if (!token) {
        set({ isLoading: false });
        return false;
      }

      const user = await getCurrentUserApi();
      await SecureStorage.setUser(user);

      set({
        user,
        access: buildAccess(user),
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
    // 关键位置：账号会话失效时同步停止后台定位，防止打卡中的定位任务脱离登录态继续运行。
    await stopAttendanceLocationTracking().catch((error) => {
      console.warn("[attendance-location] 停止后台定位失败", error);
    });
    await SecureStorage.clearAll();
    await AppAsyncStorage.removeItem(STORE_SELECTION_STORAGE_KEY);
    useCartStore.getState().reset();
    useAppNavigationStore.getState().reset();
    // 当前查询缓存可能包含银行资料、私有图片 URL 等敏感数据，退出会话时统一清除。
    clearSensitiveQueryCache(queryClient);
    set({
      user: null,
      access: buildAccess(null),
      isAuthenticated: false,
      isLoading: false,
    });
  },
}));

subscribeUnauthenticatedSession(() => {
  void stopAttendanceLocationTracking().catch((error) => {
    console.warn("[attendance-location] 会话失效时停止后台定位失败", error);
  });
  void AppAsyncStorage.removeItem(STORE_SELECTION_STORAGE_KEY);
  useCartStore.getState().reset();
  useAppNavigationStore.getState().reset();
  clearSensitiveQueryCache(queryClient);
  useAuthStore.setState({
    user: null,
    access: buildAccess(null),
    isAuthenticated: false,
    isLoading: false,
  });
});
