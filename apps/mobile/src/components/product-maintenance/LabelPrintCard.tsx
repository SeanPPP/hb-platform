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
  onOpenSettings?: () => void;
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
}: LabelPrintCardProps) {
  const { t } = useAppTranslation(["productQuery"]);

  return (
    <Card style={styles.card} mode="contained">
      <Card.Content style={styles.content}>
        <View style={styles.header}>
          <Text variant="titleSmall" style={styles.title}>
            {t("print.title")}
          </Text>
          {onOpenSettings ? (
            <Button compact icon="cog-outline" mode="text" onPress={onOpenSettings}>
              {t("print.settingsAction")}
            </Button>
          ) : null}
        </View>
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
            {t("print.productShort")}
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
            {t("print.discountShort")}
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
            {t("print.bigDiscountShort")}
          </Button>
        </View>
      </Card.Content>
    </Card>
  );
}

const styles = StyleSheet.create({
  card: {
    borderRadius: 12,
    borderWidth: 1,
    borderColor: "#E4E7EC",
    backgroundColor: "#FFFFFF",
  },
  content: {
    paddingVertical: 10,
    gap: 8,
  },
  header: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 8,
  },
  title: {
    fontWeight: "700",
    color: "#111827",
  },
  actions: {
    flexDirection: "row",
    gap: 6,
  },
  button: {
    flex: 1,
  },
});
