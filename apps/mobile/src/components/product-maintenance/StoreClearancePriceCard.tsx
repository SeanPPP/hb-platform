import { Pressable, StyleSheet, View } from "react-native";
import { Button, IconButton, Text } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { HB_COLORS, HB_SPACING } from "@/shared/theme/tokens";

interface StoreClearancePriceCardProps {
  clearanceBarcode?: string | null;
  clearancePrice: string;
  isPrintingClearance?: boolean;
  onEditClearancePrice: () => void;
  onPrintClearance?: () => void;
}

/** 清货行：嵌在标签卡第二行，行为与原清货卡一致（点价格或「设置清货价」编辑，右侧打印清货标签）。 */
export function StoreClearancePriceCard({
  clearanceBarcode,
  clearancePrice,
  isPrintingClearance = false,
  onEditClearancePrice,
  onPrintClearance,
}: StoreClearancePriceCardProps) {
  const { t } = useAppTranslation(["productQuery", "common"]);

  return (
    <View style={styles.container}>
      <Text style={styles.rowTitle} numberOfLines={1}>{t("clearancePrice.rowTitle")}</Text>
      <Pressable
        accessibilityRole="button"
        accessibilityLabel={`${t("clearancePrice.price")} ${clearancePrice || "--"}`}
        onPress={onEditClearancePrice}
        style={styles.info}
      >
        <Text style={styles.priceValue} numberOfLines={1}>
          {clearancePrice ? `$${clearancePrice}` : "--"}
        </Text>
        <Text style={styles.barcode} numberOfLines={1}>
          {clearanceBarcode || t("clearancePrice.pendingBarcode")}
        </Text>
      </Pressable>
      <Button compact mode="text" onPress={onEditClearancePrice} labelStyle={styles.setLabel}>
        {t("clearancePrice.setAction")}
      </Button>
      <IconButton
        icon="tag-outline"
        size={20}
        onPress={onPrintClearance}
        loading={isPrintingClearance}
        disabled={!onPrintClearance || isPrintingClearance}
        style={styles.printIcon}
        accessibilityLabel={t("print.clearance")}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flexDirection: "row",
    alignItems: "center",
    gap: HB_SPACING.xs,
    minHeight: 48,
    paddingLeft: HB_SPACING.sm,
    paddingRight: HB_SPACING.xxs,
  },
  rowTitle: {
    minWidth: 32,
    flexShrink: 0,
    fontSize: 13,
    fontWeight: "700",
    color: HB_COLORS.textPrimary,
  },
  info: {
    flex: 1,
    minWidth: 0,
    flexDirection: "row",
    alignItems: "baseline",
    gap: HB_SPACING.xs,
  },
  priceValue: {
    fontSize: 15,
    fontWeight: "700",
    color: HB_COLORS.textPrimary,
    fontVariant: ["tabular-nums"],
    flexShrink: 0,
  },
  barcode: {
    flex: 1,
    minWidth: 0,
    fontSize: 12,
    color: HB_COLORS.textSecondary,
    fontVariant: ["tabular-nums"],
  },
  setLabel: {
    marginHorizontal: 6,
    fontSize: 13,
  },
  printIcon: {
    margin: 0,
  },
});
