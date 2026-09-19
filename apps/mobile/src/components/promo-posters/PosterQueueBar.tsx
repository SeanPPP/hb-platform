import { StyleSheet, View } from "react-native";
import { Button, Surface, Text } from "react-native-paper";
import { MaterialCommunityIcons } from "@expo/vector-icons";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { HB_COLORS, HB_RADIUS, HB_SPACING } from "@/shared/theme/tokens";
import { usePosterQueueSummary } from "./use-poster-queue-summary";

interface PosterQueueBarProps {
  onOpen: () => void;
}

/** 扫码页底部「待打印」浮条：队列非空时显示张数、按尺寸汇总与拼版页数。 */
export function PosterQueueBar({ onOpen }: PosterQueueBarProps) {
  const { t } = useAppTranslation(["productQuery"]);
  const summary = usePosterQueueSummary();
  if (summary.count === 0) {
    return null;
  }

  return (
    <Surface style={styles.bar} elevation={2}>
      <View style={styles.icon}>
        <MaterialCommunityIcons name="layers-outline" size={20} color={HB_COLORS.action} />
      </View>
      <View style={styles.copy}>
        <Text style={styles.title} numberOfLines={1}>
          {t("poster.queueBar.title", { count: summary.count })}
        </Text>
        <Text style={styles.detail} numberOfLines={1}>
          {summary.detail}
        </Text>
      </View>
      <Button mode="contained" compact onPress={onOpen} style={styles.button} labelStyle={styles.buttonLabel}>
        {t("poster.queueBar.go")}
      </Button>
    </Surface>
  );
}

const styles = StyleSheet.create({
  bar: {
    flexDirection: "row",
    alignItems: "center",
    gap: 10,
    marginHorizontal: HB_SPACING.sm,
    marginBottom: HB_SPACING.xs,
    paddingVertical: HB_SPACING.xs,
    paddingLeft: HB_SPACING.sm,
    paddingRight: HB_SPACING.xs,
    borderRadius: HB_RADIUS.surface,
    borderWidth: 1,
    borderColor: HB_COLORS.outlineMuted,
    backgroundColor: HB_COLORS.white,
  },
  icon: {
    width: 36,
    height: 36,
    borderRadius: 18,
    backgroundColor: "#EAF2FF",
    alignItems: "center",
    justifyContent: "center",
  },
  copy: {
    flex: 1,
    minWidth: 0,
  },
  title: {
    fontSize: 14,
    lineHeight: 20,
    fontWeight: "600",
    color: HB_COLORS.textPrimary,
  },
  detail: {
    fontSize: 12,
    lineHeight: 16,
    color: HB_COLORS.textSecondary,
  },
  button: {
    borderRadius: HB_RADIUS.control,
  },
  buttonLabel: {
    marginHorizontal: 14,
    fontSize: 14,
  },
});
