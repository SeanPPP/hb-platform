import { Image, Pressable, StyleSheet, View } from "react-native";
import { Card, Text } from "react-native-paper";
import { ProductBarcodeImage } from "@/components/product-maintenance/ProductBarcodeImage";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

const PRODUCT_GRADE_CONFIG: Record<string, { color: string }> = {
  A: { color: "#722ED1" },
  B: { color: "#1890FF" },
  C: { color: "#FA8C16" },
  D: { color: "#F5222D" },
};

interface ProductHeroCardProps {
  imageUrl?: string | null;
  productName?: string;
  supplierName?: string | null;
  supplierCode?: string | null;
  barcode?: string | null;
  productType?: number | null;
  grade?: string | null;
  onPressProductType?: () => void;
}

export function ProductHeroCard({
  imageUrl,
  productName,
  supplierName,
  supplierCode,
  barcode,
  productType,
  grade,
  onPressProductType,
}: ProductHeroCardProps) {
  const { t } = useAppTranslation(["productQuery", "common"]);
  const supplierDisplay = supplierName || supplierCode || t("common:na");
  const normalizedGrade = grade?.trim().toUpperCase();
  const gradeColor = normalizedGrade ? PRODUCT_GRADE_CONFIG[normalizedGrade]?.color ?? "#98A2B3" : undefined;
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
              <Text variant="bodySmall" style={[styles.blockValue, styles.supplierValue, styles.supplierText]} numberOfLines={1}>
                {supplierDisplay}
              </Text>
              <Pressable
                accessibilityRole="button"
                onPress={onPressProductType}
                disabled={!onPressProductType}
                style={({ pressed }) => [
                  styles.typeBadge,
                  !onPressProductType ? styles.typeBadgeStatic : null,
                  pressed && onPressProductType ? styles.typeBadgePressed : null,
                ]}
              >
                <Text variant="bodySmall" style={[styles.blockValue, styles.typeValue]} numberOfLines={1}>
                  {productTypeLabel}
                </Text>
              </Pressable>
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

        <View style={[styles.infoBlock, styles.barcodeBlock, styles.barcodeRow]}>
          <View style={styles.barcodeImageWrap}>
            <ProductBarcodeImage value={barcode} />
          </View>
          {normalizedGrade ? (
            <View style={[styles.gradeBadge, { backgroundColor: gradeColor }]}>
              <Text variant="labelMedium" style={styles.gradeBadgeText}>
                {t("common:grade", { grade: normalizedGrade })}
              </Text>
            </View>
          ) : null}
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
    gap: 10,
  },
  gradeBadge: {
    alignSelf: "flex-start",
    borderRadius: 999,
    paddingHorizontal: 12,
    paddingVertical: 6,
    flexShrink: 0,
  },
  gradeBadgeText: {
    color: "#FFFFFF",
    fontWeight: "800",
    letterSpacing: 0.2,
  },
  barcodeImageWrap: {
    width: 150,
    maxWidth: "58%",
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
  typeValue: {
    color: "#166534",
    flexShrink: 0,
  },
  supplierValue: {
    color: "#B42318",
  },
  supplierText: {
    flex: 1,
  },
  typeBadge: {
    borderRadius: 999,
    paddingHorizontal: 10,
    paddingVertical: 5,
    backgroundColor: "#DCFCE7",
    borderWidth: 1,
    borderColor: "#86EFAC",
    flexShrink: 0,
  },
  typeBadgeStatic: {
    opacity: 1,
  },
  typeBadgePressed: {
    opacity: 0.78,
  },
});
