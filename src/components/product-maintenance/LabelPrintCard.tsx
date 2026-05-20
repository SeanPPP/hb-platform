import { StyleSheet, View } from "react-native";
import { Button, Card, Switch, Text } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

interface LabelPrintCardProps {
  isPrintingProduct?: boolean;
  isPrintingDiscount?: boolean;
  isPrintingClearance?: boolean;
  isPrintingBigDiscount?: boolean;
  canPrintDiscount?: boolean;
  canPrintClearance?: boolean;
  canPrintBigDiscount?: boolean;
  smallLabel?: boolean;
  onPrintProduct?: () => void;
  onPrintDiscount?: () => void;
  onPrintClearance?: () => void;
  onPrintBigDiscount?: () => void;
  onToggleSmallLabel?: () => void;
}

export function LabelPrintCard({
  isPrintingProduct = false,
  isPrintingDiscount = false,
  isPrintingClearance = false,
  isPrintingBigDiscount = false,
  canPrintDiscount = false,
  canPrintClearance = false,
  canPrintBigDiscount = false,
  smallLabel = false,
  onPrintProduct,
  onPrintDiscount,
  onPrintClearance,
  onPrintBigDiscount,
  onToggleSmallLabel,
}: LabelPrintCardProps) {
  const { t } = useAppTranslation("productQuery");

  return (
    <Card style={styles.card} mode="contained">
      <Card.Content style={styles.content}>
        <View style={styles.headerRow}>
          <Text variant="titleSmall" style={styles.title}>
            {t("print.title")}
          </Text>
          <View style={styles.smallLabelRow}>
            <Text variant="bodySmall" style={styles.smallLabelText}>
              {t("print.smallLabel")}
            </Text>
            <Switch
              value={smallLabel}
              onValueChange={onToggleSmallLabel}
              style={styles.smallLabelSwitch}
            />
          </View>
        </View>
        <View style={styles.actions}>
          <Button
            compact
            mode="contained"
            icon="printer-outline"
            onPress={onPrintProduct}
            loading={isPrintingProduct}
            disabled={!onPrintProduct || isPrintingProduct}
          >
            {isPrintingProduct ? t("print.sendingShort") : t("print.product")}
          </Button>
          <Button
            compact
            mode="contained-tonal"
            icon="sale-outline"
            onPress={onPrintDiscount}
            loading={isPrintingDiscount}
            disabled={!onPrintDiscount || !canPrintDiscount || isPrintingDiscount}
          >
            {isPrintingDiscount ? t("print.sendingShort") : t("print.discount")}
          </Button>
          <Button
            compact
            mode="outlined"
            icon="tag-outline"
            onPress={onPrintClearance}
            loading={isPrintingClearance}
            disabled={!onPrintClearance || !canPrintClearance || isPrintingClearance}
          >
            {isPrintingClearance ? t("print.sendingShort") : t("print.clearance")}
          </Button>
          <Button
            compact
            mode="outlined"
            icon="post-outline"
            onPress={onPrintBigDiscount}
            loading={isPrintingBigDiscount}
            disabled={!onPrintBigDiscount || !canPrintBigDiscount || isPrintingBigDiscount}
          >
            {isPrintingBigDiscount ? t("print.sendingShort") : t("print.bigDiscount")}
          </Button>
        </View>
      </Card.Content>
    </Card>
  );
}

const styles = StyleSheet.create({
  card: {
    borderRadius: 8,
    backgroundColor: "#FFFDF7",
  },
  content: {
    gap: 8,
    paddingVertical: 8,
  },
  headerRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
  },
  title: {
    fontWeight: "700",
    color: "#111827",
  },
  smallLabelRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: 4,
  },
  smallLabelText: {
    color: "#64748B",
  },
  smallLabelSwitch: {
    transform: [{ scaleX: 0.8 }, { scaleY: 0.8 }],
  },
  actions: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
});
