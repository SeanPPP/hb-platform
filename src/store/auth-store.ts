import { create } from "zustand";
import type { CurrentUser, AccessControl } from "@/modules/auth/types";
import { buildAccess } from "@/shared/utils/access";
import { AppAsyncStorage } from "@/shared/storage/async-storage";
import { SecureStorage } from "@/shared/storage/secure";
import { hashPassword } from "@/shared/utils/password";
import {
  loginApi,
  getCurrentUserApi,
  logoutApi,
} from "@/modules/auth/api";
import { STORE_SELECTION_STORAGE_KEY } from "@/modules/shop/types";
import { useCartStore } from "@/store/cart-store";

interface AuthState {
  user: CurrentUser | null;
  access: AccessControl;
  isAuthenticated: boolean;
  isLoading: boolean;
  login: (payload: { username: string; password: string }) => Promise<void>;
  logout: () => Promise<void>;
  restoreSession: () => Promise<boolean>;
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
        username: payload.username,
        password: hashPassword(payload.password),
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
      await SecureStorage.clearAll();
      await AppAsyncStorage.removeItem(STORE_SELECTION_STORAGE_KEY);
      useCartStore.getState().reset();
      set({
        user: null,
        access: buildAccess(null),
        isAuthenticated: false,
      });
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
      return true;
    } catch {
      await SecureStorage.clearAll();
      await AppAsyncStorage.removeItem(STORE_SELECTION_STORAGE_KEY);
      useCartStore.getState().reset();
      set({
        user: null,
        access: buildAccess(null),
        isAuthenticated: false,
        isLoading: false,
      });
      return false;
    }
  },
}));
