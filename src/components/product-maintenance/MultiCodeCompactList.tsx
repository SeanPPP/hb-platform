import { StyleSheet, View } from "react-native";
import { Button, Card, Switch, Text, TextInput } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import type { MultiCodeEditableItem } from "@/modules/product-maintenance/types";

interface MultiCodeCompactListProps {
  items: MultiCodeEditableItem[];
  onChangeItem: (uuid: string, patch: Partial<MultiCodeEditableItem>) => void;
  onSaveItem: (uuid: string) => void;
  savingItemId?: string | null;
}

function formatDecimal(value?: number | null) {
  return value == null ? "" : String(value);
}

export function MultiCodeCompactList({
  items,
  onChangeItem,
  onSaveItem,
  savingItemId,
}: MultiCodeCompactListProps) {
  const { t } = useAppTranslation("productQuery");

  if (!items.length) {
    return null;
  }

  return (
    <View style={styles.container}>
      {items.map((item) => (
        <Card key={item.uuid} style={styles.card} mode="contained">
          <Card.Content style={styles.content}>
            <Text variant="bodyMedium" style={styles.barcode}>
              {item.barcode || item.multiCodeProductCode || item.uuid}
            </Text>
            <View style={styles.priceRow}>
              <TextInput
                mode="outlined"
                dense
                label={t("multiCode.purchase")}
                keyboardType="decimal-pad"
                style={styles.input}
                value={formatDecimal(item.purchasePrice)}
                onChangeText={(value) =>
                  onChangeItem(item.uuid, {
                    purchasePrice: value.trim() === "" ? null : Number(value),
                  })
                }
              />
              <TextInput
                mode="outlined"
                dense
                label={t("multiCode.retail")}
                keyboardType="decimal-pad"
                style={styles.input}
                value={formatDecimal(item.retailPrice)}
                onChangeText={(value) =>
                  onChangeItem(item.uuid, {
                    retailPrice: value.trim() === "" ? null : Number(value),
                  })
                }
              />
            </View>
            <Text variant="bodySmall" style={styles.rate}>
              Rate {item.rate == null ? "--" : item.rate}
              {item.strategySourceLabel ? ` · ${item.strategySourceLabel}` : ""}
            </Text>
            <View style={styles.footerRow}>
              <View style={styles.switches}>
                <View style={styles.switchItem}>
                  <Text variant="bodySmall">{t("multiCode.auto")}</Text>
                  <Switch
                    value={item.isAutoPricing}
                    onValueChange={(value) => onChangeItem(item.uuid, { isAutoPricing: value })}
                  />
                </View>
                <View style={styles.switchItem}>
                  <Text variant="bodySmall">{t("multiCode.special")}</Text>
                  <Switch
                    value={item.isSpecialProduct}
                    onValueChange={(value) => onChangeItem(item.uuid, { isSpecialProduct: value })}
                  />
                </View>
              </View>
              <Button
                compact
                mode="contained-tonal"
                onPress={() => onSaveItem(item.uuid)}
                loading={savingItemId === item.uuid}
                disabled={savingItemId === item.uuid}
              >
                {t("multiCode.save")}
              </Button>
            </View>
          </Card.Content>
        </Card>
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    gap: 8,
  },
  card: {
    borderRadius: 8,
    backgroundColor: "#fff",
  },
  content: {
    gap: 8,
    paddingVertical: 10,
  },
  barcode: {
    fontWeight: "600",
    color: "#0F172A",
  },
  priceRow: {
    flexDirection: "row",
    gap: 10,
  },
  input: {
    flex: 1,
    backgroundColor: "#fff",
    height: 42,
  },
  rate: {
    color: "#475467",
  },
  footerRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 12,
  },
  switches: {
    flex: 1,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 8,
  },
  switchItem: {
    flexDirection: "row",
    alignItems: "center",
    gap: 2,
  },
});
