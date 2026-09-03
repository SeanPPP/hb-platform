import { create } from "zustand";
import { Platform } from "react-native";
import {
  getDeviceProfileApi,
  registerDeviceApi,
  unbindDeviceApi,
  validateDeviceAuthApi,
} from "@/modules/device/api";
import { stopAttendanceLocationTracking } from "@/modules/attendance/location-tracking-control";
import { collectLoginDeviceLocation } from "@/modules/attendance/required-location";
import { DeviceStorage } from "@/modules/device/storage";
import { DeviceAccountStorage } from "@/modules/device-activation/device-account-storage-runtime";
import {
  exchangeMobileDeviceSessionApi,
  unbindMobileDeviceAccountApi,
} from "@/modules/device-activation/device-activation-api";
import type { StoredMobileDeviceAccountBinding } from "@/modules/device-activation/types";
import type {
  DeviceProfile,
  DeviceValidationRequest,
  PersistedDeviceSession,
} from "@/modules/device/types";
import { useAppNavigationStore } from "@/modules/navigation/store";
import {
  isIosReviewAuthenticatedSessionActive,
  isIosReviewSessionActive,
} from "@/modules/ios-review/session";

interface DeviceState {
  session: PersistedDeviceSession | null;
  accountBinding: StoredMobileDeviceAccountBinding | null;
  isReady: boolean;
  isLoading: boolean;
  hydrate: () => Promise<PersistedDeviceSession | null>;
  refreshAccountBinding: () => Promise<StoredMobileDeviceAccountBinding | null>;
  register: (payload: { storeCode: string; storeName?: string | null }) => Promise<PersistedDeviceSession>;
  syncFromProfile: (profile: DeviceProfile, options?: { storeName?: string | null }) => Promise<PersistedDeviceSession>;
  validate: (auditPayload?: Partial<DeviceValidationRequest>) => Promise<boolean>;
  unbind: () => Promise<void>;
  unbindAccountBinding: (reason?: string) => Promise<void>;
  clear: () => Promise<void>;
}

function getCurrentDeviceSystem() {
  return Platform.OS === "ios" ? "iOS" : "Android";
}

