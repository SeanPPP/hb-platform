import type { ReactNode } from "react";
import { Pressable, StyleSheet, View } from "react-native";
import { Badge, IconButton, Text } from "react-native-paper";
import { MaterialCommunityIcons } from "@expo/vector-icons";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { HB_COLORS, HB_RADIUS, HB_SPACING } from "@/shared/theme/tokens";

interface PosterScreenHeaderProps {
  title: string;
  subtitle?: string;
  onBack: () => void;
  right?: ReactNode;
}

/** 海报编辑 / 待打印页共用的栈页头：返回 + 标题 + 副标题 + 右侧操作。 */
export function PosterScreenHeader({ title, subtitle, onBack, right }: PosterScreenHeaderProps) {
  const { t } = useAppTranslation(["productQuery", "common"]);
  return (
    <View style={styles.header}>
      <IconButton icon="arrow-left" size={24} onPress={onBack} accessibilityLabel={t("poster.back")} style={styles.back} />
      <View style={styles.titles}>
        <Text style={styles.title} numberOfLines={1} accessibilityRole="header">
          {title}
        </Text>
        {subtitle ? (
          <Text style={styles.subtitle} numberOfLines={1}>
            {subtitle}
          </Text>
        ) : null}
      </View>
      {right}
    </View>
  );
}

interface PosterQueueBadgeButtonProps {
  count: number;
  onPress: () => void;
}

/** 右上角待打印角标按钮。 */
export function PosterQueueBadgeButton({ count, onPress }: PosterQueueBadgeButtonProps) {
  const { t } = useAppTranslation(["productQuery"]);
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={t("poster.queueBar.title", { count })}
      onPress={onPress}
      style={({ pressed }) => [styles.badgeButton, pressed ? styles.pressed : null]}
    >
      <MaterialCommunityIcons name="layers-outline" size={22} color={HB_COLORS.action} />
      {count > 0 ? (
        <Badge size={18} style={styles.badge}>
          {count > 99 ? "99+" : count}
        </Badge>
      ) : null}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  header: {
    flexDirection: "row",
    alignItems: "center",
    gap: HB_SPACING.xxs,
    paddingLeft: HB_SPACING.xxs,
    paddingRight: HB_SPACING.sm,
    paddingVertical: HB_SPACING.xs,
    backgroundColor: HB_COLORS.white,
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: HB_COLORS.outlineMuted,
  },
  back: {
    margin: 0,
  },
  titles: {
    flex: 1,
    minWidth: 0,
  },
  title: {
    fontSize: 20,
    lineHeight: 28,
    fontWeight: "700",
    color: HB_COLORS.textPrimary,
  },
  subtitle: {
    fontSize: 12,
    lineHeight: 18,
    color: HB_COLORS.textSecondary,
  },
  badgeButton: {
    width: 44,
    height: 44,
    borderRadius: HB_RADIUS.control,
    backgroundColor: "#EAF2FF",
    alignItems: "center",
    justifyContent: "center",
  },
  pressed: {
    opacity: 0.8,
  },
  badge: {
    position: "absolute",
    top: 2,
    right: 2,
    backgroundColor: HB_COLORS.danger,
    color: HB_COLORS.white,
    fontWeight: "700",
  },
});
