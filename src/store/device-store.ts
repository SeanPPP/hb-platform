import { create } from "zustand";
import { Platform } from "react-native";
import {
  getDeviceProfileApi,
  registerDeviceApi,
  validateDeviceAuthApi,
} from "@/modules/device/api";
import { DeviceStorage } from "@/modules/device/storage";
import type { PersistedDeviceSession } from "@/modules/device/types";

interface DeviceState {
  session: PersistedDeviceSession | null;
  isReady: boolean;
  isLoading: boolean;
  hydrate: () => Promise<PersistedDeviceSession | null>;
  register: (payload: { storeCode: string; storeName?: string | null }) => Promise<PersistedDeviceSession>;
  validate: () => Promise<boolean>;
  clear: () => Promise<void>;
}

function getCurrentDeviceSystem() {
  return Platform.OS === "ios" ? "iOS" : "Android";
}

export const useDeviceStore = create<DeviceState>((set, get) => ({
  session: null,
  isReady: false,
  isLoading: false,

  async hydrate() {
    const session = await DeviceStorage.getSession();
    console.info("[device-session] hydrate", {
      hasSession: Boolean(session),
      hardwareId: session?.hardwareId ?? null,
      storeCode: session?.storeCode ?? null,
      status: session?.status ?? null,
    });
    set({ session, isReady: true });
    return session;
  },

  async register(payload) {
    set({ isLoading: true });
    try {
      const hardwareId = await DeviceStorage.getInstallationId();
      const device = await registerDeviceApi({
        hardwareId,
        deviceType: "Mobile",
        deviceSystem: getCurrentDeviceSystem(),
        storeCode: payload.storeCode,
      });

      const session: PersistedDeviceSession = {
        hardwareId,
        authCode: device.authCode,
        storeCode: payload.storeCode,
        storeName: payload.storeName ?? null,
        systemDeviceNumber: device.systemDeviceNumber || null,
        status: device.status,
        statusDescription: device.statusDescription,
        resolvedFromExisting: device.resolvedFromExisting ?? false,
      };

      await DeviceStorage.setSession(session);
      set({ session, isReady: true, isLoading: false });
      return session;
    } catch (error) {
      set({ isLoading: false });
      throw error;
    }
  },

  async validate() {
    const currentSession = get().session ?? (await DeviceStorage.getSession());
    if (!currentSession?.hardwareId || !currentSession.authCode) {
      set({ session: null, isReady: true, isLoading: false });
      return false;
    }

    set({ isLoading: true });

    try {
      const validation = await validateDeviceAuthApi({
        hardwareId: currentSession.hardwareId,
        authCode: currentSession.authCode,
      });

      if (!validation.isValid) {
        try {
          const profile = await getDeviceProfileApi(currentSession.hardwareId);
          const nextSession: PersistedDeviceSession = {
            ...currentSession,
            storeCode: profile.storeCode || currentSession.storeCode,
            systemDeviceNumber:
              profile.systemDeviceNumber || currentSession.systemDeviceNumber || null,
            status: profile.status,
            statusDescription: profile.statusDescription,
            resolvedFromExisting: currentSession.resolvedFromExisting ?? false,
          };
          await DeviceStorage.setSession(nextSession);
          set({ session: nextSession, isReady: true, isLoading: false });
          console.warn("[device-session] validation rejected", {
            hardwareId: currentSession.hardwareId,
            storeCode: nextSession.storeCode,
            status: nextSession.status,
          });
        } catch {
          set({ isLoading: false });
        }
        set({ isLoading: false });
        return false;
      }

      const profile = await getDeviceProfileApi(currentSession.hardwareId);
      const nextSession: PersistedDeviceSession = {
        hardwareId: currentSession.hardwareId,
        authCode: validation.newAuthCode || currentSession.authCode,
        storeCode: profile.storeCode || currentSession.storeCode,
        storeName: currentSession.storeName ?? null,
        systemDeviceNumber: profile.systemDeviceNumber || currentSession.systemDeviceNumber || null,
        status: profile.status,
        statusDescription: profile.statusDescription,
        resolvedFromExisting: currentSession.resolvedFromExisting ?? false,
      };

      await DeviceStorage.setSession(nextSession);
      set({
        session: nextSession,
        isReady: true,
        isLoading: false,
      });
      console.info("[device-session] validation completed", {
        hardwareId: nextSession.hardwareId,
        storeCode: nextSession.storeCode,
        status: nextSession.status,
        granted: profile.status === 1 && Boolean(nextSession.storeCode),
      });

      return profile.status === 1 && Boolean(nextSession.storeCode);
    } catch (error) {
      set({ isLoading: false, isReady: true });
      throw error;
    }
  },

  async clear() {
    await DeviceStorage.clearSession();
    set({ session: null, isReady: true, isLoading: false });
  },
}));
