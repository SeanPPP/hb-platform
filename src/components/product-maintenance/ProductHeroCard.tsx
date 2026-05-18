import { Image, StyleSheet, View } from "react-native";
import { Card, Text } from "react-native-paper";
import { ProductBarcodeImage } from "@/components/product-maintenance/ProductBarcodeImage";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

interface ProductHeroCardProps {
  imageUrl?: string | null;
  productName?: string;
  supplierName?: string | null;
  supplierCode?: string | null;
  itemNumber?: string | null;
  barcode?: string | null;
  productType?: number | null;
  grade?: string | null;
}

export function ProductHeroCard({
  imageUrl,
  productName,
  supplierName,
  supplierCode,
  itemNumber,
  barcode,
  productType,
  grade,
}: ProductHeroCardProps) {
  const { t } = useAppTranslation(["productQuery", "common"]);
  const productTypeKey =
    productType === 0 ? "normal"
    : productType === 1 ? "set"
    : productType === 2 ? "multi"
    : "unknown";

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
            {productName || t("hero.unnamedProduct")}
          </Text>
          <View style={styles.badgeRow}>
            <View style={[styles.badge, styles.itemBadge]}>
              <Text variant="labelSmall" style={styles.badgeLabel}>
                {t("hero.itemNumberLabel")}
              </Text>
              <Text variant="bodySmall" style={[styles.badgeValue, styles.itemValue]}>
                {itemNumber || t("common:na")}
              </Text>
            </View>
            <View style={[styles.badge, styles.typeBadge]}>
              <Text variant="labelSmall" style={styles.badgeLabel}>
                {t("hero.productTypeLabel")}
              </Text>
              <Text variant="bodySmall" style={[styles.badgeValue, styles.typeValue]}>
                {t(`hero.productType.${productTypeKey}`)}
              </Text>
            </View>
          </View>
          <View style={styles.badgeRow}>
            <View style={[styles.badge, styles.barcodeBadge]}>
              <Text variant="labelSmall" style={styles.badgeLabel}>
                {t("hero.barcodeLabel")}
              </Text>
              <Text variant="bodySmall" style={[styles.badgeValue, styles.barcodeValue]}>
                {barcode || t("common:na")}
              </Text>
            </View>
            <View style={[styles.badge, styles.supplierBadge]}>
              <Text variant="labelSmall" style={styles.badgeLabel}>
                {t("hero.supplierLabel")}
              </Text>
              <Text variant="bodySmall" style={[styles.badgeValue, styles.supplierValue]}>
                {supplierName || supplierCode || t("common:na")}
              </Text>
            </View>
          </View>
          {grade ? (
            <Text variant="bodySmall" style={styles.grade}>
              {t("common:grade", { grade })}
            </Text>
          ) : null}
          <ProductBarcodeImage value={barcode} />
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
    alignItems: "flex-start",
    paddingVertical: 8,
  },
  image: {
    width: 72,
    height: 72,
    borderRadius: 8,
    backgroundColor: "#F3F3F3",
  },
  placeholder: {
    width: 72,
    height: 72,
    borderRadius: 8,
    backgroundColor: "#F1F1F1",
  },
  info: {
    flex: 1,
    gap: 6,
  },
  name: {
    fontWeight: "700",
    color: "#111827",
  },
  badgeRow: {
    flexDirection: "row",
    gap: 8,
  },
  badge: {
    flex: 1,
    borderRadius: 8,
    paddingHorizontal: 8,
    paddingVertical: 6,
    gap: 2,
  },
  itemBadge: {
    backgroundColor: "#FFF7ED",
  },
  typeBadge: {
    backgroundColor: "#ECFDF3",
  },
  barcodeBadge: {
    backgroundColor: "#EFF6FF",
  },
  supplierBadge: {
    backgroundColor: "#FEF3F2",
  },
  badgeLabel: {
    color: "#4B5563",
  },
  badgeValue: {
    fontWeight: "700",
  },
  itemValue: {
    color: "#9A3412",
  },
  typeValue: {
    color: "#166534",
  },
  barcodeValue: {
    color: "#1D4ED8",
  },
  supplierValue: {
    color: "#B42318",
  },
  grade: {
    color: "#475467",
  },
});
