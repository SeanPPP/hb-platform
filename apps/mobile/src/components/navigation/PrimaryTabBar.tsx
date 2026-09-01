import { Platform, Pressable, StyleSheet, Text, View } from "react-native";
import { MaterialCommunityIcons } from "@expo/vector-icons";
import { useRouter } from "expo-router";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { useAppNavigationAccess } from "@/modules/navigation/access-context";
import { TAB_PATHS } from "@/modules/navigation/default-route";
import {
  buildPrimaryNavigation,
  resolvePrimaryNavigationAction,
} from "@/modules/navigation/primary-navigation";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { HB_COLORS } from "@/shared/theme/tokens";

interface PrimaryTabBarProps {
  activeRouteName?: string;
}

export function PrimaryTabBar({ activeRouteName }: PrimaryTabBarProps) {
  const router = useRouter();
  const insets = useSafeAreaInsets();
  const { t } = useAppTranslation("common");
  const { orderedVisibleRouteNames, isDeviceMode } = useAppNavigationAccess();
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
        const targetPath = TAB_PATHS[item.targetRouteName];
        const label = t(item.labelKey);
        const iconColor = item.active ? HB_COLORS.brand : HB_COLORS.textSecondary;
        const labelColor = item.active ? HB_COLORS.action : HB_COLORS.textSecondary;

        const onPress = () => {
          if (!targetPath) {
            return;
          }

          const action = resolvePrimaryNavigationAction(activeRouteName, item);
          if (action === "dismiss-to") {
            // 工作台与同一一级上下文的子页都回固定根页，离页仍受 usePreventRemove 保护。
            router.dismissTo(targetPath as Parameters<typeof router.dismissTo>[0]);
          } else if (action === "navigate") {
            router.navigate(targetPath as Parameters<typeof router.navigate>[0]);
          }
        };

        return (
          <Pressable
            key={item.key}
            accessibilityRole="button"
            accessibilityLabel={label}
            accessibilityState={{ selected: item.active }}
            onPress={onPress}
            style={({ pressed }) => [
              styles.item,
              pressed ? styles.itemPressed : null,
            ]}
          >
            <View style={styles.iconArea}>
              {item.active ? <View style={styles.activeIndicator} /> : null}
              <MaterialCommunityIcons name={item.icon} color={iconColor} size={23} />
            </View>
            <Text
              allowFontScaling
              maxFontSizeMultiplier={1.6}
              numberOfLines={2}
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
    paddingHorizontal: 2,
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
