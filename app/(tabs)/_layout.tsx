import { useEffect, useRef } from "react";
import { ActivityIndicator, View } from "react-native";
import { Tabs, useRouter } from "expo-router";
import { MaterialCommunityIcons } from "@expo/vector-icons";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { useAuthStore } from "@/store/auth-store";
import { useDeviceStore } from "@/store/device-store";

export default function TabsLayout() {
  const router = useRouter();
  const { t } = useAppTranslation("common");
  const userGuid = useAuthStore((state) => state.user?.userGUID);
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated);
  const isLoading = useAuthStore((state) => state.isLoading);
  const restoreSession = useAuthStore((state) => state.restoreSession);
  const deviceSession = useDeviceStore((state) => state.session);
  const deviceHydrated = useDeviceStore((state) => state.isReady);
  const validateDevice = useDeviceStore((state) => state.validate);
  const hasRestored = useRef(false);

  useEffect(() => {
    if (hasRestored.current) {
      return;
    }

    if (!deviceHydrated) {
      return;
    }

    if (isAuthenticated && userGuid) {
      console.info("[startup-auth] using existing user session");
      hasRestored.current = true;
      return;
    }

    if (deviceSession?.hardwareId && deviceSession.authCode && deviceSession.storeCode) {
      let cancelled = false;
      const currentDeviceSession = deviceSession;
      hasRestored.current = true;

      async function ensureDeviceSession() {
        try {
          console.info("[startup-auth] validating device session", {
            hardwareId: currentDeviceSession.hardwareId,
            storeCode: currentDeviceSession.storeCode,
            status: currentDeviceSession.status ?? null,
          });
          const isReady = await validateDevice();
          if (!isReady && !cancelled) {
            console.warn("[startup-auth] device session not ready, redirecting to login", {
              hardwareId: currentDeviceSession.hardwareId,
              storeCode: currentDeviceSession.storeCode,
              status: currentDeviceSession.status ?? null,
            });
            router.replace("/(auth)/login");
          }
        } catch {
          if (!cancelled) {
            console.warn("[startup-auth] device validation failed, redirecting to login");
            router.replace("/(auth)/login");
          }
        }
      }

      void ensureDeviceSession();

      return () => {
        cancelled = true;
      };
    }

    let cancelled = false;

    async function ensureAuthenticated() {
      console.info("[startup-auth] restoring account session");
      const restored = await restoreSession();
      hasRestored.current = true;
      if (!restored && !cancelled) {
        console.warn("[startup-auth] no device session and no account session, redirecting to login");
        router.replace("/(auth)/login");
      }
    }

    void ensureAuthenticated();

    return () => {
      cancelled = true;
    };
  }, [deviceHydrated, deviceSession?.authCode, deviceSession?.hardwareId, deviceSession?.status, deviceSession?.storeCode, isAuthenticated, restoreSession, router, userGuid, validateDevice]);

  const isDeviceMode = Boolean(
    deviceSession?.hardwareId && deviceSession.authCode && deviceSession.storeCode
  );

  if (
    (!deviceHydrated || !hasRestored.current) &&
    (isLoading || (isDeviceMode ? true : !isAuthenticated && !userGuid))
  ) {
    return (
      <View
        style={{
          flex: 1,
          justifyContent: "center",
          alignItems: "center",
          backgroundColor: "#fff",
        }}
      >
        <ActivityIndicator size="large" color="#1677FF" />
      </View>
    );
  }

  return (
    <Tabs screenOptions={{ headerShown: false }}>
      <Tabs.Screen
        name="home"
        options={{
          title: t("tabs.home"),
          tabBarIcon: ({ color, size }) => (
            <MaterialCommunityIcons name="home" color={color} size={size} />
          ),
        }}
      />
      <Tabs.Screen
        name="orders"
        options={{
          title: t("tabs.orders"),
          tabBarIcon: ({ color, size }) => (
            <MaterialCommunityIcons
              name="clipboard-list"
              color={color}
              size={size}
            />
          ),
        }}
      />
      <Tabs.Screen
        name="cart"
        options={{
          title: t("tabs.cart"),
          tabBarIcon: ({ color, size }) => (
            <MaterialCommunityIcons name="cart-outline" color={color} size={size} />
          ),
        }}
      />
      <Tabs.Screen
        name="product-query"
        options={{
          title: t("tabs.productQuery"),
          tabBarIcon: ({ color, size }) => (
            <MaterialCommunityIcons name="barcode-scan" color={color} size={size} />
          ),
        }}
      />
      <Tabs.Screen
        name="settings"
        options={{
          title: t("tabs.settings"),
          tabBarIcon: ({ color, size }) => (
            <MaterialCommunityIcons name="cog" color={color} size={size} />
          ),
        }}
      />
    </Tabs>
  );
}
