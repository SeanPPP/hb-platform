import { useEffect, useMemo, useRef } from "react";
import { ActivityIndicator, View } from "react-native";
import { Tabs, usePathname, useRouter } from "expo-router";
import { MaterialCommunityIcons } from "@expo/vector-icons";
import { ScrollableTabBar } from "@/components/navigation/ScrollableTabBar";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { useAuthStore } from "@/store/auth-store";
import { useDeviceStore } from "@/store/device-store";
import { useAppNavigationStore } from "@/modules/navigation/store";

const TAB_PATHS: Record<string, "/(tabs)/home" | "/(tabs)/orders" | "/(tabs)/cart" | "/(tabs)/warehouse" | "/(tabs)/product-query" | "/(tabs)/settings"> = {
  home: "/(tabs)/home",
  orders: "/(tabs)/orders",
  cart: "/(tabs)/cart",
  warehouse: "/(tabs)/warehouse",
  "product-query": "/(tabs)/product-query",
  settings: "/(tabs)/settings",
};

export default function TabsLayout() {
  const router = useRouter();
  const pathname = usePathname();
  const { t } = useAppTranslation("common");
  const userGuid = useAuthStore((state) => state.user?.userGUID);
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated);
  const isLoading = useAuthStore((state) => state.isLoading);
  const restoreSession = useAuthStore((state) => state.restoreSession);
  const deviceSession = useDeviceStore((state) => state.session);
  const deviceHydrated = useDeviceStore((state) => state.isReady);
  const validateDevice = useDeviceStore((state) => state.validate);
  const navigationItems = useAppNavigationStore((state) => state.items);
  const navigationReady = useAppNavigationStore((state) => state.isReady);
  const navigationLoading = useAppNavigationStore((state) => state.isLoading);
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
  const visibleRouteNames = useMemo(
    () => new Set(navigationItems.map((item) => item.routeName)),
    [navigationItems]
  );
  const firstVisibleRouteName = navigationItems[0]?.routeName ?? "settings";
  const shouldWaitForNavigation =
    (hasUserSession || isDeviceMode) && (!navigationReady || navigationLoading);
  const isRouteVisible = (routeName: string) =>
    visibleRouteNames.size === 0 ? routeName === "settings" : visibleRouteNames.has(routeName);

  useEffect(() => {
    if (shouldWaitForNavigation || visibleRouteNames.size === 0) {
      return;
    }

    const currentRouteName = pathname.split("/").filter(Boolean).pop();
    if (!currentRouteName || isRouteVisible(currentRouteName)) {
      return;
    }

    router.replace(TAB_PATHS[firstVisibleRouteName] ?? "/(tabs)/settings");
  }, [firstVisibleRouteName, pathname, router, shouldWaitForNavigation, visibleRouteNames]);

  if (
    shouldWaitForNavigation ||
    ((!deviceHydrated || !hasRestored.current) &&
      (isLoading || (isDeviceMode ? true : !isAuthenticated && !userGuid)))
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
    <Tabs screenOptions={{ headerShown: false }} tabBar={(props) => <ScrollableTabBar {...props} />}>
      <Tabs.Screen
        name="home"
        options={{
          href: isRouteVisible("home") ? undefined : null,
          title: t("tabs.home"),
          tabBarIcon: ({ color, size }) => (
            <MaterialCommunityIcons name="home" color={color} size={size} />
          ),
        }}
      />
      <Tabs.Screen
        name="orders"
        options={{
          href: isRouteVisible("orders") ? undefined : null,
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
          href: isRouteVisible("cart") ? undefined : null,
          title: t("tabs.cart"),
          tabBarIcon: ({ color, size }) => (
            <MaterialCommunityIcons name="cart-outline" color={color} size={size} />
          ),
        }}
      />
      <Tabs.Screen
        name="warehouse"
        options={{
          href: isRouteVisible("warehouse") ? undefined : null,
          title: t("tabs.warehouse"),
          tabBarIcon: ({ color, size }) => (
            <MaterialCommunityIcons name="warehouse" color={color} size={size} />
          ),
        }}
      />
      <Tabs.Screen
        name="product-query"
        options={{
          href: isRouteVisible("product-query") ? undefined : null,
          title: t("tabs.productQuery"),
          tabBarIcon: ({ color, size }) => (
            <MaterialCommunityIcons name="barcode-scan" color={color} size={size} />
          ),
        }}
      />
      <Tabs.Screen
        name="settings"
        options={{
          href: isRouteVisible("settings") ? undefined : null,
          title: t("tabs.settings"),
          tabBarIcon: ({ color, size }) => (
            <MaterialCommunityIcons
              name="account-circle-outline"
              color={color}
              size={size}
            />
          ),
        }}
      />
      <Tabs.Screen
        name="employee-profile"
        options={{
          href: null,
        }}
      />
    </Tabs>
  );
}
