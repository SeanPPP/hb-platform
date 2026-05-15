import { StyleSheet, View } from "react-native";
import { Card, Switch, Text, TextInput } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

interface StorePriceStrategyCardProps {
  storeName?: string | null;
  purchasePrice?: string;
  retailPrice?: string;
  autoPricing: boolean;
  isSpecialProduct: boolean;
  isActive: boolean;
  rate?: string;
  strategySourceLabel?: string | null;
  strategyRuleLabel?: string | null;
  evaluatingRate?: boolean;
  onChangePurchasePrice: (value: string) => void;
  onChangeRetailPrice: (value: string) => void;
  onToggleAutoPricing: (value: boolean) => void;
  onToggleSpecial: (value: boolean) => void;
  onToggleActive: (value: boolean) => void;
}

export function StorePriceStrategyCard({
  storeName,
  purchasePrice,
  retailPrice,
  autoPricing,
  isSpecialProduct,
  isActive,
  rate,
  strategySourceLabel,
  strategyRuleLabel,
  evaluatingRate = false,
  onChangePurchasePrice,
  onChangeRetailPrice,
  onToggleAutoPricing,
  onToggleSpecial,
  onToggleActive,
}: StorePriceStrategyCardProps) {
  const { t } = useAppTranslation("productQuery");

  return (
    <Card style={styles.card} mode="contained">
      <Card.Content style={styles.content}>
        <Text variant="titleSmall" style={styles.title}>
          {storeName || t("storePrice.fallbackTitle")}
        </Text>
        <View style={styles.priceRow}>
          <TextInput
            mode="outlined"
            label={t("storePrice.purchase")}
            dense
            keyboardType="decimal-pad"
            value={purchasePrice}
            onChangeText={onChangePurchasePrice}
            style={styles.input}
          />
          <TextInput
            mode="outlined"
            label={t("storePrice.retail")}
            dense
            keyboardType="decimal-pad"
            value={retailPrice}
            onChangeText={onChangeRetailPrice}
            style={styles.input}
          />
        </View>
        <View style={styles.rateRow}>
          <Text variant="bodySmall" style={styles.rateText}>
            {t("storePrice.rate", { value: rate || "--" })}
          </Text>
          {strategySourceLabel ? (
            <Text variant="bodySmall" style={styles.secondary}>
              {evaluatingRate ? t("storePrice.calculating") : strategySourceLabel}
            </Text>
          ) : null}
          {strategyRuleLabel ? (
            <Text variant="bodySmall" style={styles.secondary}>
              {strategyRuleLabel}
            </Text>
          ) : null}
        </View>
        <View style={styles.toggleRow}>
          <View style={styles.toggleItem}>
            <Text variant="bodySmall">{t("storePrice.auto")}</Text>
            <Switch value={autoPricing} onValueChange={onToggleAutoPricing} />
          </View>
          <View style={styles.toggleItem}>
            <Text variant="bodySmall">{t("storePrice.special")}</Text>
            <Switch value={isSpecialProduct} onValueChange={onToggleSpecial} />
          </View>
          <View style={styles.toggleItem}>
            <Text variant="bodySmall">{t("storePrice.active")}</Text>
            <Switch value={isActive} onValueChange={onToggleActive} />
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
    gap: 8,
    paddingVertical: 10,
  },
  title: {
    fontWeight: "700",
  },
  priceRow: {
    flexDirection: "row",
    gap: 10,
  },
  input: {
    flex: 1,
    backgroundColor: "#fff",
    height: 44,
  },
  rateRow: {
    gap: 2,
  },
  rateText: {
    color: "#1677FF",
    fontWeight: "700",
  },
  secondary: {
    color: "#666",
  },
  toggleRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 8,
  },
  toggleItem: {
    flex: 1,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
  },
});
