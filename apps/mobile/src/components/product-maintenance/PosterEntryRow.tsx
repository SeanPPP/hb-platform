import { StyleSheet, View } from "react-native";
import { Button, Text } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { HB_COLORS, HB_RADIUS, HB_SPACING } from "@/shared/theme/tokens";
import type { PromoPosterAvailability } from "@/modules/promo-posters/logic";
import { PROMO_POSTER_KINDS, type PromoPosterKind } from "@/modules/promo-posters/types";

interface PosterEntryRowProps {
  availability: PromoPosterAvailability;
  disabled?: boolean;
  onOpen: (kind: PromoPosterKind) => void;
}

/** 海报行：嵌在标签卡清货行下面，四个类型按商品状态启用（规则同「折扣」标签按钮），点击进入海报编辑页。 */
export function PosterEntryRow({ availability, disabled = false, onOpen }: PosterEntryRowProps) {
  const { t } = useAppTranslation(["productQuery"]);

  return (
    <View style={styles.row}>
      <Text style={styles.rowTitle} numberOfLines={1}>
        {t("poster.rowTitle")}
      </Text>
      <View style={styles.actions}>
        {PROMO_POSTER_KINDS.map((kind) => (
          <Button
            key={kind}
            compact
            mode="outlined"
            disabled={disabled || !availability[kind]}
            onPress={() => onOpen(kind)}
            style={styles.button}
            labelStyle={styles.buttonLabel}
            accessibilityLabel={t("poster.entryAccessibility", { kind: t(`poster.kinds.${kind}`) })}
          >
            {t(`poster.kinds.${kind}`)}
          </Button>
        ))}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: "row",
    alignItems: "center",
    gap: HB_SPACING.xs,
    minHeight: 52,
    paddingHorizontal: HB_SPACING.sm,
  },
  rowTitle: {
    minWidth: 32,
    flexShrink: 0,
    fontSize: 13,
    fontWeight: "700",
    color: HB_COLORS.textPrimary,
  },
  actions: {
    flex: 1,
    flexDirection: "row",
    gap: 6,
  },
  button: {
    flex: 1,
    borderRadius: HB_RADIUS.control,
  },
  // 四个按钮挤在一行：英文「Multi-buy」「Clearance」在 13px + 6px 边距下会被截断，这里收窄到 12px + 2px
  buttonLabel: {
    marginHorizontal: 2,
    fontSize: 12,
  },
});
