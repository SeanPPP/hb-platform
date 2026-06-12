import { useEffect, useMemo, useRef, useState } from "react";
import {
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  useWindowDimensions,
  View,
} from "react-native";
import type { BottomTabBarProps } from "@react-navigation/bottom-tabs";
import { MaterialCommunityIcons } from "@expo/vector-icons";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { getVisibleTabRouteNames } from "@/modules/navigation/default-route";
import { useAppNavigationStore } from "@/modules/navigation/store";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { useAuthStore } from "@/store/auth-store";
import { useDeviceStore } from "@/store/device-store";
import {
  buildNavigationDisplayTabs,
  isNavigationDisplayTabFocused,
  MAX_VISIBLE_TABS,
  type NavigationGroupRoute,
} from "@/components/navigation/tab-grouping";

const ACTIVE_TINT_COLOR = "#1677FF";
const INACTIVE_TINT_COLOR = "#6B7280";

type VisibleRoute = BottomTabBarProps["state"]["routes"][number] & NavigationGroupRoute;

function resolveTabLabel(
  options: BottomTabBarProps["descriptors"][string]["options"],
  routeName: string
) {
  if (typeof options.tabBarLabel === "string") {
    return options.tabBarLabel;
  }

  if (typeof options.title === "string") {
    return options.title;
  }

  return routeName;
}

