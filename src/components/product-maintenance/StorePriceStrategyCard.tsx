import { StyleSheet, View } from "react-native";
import { Card, IconButton, Switch, Text, TextInput } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

interface StorePriceStrategyCardProps {
  storeName?: string | null;
  purchasePrice?: string;
  retailPrice?: string;
  discountPercent?: string;
  discountedRetailPrice?: string;
  autoPricing: boolean;
  isSpecialProduct: boolean;
  rate?: string;
  strategySourceLabel?: string | null;
  strategyRuleLabel?: string | null;
  evaluatingRate?: boolean;
  isPrintingProductLabel?: boolean;
  isPrintingDiscountLabel?: boolean;
  onChangePurchasePrice: (value: string) => void;
  onChangeRetailPrice: (value: string) => void;
  onChangeDiscountPercent: (value: string) => void;
  onChangeDiscountedRetailPrice: (value: string) => void;
  onPrintProductLabel?: () => void;
  onPrintDiscountLabel?: () => void;
  onToggleAutoPricing: (value: boolean) => void;
  onToggleSpecial: (value: boolean) => void;
}

export function StorePriceStrategyCard({
  storeName,
  purchasePrice,
  retailPrice,
  discountPercent,
  discountedRetailPrice,
  autoPricing,
  isSpecialProduct,
  rate,
  strategySourceLabel,
  strategyRuleLabel,
  evaluatingRate = false,
  isPrintingProductLabel = false,
  isPrintingDiscountLabel = false,
  onChangePurchasePrice,
  onChangeRetailPrice,
  onChangeDiscountPercent,
  onChangeDiscountedRetailPrice,
  onPrintProductLabel,
  onPrintDiscountLabel,
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
          <IconButton
            accessibilityLabel={isPrintingProductLabel ? t("print.sendingShort") : t("print.quick")}
            icon="printer-outline"
            onPress={onPrintProductLabel}
            loading={isPrintingProductLabel}
            disabled={!onPrintProductLabel || isPrintingProductLabel}
            mode="contained"
            size={18}
            style={styles.iconButton}
          />
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
          <TextInput
            mode="outlined"
            label={t("storePrice.discountPercent")}
            dense
            keyboardType="decimal-pad"
            value={discountPercent}
            onChangeText={onChangeDiscountPercent}
            style={styles.input}
            textColor="#B42318"
          />
          <TextInput
            mode="outlined"
            label={t("storePrice.discountedRetail")}
            dense
            keyboardType="decimal-pad"
            value={discountedRetailPrice}
            onChangeText={onChangeDiscountedRetailPrice}
            style={styles.input}
            textColor="#166534"
          />
          <IconButton
            accessibilityLabel={isPrintingDiscountLabel ? t("print.sendingShort") : t("print.discount")}
            icon="sale-outline"
            onPress={onPrintDiscountLabel}
            loading={isPrintingDiscountLabel}
            disabled={!onPrintDiscountLabel || isPrintingDiscountLabel}
            mode="contained-tonal"
            size={18}
            style={[styles.iconButton, styles.discountIconButton]}
          />
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
    flex: 1,
    backgroundColor: "#fff",
    height: 38,
  },
  iconButton: {
    width: 36,
    height: 36,
    margin: 0,
    borderRadius: 8,
    alignSelf: "center",
  },
  discountIconButton: {
    backgroundColor: "#ECFDF3",
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
