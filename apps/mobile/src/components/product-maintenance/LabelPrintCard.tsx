import type { ReactNode } from "react";
import { StyleSheet, View } from "react-native";
import { Button, Card, IconButton, Text } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { HB_COLORS, HB_RADIUS, HB_SPACING } from "@/shared/theme/tokens";

interface LabelPrintCardProps {
  isPrintingProduct?: boolean;
  isPrintingDiscount?: boolean;
  isPrintingBigDiscount?: boolean;
  canPrintDiscount?: boolean;
  canPrintBigDiscount?: boolean;
  onPrintProduct?: () => void;
  onPrintDiscount?: () => void;
  onPrintBigDiscount?: () => void;
  onOpenSettings?: () => void;
  /** 与标签合并在同一张卡里的下一行（清货价行）。 */
  footer?: ReactNode;
}

export function LabelPrintCard({
  isPrintingProduct = false,
  isPrintingDiscount = false,
  isPrintingBigDiscount = false,
  canPrintDiscount = false,
  canPrintBigDiscount = false,
  onPrintProduct,
  onPrintDiscount,
  onPrintBigDiscount,
  onOpenSettings,
  footer,
}: LabelPrintCardProps) {
  const { t } = useAppTranslation(["productQuery"]);

  return (
    <Card style={styles.card} mode="contained">
      <View style={styles.row}>
        <Text style={styles.rowTitle} numberOfLines={1}>{t("print.label")}</Text>
        <View style={styles.actions}>
          <Button
            compact
            mode="contained"
            onPress={onPrintProduct}
            loading={isPrintingProduct}
            disabled={!onPrintProduct || isPrintingProduct}
            style={styles.button}
            labelStyle={styles.buttonLabel}
          >
            {t("print.productShort")}
          </Button>
          <Button
            compact
            mode="contained-tonal"
            onPress={onPrintDiscount}
            loading={isPrintingDiscount}
            disabled={!onPrintDiscount || !canPrintDiscount || isPrintingDiscount}
            style={styles.button}
            labelStyle={styles.buttonLabel}
          >
            {t("print.discountShort")}
          </Button>
          <Button
            compact
            mode="outlined"
            onPress={onPrintBigDiscount}
            loading={isPrintingBigDiscount}
            disabled={!onPrintBigDiscount || !canPrintBigDiscount || isPrintingBigDiscount}
            style={styles.button}
            labelStyle={styles.buttonLabel}
          >
            {t("print.bigDiscountShort")}
          </Button>
        </View>
        {onOpenSettings ? (
          <IconButton
            icon="cog-outline"
            accessibilityLabel={t("print.settingsTitle")}
            size={20}
            onPress={onOpenSettings}
            style={styles.settingsButton}
          />
        ) : null}
      </View>
      {footer ? <View style={styles.footer}>{footer}</View> : null}
    </Card>
  );
}

const styles = StyleSheet.create({
  card: {
    borderRadius: HB_RADIUS.surface,
    borderWidth: 1,
    borderColor: HB_COLORS.outlineMuted,
    backgroundColor: HB_COLORS.white,
    overflow: "hidden",
  },
  row: {
    flexDirection: "row",
    alignItems: "center",
    gap: HB_SPACING.xs,
    minHeight: 52,
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
  actions: {
    flex: 1,
    flexDirection: "row",
    gap: 6,
  },
  button: {
    flex: 1,
    borderRadius: HB_RADIUS.control,
  },
  buttonLabel: {
    marginHorizontal: 6,
    fontSize: 13,
  },
  settingsButton: {
    margin: 0,
  },
  footer: {
    borderTopWidth: StyleSheet.hairlineWidth,
    borderTopColor: HB_COLORS.outlineMuted,
  },
});
