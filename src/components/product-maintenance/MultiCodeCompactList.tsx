import { StyleSheet, View } from "react-native";
import { Button, Card, Text, TextInput } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import type { MultiCodeEditableItem } from "@/modules/product-maintenance/types";

interface MultiCodeCompactListProps {
  items: MultiCodeEditableItem[];
  savingItemId?: string | null;
  draftBarcode: string;
  mainRetailPrice?: number | null;
  onChangeDraftBarcode: (value: string) => void;
  onChangeItem: (setCodeId: string, patch: Partial<MultiCodeEditableItem>) => void;
  onSaveItem: (setCodeId: string) => void;
  onCreateItem: () => void;
}

function formatPrice(value?: number | null) {
  return value == null ? "" : value.toFixed(2);
}

export function MultiCodeCompactList({
  items,
  savingItemId,
  draftBarcode,
  mainRetailPrice,
  onChangeDraftBarcode,
  onChangeItem,
  onSaveItem,
  onCreateItem,
}: MultiCodeCompactListProps) {
  const { t } = useAppTranslation("productQuery");

  return (
    <Card style={styles.card} mode="contained">
      <Card.Content style={styles.content}>
        <Text variant="titleSmall" style={styles.title}>
          {t("multiCode.title")}
        </Text>

        {items.map((item) => (
          <View key={item.setCodeId || item.uuid} style={styles.item}>
            <TextInput
              mode="outlined"
              dense
              label={t("multiCode.barcode")}
              value={item.barcode ?? ""}
              onChangeText={(value) => onChangeItem(item.setCodeId, { barcode: value })}
              style={styles.input}
            />
            <View style={styles.footerRow}>
              <TextInput
                mode="outlined"
                dense
                label={t("multiCode.retail")}
                value={formatPrice(item.retailPrice)}
                editable={false}
                style={[styles.input, styles.priceInput]}
              />
              <Button
                compact
                mode="contained-tonal"
                onPress={() => onSaveItem(item.setCodeId)}
                loading={savingItemId === item.setCodeId}
                disabled={savingItemId === item.setCodeId}
              >
                {t("multiCode.save")}
              </Button>
            </View>
          </View>
        ))}

        <View style={[styles.item, styles.draftItem]}>
          <Text variant="bodySmall" style={styles.addTitle}>
            {t("multiCode.addTitle")}
          </Text>
          <TextInput
            mode="outlined"
            dense
            label={t("multiCode.barcode")}
            value={draftBarcode}
            onChangeText={onChangeDraftBarcode}
            style={styles.input}
          />
          <View style={styles.footerRow}>
            <TextInput
              mode="outlined"
              dense
              label={t("multiCode.retail")}
              value={formatPrice(mainRetailPrice)}
              editable={false}
              style={[styles.input, styles.priceInput]}
            />
            <Button
              compact
              mode="contained"
              onPress={onCreateItem}
              loading={savingItemId === "new-multi"}
              disabled={savingItemId === "new-multi"}
            >
              {t("multiCode.add")}
            </Button>
          </View>
          <Text variant="bodySmall" style={styles.hint}>
            {t("multiCode.followMain")}
          </Text>
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
  hint: {
    color: "#475467",
  },
});
