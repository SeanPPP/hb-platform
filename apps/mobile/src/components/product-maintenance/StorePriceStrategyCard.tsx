import { Pressable, StyleSheet, View } from "react-native";
import { Card, Switch, Text, TextInput } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

interface StorePriceStrategyCardProps {
  storeName?: string | null;
  purchasePrice?: string;
  retailPrice?: string;
  retailGp?: string;
  discountPercent?: string;
  discountedRetailPrice?: string;
  discountedRetailGp?: string;
  autoPricing: boolean;
  isSpecialProduct: boolean;
  rate?: string;
  strategySourceLabel?: string | null;
  strategyRuleLabel?: string | null;
  evaluatingRate?: boolean;
  onEditPurchasePrice: () => void;
  onEditRetailPrice: () => void;
  onEditDiscountPercent: () => void;
  onEditDiscountedRetailPrice: () => void;
  onToggleAutoPricing: (value: boolean) => void;
  onToggleSpecial: (value: boolean) => void;
}

export function StorePriceStrategyCard({
  storeName,
  purchasePrice,
  retailPrice,
  retailGp,
  discountPercent,
  discountedRetailPrice,
  discountedRetailGp,
  autoPricing,
  isSpecialProduct,
  rate,
  strategySourceLabel,
  strategyRuleLabel,
  evaluatingRate = false,
  onEditPurchasePrice,
  onEditRetailPrice,
  onEditDiscountPercent,
  onEditDiscountedRetailPrice,
  onToggleAutoPricing,
  onToggleSpecial,
}: StorePriceStrategyCardProps) {
  const { t } = useAppTranslation("productQuery");
  const strategyText = evaluatingRate
    ? t("storePrice.calculating")
    : [strategySourceLabel, strategyRuleLabel].filter(Boolean).join(" ");

  return (
    <Card style={styles.card} mode="contained">
      <Card.Content style={styles.content}>
        <View style={styles.headerRow}>
          <Text variant="titleSmall" style={styles.title}>
            {storeName || t("storePrice.fallbackTitle")}
          </Text>
          <Text variant="bodySmall" style={styles.rateText}>
            {t("storePrice.rate", { value: rate || "--" })}
          </Text>
        </View>
        <View style={styles.priceRow}>
          <Pressable style={styles.narrowInput} onPress={onEditPurchasePrice}>
            <View pointerEvents="none">
              <TextInput
                mode="outlined"
                label={t("storePrice.purchase")}
                dense
                value={purchasePrice}
                editable={false}
                style={styles.input}
              />
            </View>
          </Pressable>
          <View style={styles.inputFill}>
            <View style={styles.inlineRow}>
              <Pressable style={styles.inputFlex} onPress={onEditRetailPrice}>
                <View pointerEvents="none">
                  <TextInput
                    mode="outlined"
                    label={t("storePrice.retail")}
                    dense
                    value={retailPrice}
                    editable={false}
                    style={styles.input}
                  />
                </View>
              </Pressable>
              <Text variant="labelSmall" style={styles.gpText}>
                GP:{retailGp || "--"}
              </Text>
            </View>
          </View>
        </View>
        <View style={styles.summaryRow}>
          {strategyText ? (
            <Text variant="bodySmall" style={styles.secondary} numberOfLines={1}>
              {strategyText}
            </Text>
          ) : (
            <View style={styles.summarySpacer} />
          )}
          <View style={styles.toggleRow}>
            <View style={styles.toggleItem}>
              <Text variant="bodySmall" style={styles.toggleLabel}>{t("storePrice.auto")}</Text>
              <Switch value={autoPricing} onValueChange={onToggleAutoPricing} />
            </View>
            <View style={styles.toggleItem}>
              <Text variant="bodySmall" style={styles.toggleLabel}>{t("storePrice.special")}</Text>
              <Switch value={isSpecialProduct} onValueChange={onToggleSpecial} />
            </View>
          </View>
        </View>
        <View style={styles.discountRow}>
          <Pressable style={styles.narrowInput} onPress={onEditDiscountPercent}>
            <View pointerEvents="none">
              <TextInput
                mode="outlined"
                label={t("storePrice.discountPercent")}
                dense
                value={discountPercent}
                editable={false}
                style={styles.input}
                textColor="#B42318"
              />
            </View>
          </Pressable>
          <View style={styles.inputFill}>
            <View style={styles.inlineRow}>
              <Pressable style={styles.inputFlex} onPress={onEditDiscountedRetailPrice}>
                <View pointerEvents="none">
                  <TextInput
                    mode="outlined"
                    label={t("storePrice.discountedRetail")}
                    dense
                    value={discountedRetailPrice}
                    editable={false}
                    style={styles.input}
                    textColor="#166534"
                  />
                </View>
              </Pressable>
              <Text variant="labelSmall" style={styles.gpText}>
                GP:{discountedRetailGp || "--"}
              </Text>
            </View>
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
    gap: 4,
    paddingVertical: 6,
  },
  headerRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 6,
  },
  title: {
    fontWeight: "700",
    color: "#111827",
  },
  priceRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: 6,
  },
  discountRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
  },
  input: {
    backgroundColor: "#fff",
    height: 38,
  },
  narrowInput: {
    width: 88,
    flexShrink: 0,
  },
  inputFill: {
    flex: 1,
  },
  inputFlex: {
    flex: 1,
  },
  inlineRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: 4,
  },
  gpText: {
    fontWeight: "700",
    fontSize: 12,
    color: "#1677FF",
    flexShrink: 0,
  },
  rateText: {
    color: "#1677FF",
    fontWeight: "700",
    flexShrink: 0,
  },
  secondary: {
    color: "#475467",
    flex: 1,
    minWidth: 0,
  },
  summaryRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 8,
    paddingTop: 2,
  },
  summarySpacer: {
    flex: 1,
  },
  toggleRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "flex-end",
    gap: 6,
    flexShrink: 0,
  },
  toggleItem: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "flex-end",
    gap: 2,
  },
  toggleLabel: {
    color: "#344054",
  },
});
