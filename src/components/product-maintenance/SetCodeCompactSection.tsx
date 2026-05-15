import { StyleSheet, View } from "react-native";
import { Card, Text } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import type { ProductSetCodeItem } from "@/modules/product-maintenance/types";

interface SetCodeCompactSectionProps {
  items: ProductSetCodeItem[];
}

export function SetCodeCompactSection({ items }: SetCodeCompactSectionProps) {
  const { t } = useAppTranslation(["productQuery", "common"]);

  if (!items.length) {
    return null;
  }

  return (
    <Card style={styles.card} mode="contained">
      <Card.Content style={styles.content}>
        <Text variant="titleSmall" style={styles.title}>
          {t("setCode.title")}
        </Text>
        {items.slice(0, 3).map((item) => (
          <View key={item.setCodeId} style={styles.item}>
            <Text variant="bodyMedium">{item.setBarcode || t("common:na")}</Text>
            <Text variant="bodySmall" style={styles.meta}>
              {t("setCode.meta", {
                itemNumber: item.setItemNumber || t("common:na"),
                quantity: item.setQuantity ?? 0,
                price: item.setRetailPrice ?? t("common:na"),
              })}
            </Text>
          </View>
        ))}
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
    gap: 8,
    paddingVertical: 10,
  },
  title: {
    fontWeight: "700",
  },
  item: {
    gap: 2,
  },
  meta: {
    color: "#666",
  },
});
