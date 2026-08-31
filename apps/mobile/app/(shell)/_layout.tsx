import { useEffect, useMemo, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ActivityIndicator, View } from "react-native";
import { Stack, usePathname, useRouter } from "expo-router";
import { PrimaryTabBar } from "@/components/navigation";
import { useAuthStore } from "@/store/auth-store";
import { useDeviceStore } from "@/store/device-store";
import { useAppNavigationStore } from "@/modules/navigation/store";
import { useAppDeviceStatusHeartbeat } from "@/modules/device-management/use-app-device-heartbeat";
import {
  filterAccountTabRouteNames,
  getVisibleTabRouteNames,
  resolvePreferredDefaultTabRoute,
  resolveTabRouteCorrection,
  TAB_PATHS,
} from "@/modules/navigation/default-route";
import { prepareStoredDeviceSession } from "@/modules/auth/device-login-session";
import {
  EMPLOYEE_PROFILE_REVIEW_ROUTE,
  filterEmployeeProfileReviewRouteNames,
  getEmployeeProfileReviewAccess,
} from "@/modules/employee-profile-review/access";
import { getEmployeeProfileReviewRequestsApi } from "@/modules/employee-profile-review/api";
import { AppNavigationAccessProvider } from "@/modules/navigation/access-context";

export const unstable_settings = {
  initialRouteName: "workbench",
};

export default function ShellLayout() {
  const router = useRouter();
  const pathname = usePathname();
  const currentRouteName = pathname.split("/").filter(Boolean).pop();
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
  const navigationErrorMessage = useAppNavigationStore((state) => state.errorMessage);
  const canViewAttendanceManagement = useAuthStore(
    (state) => state.access.canViewAttendanceManagement
  );
  const canCreateOrder = useAuthStore((state) => state.access.canCreateOrder);
  const isWarehouseStaffOnly = useAuthStore((state) => state.access.isWarehouseStaffOnly);
  const hasRestored = useRef(false);
  const hasAppliedDefaultRoute = useRef(false);
  const awaitingPreferredDefaultRoute = useRef(false);
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
  const preferredDefaultRoute = resolvePreferredDefaultTabRoute({
    isDeviceMode,
    isWarehouseStaffOnly,
    routeNames: orderedVisibleRouteNames,
  });
  const shouldAwaitPreferredDefaultRouteRecovery = Boolean(
    navigationErrorMessage
      && (isDeviceMode || isWarehouseStaffOnly)
      && !preferredDefaultRoute
  );

  useEffect(() => {
    if (shouldWaitForNavigation || visibleRouteNames.size === 0) {
      return;
    }

    if (shouldAwaitPreferredDefaultRouteRecovery) {
      awaitingPreferredDefaultRoute.current = true;
    }

    const preferredDefaultStillUnavailable = Boolean(
      awaitingPreferredDefaultRoute.current && !preferredDefaultRoute
    );
    const nextPath = resolveTabRouteCorrection({
      currentRouteName,
      hasAppliedDefaultRoute: awaitingPreferredDefaultRoute.current
        ? false
        : hasAppliedDefaultRoute.current,
      isDeviceMode,
      isWarehouseStaffOnly,
      routeNames: orderedVisibleRouteNames,
    });

    if (!nextPath) {
      if (preferredDefaultStillUnavailable) {
        hasAppliedDefaultRoute.current = false;
        return;
      }

      awaitingPreferredDefaultRoute.current = false;
      hasAppliedDefaultRoute.current = true;
      return;
    }

    if (preferredDefaultStillUnavailable) {
      hasAppliedDefaultRoute.current = false;
    } else {
      awaitingPreferredDefaultRoute.current = false;
      hasAppliedDefaultRoute.current = true;
    }
    if (nextPath === TAB_PATHS.workbench) {
      // 深链会自动把工作台放在栈底；回工作台时弹到锚点，避免 replace 生成重复根页。
      router.dismissTo(nextPath as Parameters<typeof router.dismissTo>[0]);
      return;
    }

    // 其他权限纠偏和默认入口使用 replace，确保被撤权页面不会留在返回历史中。
    router.replace(nextPath as Parameters<typeof router.replace>[0], { withAnchor: true });
  }, [
    isDeviceMode,
    isWarehouseStaffOnly,
    orderedVisibleRouteNames,
    currentRouteName,
    preferredDefaultRoute,
    router,
    shouldAwaitPreferredDefaultRouteRecovery,
    shouldWaitForNavigation,
    visibleRouteNames,
  ]);

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
    <AppNavigationAccessProvider
      value={{
        orderedVisibleRouteNames,
        navigationErrorMessage,
        navigationLoading,
        pendingProfileReviewCount: pendingReviewQuery.data?.total ?? 0,
        isDeviceMode,
        isWarehouseStaffOnly,
      }}
    >
      <View style={{ flex: 1 }}>
        <View style={{ flex: 1 }}>
          <Stack
            screenOptions={{
              headerShown: false,
              gestureEnabled: true,
            }}
          />
        </View>
        <PrimaryTabBar activeRouteName={currentRouteName} />
      </View>
    </AppNavigationAccessProvider>
  );
}
