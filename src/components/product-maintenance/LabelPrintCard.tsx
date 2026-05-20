import { StyleSheet, View } from "react-native";
import { Button, Card, Text } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

interface LabelPrintCardProps {
  isPrintingProduct?: boolean;
  isPrintingDiscount?: boolean;
  isPrintingClearance?: boolean;
  isPrintingBigDiscount?: boolean;
  isPrintingClearanceProduct?: boolean;
  canPrintDiscount?: boolean;
  canPrintClearance?: boolean;
  canPrintBigDiscount?: boolean;
  canPrintClearanceProduct?: boolean;
  onPrintProduct?: () => void;
  onPrintDiscount?: () => void;
  onPrintClearance?: () => void;
  onPrintBigDiscount?: () => void;
  onPrintClearanceProduct?: () => void;
}

export function LabelPrintCard({
  isPrintingProduct = false,
  isPrintingDiscount = false,
  isPrintingClearance = false,
  isPrintingBigDiscount = false,
  isPrintingClearanceProduct = false,
  canPrintDiscount = false,
  canPrintClearance = false,
  canPrintBigDiscount = false,
  canPrintClearanceProduct = false,
  onPrintProduct,
  onPrintDiscount,
  onPrintClearance,
  onPrintBigDiscount,
  onPrintClearanceProduct,
}: LabelPrintCardProps) {
  const { t } = useAppTranslation("productQuery");

  return (
    <Card style={styles.card} mode="contained">
      <Card.Content style={styles.content}>
        <Text variant="titleSmall" style={styles.title}>
          {t("print.title")}
        </Text>
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
            icon="barcode"
            onPress={onPrintClearanceProduct}
            loading={isPrintingClearanceProduct}
            disabled={!onPrintClearanceProduct || !canPrintClearanceProduct || isPrintingClearanceProduct}
          >
            {isPrintingClearanceProduct ? t("print.sendingShort") : t("print.clearanceProduct")}
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
  title: {
    fontWeight: "700",
    color: "#111827",
  },
  actions: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
});
