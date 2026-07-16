import { useEffect, useMemo, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ActivityIndicator, View } from "react-native";
import { Tabs, usePathname, useRouter } from "expo-router";
import { MaterialCommunityIcons } from "@expo/vector-icons";
import { ScrollableTabBar } from "@/components/navigation/ScrollableTabBar";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { useAuthStore } from "@/store/auth-store";
import { useDeviceStore } from "@/store/device-store";
import { useAppNavigationStore } from "@/modules/navigation/store";
import { useAppDeviceStatusHeartbeat } from "@/modules/device-management/use-app-device-heartbeat";
import {
  filterAccountTabRouteNames,
  getVisibleTabRouteNames,
  resolveTabRouteCorrection,
} from "@/modules/navigation/default-route";
import { prepareStoredDeviceSession } from "@/modules/auth/device-login-session";
import {
  EMPLOYEE_PROFILE_REVIEW_ROUTE,
  filterEmployeeProfileReviewRouteNames,
  getEmployeeProfileReviewAccess,
} from "@/modules/employee-profile-review/access";
import { getEmployeeProfileReviewRequestsApi } from "@/modules/employee-profile-review/api";

export default function TabsLayout() {
  const router = useRouter();
  const pathname = usePathname();
  const { t } = useAppTranslation("common");
  const userGuid = useAuthStore((state) => state.user?.userGUID);
  const currentUser = useAuthStore((state) => state.user);
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated);
  const sessionKind = useAuthStore((state) => state.sessionKind);
  const isLoading = useAuthStore((state) => state.isLoading);
  const restoreSession = useAuthStore((state) => state.restoreSession);
  const clearLocalAuthSession = useAuthStore((state) => state.clearLocalSession);
  const setSessionKind = useAuthStore((state) => state.setSessionKind);
  const deviceSession = useDeviceStore((state) => state.session);
  const deviceHydrated = useDeviceStore((state) => state.isReady);
  const validateDevice = useDeviceStore((state) => state.validate);
  const navigationItems = useAppNavigationStore((state) => state.items);
  const navigationReady = useAppNavigationStore((state) => state.isReady);
  const navigationLoading = useAppNavigationStore((state) => state.isLoading);
  const canViewAttendanceManagement = useAuthStore(
    (state) => state.access.canViewAttendanceManagement
  );
  const canCreateOrder = useAuthStore((state) => state.access.canCreateOrder);
  const isWarehouseStaffOnly = useAuthStore((state) => state.access.isWarehouseStaffOnly);
  const hasRestored = useRef(false);
  const hasAppliedDefaultRoute = useRef(false);
  const [heartbeatReady, setHeartbeatReady] = useState(false);
  const [heartbeatUsesDeviceSession, setHeartbeatUsesDeviceSession] = useState(false);
  const hasUserSession = Boolean(isAuthenticated && userGuid);
  const hasStoredDeviceSession = Boolean(
    deviceSession?.hardwareId && deviceSession.authCode && deviceSession.storeCode
  );
  const isIosReviewSession = sessionKind === "iosReview";
  useAppDeviceStatusHeartbeat({
    enabled:
      !isIosReviewSession &&
      heartbeatReady &&
      (hasUserSession || hasStoredDeviceSession),
    useDeviceSession: heartbeatUsesDeviceSession,
  });

  useEffect(() => {
    if (hasRestored.current) {
      return;
    }

    if (!deviceHydrated) {
      return;
    }

    if (isIosReviewSession) {
      // 审核会话完全离线，不校验已保存设备，也不启动设备状态心跳。
      hasRestored.current = true;
      setHeartbeatReady(false);
      setHeartbeatUsesDeviceSession(false);
      return;
    }

    if (hasUserSession) {
      console.info("[startup-auth] using existing user session");
      hasRestored.current = true;
      setHeartbeatReady(false);
      setHeartbeatUsesDeviceSession(false);
      if (hasStoredDeviceSession) {
        let cancelled = false;
        async function validateStoredDeviceForHeartbeat() {
          try {
            const isValidDeviceSession = await validateDevice();
            if (!cancelled) {
              setHeartbeatUsesDeviceSession(isValidDeviceSession);
              setHeartbeatReady(true);
            }
          } catch {
            if (!cancelled) {
              setHeartbeatUsesDeviceSession(false);
              setHeartbeatReady(true);
            }
          }
        }

        void validateStoredDeviceForHeartbeat();
        return () => {
          cancelled = true;
        };
      }

      setHeartbeatReady(true);
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
          const isReady = await prepareStoredDeviceSession({
            clearAccountSession: clearLocalAuthSession,
            validateDevice,
          });
          if (isReady && !cancelled) {
            // 设备完成在线校验后才解除 review 构建的 Root 副作用守卫。
            setSessionKind("device");
            setHeartbeatUsesDeviceSession(true);
            setHeartbeatReady(true);
          }
          if (!isReady && !cancelled) {
            console.warn("[startup-auth] device session not ready, attempting account session restore", {
              hardwareId: currentDeviceSession.hardwareId,
              storeCode: currentDeviceSession.storeCode,
              status: currentDeviceSession.status ?? null,
            });
            const restored = await restoreSession();
            if (restored && !cancelled) {
              setHeartbeatUsesDeviceSession(false);
              setHeartbeatReady(true);
            }
            if (!restored && !cancelled) {
              console.warn("[startup-auth] no account session available after device validation rejection, redirecting to login");
              router.replace("/(auth)/login");
            }
          }
        } catch {
          if (!cancelled) {
            console.warn("[startup-auth] device validation failed, attempting account session restore");
            const restored = await restoreSession();
            if (restored && !cancelled) {
              setHeartbeatUsesDeviceSession(false);
              setHeartbeatReady(true);
            }
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
      if (!cancelled) {
        setHeartbeatUsesDeviceSession(false);
        setHeartbeatReady(restored);
      }
      if (!restored && !cancelled) {
        console.warn("[startup-auth] no device session and no account session, redirecting to login");
        router.replace("/(auth)/login");
      }
    }

    void ensureAuthenticated();

    return () => {
      cancelled = true;
    };
  }, [
    clearLocalAuthSession,
    deviceHydrated,
    deviceSession,
    hasStoredDeviceSession,
    hasUserSession,
    isIosReviewSession,
    restoreSession,
    router,
    setSessionKind,
    validateDevice,
  ]);

  const isDeviceMode = Boolean(hasStoredDeviceSession && !hasUserSession);
  const accountRouteNames = useMemo(
    () =>
      filterAccountTabRouteNames(
        navigationItems.map((item) => item.routeName),
        { canCreateOrder, isWarehouseStaffOnly }
      ),
    [canCreateOrder, isWarehouseStaffOnly, navigationItems]
  );
  const employeeProfileReviewAccess = useMemo(
    () => getEmployeeProfileReviewAccess({
      roleNames: currentUser?.roleNames,
      permissions: currentUser?.permissions,
      menuRouteNames: navigationItems.map((item) => item.routeName),
      sessionKind,
    }),
    [currentUser?.permissions, currentUser?.roleNames, navigationItems, sessionKind]
  );
  const orderedVisibleRouteNames = useMemo(
    () => filterEmployeeProfileReviewRouteNames(
      getVisibleTabRouteNames({
        routeNames: accountRouteNames,
        isDeviceMode,
        canViewAttendanceManagement,
      }),
      employeeProfileReviewAccess.allowed
    ),
    [
      accountRouteNames,
      canViewAttendanceManagement,
      employeeProfileReviewAccess.allowed,
      isDeviceMode,
    ]
  );
  const visibleRouteNames = useMemo(
    () => new Set(orderedVisibleRouteNames),
    [orderedVisibleRouteNames]
  );
  const pendingReviewQuery = useQuery({
    queryKey: ["employeeProfileReview", "requests", "Pending", "count"],
    enabled:
      navigationReady
      && employeeProfileReviewAccess.allowed
      && visibleRouteNames.has(EMPLOYEE_PROFILE_REVIEW_ROUTE),
    queryFn: () => getEmployeeProfileReviewRequestsApi({
      page: 1,
      pageSize: 1,
      status: "Pending",
    }),
    staleTime: 30_000,
  });
  const shouldWaitForNavigation =
    (hasUserSession || isDeviceMode) && (!navigationReady || navigationLoading);
  const isRouteVisible = (routeName: string) =>
    visibleRouteNames.size === 0 ? routeName === "settings" : visibleRouteNames.has(routeName);

  useEffect(() => {
    if (shouldWaitForNavigation || visibleRouteNames.size === 0) {
      return;
    }

    const currentRouteName = pathname.split("/").filter(Boolean).pop();
    const nextPath = resolveTabRouteCorrection({
      currentRouteName,
      hasAppliedDefaultRoute: hasAppliedDefaultRoute.current,
      isDeviceMode,
      routeNames: orderedVisibleRouteNames,
    });

    if (!nextPath) {
      hasAppliedDefaultRoute.current = true;
      return;
    }

    hasAppliedDefaultRoute.current = true;
    router.navigate(nextPath as Parameters<typeof router.navigate>[0]);
  }, [isDeviceMode, orderedVisibleRouteNames, pathname, router, shouldWaitForNavigation, visibleRouteNames]);

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
        name="domestic-purchase"
        options={{
          href: isRouteVisible("domestic-purchase") ? undefined : null,
          title: t("tabs.domesticPurchase"),
          tabBarIcon: ({ color, size }) => (
            <MaterialCommunityIcons name="shopping-outline" color={color} size={size} />
          ),
        }}
      />
      <Tabs.Screen
        name="local-supplier-invoices"
        options={{
          href: isRouteVisible("local-supplier-invoices") ? undefined : null,
          title: t("tabs.localSupplierInvoices"),
          tabBarIcon: ({ color, size }) => (
            <MaterialCommunityIcons name="receipt-text-outline" color={color} size={size} />
          ),
        }}
      />
      <Tabs.Screen
        name="installment-orders"
        options={{
          href: isRouteVisible("installment-orders") ? undefined : null,
          title: t("tabs.installmentOrders"),
          tabBarIcon: ({ color, size }) => (
            <MaterialCommunityIcons name="cash-clock" color={color} size={size} />
          ),
        }}
      />
      <Tabs.Screen
        name="advertisements"
        options={{
          href: isRouteVisible("advertisements") ? undefined : null,
          title: t("tabs.advertisements"),
          tabBarIcon: ({ color, size }) => (
            <MaterialCommunityIcons name="bullhorn-outline" color={color} size={size} />
          ),
        }}
      />
      <Tabs.Screen
        name="promotions"
        options={{
          href: isRouteVisible("promotions") ? undefined : null,
          title: t("tabs.promotions"),
          tabBarIcon: ({ color, size }) => (
            <MaterialCommunityIcons name="ticket-percent-outline" color={color} size={size} />
          ),
        }}
      />
      <Tabs.Screen
        name="reports"
        options={{
          href: isRouteVisible("reports") ? undefined : null,
          title: t("tabs.reports"),
          tabBarIcon: ({ color, size }) => (
            <MaterialCommunityIcons name="chart-line" color={color} size={size} />
          ),
        }}
      />
      <Tabs.Screen
        name="store-vouchers"
        options={{
          href: isRouteVisible("store-vouchers") ? undefined : null,
          title: t("tabs.storeVouchers"),
          tabBarIcon: ({ color, size }) => (
            <MaterialCommunityIcons name="ticket-percent-outline" color={color} size={size} />
          ),
        }}
      />
      <Tabs.Screen
        name="seasonal-cards"
        options={{
          href: isRouteVisible("seasonal-cards") ? undefined : null,
          title: t("tabs.seasonalCards"),
          tabBarIcon: ({ color, size }) => (
            <MaterialCommunityIcons name="cards-outline" color={color} size={size} />
          ),
        }}
      />
      <Tabs.Screen
        name="attendance"
        options={{
          href: null,
          title: t("tabs.attendance"),
          tabBarIcon: ({ color, size }) => (
            <MaterialCommunityIcons name="calendar-clock" color={color} size={size} />
          ),
        }}
      />
      <Tabs.Screen
        name="attendance-personal"
        options={{
          href: isRouteVisible("attendance-personal") ? undefined : null,
          title: t("tabs.attendancePersonal"),
          tabBarIcon: ({ color, size }) => (
            <MaterialCommunityIcons name="account-clock-outline" color={color} size={size} />
          ),
        }}
      />
      <Tabs.Screen
        name="attendance-management"
        options={{
          href: isRouteVisible("attendance-management") ? undefined : null,
          title: t("tabs.attendanceManagement"),
          tabBarIcon: ({ color, size }) => (
            <MaterialCommunityIcons name="calendar-edit" color={color} size={size} />
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
        name="users"
        options={{
          href: isRouteVisible("users") ? undefined : null,
          title: t("tabs.users"),
          tabBarIcon: ({ color, size }) => (
            <MaterialCommunityIcons name="account-group-outline" color={color} size={size} />
          ),
        }}
      />
      <Tabs.Screen
        name="employee-profile"
        options={{
          href: isRouteVisible("employee-profile") ? undefined : null,
          title: t("tabs.employeeProfile"),
          tabBarIcon: ({ color, size }) => (
            <MaterialCommunityIcons name="card-account-details-outline" color={color} size={size} />
          ),
        }}
      />
      <Tabs.Screen
        name="employee-profile-review"
        options={{
          href: isRouteVisible(EMPLOYEE_PROFILE_REVIEW_ROUTE) ? undefined : null,
          title: t("tabs.employeeProfileReview"),
          tabBarBadge:
            pendingReviewQuery.data?.total && pendingReviewQuery.data.total > 0
              ? pendingReviewQuery.data.total
              : undefined,
          tabBarIcon: ({ color, size }) => (
            <MaterialCommunityIcons name="account-check-outline" color={color} size={size} />
          ),
        }}
      />
      <Tabs.Screen
        name="device-management"
        options={{
          href: isRouteVisible("device-management") ? undefined : null,
          title: t("tabs.deviceManagement"),
          tabBarIcon: ({ color, size }) => (
            <MaterialCommunityIcons name="cellphone-cog" color={color} size={size} />
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
    </Tabs>
  );
}
