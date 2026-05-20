import { Pressable, StyleSheet, View } from "react-native";
import { IconButton, Text } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

interface StoreClearancePriceCardProps {
  clearanceBarcode?: string | null;
  clearancePrice: string;
  isPrintingClearance?: boolean;
  onEditClearancePrice: () => void;
  onPrintClearance?: () => void;
}

export function StoreClearancePriceCard({
  clearanceBarcode,
  clearancePrice,
  isPrintingClearance = false,
  onEditClearancePrice,
  onPrintClearance,
}: StoreClearancePriceCardProps) {
  const { t } = useAppTranslation(["productQuery", "common"]);

  return (
    <View style={styles.container}>
      <Text
        variant="bodySmall"
        style={styles.barcode}
        numberOfLines={1}
      >
        {clearanceBarcode || t("clearancePrice.pendingBarcode")}
      </Text>

      <Pressable onPress={onEditClearancePrice} style={styles.pricePressable}>
        <View style={styles.priceContent} pointerEvents="none">
          <Text variant="labelSmall" style={styles.priceLabel}>
            {t("clearancePrice.price")}
          </Text>
          <Text variant="bodyMedium" style={styles.priceValue}>
            {clearancePrice || "--"}
          </Text>
        </View>
      </Pressable>

      <IconButton
        icon="tag-outline"
        size={20}
        onPress={onPrintClearance}
        loading={isPrintingClearance}
        disabled={!onPrintClearance || isPrintingClearance}
        style={styles.printIcon}
        accessibilityLabel={t("print.clearance")}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flexDirection: "row",
    alignItems: "center",
    backgroundColor: "#fff",
    borderRadius: 8,
    paddingHorizontal: 12,
    paddingVertical: 4,
    gap: 10,
  },
  barcode: {
    flex: 1,
    flexShrink: 1,
    color: "#666",
  },
  pricePressable: {
    flexShrink: 0,
  },
  priceContent: {
    alignItems: "center",
    gap: 1,
  },
  priceLabel: {
    color: "#999",
    fontSize: 10,
  },
  priceValue: {
    color: "#0F172A",
    fontWeight: "700",
  },
  printIcon: {
    margin: 0,
  },
});
