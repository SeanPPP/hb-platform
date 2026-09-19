import { useState } from "react";
import { Pressable, StyleSheet, View } from "react-native";
import { Icon, Text } from "react-native-paper";
import { summarizePromotions } from "@/modules/product-maintenance/product-query-presentation";
import type { PromotionListItem } from "@/modules/promotions/types";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { HB_RADIUS, HB_SPACING } from "@/shared/theme/tokens";

interface ProductPromotionCardProps {
  items: PromotionListItem[];
}

function formatFixedPrice(value: number) {
  return Number.isFinite(value) ? value.toFixed(2) : "0.00";
}

/** 默认一行浅橙摘要条，点击展开完整活动列表（内容与原卡片一致）。 */
export function ProductPromotionCard({ items }: ProductPromotionCardProps) {
  const { t } = useAppTranslation("productQuery");
  const [expanded, setExpanded] = useState(false);
  const summary = summarizePromotions(items);

  if (!summary) {
    return null;
  }

  const summaryText = [
    t("promotion.active"),
    t("promotion.rule", { count: summary.applyQuantity, price: summary.fixedPrice }),
    summary.extraCount > 0 ? t("promotion.more", { count: summary.extraCount + 1 }) : null,
    summary.endDate ? t("promotion.until", { date: summary.endDate }) : null,
  ].filter(Boolean).join(" · ");

  return (
    <View style={styles.card}>
      <Pressable
        accessibilityRole="button"
        accessibilityState={{ expanded }}
        accessibilityLabel={`${t("promotion.title")} ${summaryText}`}
        onPress={() => setExpanded((current) => !current)}
        style={({ pressed }) => [styles.summaryRow, pressed ? styles.pressed : null]}
      >
        <Icon source="tag-heart-outline" size={16} color="#AD4E00" />
        <Text style={styles.summaryText} numberOfLines={1}>
          {summaryText}
        </Text>
        <Icon source={expanded ? "chevron-up" : "chevron-right"} size={18} color="#AD4E00" />
      </Pressable>
      {expanded ? (
        <View style={styles.details}>
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
        </View>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  card: {
    borderRadius: HB_RADIUS.control,
    borderWidth: 1,
    borderColor: "#FFD591",
    backgroundColor: "#FFF7E6",
    overflow: "hidden",
  },
  summaryRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: 6,
    minHeight: 36,
    paddingHorizontal: 10,
  },
  summaryText: {
    flex: 1,
    minWidth: 0,
    fontSize: 13,
    fontWeight: "700",
    color: "#AD4E00",
    fontVariant: ["tabular-nums"],
  },
  details: {
    gap: 4,
    paddingHorizontal: HB_SPACING.sm,
    paddingBottom: HB_SPACING.xs,
    borderTopWidth: StyleSheet.hairlineWidth,
    borderTopColor: "#FFD591",
    paddingTop: HB_SPACING.xs,
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
  pressed: {
    opacity: 0.72,
  },
});