export const useDeviceStore = create<DeviceState>((set, get) => ({
  session: null,
  accountBinding: null,
  isReady: false,
  isLoading: false,

  async hydrate() {
    if (isIosReviewAuthenticatedSessionActive()) {
      // 审核账号不读取普通设备绑定，也不让历史设备会话切换认证模式。
      set({ session: null, accountBinding: null, isReady: true, isLoading: false });
      return null;
    }
    const [session, accountBinding] = await Promise.all([
      DeviceStorage.getSession(),
      DeviceAccountStorage.loadBinding(),
    ]);
    console.info("[device-session] hydrate", {
      hasSession: Boolean(session),
      hardwareId: session?.hardwareId ?? null,
      storeCode: session?.storeCode ?? null,
      status: session?.status ?? null,
    });
    set({ session, accountBinding, isReady: true });
    return session;
  },

  async refreshAccountBinding() {
    if (isIosReviewAuthenticatedSessionActive()) {
      set({ accountBinding: null });
      return null;
    }
    const [accountBinding, session] = await Promise.all([
      DeviceAccountStorage.loadBinding(),
      DeviceStorage.getSession(),
    ]);
    set({ accountBinding, session, isReady: true });
    return accountBinding;
  },

  async register(payload) {
    if (isIosReviewSessionActive()) {
      // 返回可展示的模拟结果，但不写 DeviceStorage、不设置 device session。
      return {
        hardwareId: "ios-review-device",
        authCode: "",
        storeCode: payload.storeCode,
        storeName: payload.storeName ?? payload.storeCode,
        systemDeviceNumber: "REV-IOS-001",
        status: 1,
        statusDescription: "App Review Demo",
        resolvedFromExisting: true,
      };
    }
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
        storeName: payload.storeName ?? device.storeName ?? null,
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

  async syncFromProfile(profile, options) {
    if (isIosReviewSessionActive()) {
      return {
        hardwareId: "ios-review-device",
        authCode: "",
        storeCode: profile.storeCode || "REV001",
        storeName: options?.storeName ?? profile.storeName ?? "Demo Brisbane",
        systemDeviceNumber: "REV-IOS-001",
        status: 1,
        statusDescription: "App Review Demo",
        resolvedFromExisting: true,
      };
    }
    const previousSession = await DeviceStorage.getSession();
    const session: PersistedDeviceSession = {
      hardwareId: profile.hardwareId,
      // 设备资料接口不再回传授权码；同一硬件继续使用本机安全存储中的旧凭据。
      authCode:
        profile.authCode ||
        (previousSession?.hardwareId === profile.hardwareId
          ? previousSession.authCode
          : ""),
      storeCode: profile.storeCode ?? "",
      storeName: options?.storeName ?? profile.storeName ?? null,
      systemDeviceNumber: profile.systemDeviceNumber || null,
      status: profile.status,
      statusDescription: profile.statusDescription,
      resolvedFromExisting: profile.resolvedFromExisting ?? true,
    };

    await DeviceStorage.setSession(session);
    set({ session, isReady: true, isLoading: false });
    return session;
  },

  async validate(auditPayload) {
    if (isIosReviewSessionActive()) {
      return true;
    }
    const currentSession = get().session ?? (await DeviceStorage.getSession());
    if (!currentSession?.hardwareId || !currentSession.authCode) {
      set({ session: null, isReady: true, isLoading: false });
      return false;
    }

    set({ isLoading: true });

    try {
      const locationAuditPayload = auditPayload ?? (await collectLoginDeviceLocation());
      const validation = await validateDeviceAuthApi({
        ...locationAuditPayload,
        hardwareId: currentSession.hardwareId,
        authCode: currentSession.authCode,
        systemDeviceNumber:
          locationAuditPayload.systemDeviceNumber ??
          currentSession.systemDeviceNumber ??
          undefined,
        deviceSystem: locationAuditPayload.deviceSystem ?? getCurrentDeviceSystem(),
      });

      if (!validation.isValid) {
        try {
          const profile = await getDeviceProfileApi(currentSession.hardwareId);
          const nextSession: PersistedDeviceSession = {
            ...currentSession,
            storeCode: profile.storeCode || currentSession.storeCode,
            storeName: profile.storeName || currentSession.storeName || null,
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
        useAppNavigationStore.getState().reset();
        return false;
      }

      const profile = await getDeviceProfileApi(currentSession.hardwareId);
      const nextSession: PersistedDeviceSession = {
        hardwareId: currentSession.hardwareId,
        authCode: validation.newAuthCode || currentSession.authCode,
        storeCode: profile.storeCode || currentSession.storeCode,
        storeName: profile.storeName || currentSession.storeName || null,
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

      if (profile.status === 1 && Boolean(nextSession.storeCode)) {
        await useAppNavigationStore.getState().fetchMenu();
      } else {
        useAppNavigationStore.getState().reset();
      }

      return profile.status === 1 && Boolean(nextSession.storeCode);
    } catch (error) {
      set({ isLoading: false, isReady: true });
      throw error;
    }
  },

  async unbind() {
    if (isIosReviewSessionActive()) {
      // 审核模式模拟解绑完成，不清理真实设备持久化数据，也不切换 device mode。
      set({ session: null, isReady: true, isLoading: false });
      return;
    }
    const currentSession = get().session ?? (await DeviceStorage.getSession());
    if (!currentSession?.hardwareId || !currentSession.authCode) {
      await get().clear();
      return;
    }

    set({ isLoading: true });

    try {
      await unbindDeviceApi({
        hardwareId: currentSession.hardwareId,
        authCode: currentSession.authCode,
      });
      await get().clear();
    } catch (error) {
      set({ isLoading: false, isReady: true });
      throw error;
    }
  },

  async unbindAccountBinding(reason) {
    if (isIosReviewSessionActive()) {
      set({ accountBinding: null, isReady: true, isLoading: false });
      return;
    }
    set({ isLoading: true });
    try {
      const binding = get().accountBinding ?? (await DeviceAccountStorage.loadBinding());
      if (!binding) {
        await DeviceAccountStorage.clearPending();
        set({ accountBinding: null, isReady: true, isLoading: false });
        return;
      }
      const token = await exchangeMobileDeviceSessionApi({
        hardwareId: binding.hardwareId,
        credential: binding.credential,
        apiHost: binding.apiHost,
      });
      await unbindMobileDeviceAccountApi(
        reason,
        token.accessToken,
        binding.apiHost,
      );
      await Promise.all([
        DeviceAccountStorage.clearBinding(),
        DeviceAccountStorage.clearPending(),
        DeviceStorage.clearSession(),
      ]);
      set({
        session: null,
        accountBinding: null,
        isReady: true,
        isLoading: false,
      });
    } catch (error) {
      set({ isLoading: false, isReady: true });
      throw error;
    }
  },

  async clear() {
    if (isIosReviewSessionActive()) {
      set({ session: null, isReady: true, isLoading: false });
      return;
    }
    // 关键位置：设备会话被清理时同步停止班中定位，避免旧设备上下文继续上传。
    await stopAttendanceLocationTracking().catch((error) => {
      console.warn("[attendance-location] 清理设备时停止后台定位失败", error);
    });
    await DeviceStorage.clearSession();
    useAppNavigationStore.getState().reset();
    set({ session: null, isReady: true, isLoading: false });
  },
}));
