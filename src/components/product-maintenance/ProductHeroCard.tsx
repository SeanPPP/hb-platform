import { Image, StyleSheet, View } from "react-native";
import { Card, Text } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

interface ProductHeroCardProps {
  imageUrl?: string | null;
  productName?: string;
  productCode: string;
  itemNumber?: string | null;
  barcode?: string | null;
  productTypeLabel?: string | null;
  grade?: string | null;
}

export function ProductHeroCard({
  imageUrl,
  productName,
  productCode,
  itemNumber,
  barcode,
  productTypeLabel,
  grade,
}: ProductHeroCardProps) {
  const { t } = useAppTranslation(["productQuery", "common"]);

  return (
    <Card style={styles.card} mode="contained">
      <Card.Content style={styles.content}>
        {imageUrl ? (
          <Image source={{ uri: imageUrl }} style={styles.image} resizeMode="cover" />
        ) : (
          <View style={styles.placeholder} />
        )}
        <View style={styles.info}>
          <Text variant="titleSmall" numberOfLines={2} style={styles.name}>
            {productName || productCode}
          </Text>
          <Text variant="bodySmall" style={styles.meta}>
            {productCode} · {itemNumber || t("common:na")}
          </Text>
          <Text variant="bodySmall" style={styles.meta}>
            {barcode || t("common:na")} · {productTypeLabel || t("hero.product")}
            {grade ? ` · ${t("common:grade", { grade })}` : ""}
          </Text>
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
    flexDirection: "row",
    gap: 12,
    alignItems: "center",
    paddingVertical: 10,
  },
  image: {
    width: 56,
    height: 56,
    borderRadius: 8,
    backgroundColor: "#F3F3F3",
  },
  placeholder: {
    width: 56,
    height: 56,
    borderRadius: 8,
    backgroundColor: "#F1F1F1",
  },
  info: {
    flex: 1,
    gap: 2,
  },
  name: {
    fontWeight: "700",
  },
  meta: {
    color: "#666",
  },
});
