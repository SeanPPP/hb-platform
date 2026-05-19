import { Image, StyleSheet, View } from "react-native";
import { Card, IconButton, Text } from "react-native-paper";
import { ProductBarcodeImage } from "@/components/product-maintenance/ProductBarcodeImage";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

interface ProductHeroCardProps {
  imageUrl?: string | null;
  isPrintingProductLabel?: boolean;
  onPrintProductLabel?: () => void;
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
  isPrintingProductLabel = false,
  onPrintProductLabel,
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
  const productTypeLabel =
    productType === 0 ? t("hero.productType.normal")
    : productType === 1 ? t("hero.productType.set")
    : productType === 2 ? t("hero.productType.multi")
    : t("hero.productType.unknown");

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
            <View style={styles.nameBlock}>
              <Text variant="titleSmall" numberOfLines={2} style={styles.name}>
                {productName || t("hero.unnamedProduct")}
              </Text>
            </View>

            <View style={styles.metaLine}>
              <Text variant="bodyMedium" style={[styles.blockValue, styles.itemValue]} numberOfLines={1}>
                {itemDisplay}
              </Text>
            </View>

            <View style={styles.metaLine}>
              <Text variant="bodySmall" style={[styles.blockValue, styles.supplierValue, styles.supplierText]} numberOfLines={1}>
                {grade ? `${supplierDisplay} · ${t("common:grade", { grade })}` : supplierDisplay}
              </Text>
              <Text variant="bodySmall" style={[styles.blockValue, styles.typeValue]} numberOfLines={1}>
                {productTypeLabel}
              </Text>
            </View>
          </View>
        </View>

        <View style={styles.barcodeInfoRow}>
          <View style={[styles.infoBlock, styles.barcodeMetaBlock]}>
            <Text variant="bodyMedium" style={[styles.blockValue, styles.supplierValue]} numberOfLines={1}>
              {supplierDisplay}
            </Text>
          </View>
          <View style={[styles.infoBlock, styles.typeBlock]}>
            <Text variant="bodyMedium" style={[styles.blockValue, styles.typeValue]} numberOfLines={1}>
              {productTypeLabel}
            </Text>
          </View>
        </View>

        <View style={[styles.infoBlock, styles.barcodeBlock]}>
          <View style={styles.barcodeRow}>
            <View style={styles.barcodeImageWrap}>
              <ProductBarcodeImage value={barcode} />
            </View>
            <IconButton
              accessibilityLabel={isPrintingProductLabel ? t("print.sendingShort") : t("print.quick")}
              icon="printer-outline"
              onPress={onPrintProductLabel}
              loading={isPrintingProductLabel}
              disabled={!onPrintProductLabel || isPrintingProductLabel}
              mode="contained"
              size={18}
              style={styles.printButton}
            />
          </View>
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
    paddingVertical: 6,
  },
  heroRow: {
    flexDirection: "row",
    alignItems: "stretch",
    gap: 8,
  },
  image: {
    width: 104,
    height: 118,
    borderRadius: 8,
    backgroundColor: "#F3F3F3",
  },
  placeholder: {
    width: 104,
    height: 118,
    borderRadius: 8,
    backgroundColor: "#F1F1F1",
  },
  heroMeta: {
    flex: 1,
    minHeight: 118,
    justifyContent: "space-between",
    gap: 6,
  },
  infoBlock: {
    borderRadius: 8,
    paddingHorizontal: 10,
    paddingVertical: 5,
    gap: 2,
  },
  nameBlock: {
    minHeight: 40,
    justifyContent: "flex-start",
    paddingHorizontal: 10,
    paddingVertical: 6,
    borderRadius: 8,
    backgroundColor: "#FDF2F8",
  },
  metaLine: {
    minHeight: 24,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 8,
    paddingHorizontal: 10,
    paddingVertical: 4,
    borderRadius: 8,
    backgroundColor: "#FFF7ED",
  },
  barcodeInfoRow: {
    display: "none",
  },
  barcodeMetaBlock: {
    display: "none",
  },
  typeBlock: {
    minHeight: 0,
    justifyContent: "center",
    backgroundColor: "#ECFDF3",
  },
  barcodeBlock: {
    backgroundColor: "#EFF6FF",
  },
  barcodeRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
  },
  barcodeImageWrap: {
    flex: 1,
    minWidth: 0,
  },
  blockValue: {
    fontWeight: "700",
    flexShrink: 1,
  },
  name: {
    fontWeight: "700",
    color: "#111827",
    lineHeight: 18,
  },
  itemValue: {
    color: "#9A3412",
    flex: 1,
  },
  typeValue: {
    color: "#166534",
    flexShrink: 0,
  },
  printButton: {
    width: 36,
    height: 36,
    margin: 0,
    borderRadius: 8,
    alignSelf: "center",
  },
  supplierValue: {
    color: "#B42318",
  },
  supplierText: {
    flex: 1,
  },
});