export function ScrollableTabBar({
  state,
  descriptors,
  navigation,
}: BottomTabBarProps) {
  const insets = useSafeAreaInsets();
  const { t } = useAppTranslation("common");
  const { width } = useWindowDimensions();
  const scrollViewRef = useRef<ScrollView>(null);
  const [isStoreMenuOpen, setIsStoreMenuOpen] = useState(false);
  const [scrollOffset, setScrollOffset] = useState(0);
  const navigationItems = useAppNavigationStore((store) => store.items);
  const canViewAttendanceManagement = useAuthStore(
    (state) => state.access.canViewAttendanceManagement
  );
  const hasUserSession = useAuthStore(
    (state) => Boolean(state.isAuthenticated && state.user?.userGUID)
  );
  const deviceSession = useDeviceStore((state) => state.session);
  const isDeviceMode = Boolean(
    deviceSession?.hardwareId &&
      deviceSession.authCode &&
      deviceSession.storeCode &&
      !hasUserSession
  );
  const visibleRouteNames = useMemo(
    () =>
      new Set(
        getVisibleTabRouteNames({
          routeNames: navigationItems.map((item) => item.routeName),
          isDeviceMode,
          canViewAttendanceManagement,
        }),
      ),
    [canViewAttendanceManagement, isDeviceMode, navigationItems]
  );
  const visibleRoutes: VisibleRoute[] = state.routes
    .map((route, index) => ({ ...route, index }))
    .filter((route) => {
      const options = descriptors[route.key]?.options as { href?: unknown } | undefined;
      const isAllowedByMenu =
        visibleRouteNames.size === 0 ? route.name === "settings" : visibleRouteNames.has(route.name);
      return options?.href !== null && isAllowedByMenu;
    });

  const displayTabs = useMemo(() => buildNavigationDisplayTabs(visibleRoutes), [visibleRoutes]);

  const visibleTabCount = Math.min(displayTabs.length, MAX_VISIBLE_TABS);
  const tabWidth = width / Math.max(visibleTabCount, 1);
  const isScrollable = displayTabs.length > MAX_VISIBLE_TABS;
  const focusedVisibleIndex = displayTabs.findIndex((item) => isNavigationDisplayTabFocused(item, state.index));
  const storeDisplayIndex = displayTabs.findIndex((item) => item.type === "store");
  const storeDisplayTab = displayTabs[storeDisplayIndex];
  const storeBubbleWidth = Math.max(tabWidth - 12, 58);
  const storeBubbleLeft =
    storeDisplayIndex >= 0
      ? Math.min(
          Math.max(6, storeDisplayIndex * tabWidth - scrollOffset + 6),
          Math.max(6, width - storeBubbleWidth - 6)
        )
      : 6;

  useEffect(() => {
    if (!isScrollable) {
      return;
    }

    const activeIndex = focusedVisibleIndex >= 0 ? focusedVisibleIndex : 0;
    const offset = Math.max(0, activeIndex * tabWidth - tabWidth * 2);
    scrollViewRef.current?.scrollTo({ x: offset, animated: true });
  }, [focusedVisibleIndex, isScrollable, tabWidth]);

  useEffect(() => {
    if (displayTabs.every((item) => item.type !== "store")) {
      setIsStoreMenuOpen(false);
    }
  }, [displayTabs]);

  const navigateToRoute = (item: VisibleRoute, isFocused: boolean) => {
    const event = navigation.emit({
      type: "tabPress",
      target: item.key,
      canPreventDefault: true,
    });

    if (!isFocused && !event.defaultPrevented) {
      navigation.navigate(item.name, item.params);
    }
  };

  return (
    <View style={[styles.container, { paddingBottom: insets.bottom }]}>
      <ScrollView
        ref={scrollViewRef}
        horizontal
        bounces={false}
        showsHorizontalScrollIndicator={false}
        scrollEventThrottle={16}
        onScroll={(event) => setScrollOffset(event.nativeEvent.contentOffset.x)}
        contentContainerStyle={[
          styles.contentContainer,
          {
            minWidth: width,
            width: isScrollable ? tabWidth * displayTabs.length : width,
          },
        ]}
      >
        {displayTabs.map((item) => {
          if (item.type === "store") {
            const isFocused = isNavigationDisplayTabFocused(item, state.index);
            const color = isFocused ? ACTIVE_TINT_COLOR : INACTIVE_TINT_COLOR;

            return (
              <View key={item.key} style={[styles.tabSlot, { width: tabWidth }]}>
                <Pressable
                  accessibilityRole="button"
                  accessibilityState={isFocused ? { selected: true, expanded: isStoreMenuOpen } : { expanded: isStoreMenuOpen }}
                  accessibilityLabel={t("tabs.store")}
                  onPress={() => setIsStoreMenuOpen((current) => !current)}
                  style={({ pressed }) => [
                    styles.tab,
                    { opacity: pressed ? 0.82 : 1 },
                  ]}
                >
                  <View style={styles.iconContainer}>
                    <MaterialCommunityIcons
                      name="storefront-outline"
                      color={color}
                      size={22}
                    />
                  </View>
                  <Text
                    numberOfLines={1}
                    style={[
                      styles.label,
                      isFocused ? styles.labelActive : null,
                      { color },
                    ]}
                  >
                    {t("tabs.store")}
                  </Text>
                </Pressable>
              </View>
            );
          }

          const { route } = item;
          const descriptor = descriptors[route.key];
          const { options } = descriptor;
          const isFocused = state.index === route.index;
          const color = isFocused
            ? options.tabBarActiveTintColor ?? ACTIVE_TINT_COLOR
            : options.tabBarInactiveTintColor ?? INACTIVE_TINT_COLOR;
          const label = resolveTabLabel(options, route.name);

          const onPress = () => {
            setIsStoreMenuOpen(false);
            navigateToRoute(route, isFocused);
          };

          const onLongPress = () => {
            navigation.emit({
              type: "tabLongPress",
              target: route.key,
            });
          };

          return (
            <View key={route.key} style={[styles.tabSlot, { width: tabWidth }]}>
              <Pressable
                accessibilityRole="button"
                accessibilityState={isFocused ? { selected: true } : {}}
                accessibilityLabel={options.tabBarAccessibilityLabel}
                testID={options.tabBarButtonTestID}
                onPress={onPress}
                onLongPress={onLongPress}
                style={({ pressed }) => [
                  styles.tab,
                  { opacity: pressed ? 0.82 : 1 },
                ]}
              >
                <View style={styles.iconContainer}>
                  {options.tabBarIcon?.({
                    focused: isFocused,
                    color,
                    size: 22,
                  })}
                </View>
                <Text
                  numberOfLines={2}
                  style={[
                    styles.label,
                    isFocused ? styles.labelActive : null,
                    { color },
                  ]}
                >
                  {label}
                </Text>
              </Pressable>
            </View>
          );
        })}
      </ScrollView>
      {isStoreMenuOpen && storeDisplayTab?.type === "store" ? (
        <View
          pointerEvents="box-none"
          style={[
            styles.storeBubbleStack,
            {
              left: storeBubbleLeft,
              width: storeBubbleWidth,
              bottom: insets.bottom + 70,
            },
          ]}
        >
          {storeDisplayTab.children.map((child) => {
            const descriptor = descriptors[child.key];
            const options = descriptor.options;
            const childFocused = state.index === child.index;
            const childColor = childFocused
              ? options.tabBarActiveTintColor ?? ACTIVE_TINT_COLOR
              : INACTIVE_TINT_COLOR;
            const label = resolveTabLabel(options, child.name);

            return (
              <Pressable
                key={child.key}
                accessibilityRole="button"
                accessibilityState={childFocused ? { selected: true } : {}}
                accessibilityLabel={options.tabBarAccessibilityLabel ?? label}
                onPress={() => {
                  navigateToRoute(child, childFocused);
                  setIsStoreMenuOpen(false);
                }}
                style={({ pressed }) => [
                  styles.storeBubble,
                  childFocused ? styles.storeBubbleActive : null,
                  { opacity: pressed ? 0.82 : 1 },
                ]}
              >
                {options.tabBarIcon?.({
                  focused: childFocused,
                  color: childColor,
                  size: 18,
                })}
                <Text
                  numberOfLines={1}
                  style={[
                    styles.storeBubbleLabel,
                    childFocused ? styles.labelActive : null,
                    { color: childColor },
                  ]}
                >
                  {label}
                </Text>
              </Pressable>
            );
          })}
        </View>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    backgroundColor: "#FFFFFF",
    borderTopWidth: StyleSheet.hairlineWidth,
    borderTopColor: "#D9E2F2",
    shadowColor: "#0F172A",
    shadowOpacity: 0.08,
    shadowRadius: 8,
    shadowOffset: { width: 0, height: -2 },
    elevation: 12,
    overflow: "visible",
  },
  contentContainer: {
    flexDirection: "row",
    overflow: "visible",
  },
  tabSlot: {
    position: "relative",
    overflow: "visible",
  },
  tab: {
    alignItems: "center",
    justifyContent: "center",
    minHeight: 66,
    paddingHorizontal: 8,
    paddingTop: 8,
    paddingBottom: 6,
  },
  iconContainer: {
    minHeight: 24,
    justifyContent: "center",
  },
  label: {
    marginTop: 4,
    fontSize: 11,
    fontWeight: "500",
    lineHeight: 13,
    minHeight: 26,
    textAlign: "center",
  },
  labelActive: {
    fontWeight: "700",
  },
  storeBubbleStack: {
    position: "absolute",
    zIndex: 20,
    gap: 8,
    alignItems: "center",
  },
  storeBubble: {
    width: "100%",
    minHeight: 44,
    alignItems: "center",
    justifyContent: "center",
    gap: 2,
    paddingHorizontal: 6,
    paddingVertical: 7,
    borderRadius: 999,
    borderWidth: StyleSheet.hairlineWidth,
    borderColor: "#D9E2F2",
    backgroundColor: "#FFFFFF",
    shadowColor: "#0F172A",
    shadowOpacity: 0.14,
    shadowRadius: 8,
    shadowOffset: { width: 0, height: 3 },
    elevation: 10,
  },
  storeBubbleActive: {
    borderColor: "#BBD7FF",
    backgroundColor: "#EFF6FF",
  },
  storeBubbleLabel: {
    fontSize: 10,
    fontWeight: "600",
  },
});
