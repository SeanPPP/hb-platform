import { Image, Pressable, StyleSheet, View } from "react-native";
import { Badge, Card, IconButton, Text } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import type { ProductDynamicDataMap, StoreOrderProductItem } from "@/modules/shop/types";

const PRODUCT_GRADE_CONFIG: Record<string, { color: string }> = {
  A: { color: "#722ED1" },
  B: { color: "#1890FF" },
  C: { color: "#FA8C16" },
  D: { color: "#F5222D" },
};

interface ProductCardProps {
  product: StoreOrderProductItem;
  dynamicDataMap: ProductDynamicDataMap;
  disabled?: boolean;
  isUpdatingCart?: boolean;
  onAddToCart: (product: StoreOrderProductItem) => void;
  onDecreaseCartQuantity: (product: StoreOrderProductItem, currentQuantity: number) => void;
  onEditCartQuantity: (product: StoreOrderProductItem, currentQuantity: number) => void;
  onIncreaseCartQuantity: (product: StoreOrderProductItem) => void;
}

function formatPrice(value: number | null | undefined): string {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    return "--";
  }

  return `$${value.toFixed(2)}`;
}

export function ProductCard({
  product,
  dynamicDataMap,
  disabled = false,
  isUpdatingCart = false,
  onAddToCart,
  onDecreaseCartQuantity,
  onEditCartQuantity,
  onIncreaseCartQuantity,
}: ProductCardProps) {
  const { t } = useAppTranslation("common");
  const dynamicData = dynamicDataMap[product.productCode];
  const cartQuantity = dynamicData?.cartQuantity ?? 0;
  const hasCartQuantity = cartQuantity > 0;
  const canEditQuantity = !disabled && !isUpdatingCart;
  const grade = product.grade?.trim().toUpperCase();
  const gradeColor = grade ? PRODUCT_GRADE_CONFIG[grade]?.color ?? "#999" : undefined;

  return (
    <Card style={styles.card} mode="outlined">
      {grade ? (
        <View style={[styles.gradeBadge, { backgroundColor: gradeColor }]}>
          <Text style={styles.gradeBadgeText}>{t("grade", { grade })}</Text>
        </View>
      ) : null}
      {hasCartQuantity ? <Badge style={styles.cartBadge}>{cartQuantity}</Badge> : null}
      {product.productImage ? (
        <Image source={{ uri: product.productImage }} style={styles.image} resizeMode="contain" />
      ) : (
        <View style={styles.imagePlaceholder} />
      )}
      <Card.Content style={styles.content}>
        <Text variant="labelLarge" numberOfLines={2} style={styles.titleText}>
          {product.productName || product.productCode}
        </Text>
        <Text variant="labelSmall" numberOfLines={1} style={styles.secondaryText}>
          {t("labels.itemNumber")}: {product.itemNumber || t("na")}
        </Text>
        <View style={styles.priceRow}>
          <View style={styles.priceColumn}>
            <Text variant="labelSmall" numberOfLines={1} style={styles.priceLabel}>
              {t("labels.importPrice")}
            </Text>
            <Text numberOfLines={1} adjustsFontSizeToFit style={styles.priceText}>
              {formatPrice(product.importPrice)}
            </Text>
          </View>
          <View style={styles.priceColumn}>
            <Text variant="labelSmall" numberOfLines={1} style={styles.priceLabel}>
              {t("labels.retailPrice")}
            </Text>
            <Text numberOfLines={1} adjustsFontSizeToFit style={styles.priceText}>
              {formatPrice(product.oemPrice)}
            </Text>
          </View>
        </View>
      </Card.Content>
      <Card.Actions style={styles.actions}>
        <View style={styles.stepperRow}>
          <IconButton
            icon="minus"
            mode="outlined"
            size={16}
            disabled={disabled || isUpdatingCart || !hasCartQuantity}
            loading={isUpdatingCart && hasCartQuantity}
            onPress={() => onDecreaseCartQuantity(product, cartQuantity)}
            style={styles.quantityButton}
          />
          {/* 数字只负责打开编辑器；后端写入和乐观回滚由 Home 页复用现有 mutation 处理。 */}
          <Pressable
            accessibilityRole="button"
            accessibilityLabel={t("labels.editCartQuantity", { quantity: cartQuantity })}
            disabled={!canEditQuantity}
            hitSlop={10}
            onPress={() => onEditCartQuantity(product, cartQuantity)}
            style={({ pressed }) => [
              styles.stepperQuantityWrap,
              canEditQuantity ? styles.stepperQuantityEditable : null,
              pressed && canEditQuantity ? styles.stepperQuantityPressed : null,
            ]}
          >
            <Text variant="labelMedium" style={styles.stepperQuantityText}>
              {cartQuantity}
            </Text>
          </Pressable>
          <IconButton
            icon="plus"
            mode="contained-tonal"
            size={16}
            disabled={disabled || isUpdatingCart}
            loading={isUpdatingCart && !hasCartQuantity}
            onPress={() => (hasCartQuantity ? onIncreaseCartQuantity(product) : onAddToCart(product))}
            style={styles.quantityButton}
          />
        </View>
      </Card.Actions>
    </Card>
  );
}

