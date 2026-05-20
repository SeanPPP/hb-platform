import { useEffect, useRef } from "react";
import { ActivityIndicator, View } from "react-native";
import { Tabs, useRouter } from "expo-router";
import { MaterialCommunityIcons } from "@expo/vector-icons";
import type { ComponentProps } from "react";
import { ScrollableTabBar } from "@/components/navigation";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { useAuthStore } from "@/store/auth-store";
import { useDeviceStore } from "@/store/device-store";

export default function TabsLayout() {
  const router = useRouter();
  const { t } = useAppTranslation("common");
  const userGuid = useAuthStore((state) => state.user?.userGUID);
  const access = useAuthStore((state) => state.access);
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated);
  const isLoading = useAuthStore((state) => state.isLoading);
  const restoreSession = useAuthStore((state) => state.restoreSession);
  const deviceSession = useDeviceStore((state) => state.session);
  const deviceHydrated = useDeviceStore((state) => state.isReady);
  const validateDevice = useDeviceStore((state) => state.validate);
  const hasRestored = useRef(false);
  const hasUserSession = Boolean(isAuthenticated && userGuid);
  const hasStoredDeviceSession = Boolean(
    deviceSession?.hardwareId && deviceSession.authCode && deviceSession.storeCode
  );

  useEffect(() => {
    if (hasRestored.current) {
      return;
    }

    if (!deviceHydrated) {
      return;
    }

    if (hasUserSession) {
      console.info("[startup-auth] using existing user session");
      hasRestored.current = true;
      return;
    }

    if (hasStoredDeviceSession) {
      let cancelled = false;
      const currentDeviceSession = deviceSession!;
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
            console.warn("[startup-auth] device session not ready, attempting account session restore", {
              hardwareId: currentDeviceSession.hardwareId,
              storeCode: currentDeviceSession.storeCode,
              status: currentDeviceSession.status ?? null,
            });
            const restored = await restoreSession();
            if (!restored && !cancelled) {
              console.warn("[startup-auth] no account session available after device validation rejection, redirecting to login");
              router.replace("/(auth)/login");
            }
          }
        } catch {
          if (!cancelled) {
            console.warn("[startup-auth] device validation failed, attempting account session restore");
            const restored = await restoreSession();
            if (!restored && !cancelled) {
              console.warn("[startup-auth] device validation failed and no account session restored, redirecting to login");
              router.replace("/(auth)/login");
            }
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
  }, [deviceHydrated, deviceSession, hasStoredDeviceSession, hasUserSession, restoreSession, router, validateDevice]);

  const isDeviceMode = Boolean(hasStoredDeviceSession && !hasUserSession);
  const tabItems: Array<{
    name: string;
    title: string;
    icon: ComponentProps<typeof MaterialCommunityIcons>["name"];
  }> = [
    {
      name: "home",
      title: t("tabs.home"),
      icon: "home",
    },
    {
      name: "orders",
      title: t("tabs.orders"),
      icon: "clipboard-list",
    },
    {
      name: "cart",
      title: t("tabs.cart"),
      icon: "cart-outline",
    },
    {
      name: "product-query",
      title: t("tabs.productQuery"),
      icon: "barcode-scan",
    },
    {
      name: "settings",
      title: t("tabs.settings"),
      icon: "cog",
    },
  ];

  if (access.isStoreManager || access.canReadUser) {
    tabItems.push({
      name: "users",
      title: t("tabs.users"),
      icon: "account-multiple-outline",
    });
  }

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
    <Tabs
      screenOptions={{ headerShown: false }}
      tabBar={(props) => <ScrollableTabBar {...props} />}
    >
      {tabItems.map((tabItem) => (
        <Tabs.Screen
          key={tabItem.name}
          name={tabItem.name}
          options={{
            title: tabItem.title,
            tabBarIcon: ({ color, size }) => (
              <MaterialCommunityIcons name={tabItem.icon} color={color} size={size} />
            ),
          }}
        />
      ))}
    </Tabs>
  );
}
