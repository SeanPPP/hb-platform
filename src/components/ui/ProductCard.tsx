import { Image, StyleSheet, View } from "react-native";
import { Badge, Button, Card, IconButton, Text } from "react-native-paper";
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
  onIncreaseCartQuantity: (product: StoreOrderProductItem) => void;
}

export function ProductCard({
  product,
  dynamicDataMap,
  disabled = false,
  isUpdatingCart = false,
  onAddToCart,
  onDecreaseCartQuantity,
  onIncreaseCartQuantity,
}: ProductCardProps) {
  const { t } = useAppTranslation("common");
  const dynamicData = dynamicDataMap[product.productCode];
  const cartQuantity = dynamicData?.cartQuantity ?? 0;
  const hasCartQuantity = cartQuantity > 0;
  const grade = product.grade?.trim().toUpperCase();
  const gradeColor = grade ? PRODUCT_GRADE_CONFIG[grade]?.color ?? "#999" : undefined;

  return (
    <Card style={styles.card} mode="elevated">
      {grade ? (
        <View style={[styles.gradeBadge, { backgroundColor: gradeColor }]}>
          <Text style={styles.gradeBadgeText}>Grade {grade}</Text>
        </View>
      ) : null}
      {hasCartQuantity ? <Badge style={styles.cartBadge}>{cartQuantity}</Badge> : null}
      {product.productImage ? (
        <Image source={{ uri: product.productImage }} style={styles.image} resizeMode="contain" />
      ) : (
        <View style={styles.imagePlaceholder} />
      )}
      <Card.Content style={styles.content}>
        <Text variant="titleSmall" numberOfLines={2}>
          {product.productName || product.productCode}
        </Text>
        <Text variant="bodySmall" style={styles.secondaryText}>
          {t("labels.itemNumber")}: {product.itemNumber || t("na")}
        </Text>
        <Text variant="titleMedium" style={styles.priceText}>
          ${Number(product.oemPrice ?? 0).toFixed(2)}
        </Text>
        <Text variant="bodySmall" style={hasCartQuantity ? styles.cartQuantityHighlight : styles.secondaryText}>
          {t("cart:item.inCart")}: {cartQuantity}
        </Text>
        {dynamicData?.lastOrderDate ? (
          <Text variant="bodySmall" style={styles.secondaryText}>
            {t("labels.recentOrder")}: {dynamicData.lastOrderDate}
          </Text>
        ) : null}
      </Card.Content>
      <Card.Actions>
        {hasCartQuantity ? (
          <View style={styles.stepperRow}>
            <IconButton
              icon="minus"
              mode="contained-tonal"
              size={18}
              disabled={disabled || isUpdatingCart}
              loading={isUpdatingCart}
              onPress={() => onDecreaseCartQuantity(product, cartQuantity)}
            />
            <View style={styles.stepperQuantityWrap}>
              <Text variant="labelSmall" style={styles.secondaryText}>
                {t("cart:item.inCart")}
              </Text>
              <Text variant="titleMedium" style={styles.stepperQuantityText}>
                {cartQuantity}
              </Text>
            </View>
            <IconButton
              icon="plus"
              mode="contained"
              size={18}
              disabled={disabled || isUpdatingCart}
              loading={isUpdatingCart}
              onPress={() => onIncreaseCartQuantity(product)}
            />
          </View>
        ) : (
          <Button mode="contained" disabled={disabled} loading={isUpdatingCart} onPress={() => onAddToCart(product)}>
            {t("labels.addToCart")}
          </Button>
        )}
      </Card.Actions>
    </Card>
  );
}

const styles = StyleSheet.create({
  card: {
    flex: 1,
    margin: 6,
    overflow: "hidden",
  },
  cartBadge: {
    position: "absolute",
    top: 10,
    left: 10,
    zIndex: 2,
  },
  gradeBadge: {
    position: "absolute",
    top: 0,
    right: 0,
    zIndex: 3,
    paddingHorizontal: 8,
    paddingVertical: 2,
    borderBottomLeftRadius: 8,
    shadowColor: "#000",
    shadowOpacity: 0.15,
    shadowRadius: 4,
    shadowOffset: { width: 0, height: 2 },
    elevation: 2,
  },
  gradeBadgeText: {
    color: "#fff",
    fontSize: 12,
    fontWeight: "700",
  },
  image: {
    width: "100%",
    height: 140,
    backgroundColor: "#f5f5f5",
  },
  imagePlaceholder: {
    width: "100%",
    height: 140,
    backgroundColor: "#f0f0f0",
  },
  content: {
    gap: 6,
  },
  priceText: {
    color: "#1677FF",
    fontWeight: "700",
  },
  cartQuantityHighlight: {
    color: "#1677FF",
    fontWeight: "700",
  },
  secondaryText: {
    color: "#666",
  },
  stepperRow: {
    flex: 1,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    width: "100%",
  },
  stepperQuantityWrap: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    gap: 2,
  },
  stepperQuantityText: {
    color: "#1677FF",
    fontWeight: "700",
  },
});
