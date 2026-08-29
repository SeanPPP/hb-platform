import { Platform, Pressable, StyleSheet, Text, View } from "react-native";
import type { BottomTabBarProps } from "@react-navigation/bottom-tabs";
import { MaterialCommunityIcons } from "@expo/vector-icons";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { useAppNavigationAccess } from "@/modules/navigation/access-context";
import { buildPrimaryNavigation } from "@/modules/navigation/primary-navigation";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { HB_COLORS } from "@/shared/theme/tokens";

export function PrimaryTabBar({ state, navigation }: BottomTabBarProps) {
  const insets = useSafeAreaInsets();
  const { t } = useAppTranslation("common");
  const { orderedVisibleRouteNames, isDeviceMode } = useAppNavigationAccess();
  const activeRouteName = state.routes[state.index]?.name;
  const items = buildPrimaryNavigation({
    activeRouteName,
    visibleRouteNames: orderedVisibleRouteNames,
    isDeviceMode,
  });

  return (
    <View
      style={[
        styles.container,
        {
          paddingBottom: Math.max(insets.bottom, Platform.OS === "android" ? 4 : 0),
        },
      ]}
    >
      {items.map((item) => {
        const targetRoute = state.routes.find(
          (route) => route.name === item.targetRouteName
        );
        const label = t(item.labelKey);
        const iconColor = item.locked
          ? HB_COLORS.outline
          : item.active
            ? HB_COLORS.brand
            : HB_COLORS.textSecondary;
        const labelColor = item.locked
          ? HB_COLORS.outline
          : item.active
            ? HB_COLORS.action
            : HB_COLORS.textSecondary;

        const onPress = () => {
          if (item.locked || !targetRoute) {
            return;
          }

          const event = navigation.emit({
            type: "tabPress",
            target: targetRoute.key,
            canPreventDefault: true,
          });
          if (!item.active && !event.defaultPrevented) {
            navigation.navigate(targetRoute.name, targetRoute.params);
          }
        };

        return (
          <Pressable
            key={item.key}
            accessibilityRole="button"
            accessibilityLabel={
              item.locked ? t("tabs.lockedLabel", { label }) : label
            }
            accessibilityState={{
              selected: item.active,
              disabled: item.locked,
            }}
            disabled={item.locked}
            onPress={onPress}
            onLongPress={() => {
              if (!item.locked && targetRoute) {
                navigation.emit({
                  type: "tabLongPress",
                  target: targetRoute.key,
                });
              }
            }}
            style={({ pressed }) => [
              styles.item,
              pressed && !item.locked ? styles.itemPressed : null,
            ]}
          >
            <View style={styles.iconArea}>
              {item.active ? <View style={styles.activeIndicator} /> : null}
              <MaterialCommunityIcons name={item.icon} color={iconColor} size={23} />
              {item.locked ? (
                <View style={styles.lockBadge}>
                  <MaterialCommunityIcons
                    name="lock-outline"
                    color={HB_COLORS.textSecondary}
                    size={10}
                  />
                </View>
              ) : null}
            </View>
            <Text
              allowFontScaling
              maxFontSizeMultiplier={1.6}
              numberOfLines={1}
              style={[
                styles.label,
                item.active ? styles.labelActive : null,
                { color: labelColor },
              ]}
            >
              {label}
            </Text>
          </Pressable>
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flexDirection: "row",
    backgroundColor: HB_COLORS.surface,
    borderTopWidth: StyleSheet.hairlineWidth,
    borderTopColor: HB_COLORS.outline,
  },
  item: {
    flex: 1,
    minHeight: Platform.OS === "android" ? 56 : 50,
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: 4,
    paddingVertical: 5,
  },
  itemPressed: {
    backgroundColor: "#EAF2FF",
  },
  iconArea: {
    minWidth: 32,
    minHeight: 26,
    alignItems: "center",
    justifyContent: "center",
  },
  activeIndicator: {
    position: "absolute",
    top: -5,
    width: 24,
    height: 3,
    borderRadius: 2,
    backgroundColor: HB_COLORS.brand,
  },
  lockBadge: {
    position: "absolute",
    right: 0,
    bottom: 0,
    width: 15,
    height: 15,
    borderRadius: 8,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: HB_COLORS.surfaceMuted,
    borderWidth: StyleSheet.hairlineWidth,
    borderColor: HB_COLORS.outline,
  },
  label: {
    marginTop: 1,
    fontSize: 12,
    lineHeight: 16,
    fontWeight: "500",
    textAlign: "center",
  },
  labelActive: {
    fontWeight: "700",
  },
});
