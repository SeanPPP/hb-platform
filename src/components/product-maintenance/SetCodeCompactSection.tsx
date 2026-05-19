import { StyleSheet, View } from "react-native";
import { Button, Card, Text, TextInput } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import type { ProductSetCodeItem } from "@/modules/product-maintenance/types";

interface SetCodeCompactSectionProps {
  items: ProductSetCodeItem[];
  savingItemId?: string | null;
  draftBarcode: string;
  draftRetailPrice: string;
  onChangeDraftBarcode: (value: string) => void;
  onChangeDraftRetailPrice: (value: string) => void;
  onChangeItem: (setCodeId: string, patch: Partial<ProductSetCodeItem>) => void;
  onSaveItem: (setCodeId: string) => void;
  onCreateItem: () => void;
}

function formatPrice(value?: number | null) {
  return value == null ? "" : value.toFixed(2);
}

export function SetCodeCompactSection({
  items,
  savingItemId,
  draftBarcode,
  draftRetailPrice,
  onChangeDraftBarcode,
  onChangeDraftRetailPrice,
  onChangeItem,
  onSaveItem,
  onCreateItem,
}: SetCodeCompactSectionProps) {
  const { t } = useAppTranslation("productQuery");

  return (
    <Card style={styles.card} mode="contained">
      <Card.Content style={styles.content}>
        <Text variant="titleSmall" style={styles.title}>
          {t("setCode.title")}
        </Text>

        {items.map((item) => (
          <View key={item.setCodeId} style={styles.item}>
            <TextInput
              mode="outlined"
              dense
              label={t("setCode.barcode")}
              value={item.setBarcode ?? ""}
              onChangeText={(value) => onChangeItem(item.setCodeId, { setBarcode: value })}
              style={styles.input}
            />
            <View style={styles.footerRow}>
              <TextInput
                mode="outlined"
                dense
                label={t("setCode.retail")}
                keyboardType="decimal-pad"
                value={formatPrice(item.setRetailPrice)}
                onChangeText={(value) =>
                  onChangeItem(item.setCodeId, {
                    setRetailPrice: value.trim() === "" ? null : Number(value),
                  })
                }
                style={[styles.input, styles.priceInput]}
              />
              <Button
                compact
                mode="contained-tonal"
                onPress={() => onSaveItem(item.setCodeId)}
                loading={savingItemId === item.setCodeId}
                disabled={savingItemId === item.setCodeId}
              >
                {t("setCode.save")}
              </Button>
            </View>
          </View>
        ))}

        <View style={[styles.item, styles.draftItem]}>
          <Text variant="bodySmall" style={styles.addTitle}>
            {t("setCode.addTitle")}
          </Text>
          <TextInput
            mode="outlined"
            dense
            label={t("setCode.barcode")}
            value={draftBarcode}
            onChangeText={onChangeDraftBarcode}
            style={styles.input}
          />
          <View style={styles.footerRow}>
            <TextInput
              mode="outlined"
              dense
              label={t("setCode.retail")}
              keyboardType="decimal-pad"
              value={draftRetailPrice}
              onChangeText={onChangeDraftRetailPrice}
              style={[styles.input, styles.priceInput]}
            />
            <Button
              compact
              mode="contained"
              onPress={onCreateItem}
              loading={savingItemId === "new-set"}
              disabled={savingItemId === "new-set"}
            >
              {t("setCode.add")}
            </Button>
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
    gap: 10,
    paddingVertical: 10,
  },
  title: {
    fontWeight: "700",
  },
  item: {
    gap: 8,
  },
  draftItem: {
    borderTopWidth: StyleSheet.hairlineWidth,
    borderTopColor: "#E5E7EB",
    paddingTop: 10,
  },
  addTitle: {
    color: "#475467",
  },
  input: {
    backgroundColor: "#fff",
  },
  footerRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: 10,
  },
  priceInput: {
    flex: 1,
  },
});
