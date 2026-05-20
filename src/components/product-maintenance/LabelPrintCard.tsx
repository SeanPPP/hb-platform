import { StyleSheet, View } from "react-native";
import { Button, Card, Text } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

interface LabelPrintCardProps {
  isPrintingProduct?: boolean;
  isPrintingDiscount?: boolean;
  isPrintingBigDiscount?: boolean;
  canPrintDiscount?: boolean;
  canPrintBigDiscount?: boolean;
  onPrintProduct?: () => void;
  onPrintDiscount?: () => void;
  onPrintBigDiscount?: () => void;
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
            style={styles.button}
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
            style={styles.button}
          >
            {isPrintingDiscount ? t("print.sendingShort") : t("print.discount")}
          </Button>
          <Button
            compact
            mode="outlined"
            icon="post-outline"
            onPress={onPrintBigDiscount}
            loading={isPrintingBigDiscount}
            disabled={!onPrintBigDiscount || !canPrintBigDiscount || isPrintingBigDiscount}
            style={styles.button}
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
    gap: 8,
  },
  button: {
    flex: 1,
  },
});
