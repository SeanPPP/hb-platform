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
  const supplierDisplay = supplierName || supplierCode || t("common:na");
  const itemDisplay = itemNumber || t("common:na");
  const barcodeDisplay = barcode || t("common:na");
  const productTypeKey =
    productType === 0 ? "normal"
    : productType === 1 ? "set"
    : productType === 2 ? "multi"
    : "unknown";

  return (
    <Card style={styles.card} mode="contained">
      <Card.Content style={styles.content}>
        <View style={styles.heroRow}>
          {imageUrl ? (
            <Image source={{ uri: imageUrl }} style={styles.image} resizeMode="cover" />
          ) : (
            <View style={styles.placeholder} />
          )}
          <View style={styles.heroMeta}>
            <View style={[styles.infoBlock, styles.nameBlock]}>
              <Text variant="titleMedium" numberOfLines={2} style={styles.name}>
                {productName || t("hero.unnamedProduct")}
              </Text>
              {grade ? (
                <Text variant="bodySmall" style={styles.grade}>
                  {t("common:grade", { grade })}
                </Text>
              ) : null}
            </View>
            <View style={[styles.infoBlock, styles.typeBlock]}>
              <Text variant="bodyMedium" style={[styles.blockValue, styles.typeValue]} numberOfLines={1}>
                {t(`hero.productType.${productTypeKey}`)}
              </Text>
            </View>
          </View>
        </View>

        <View style={styles.metaRow}>
          <View style={[styles.infoBlock, styles.supplierBlock]}>
            <Text variant="bodyMedium" style={[styles.blockValue, styles.supplierValue]} numberOfLines={1}>
              {supplierDisplay}
            </Text>
          </View>
          <View style={[styles.infoBlock, styles.itemBlock]}>
            <Text variant="bodyMedium" style={[styles.blockValue, styles.itemValue]} numberOfLines={1}>
              {itemDisplay}
            </Text>
          </View>
        </View>

        <View style={[styles.infoBlock, styles.barcodeBlock]}>
          <Text variant="bodyMedium" style={[styles.blockValue, styles.barcodeValue]} numberOfLines={1}>
            {barcodeDisplay}
          </Text>
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
    gap: 10,
    paddingVertical: 8,
  },
  heroRow: {
    flexDirection: "row",
    alignItems: "flex-start",
    gap: 10,
  },
  image: {
    width: 112,
    height: 126,
    borderRadius: 8,
    backgroundColor: "#F3F3F3",
  },
  placeholder: {
    width: 112,
    height: 126,
    borderRadius: 8,
    backgroundColor: "#F1F1F1",
  },
  heroMeta: {
    flex: 1,
    minHeight: 126,
    gap: 10,
  },
  metaRow: {
    flexDirection: "row",
    gap: 10,
  },
  infoBlock: {
    borderRadius: 8,
    paddingHorizontal: 10,
    paddingVertical: 7,
    gap: 4,
  },
  nameBlock: {
    flex: 1,
    minHeight: 54,
    justifyContent: "center",
    backgroundColor: "#FDF2F8",
  },
  typeBlock: {
    minHeight: 46,
    justifyContent: "center",
    backgroundColor: "#ECFDF3",
  },
  supplierBlock: {
    flex: 1.2,
    backgroundColor: "#FEF3F2",
  },
  itemBlock: {
    flex: 1,
    backgroundColor: "#FFF7ED",
  },
  barcodeBlock: {
    backgroundColor: "#EFF6FF",
  },
  blockValue: {
    fontWeight: "700",
    flexShrink: 1,
  },
  name: {
    fontWeight: "700",
    color: "#111827",
    lineHeight: 22,
  },
  itemValue: {
    color: "#9A3412",
  },
  typeValue: {
    color: "#166534",
  },
  barcodeValue: {
    color: "#1D4ED8",
    fontVariant: ["tabular-nums"],
  },
  supplierValue: {
    color: "#B42318",
  },
  grade: {
    color: "#475467",
  },
});
