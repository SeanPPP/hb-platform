import { Pressable, StyleSheet, View } from "react-native";
import { Button, Card, Text, TextInput } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

interface StoreClearancePriceCardProps {
  storeCode?: string | null;
  storeName?: string | null;
  clearanceBarcode?: string | null;
  clearancePrice: string;
  saving?: boolean;
  onEditClearancePrice: () => void;
  onSave: () => void;
}

export function StoreClearancePriceCard({
  storeCode,
  storeName,
  clearanceBarcode,
  clearancePrice,
  saving,
  onEditClearancePrice,
  onSave,
}: StoreClearancePriceCardProps) {
  const { t } = useAppTranslation(["productQuery", "common"]);

  return (
    <Card style={styles.card} mode="contained">
      <Card.Content style={styles.content}>
        <View style={styles.headerRow}>
          <Text variant="titleSmall" style={styles.title}>
            {t("clearancePrice.title")}
          </Text>
          <Button
            compact
            mode="contained-tonal"
            onPress={onSave}
            loading={saving}
            disabled={saving}
          >
            {t("clearancePrice.save")}
          </Button>
        </View>

        <View style={styles.row}>
          <Text variant="bodySmall" style={styles.label}>
            {t("clearancePrice.store")}
          </Text>
          <Text variant="bodyMedium" style={styles.value}>
            {storeName || storeCode || t("common:na")}
          </Text>
        </View>

        <View style={styles.row}>
          <Text variant="bodySmall" style={styles.label}>
            {t("clearancePrice.barcode")}
          </Text>
          <Text variant="bodyMedium" style={styles.value} numberOfLines={1}>
            {clearanceBarcode || t("clearancePrice.pendingBarcode")}
          </Text>
        </View>

        <Pressable onPress={onEditClearancePrice}>
          <View pointerEvents="none">
            <TextInput
              mode="outlined"
              dense
              label={t("clearancePrice.price")}
              value={clearancePrice}
              editable={false}
              style={styles.input}
            />
          </View>
        </Pressable>
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
    gap: 10,
    paddingVertical: 10,
  },
  headerRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 12,
  },
  title: {
    fontWeight: "700",
    color: "#111827",
  },
  row: {
    flexDirection: "row",
    justifyContent: "space-between",
    gap: 12,
  },
  label: {
    color: "#666",
  },
  value: {
    flex: 1,
    textAlign: "right",
    color: "#0F172A",
  },
  input: {
    backgroundColor: "#fff",
  },
});
