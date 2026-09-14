import type { ComponentProps, ReactNode } from "react";
import { Pressable, StyleSheet, useWindowDimensions, View } from "react-native";
import { MaterialCommunityIcons } from "@expo/vector-icons";
import { Text } from "react-native-paper";

import { HB_COLORS, HB_SPACING } from "@/shared/theme/tokens";

export function ProfileSummaryRow({
  label,
  value,
  detail,
  trailing,
  onPress,
  isLast = false,
  inline = false,
  icon,
  stackTrailingOnLargeText = false,
}: {
  label: string;
  value: string;
  detail?: string;
  trailing?: ReactNode;
  onPress?: () => void;
  isLast?: boolean;
  inline?: boolean;
  icon?: ComponentProps<typeof MaterialCommunityIcons>["name"];
  stackTrailingOnLargeText?: boolean;
}) {
  const { fontScale } = useWindowDimensions();
  const stackInline = inline && fontScale > 1.2;
  const stackTrailing = Boolean(trailing) && stackTrailingOnLargeText && fontScale > 1.2;

  return (
    <Pressable
      accessibilityRole={onPress ? "button" : undefined}
      onPress={onPress}
      disabled={!onPress}
      style={({ pressed }) => [
        styles.row,
        inline && styles.inlineRow,
        stackInline && styles.stackedInlineRow,
        !isLast && styles.divider,
        pressed && styles.pressed,
      ]}
    >
      {icon ? <MaterialCommunityIcons name={icon} size={19} color="#667085" /> : null}
      <View style={[styles.copy, inline && styles.inlineCopy, stackInline && styles.stackedInlineCopy]}>
        <Text variant="labelMedium" style={[styles.label, inline && styles.inlineLabel]}>{label}</Text>
        <Text
          variant="bodyMedium"
          style={[styles.value, inline && styles.inlineValue, stackInline && styles.stackedInlineValue]}
        >
          {value}
        </Text>
        {detail ? <Text variant="bodySmall" style={styles.detail}>{detail}</Text> : null}
        {stackTrailing ? <View style={styles.stackedTrailing}>{trailing}</View> : null}
      </View>
      {!stackTrailing ? trailing : null}
      {onPress ? <Text style={styles.chevron}>›</Text> : null}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  row: {
    minHeight: 62,
    flexDirection: "row",
    alignItems: "center",
    gap: HB_SPACING.xs,
    paddingVertical: 10,
  },
  inlineRow: {
    minHeight: 46,
    paddingVertical: 7,
  },
  stackedInlineRow: {
    alignItems: "flex-start",
    minHeight: 58,
    paddingVertical: 9,
  },
  divider: {
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: HB_COLORS.outlineMuted,
  },
  pressed: {
    opacity: 0.68,
  },
  copy: {
    flex: 1,
    gap: 2,
  },
  inlineCopy: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: HB_SPACING.sm,
  },
  stackedInlineCopy: {
    flexDirection: "column",
    alignItems: "flex-start",
    justifyContent: "flex-start",
    gap: 3,
  },
  inlineLabel: {
    flexShrink: 0,
  },
  inlineValue: {
    flex: 1,
    textAlign: "right",
    fontWeight: "500",
  },
  stackedInlineValue: {
    textAlign: "left",
  },
  stackedTrailing: {
    alignSelf: "flex-start",
    paddingTop: 3,
  },
  label: {
    color: HB_COLORS.textSecondary,
  },
  value: {
    color: HB_COLORS.textPrimary,
    fontWeight: "600",
  },
  detail: {
    color: HB_COLORS.textSecondary,
  },
  chevron: {
    color: HB_COLORS.textSecondary,
    fontSize: 28,
    lineHeight: 30,
  },
});
