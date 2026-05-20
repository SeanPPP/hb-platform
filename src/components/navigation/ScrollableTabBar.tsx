import { useEffect, useRef } from "react";
import {
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  useWindowDimensions,
  View,
} from "react-native";
import type { BottomTabBarProps } from "@react-navigation/bottom-tabs";
import { useSafeAreaInsets } from "react-native-safe-area-context";

const MAX_VISIBLE_TABS = 5;
const ACTIVE_TINT_COLOR = "#1677FF";
const INACTIVE_TINT_COLOR = "#6B7280";

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
  const { width } = useWindowDimensions();
  const scrollViewRef = useRef<ScrollView>(null);
  const visibleRoutes = state.routes
    .map((route, index) => ({ route, index }))
    .filter(({ route }) => {
      const options = descriptors[route.key]?.options as { href?: unknown } | undefined;
      return options?.href !== null;
    });
  const visibleTabCount = Math.min(visibleRoutes.length, MAX_VISIBLE_TABS);
  const tabWidth = width / Math.max(visibleTabCount, 1);
  const isScrollable = visibleRoutes.length > MAX_VISIBLE_TABS;
  const focusedVisibleIndex = visibleRoutes.findIndex(({ index }) => index === state.index);

  useEffect(() => {
    if (!isScrollable) {
      return;
    }

    const activeIndex = focusedVisibleIndex >= 0 ? focusedVisibleIndex : 0;
    const offset = Math.max(0, activeIndex * tabWidth - tabWidth * 2);
    scrollViewRef.current?.scrollTo({ x: offset, animated: true });
  }, [focusedVisibleIndex, isScrollable, tabWidth]);

  return (
    <View style={[styles.container, { paddingBottom: insets.bottom }]}>
      <ScrollView
        ref={scrollViewRef}
        horizontal
        bounces={false}
        showsHorizontalScrollIndicator={false}
        contentContainerStyle={[
          styles.contentContainer,
          {
            minWidth: width,
            width: isScrollable ? tabWidth * visibleRoutes.length : width,
          },
        ]}
      >
        {visibleRoutes.map(({ route, index }) => {
          const descriptor = descriptors[route.key];
          const { options } = descriptor;
          const isFocused = state.index === index;
          const color = isFocused
            ? options.tabBarActiveTintColor ?? ACTIVE_TINT_COLOR
            : options.tabBarInactiveTintColor ?? INACTIVE_TINT_COLOR;
          const label = resolveTabLabel(options, route.name);

          const onPress = () => {
            const event = navigation.emit({
              type: "tabPress",
              target: route.key,
              canPreventDefault: true,
            });

            if (!isFocused && !event.defaultPrevented) {
              navigation.navigate(route.name, route.params);
            }
          };

          const onLongPress = () => {
            navigation.emit({
              type: "tabLongPress",
              target: route.key,
            });
          };

          return (
            <Pressable
              key={route.key}
              accessibilityRole="button"
              accessibilityState={isFocused ? { selected: true } : {}}
              accessibilityLabel={options.tabBarAccessibilityLabel}
              testID={options.tabBarButtonTestID}
              onPress={onPress}
              onLongPress={onLongPress}
              style={({ pressed }) => [
                styles.tab,
                { width: tabWidth, opacity: pressed ? 0.82 : 1 },
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
                numberOfLines={1}
                style={[
                  styles.label,
                  isFocused ? styles.labelActive : null,
                  { color },
                ]}
              >
                {label}
              </Text>
            </Pressable>
          );
        })}
      </ScrollView>
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
  },
  contentContainer: {
    flexDirection: "row",
  },
  tab: {
    alignItems: "center",
    justifyContent: "center",
    minHeight: 58,
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
  },
  labelActive: {
    fontWeight: "700",
  },
});
