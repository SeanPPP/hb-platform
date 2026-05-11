import { Image, StyleSheet, View } from "react-native";
import { Badge, Button, Card, Chip, IconButton, Text } from "react-native-paper";
import type { ProductDynamicDataMap, StoreOrderProductItem } from "@/modules/shop/types";

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
  const dynamicData = dynamicDataMap[product.productCode];
  const cartQuantity = dynamicData?.cartQuantity ?? 0;
  const hasCartQuantity = cartQuantity > 0;

  return (
    <Card style={styles.card} mode="elevated">
      {hasCartQuantity ? <Badge style={styles.cartBadge}>{cartQuantity}</Badge> : null}
      {product.productImage ? <Image source={{ uri: product.productImage }} style={styles.image} /> : <View style={styles.imagePlaceholder} />}
      <Card.Content style={styles.content}>
        <Text variant="titleSmall" numberOfLines={2}>
          {product.productName || "未命名商品"}
        </Text>
        <Text variant="bodySmall" style={styles.secondaryText}>
          货号: {product.itemNumber || "--"}
        </Text>
        <Text variant="bodySmall" style={styles.secondaryText}>
          条码: {product.barcode || "--"}
        </Text>
        <View style={styles.metaRow}>
          <Chip compact>{product.isInStock ? `库存 ${product.stockQuantity}` : "无库存"}</Chip>
          <Chip compact>起订 {product.minOrderQuantity || 1}</Chip>
        </View>
        <Text variant="titleMedium" style={styles.priceText}>
          ${Number(product.oemPrice ?? 0).toFixed(2)}
        </Text>
        <Text variant="bodySmall" style={hasCartQuantity ? styles.cartQuantityHighlight : styles.secondaryText}>
          车内数量: {cartQuantity}
        </Text>
        {dynamicData?.lastOrderDate ? (
          <Text variant="bodySmall" style={styles.secondaryText}>
            最近订货: {dynamicData.lastOrderDate}
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
                购物车
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
            加入购物车
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
    right: 10,
    zIndex: 2,
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
  metaRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
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
