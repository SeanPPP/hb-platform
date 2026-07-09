import { useEffect, useMemo, useRef } from "react";
import { AppState, Platform } from "react-native";
import * as Application from "expo-application";
import { DeviceStorage } from "@/modules/device/storage";
import { getCurrentAppUpdateInfo } from "@/modules/updates/app-update-runtime";
import { buildAppDeviceHeartbeatPayload } from "@/modules/device-management/app-device-heartbeat-payload";
import { sendAppDeviceHeartbeat } from "@/modules/device-management/api";
import { useAuthStore } from "@/store/auth-store";
import { useDeviceStore } from "@/store/device-store";

const HEARTBEAT_INTERVAL_MS = 5 * 60 * 1000;

export function useAppDeviceStatusHeartbeat({
  enabled,
  useDeviceSession,
}: {
  enabled: boolean;
  useDeviceSession: boolean;
}) {
  const userGuid = useAuthStore((state) => state.user?.userGUID);
  const deviceSession = useDeviceStore((state) => state.session);
  const lastSentAtRef = useRef(0);
  const inFlightSessionKeyRef = useRef<string | null>(null);
  const sessionKey = useMemo(
    () =>
      [
        userGuid ?? "",
        useDeviceSession ? "device" : "install",
        deviceSession?.hardwareId ?? "",
        deviceSession?.authCode ?? "",
        deviceSession?.storeCode ?? "",
        deviceSession?.systemDeviceNumber ?? "",
      ].join("|"),
    [
      deviceSession?.authCode,
      deviceSession?.hardwareId,
      deviceSession?.storeCode,
      deviceSession?.systemDeviceNumber,
      useDeviceSession,
      userGuid,
    ]
  );

  useEffect(() => {
    if (!enabled) {
      return;
    }

    let cancelled = false;
    const activeSessionKey = sessionKey;

    async function sendHeartbeat(force = false) {
      if (
        cancelled ||
        inFlightSessionKeyRef.current === activeSessionKey
      ) {
        return;
      }

      const now = Date.now();
      if (!force && now - lastSentAtRef.current < HEARTBEAT_INTERVAL_MS) {
        return;
      }

      inFlightSessionKeyRef.current = activeSessionKey;
      try {
        const [installationId, session] = await Promise.all([
          DeviceStorage.getInstallationId(),
          DeviceStorage.getSession(),
        ]);
        if (cancelled) {
          return;
        }

        const heartbeatSession = useDeviceSession ? session : null;
        const payload = buildAppDeviceHeartbeatPayload({
          installationId,
          platformOS: Platform.OS,
          session: heartbeatSession,
          updateInfo: getCurrentAppUpdateInfo(),
          applicationInfo: {
            nativeApplicationVersion: Application.nativeApplicationVersion,
            nativeBuildVersion: Application.nativeBuildVersion,
          },
        });

        if (!payload.hardwareId) {
          return;
        }

        if (cancelled) {
          return;
        }

        await sendAppDeviceHeartbeat(payload, { deviceSession: heartbeatSession });
        lastSentAtRef.current = Date.now();
      } catch (error) {
        console.warn("[app-device-status] heartbeat failed", error);
      } finally {
        if (inFlightSessionKeyRef.current === activeSessionKey) {
          inFlightSessionKeyRef.current = null;
        }
      }
    }

    void sendHeartbeat(true);
    const interval = setInterval(() => {
      if (AppState.currentState === "active") {
        void sendHeartbeat(false);
      }
    }, HEARTBEAT_INTERVAL_MS);
    const subscription = AppState.addEventListener("change", (state) => {
      if (state === "active") {
        void sendHeartbeat(true);
      }
    });

    return () => {
      cancelled = true;
      clearInterval(interval);
      subscription.remove();
    };
  }, [enabled, sessionKey, useDeviceSession]);
}
