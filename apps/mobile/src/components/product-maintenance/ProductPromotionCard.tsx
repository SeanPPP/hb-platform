import { StyleSheet, View } from "react-native";
import { Card, Text } from "react-native-paper";
import type { PromotionListItem } from "@/modules/promotions/types";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

interface ProductPromotionCardProps {
  items: PromotionListItem[];
}

function formatFixedPrice(value: number) {
  return Number.isFinite(value) ? value.toFixed(2) : "0.00";
}

export function ProductPromotionCard({ items }: ProductPromotionCardProps) {
  const { t } = useAppTranslation("productQuery");

  if (!items.length) {
    return null;
  }

  return (
    <Card style={styles.card} mode="contained">
      <Card.Content style={styles.content}>
        <Text variant="labelLarge" style={styles.title}>
          {t("promotion.title")}
        </Text>
        {items.map((item, index) => (
          <View
            key={item.id}
            style={[styles.promotionRow, index > 0 ? styles.promotionRowDivider : null]}
          >
            <Text variant="bodyMedium" numberOfLines={1} style={styles.name}>
              {item.name || t("promotion.unnamed")}
            </Text>
            <View style={styles.ruleBadge}>
              <Text variant="labelMedium" style={styles.ruleText}>
                {t("promotion.rule", {
                  count: item.applyQuantity,
                  price: formatFixedPrice(item.fixedPrice),
                })}
              </Text>
            </View>
          </View>
        ))}
      </Card.Content>
    </Card>
  );
}

const styles = StyleSheet.create({
  card: {
    borderRadius: 8,
    borderWidth: 1,
    borderColor: "#FFD591",
    backgroundColor: "#FFF7E6",
  },
  content: {
    gap: 4,
    paddingVertical: 8,
  },
  title: {
    color: "#AD4E00",
    fontWeight: "800",
  },
  promotionRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 8,
    paddingVertical: 3,
  },
  promotionRowDivider: {
    borderTopWidth: StyleSheet.hairlineWidth,
    borderTopColor: "#FFD591",
    paddingTop: 7,
  },
  name: {
    minWidth: 150,
    flexGrow: 1,
    flexShrink: 1,
    color: "#7A2E0E",
    fontWeight: "700",
  },
  ruleBadge: {
    marginLeft: "auto",
    borderRadius: 999,
    backgroundColor: "#AD4E00",
    paddingHorizontal: 10,
    paddingVertical: 4,
    flexShrink: 0,
  },
  ruleText: {
    color: "#FFFFFF",
    fontWeight: "800",
  },
});
