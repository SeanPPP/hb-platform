import { StyleSheet, View } from "react-native";
import { Card, Text } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

interface StoreClearancePriceCardProps {
  storeCode?: string | null;
  storeName?: string | null;
  clearanceBarcode?: string | null;
  clearancePrice?: string;
}

export function StoreClearancePriceCard({
  storeCode,
  storeName,
  clearanceBarcode,
  clearancePrice,
}: StoreClearancePriceCardProps) {
  const { t } = useAppTranslation("productQuery");

  return (
    <Card style={styles.card} mode="contained">
      <Card.Content style={styles.content}>
        <View style={styles.headerRow}>
          <Text variant="titleSmall" style={styles.title}>
            {t("clearancePrice.title")}
          </Text>
          <Text variant="titleSmall" style={styles.price}>
            {clearancePrice || t("common:na")}
          </Text>
        </View>
        <View style={styles.row}>
          <Text variant="bodySmall" style={styles.label}>
            {t("clearancePrice.store")}
          </Text>
          <Text variant="bodyMedium">{storeName || storeCode || t("common:na")}</Text>
        </View>
        <View style={styles.row}>
          <Text variant="bodySmall" style={styles.label}>
            {t("clearancePrice.barcode")}
          </Text>
          <Text variant="bodyMedium" numberOfLines={1}>{clearanceBarcode || t("common:na")}</Text>
        </View>
      </Card.Content>
    </Card>
  );
}

const styles = StyleSheet.create({
  card: {
    borderRadius: 8,
    backgroundColor: "#fff",
  },
  content: {
    gap: 6,
    paddingVertical: 8,
  },
  headerRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 12,
  },
  title: {
    fontWeight: "700",
    color: "#111827",
  },
  row: {
    flexDirection: "row",
    justifyContent: "space-between",
    gap: 12,
  },
  label: {
    color: "#666",
  },
  price: {
    color: "#B54708",
    fontWeight: "700",
  },
});
