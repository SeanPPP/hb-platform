import { Pressable, StyleSheet, View } from "react-native";
import { ActivityIndicator, Button, Card, Text, TextInput } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import type { ProductSetCodeItem } from "@/modules/product-maintenance/types";

interface SetCodeCompactSectionProps {
  items: ProductSetCodeItem[];
  savingItemId?: string | null;
  printingItemId?: string | null;
  draftBarcode: string;
  draftRetailPrice: string;
  totalCount?: number;
  loading?: boolean;
  loadingMore?: boolean;
  hasMore?: boolean;
  onChangeDraftBarcode: (value: string) => void;
  onEditDraftRetailPrice: () => void;
  onChangeItem: (setCodeId: string, patch: Partial<ProductSetCodeItem>) => void;
  onEditItemRetailPrice: (setCodeId: string) => void;
  onSaveItem: (setCodeId: string) => void;
  onPrintItem: (setCodeId: string) => void;
  onCreateItem: () => void;
  onLoadMore?: () => void;
}

function formatPrice(value?: number | null) {
  return value == null ? "" : value.toFixed(2);
}

export function SetCodeCompactSection({
  items,
  savingItemId,
  printingItemId,
  draftBarcode,
  draftRetailPrice,
  totalCount,
  loading,
  loadingMore,
  hasMore,
  onChangeDraftBarcode,
  onEditDraftRetailPrice,
  onChangeItem,
  onEditItemRetailPrice,
  onSaveItem,
  onPrintItem,
  onCreateItem,
  onLoadMore,
}: SetCodeCompactSectionProps) {
  const { t } = useAppTranslation("productQuery");

  return (
    <Card style={styles.card} mode="contained">
      <Card.Content style={styles.content}>
        <Text variant="titleSmall" style={styles.title}>
          {t("setCode.title")}
          {totalCount != null ? ` (${items.length}/${totalCount})` : ""}
        </Text>

        {loading ? (
          <View style={styles.loadingRow}>
            <ActivityIndicator size="small" />
            <Text variant="bodySmall" style={styles.loadingText}>
              {t("setCode.loading")}
            </Text>
          </View>
        ) : null}

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
              <Pressable style={styles.priceInput} onPress={() => onEditItemRetailPrice(item.setCodeId)}>
                <View pointerEvents="none">
                  <TextInput
                    mode="outlined"
                    dense
                    label={t("setCode.retail")}
                    value={formatPrice(item.setRetailPrice)}
                    editable={false}
                    style={styles.input}
                  />
                </View>
              </Pressable>
              <Button
                compact
                mode="outlined"
                icon="printer-outline"
                onPress={() => onPrintItem(item.setCodeId)}
                loading={printingItemId === item.setCodeId}
                disabled={printingItemId === item.setCodeId}
              >
                {printingItemId === item.setCodeId ? t("print.sendingShort") : t("print.product")}
              </Button>
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

        {hasMore && onLoadMore ? (
          <Button
            compact
            mode="text"
            onPress={onLoadMore}
            loading={loadingMore}
            disabled={loadingMore}
          >
            {t("setCode.loadMore")}
          </Button>
        ) : null}

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
            <Pressable style={styles.priceInput} onPress={onEditDraftRetailPrice}>
              <View pointerEvents="none">
                <TextInput
                  mode="outlined"
                  dense
                  label={t("setCode.retail")}
                  value={draftRetailPrice}
                  editable={false}
                  style={styles.input}
                />
              </View>
            </Pressable>
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
  loadingRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
  },
  loadingText: {
    color: "#475467",
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