const styles = StyleSheet.create({
  card: {
    flex: 1,
    margin: 4,
    overflow: "hidden",
    borderRadius: 8,
    borderColor: "#C5C6CD",
    backgroundColor: "#FFFFFF",
  },
  cartBadge: {
    position: "absolute",
    top: 6,
    right: 6,
    zIndex: 2,
    backgroundColor: "#BA1A1A",
  },
  gradeBadge: {
    position: "absolute",
    top: 0,
    right: 0,
    zIndex: 3,
    paddingHorizontal: 6,
    paddingVertical: 2,
    borderBottomLeftRadius: 6,
    shadowColor: "#000",
    shadowOpacity: 0.15,
    shadowRadius: 4,
    shadowOffset: { width: 0, height: 2 },
    elevation: 2,
  },
  gradeBadgeText: {
    color: "#fff",
    fontSize: 10,
    fontWeight: "700",
  },
  image: {
    width: "100%",
    height: 86,
    backgroundColor: "#EAEFF3",
  },
  imagePlaceholder: {
    width: "100%",
    height: 86,
    backgroundColor: "#EAEFF3",
  },
  content: {
    gap: 2,
    minHeight: 70,
    paddingHorizontal: 8,
    paddingTop: 8,
    paddingBottom: 4,
  },
  titleText: {
    color: "#171C1F",
    fontSize: 12,
    lineHeight: 16,
    fontWeight: "800",
  },
  priceLabel: {
    color: "#75777D",
    fontSize: 9,
    marginTop: 2,
  },
  priceRow: {
    flexDirection: "row",
    alignItems: "flex-start",
    gap: 8,
    marginTop: 2,
  },
  priceColumn: {
    flex: 1,
    minWidth: 0,
  },
  priceText: {
    color: "#000000",
    fontWeight: "800",
    fontSize: 13,
    lineHeight: 16,
  },
  secondaryText: {
    color: "#45474C",
    fontSize: 10,
  },
  actions: {
    paddingHorizontal: 8,
    paddingBottom: 8,
    paddingTop: 0,
  },
  stepperRow: {
    flex: 1,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    width: "100%",
    minHeight: 30,
    borderRadius: 6,
    backgroundColor: "#F0F4F8",
  },
  stepperQuantityWrap: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    alignSelf: "stretch",
  },
  stepperQuantityEditable: {
    borderRadius: 4,
  },
  stepperQuantityPressed: {
    backgroundColor: "#E1E8F0",
  },
  stepperQuantityText: {
    color: "#171C1F",
    fontWeight: "800",
  },
  quantityButton: {
    width: 30,
    height: 30,
    margin: 0,
    borderRadius: 4,
  },
});
